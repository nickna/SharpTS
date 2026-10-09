using System.Reflection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Documentation;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.DotNet;
using SharpTS.TypeSystem;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Hover for .NET members reached through <c>@DotNetType</c> (Phase 4a). Shows the *real* CLR
/// member — its overloads, signatures, and XML doc — which the built-in TS server cannot,
/// since it only sees the TypeScript <c>declare</c> surface.
///
/// Two cases, both token-based for position detection:
/// <list type="bullet">
/// <item><b>Declaration</b> — cursor on a member declared inside a <c>@DotNetType</c> class.
/// Resolved purely from tokens + reflection (no type check).</item>
/// <item><b>Usage</b> — cursor on a member access (<c>sb.append</c>) in code. Uses the type
/// checker's <see cref="TypeMap"/> to resolve the receiver to its <see cref="TypeInfo.ExternalDotNetType"/>.</item>
/// </list>
/// </summary>
public sealed class MemberHoverService
{
    private readonly Func<string, Type?> _resolve;
    private readonly XmlDocLoader _xmlDoc = new();

    public MemberHoverService(Func<string, Type?>? resolve = null)
        => _resolve = resolve ?? DotNetTypeRegistry.Resolve;

    public Hover? Hover(string text, int line, int character, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MayHaveBindings(text)) return null;
        var statements = ParseInterop(text, cancellationToken);
        if (statements is null) return null;
        var pos = new PositionMap(text);

        var bindings = CollectBindings(statements);
        return DeclarationHover(statements, pos, line, character)
            ?? (bindings.Count == 0 ? null : UsageHover(statements, pos, line, character, bindings, cancellationToken));
    }

    /// <summary>The existing CLR declaration priority needs parsing, but no semantic check.</summary>
    internal Hover? DeclarationHover(string text, int line, int character, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!text.Contains("DotNetType", StringComparison.Ordinal)) return null;
        var statements = ParseInterop(text, cancellationToken);
        return statements is null ? null : DeclarationHover(statements, new PositionMap(text), line, character);
    }

    private static List<Stmt>? ParseInterop(string text, CancellationToken cancellationToken)
    {
        try
        {
            var tokens = new Lexer(text).WithCancellation(cancellationToken).ScanTokens();
            var parsed = new Parser(tokens, DecoratorMode.Stage3).WithCancellation(cancellationToken).Parse();
            return parsed.IsSuccess ? parsed.Statements : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>Full-mode usage hover reuses the captured AST and a final successful receiver.</summary>
    internal Hover? Hover(CheckedNavigationModel model, int offset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (model.Document.EditorSyntax?.FindMember(offset) is not { IsRecovered: false, IsIndex: false } member ||
            !member.Name.Span.Contains(offset)) return null;
        var occurrence = model.EditorFacts.GetOccurrence(model.Document, member.Receiver);
        if (occurrence is not { Availability: EditorFactAvailability.Available, Type.IsAvailable: true }) return null;
        TypeInfo? type = model.Snapshot.GetType(member.Receiver);
        Type? clr = null;
        if (type is TypeInfo.ExternalDotNetType external)
            clr = ResolveExternal(external, new Dictionary<string, Type>());
        else if (type is not null && DotNetTypeSynthesizer.TryGetClrType(type, out var imported))
            clr = imported;
        else if (type is TypeInfo.Class @class && DotNetTypeSynthesizer.TryGetClrType(new TypeInfo.Instance(@class), out var staticImported))
            clr = staticImported;
        else if (SourceClassId(type) is var classId && classId != 0 &&
            model.Members.GetClassInfo(classId)?.Owner is Stmt.Class declaration)
            clr = DotNetTypeOf(declaration);
        if (clr is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var hover = Render(RenderMember(clr, member.Name.Lexeme));
        if (hover is null) return null;
        var (startLine, startColumn) = model.Document.Lines.ToPosition(member.Name.Start);
        var (endLine, endColumn) = model.Document.Lines.ToPosition(member.Name.End);
        return new Hover
        {
            Contents = hover.Contents,
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                startLine - 1, startColumn - 1, endLine - 1, endColumn - 1),
        };
    }

    private static bool MayHaveBindings(string text) => text.Contains("DotNetType", StringComparison.Ordinal) ||
        text.Contains("dotnet:", StringComparison.Ordinal);

    private static int SourceClassId(TypeInfo? type)
    {
        for (int depth = 0; depth < 32; depth++)
            switch (type)
            {
                case TypeInfo.Instance instance: type = instance.ResolvedClassType; break;
                case TypeInfo.InstantiatedGeneric instantiated: type = instantiated.GenericDefinition; break;
                case TypeInfo.Class @class: return @class.Core.DeclarationId;
                case TypeInfo.GenericClass generic: return generic.Core.DeclarationId;
                default: return 0;
            }
        return 0;
    }

    // TS class name -> CLR type, for every @DotNetType declaration and dotnet: import
    // in the file. Imports are keyed under both the local binding name (new SB()) and the
    // CLR simple name — the checker's synthesized class carries the CLR name, so usage
    // hovers on receivers resolve through it.
    private Dictionary<string, Type> CollectBindings(IReadOnlyList<Stmt> statements)
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var s in statements)
        {
            if (s is Stmt.Class cls && DotNetTypeOf(cls) is { } t)
                map[cls.Name.Lexeme] = t;

            if (s is Stmt.Import import
                && SharpTS.Modules.DotNetImports.IsDotNetSpecifier(import.ModulePath)
                && import.NamedImports != null)
            {
                string specifier = import.ModulePath[SharpTS.Modules.DotNetImports.Prefix.Length..];
                foreach (var spec in import.NamedImports)
                {
                    try
                    {
                        var type = SharpTS.Modules.DotNetImports.ResolveExportType(
                            specifier, spec.Imported.Lexeme, _resolve);
                        map[spec.LocalName?.Lexeme ?? spec.Imported.Lexeme] = type;
                        map.TryAdd(type.Name, type);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch
                    {
                        // Unresolvable import — no hover; InteropAnalyzer reports the diagnostic.
                    }
                }
            }
        }
        return map;
    }

    // --- Declaration: cursor on a member name inside a @DotNetType class ---
    private Hover? DeclarationHover(IReadOnlyList<Stmt> statements, PositionMap pos, int line, int ch)
    {
        foreach (var stmt in statements)
        {
            if (stmt is not Stmt.Class cls) continue;
            var clrType = DotNetTypeOf(cls);
            if (clrType is null) continue;

            foreach (var m in cls.Methods)
                if (m.Name.Lexeme != "constructor" && pos.Contains(m.Name, line, ch))
                    return Render(RenderMember(clrType, m.Name.Lexeme));
            foreach (var f in cls.Fields)
                if (pos.Contains(f.Name, line, ch))
                    return Render(RenderMember(clrType, f.Name.Lexeme));
        }
        return null;
    }

    // --- Usage: cursor on a member-access name; resolve the receiver via the TypeMap ---
    private Hover? UsageHover(List<Stmt> statements, PositionMap pos, int line, int ch, IReadOnlyDictionary<string, Type> bindings,
        CancellationToken cancellationToken)
    {
        var finder = new GetFinder(pos, line, ch);
        foreach (var stmt in statements) finder.Visit(stmt);
        if (finder.Found is null) return null;

        TypeMap typeMap;
        try
        {
            var checker = new TypeChecker().WithCancellation(cancellationToken);
            checker.SetDecoratorMode(DecoratorMode.Stage3);
            typeMap = checker.CheckWithRecovery(statements).TypeMap;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null; // a half-typed buffer shouldn't surface hover errors
        }

        var clrType = ResolveExternal(typeMap.Get(finder.Found.Object), bindings);
        if (clrType is null) return null;
        return Render(RenderMember(clrType, finder.Found.Name.Lexeme));
    }

    private Type? DotNetTypeOf(Stmt.Class cls)
    {
        if (cls.Decorators is null) return null;
        foreach (var d in cls.Decorators)
            if (d.Expression is Expr.Call { Callee: Expr.Variable { Name.Lexeme: "DotNetType" }, Arguments: [Expr.Literal { Value: string clr }] })
            {
                try { return DotNetTypeRegistry.ResolveFriendly(clr, _resolve); }
                catch (ArgumentException) { return null; }
            }
        return null;
    }

    // Instantiated @DotNetType classes are ordinary Instance(Class) values — map the class
    // name back to the binding. (A direct ExternalDotNetType is also handled, just in case.)
    private Type? ResolveExternal(TypeInfo? ti, IReadOnlyDictionary<string, Type> bindings)
    {
        if (ti is TypeInfo.ExternalDotNetType ext)
        {
            try { return DotNetTypeRegistry.ResolveFriendly(ext.ClrTypeName, _resolve) ?? ext.ResolvedType; }
            catch (ArgumentException) { return ext.ResolvedType; }
        }
        var name = ClassName(ti);
        return name is not null && bindings.TryGetValue(name, out var t) ? t : null;
    }

    private static string? ClassName(TypeInfo? ti) => ti switch
    {
        TypeInfo.Instance inst => ClassName(inst.ClassType),
        TypeInfo.Class c => c.Name,
        TypeInfo.MutableClass mc => mc.Name,
        TypeInfo.ExternalDotNetType ext => ext.TypeScriptName,
        _ => null
    };

    private string? RenderMember(Type type, string jsName)
    {
        var methods = DotNetTypeRegistry.GetMethods(type, jsName, false)
            .Concat(DotNetTypeRegistry.GetMethods(type, jsName, true))
            .GroupBy(m => m.ToString()).Select(g => g.First()).ToArray();

        if (methods.Length > 0)
        {
            var shown = methods.Take(10).Select(m => FormatMethod(m, type));
            string md = "```csharp\n" + string.Join("\n", shown) + "\n```";
            if (methods.Length > 10) md += $"\n\n_+{methods.Length - 10} more overload(s)_";
            var doc = _xmlDoc.GetMethodSummary(type, methods[0].Name);
            if (!string.IsNullOrWhiteSpace(doc)) md += $"\n\n{doc}";
            return md;
        }

        var member = DotNetTypeRegistry.GetPropertyOrField(type, jsName, false)
                  ?? DotNetTypeRegistry.GetPropertyOrField(type, jsName, true);
        switch (member)
        {
            case PropertyInfo p:
                string pmd = $"```csharp\n{Short(p.PropertyType)} {type.Name}.{p.Name}\n```";
                var pdoc = _xmlDoc.GetPropertySummary(type, p.Name);
                if (!string.IsNullOrWhiteSpace(pdoc)) pmd += $"\n\n{pdoc}";
                return pmd;
            case FieldInfo f:
                return $"```csharp\n{Short(f.FieldType)} {type.Name}.{f.Name}\n```";
            default:
                return null;
        }
    }

    private static string FormatMethod(MethodInfo m, Type owner)
    {
        var ps = string.Join(", ", m.GetParameters().Select(p => $"{Short(p.ParameterType)} {p.Name}"));
        return $"{Short(m.ReturnType)} {owner.Name}.{m.Name}({ps})";
    }

    private static string Short(Type t) => t.Name;

    private static Hover? Render(string? markdown) => markdown is null ? null : new Hover
    {
        Contents = new MarkedStringsOrMarkupContent(
            new MarkupContent { Kind = MarkupKind.Markdown, Value = markdown })
    };

    // Finds the first member-access Get whose name token sits under the cursor.
    private sealed class GetFinder(PositionMap pos, int line, int ch) : AstVisitorBase
    {
        public Expr.Get? Found;

        protected override void VisitGet(Expr.Get expr)
        {
            if (Found is null && pos.Contains(expr.Name, line, ch)) Found = expr;
            base.VisitGet(expr);
        }
    }
}
