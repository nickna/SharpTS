using System.Reflection;
using System.Runtime.CompilerServices;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.DotNet;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Static analyzer for <c>@DotNetType</c> interop bindings. Reproduces, at type-check
/// time, the .NET-resolution errors that today only surface at interpret time
/// (<c>Execution/Interpreter.DotNet.cs</c>) and compile time
/// (<c>Compilation/ILEmitter.Calls.ExternalInterop.cs</c>) — the diagnostics tsserver
/// structurally cannot produce, which are the unique value of the SharpTS language server.
///
/// Diagnostics carry no <c>TsCode</c> (SharpTS-only), so they are PUBLISHED under the
/// "complement tsserver" model rather than suppressed.
///
/// Coverage (reflection-only — no type-checker required):
///   Tier 1  — @DotNetType target type not found.
///   Tier 2  — declared member not found on the CLR type.
///   Tier 3a — @DotNetOverload hint is malformed / matches no overload.
///   Tier 3b — member exists but with the opposite static-ness (precise message).
///   Tier 3c — binding declares a constructor but the CLR type has no public one.
///   Tier 3d — addEventListener/removeEventListener arity + unknown event name.
/// (Argument-type / overload resolution at call sites is Tier 3e — needs the type
///  checker's inferred argument types and is intentionally out of scope here.)
///
/// When a <see cref="PositionMap"/> is supplied to <see cref="Analyze"/>, diagnostics carry
/// token-precise ranges (Phase 4a); otherwise they fall back to line-only locations.
///
/// RESOLUTION SEAM: <see cref="DotNetTypeRegistry.ResolveFriendly"/> by default (in-process,
/// mirrors the interpreter); the production server injects
/// <c>AssemblyReferenceLoader.TryResolve</c> to validate against the project's referenced
/// assemblies. Member/overload/event lookups reuse the runtime's own resolvers
/// (<see cref="DotNetTypeRegistry"/>, <see cref="DotNetMethodResolver"/>) so verdicts
/// match runtime behavior exactly — no reimplemented semantics, no divergence.
/// </summary>
public sealed class InteropAnalyzer
{
    private readonly Func<string, Type?> _resolve;
    private readonly Func<IEnumerable<string>>? _typeNames;

    public InteropAnalyzer(
        Func<string, Type?>? resolve = null,
        Func<IEnumerable<string>>? typeNames = null)
    {
        _resolve = resolve ?? DotNetTypeRegistry.Resolve;
        _typeNames = typeNames;
    }

    // DOM-style addEventListener/removeEventListener are event-binder intrinsics
    // (Runtime/DotNet/DotNetEventBinder.cs), not real CLR methods — never flag them as
    // missing members; their *calls* are validated separately (Tier 3d).
    private static readonly HashSet<string> EventIntrinsics =
        new(StringComparer.Ordinal) { "addEventListener", "removeEventListener" };

    private static SourceLocation Loc(Token token, PositionMap? pos)
        => pos is not null ? pos.Span(token) : SourceLocation.FromLine(token.Line);
    private static SourceLocation Loc(Token start, Token end, PositionMap? pos)
        => pos is not null ? pos.Span(start, end) : SourceLocation.FromLine(start.Line);

    public List<Diagnostic> Analyze(
        IEnumerable<Stmt> statements,
        PositionMap? positions = null,
        CancellationToken cancellationToken = default)
    {
        var diags = new List<Diagnostic>();
        var bindings = new Dictionary<string, Type>(StringComparer.Ordinal);

        var stmtList = statements as IReadOnlyList<Stmt> ?? statements.ToList();

        // Pass 1 — validate each @DotNetType class and dotnet: import, recording
        // name -> CLR type bindings for the call-site pass.
        foreach (var stmt in stmtList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stmt is Stmt.Class cls)
                AnalyzeClass(cls, diags, bindings, positions);
            else if (stmt is Stmt.Import import && DotNetImports.IsDotNetSpecifier(import.ModulePath))
                AnalyzeDotNetImport(import, diags, bindings, positions);
            else if (stmt is Stmt.Import extensionImport &&
                     DotNetExtensionImports.IsSpecifier(extensionImport.ModulePath))
                AnalyzeDotNetExtensionImport(extensionImport, diags, positions);
        }

        // Pass 2 — Tier 3d: validate event-subscription call sites against the bindings.
        var visitor = new EventCallVisitor(bindings, diags, positions);
        foreach (var stmt in stmtList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor.Visit(stmt);
        }

        return diags;
    }

    private void AnalyzeDotNetExtensionImport(
        Stmt.Import import,
        List<Diagnostic> diags,
        PositionMap? pos)
    {
        if (import.DefaultImport != null ||
            import.NamespaceImport != null ||
            import.NamedImports != null ||
            import.IsTypeOnly)
        {
            diags.Add(Diagnostic.TypeError(
                $"'{import.ModulePath}' must be imported for side effects.",
                Loc(import.Keyword, pos)));
            return;
        }

        string typeName = import.ModulePath[DotNetExtensionImports.Prefix.Length..];
        Type? container;
        try
        {
            container = DotNetTypeRegistry.ResolveFriendly(typeName, _resolve);
        }
        catch (ArgumentException ex)
        {
            diags.Add(Diagnostic.TypeError(ex.Message, Loc(import.Keyword, pos)));
            return;
        }

        if (container == null || !(container.IsPublic || container.IsNestedPublic))
        {
            diags.Add(Diagnostic.TypeError(
                $"Cannot resolve public extension container '{typeName}'.",
                Loc(import.Keyword, pos)));
            return;
        }
        if (!(container.IsAbstract && container.IsSealed) ||
            !container.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.IsDefined(
                    typeof(ExtensionAttribute), inherit: false)))
        {
            diags.Add(Diagnostic.TypeError(
                $"'{typeName}' is not a public static extension-method container.",
                Loc(import.Keyword, pos)));
        }
    }

    /// <summary>
    /// Validates a <c>dotnet:</c> import statement: unsupported forms (default / namespace-star)
    /// and each named import's resolvability, via the same
    /// <see cref="DotNetImports.ResolveExportType"/> algorithm module loading uses — so the
    /// squiggle at edit time and the load-time error can never disagree. Resolved names are
    /// recorded as bindings for the event-call pass, exactly like @DotNetType classes.
    /// </summary>
    private void AnalyzeDotNetImport(Stmt.Import import, List<Diagnostic> diags, Dictionary<string, Type> bindings, PositionMap? pos)
    {
        string specifier = import.ModulePath[DotNetImports.Prefix.Length..];

        if (import.DefaultImport != null)
        {
            Diagnostic diagnostic = Diagnostic.TypeError(
                $"'{import.ModulePath}' has no default export — dotnet: modules support named imports only.",
                Loc(import.DefaultImport, pos));
            string? exportName = ResolveDefaultImportExportName(specifier);
            if (exportName is not null)
            {
                string localName = import.DefaultImport.Lexeme;
                string replacement = string.Equals(
                    exportName,
                    localName,
                    StringComparison.Ordinal)
                    ? $"{{ {exportName} }}"
                    : $"{{ {exportName} as {localName} }}";
                diagnostic = WithReplacement(
                    diagnostic,
                    $"Convert to named import '{exportName}'",
                    replacement);
            }
            diags.Add(diagnostic);
        }

        if (import.NamespaceImport != null)
        {
            diags.Add(Diagnostic.TypeError(
                $"namespace imports (import * as …) are not supported for '{import.ModulePath}' — import the types you need by name.",
                Loc(import.NamespaceImport, pos)));
        }

        if (import.NamedImports == null) return;

        foreach (var spec in import.NamedImports)
        {
            Type type;
            try
            {
                type = DotNetImports.ResolveExportType(specifier, spec.Imported.Lexeme, _resolve);
            }
            catch (Exception ex)
            {
                diags.Add(Diagnostic.TypeError(ex.Message, Loc(spec.Imported, pos)));
                continue;
            }
            bindings[spec.LocalName?.Lexeme ?? spec.Imported.Lexeme] = type;
        }
    }

    private void AnalyzeClass(Stmt.Class cls, List<Diagnostic> diags, Dictionary<string, Type> bindings, PositionMap? pos)
    {
        var (mapping, at, nameTok, endTok) = FindDotNetType(cls);
        if (mapping is null) return;

        Type? type;
        try
        {
            type = DotNetTypeRegistry.ResolveFriendly(mapping, _resolve);
        }
        catch (ArgumentException ex)
        {
            diags.Add(Diagnostic.TypeError(
                $"@DotNetType: invalid .NET type '{mapping}': {ex.Message}",
                Loc(at!, nameTok!, pos)));
            return;
        }
        if (type == null)
        {
            // Tier 1 — mirrors Interpreter.DotNet.cs:31, surfaced statically at edit time.
            string? suggestion = FindNearest(
                mapping,
                _typeNames?.Invoke() ?? []);
            Diagnostic diagnostic = Diagnostic.TypeError(
                $"@DotNetType: .NET type '{mapping}' not found in any loaded assembly.",
                suggestion is null
                    ? Loc(at!, nameTok!, pos)
                    : Loc(at!, endTok!, pos));
            if (suggestion is not null)
            {
                diagnostic = WithReplacement(
                    diagnostic,
                    $"Change .NET type to '{suggestion}'",
                    $"@DotNetType(\"{suggestion}\")");
            }
            diags.Add(diagnostic);
            return;
        }

        bindings[cls.Name.Lexeme] = type;

        foreach (var m in cls.Methods)
        {
            string name = m.Name.Lexeme;

            if (name == "constructor") { CheckConstructor(type, m.Name, diags, pos); continue; }
            if (EventIntrinsics.Contains(name)) continue;

            if (!MemberExists(type, name, m.IsStatic))
            {
                AddMissingMember(type, name, m.IsStatic, "method", m.Name, diags, pos);
                continue;
            }

            CheckOverloadHint(m, type, diags, pos); // Tier 3a (only when the method resolves)
        }

        foreach (var f in cls.Fields)
        {
            string name = f.Name.Lexeme;
            if (!MemberExists(type, name, f.IsStatic))
                AddMissingMember(type, name, f.IsStatic, "property/field", f.Name, diags, pos);
        }
    }

    private static bool MemberExists(Type type, string name, bool isStatic)
        => DotNetTypeRegistry.GetMethods(type, name, isStatic).Length > 0
        || DotNetTypeRegistry.GetPropertyOrField(type, name, isStatic) != null;

    /// <summary>Tier 2 + Tier 3b: "not found", upgraded to a static-ness mismatch message
    /// when the member exists with the opposite static-ness.</summary>
    private static void AddMissingMember(Type type, string name, bool isStatic, string kind, Token token, List<Diagnostic> diags, PositionMap? pos)
    {
        string pascal = DotNetTypeRegistry.ToPascalCase(name);
        if (MemberExists(type, name, !isStatic))
        {
            string declared = isStatic ? "static" : "instance";
            string actual = isStatic ? "instance" : "static";
            Diagnostic diagnostic = Diagnostic.TypeError(
                $"@DotNetType '{type.FullName}': member '{name}' exists but is {actual}, not {declared} as declared.",
                Loc(token, pos));
            if (!isStatic)
            {
                diagnostic = WithReplacement(
                    diagnostic,
                    $"Make '{name}' static",
                    $"static {name}");
            }
            diags.Add(diagnostic);
        }
        else
        {
            Diagnostic diagnostic = Diagnostic.TypeError(
                $"@DotNetType '{type.FullName}': no {kind} '{name}' (nor PascalCase '{pascal}').",
                Loc(token, pos));
            string? suggestion = FindNearest(
                name,
                MemberCandidates(type, isStatic, kind, name));
            if (suggestion is not null)
            {
                diagnostic = WithReplacement(
                    diagnostic,
                    $"Change member to '{suggestion}'",
                    suggestion);
            }
            diags.Add(diagnostic);
        }
    }

    // Primitive aliases accepted in an @DotNetOverload hint. Mirrors the alias arm of
    // DotNetMethodResolver.ResolveHintType; ideally shared via an internal API rather than
    // duplicated (production follow-up). Anything not here is resolved via the injected
    // resolver, so fully-qualified names ("System.Guid") work in whatever context applies.
    private static readonly HashSet<string> HintAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "int32", "long", "int64", "short", "int16", "byte", "sbyte",
        "uint", "uint32", "ulong", "uint64", "ushort", "uint16",
        "float", "single", "double", "decimal", "bool", "boolean", "char", "string", "object"
    };

    /// <summary>Tier 3a: validate that every type named in an @DotNetOverload hint actually
    /// resolves. We validate the hint's TYPE NAMES only — not whether they match an overload.
    ///
    /// Why name-only: the runtime's overload-match step (<see cref="DotNetMethodResolver"/>)
    /// compares hint types to candidate parameter types by reference equality via in-process
    /// <c>typeof</c>, which is unsound when candidates come from a MetadataLoadContext (the
    /// production resolver) — cross-context types never compare equal, producing false
    /// positives. Validating names via the injected resolver is context-correct; overload
    /// *matching* belongs to Tier 3e (by type name, not identity).</summary>
    private void CheckOverloadHint(Stmt.Function m, Type type, List<Diagnostic> diags, PositionMap? pos)
    {
        var decorator = FindDecoratorStringArg(
            m.Decorators,
            "DotNetOverload");
        if (decorator is null) return;
        string hint = decorator.Value.Value;
        if (DotNetTypeRegistry.GetMethods(type, m.Name.Lexeme, m.IsStatic).Length == 0) return;

        string[] parts = hint.Split(
            ',',
            StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            if (HintAliases.Contains(part) || _resolve(part) != null) continue;
            string? suggestion = FindNearest(
                part,
                HintAliases.Concat(_typeNames?.Invoke() ?? []));
            Diagnostic diagnostic = Diagnostic.TypeError(
                $"@DotNetOverload(\"{hint}\") on '{m.Name.Lexeme}': unknown type '{part}' in hint.",
                suggestion is null
                    ? Loc(m.Name, pos)
                    : Loc(
                        decorator.Value.At,
                        decorator.Value.End,
                        pos));
            if (suggestion is not null)
            {
                string[] replacementParts = [.. parts];
                replacementParts[index] = suggestion;
                string replacementHint = string.Join(", ", replacementParts);
                diagnostic = WithReplacement(
                    diagnostic,
                    $"Change overload type to '{suggestion}'",
                    $"@DotNetOverload(\"{replacementHint}\")");
            }
            diags.Add(diagnostic);
        }
    }

    /// <summary>Tier 3c: a declared constructor needs a public instance constructor on the
    /// CLR type. Skips value types (structs are always constructible; GetConstructors omits
    /// the implicit default ctor).</summary>
    private static void CheckConstructor(Type type, Token token, List<Diagnostic> diags, PositionMap? pos)
    {
        if (type.IsValueType) return;
        if (type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0)
            diags.Add(Diagnostic.TypeError(
                $"@DotNetType '{type.FullName}': no public constructor (the type cannot be instantiated with 'new').",
                Loc(token, pos)));
    }

    private static (string? mapping, Token? at, Token? name, Token? end) FindDotNetType(Stmt.Class cls)
    {
        if (cls.Decorators == null) return (null, null, null, null);
        foreach (var d in cls.Decorators)
            if (d.Expression is Expr.Call { Callee: Expr.Variable v, Arguments: [Expr.Literal { Value: string typeName }] } call
                && v.Name.Lexeme == "DotNetType")
                return (typeName, d.AtToken, v.Name, call.Paren);
        return (null, null, null, null);
    }

    private static (string Value, Token At, Token End)?
        FindDecoratorStringArg(
            List<Decorator>? decorators,
            string name)
    {
        if (decorators == null) return null;
        foreach (var d in decorators)
            if (d.Expression is Expr.Call { Callee: Expr.Variable v, Arguments: [Expr.Literal { Value: string arg }] } call
                && v.Name.Lexeme == name)
                return (arg, d.AtToken, call.Paren);
        return null;
    }

    private string? ResolveDefaultImportExportName(string specifier)
    {
        try
        {
            Type? type =
                DotNetTypeRegistry.ResolveFriendly(specifier, _resolve);
            if (type is null)
                return null;
            int genericMarker = type.Name.IndexOf('`');
            return genericMarker < 0
                ? type.Name
                : type.Name[..genericMarker];
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> MemberCandidates(
        Type type,
        bool isStatic,
        string kind,
        string sourceName)
    {
        BindingFlags flags = BindingFlags.Public |
            (isStatic
                ? BindingFlags.Static | BindingFlags.FlattenHierarchy
                : BindingFlags.Instance);
        IEnumerable<string> names = string.Equals(
            kind,
            "method",
            StringComparison.Ordinal)
            ? type.GetMethods(flags)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
            : type.GetProperties(flags)
                .Select(property => property.Name)
                .Concat(type.GetFields(flags).Select(field => field.Name));

        bool lowerCamel =
            sourceName.Length > 0 && char.IsLower(sourceName[0]);
        return names
            .Select(name =>
                lowerCamel && name.Length > 0
                    ? char.ToLowerInvariant(name[0]) + name[1..]
                    : name)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindNearest(
        string source,
        IEnumerable<string> candidates)
    {
        int bestDistance = int.MaxValue;
        string? best = null;
        bool tied = false;
        foreach (string candidate in candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int distance = EditDistance(source, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
                tied = false;
            }
            else if (distance == bestDistance &&
                     !string.Equals(
                         best,
                         candidate,
                         StringComparison.OrdinalIgnoreCase))
            {
                tied = true;
            }
        }

        return bestDistance <= 2 && !tied ? best : null;
    }

    private static int EditDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = char.ToUpperInvariant(left[i - 1]) ==
                    char.ToUpperInvariant(right[j - 1])
                    ? 0
                    : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static Diagnostic WithReplacement(
        Diagnostic diagnostic,
        string title,
        string newText) =>
        diagnostic with
        {
            Properties = InteropCodeActionMetadata.Replacement(
                title,
                newText),
        };

    /// <summary>Tier 3d: finds addEventListener/removeEventListener calls whose receiver
    /// resolves (purely structurally) to a known @DotNetType, and checks arity + event name.
    /// Bails to no-diagnostic whenever the receiver type can't be resolved — false negatives,
    /// never false positives.</summary>
    private sealed class EventCallVisitor(IReadOnlyDictionary<string, Type> bindings, List<Diagnostic> diags, PositionMap? pos) : AstVisitorBase
    {
        protected override void VisitCall(Expr.Call call)
        {
            if (call.Callee is Expr.Get { Name.Lexeme: "addEventListener" or "removeEventListener" } g
                && ResolveReceiver(g.Object) is var (type, isStatic) && type is not null)
            {
                string op = g.Name.Lexeme;
                if (call.Arguments.Count < 2)
                {
                    diags.Add(Diagnostic.TypeError(
                        $"'{op}' on '@DotNetType {type.FullName}' requires (eventName, handler) — got {call.Arguments.Count} argument(s).",
                        Loc(g.Name, pos)));
                }
                else if (call.Arguments[0] is Expr.Literal { Value: string evName }
                         && DotNetTypeRegistry.GetEvent(type, evName, isStatic) == null)
                {
                    diags.Add(Diagnostic.TypeError(
                        $"Event '{evName}' not found on '@DotNetType {type.FullName}'.",
                        Loc(g.Name, pos)));
                }
            }

            base.VisitCall(call); // keep traversing nested expressions
        }

        // Structurally resolve a receiver to (CLR type, isStaticContext). Returns (null,false)
        // when it can't be determined — the caller treats that as "skip".
        private (Type? type, bool isStatic) ResolveReceiver(Expr e)
        {
            switch (e)
            {
                case Expr.Variable v when bindings.TryGetValue(v.Name.Lexeme, out var t):
                    return (t, true); // class name used as a static accessor
                case Expr.New { Callee: Expr.Variable cv } when bindings.TryGetValue(cv.Name.Lexeme, out var nt):
                    return (nt, false); // a freshly constructed instance
                case Expr.Get g:
                    var (rt, rStatic) = ResolveReceiver(g.Object);
                    if (rt is null) return (null, false);
                    return DotNetTypeRegistry.GetPropertyOrField(rt, g.Name.Lexeme, rStatic) switch
                    {
                        PropertyInfo p => (p.PropertyType, false),
                        FieldInfo f => (f.FieldType, false),
                        _ => (null, false)
                    };
                case Expr.Grouping grp: return ResolveReceiver(grp.Expression);
                case Expr.NonNullAssertion nn: return ResolveReceiver(nn.Expression);
                default: return (null, false);
            }
        }
    }
}
