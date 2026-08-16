using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Namespace compilation methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Namespace-level var/let/const backing fields: namespace path -> var name -> static field.
    /// See <see cref="CompilationContext.NamespaceVarFields"/> for the rationale (#567).
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _namespaceVarFields = [];

    /// <summary>
    /// The namespace path whose members are currently being defined or emitted, or null when
    /// not inside a namespace. Set by <see cref="DefineNamespaceFields"/> (define phase) and
    /// <see cref="EmitNamespaceMemberBodies"/> (emit phase) so a namespace member body can
    /// surface its enclosing namespaces' var fields to its resolver (#567). Read by
    /// <see cref="BuildTopLevelStaticVarsForModule"/>, which augments the static-var view with
    /// those fields whenever it is non-null.
    /// </summary>
    private string? _currentNamespacePath;

    /// <summary>
    /// Namespace function import aliases (<c>import alias = Target.member</c> where the member is
    /// a function), collected during the define phase as (consumer namespace path, alias stmt)
    /// and resolved in <see cref="ResolveNamespaceImportAliasFunctions"/> after every namespace is
    /// defined. Deferring lets the alias point at a sibling namespace's function declared later in
    /// source. Once namespace member functions are namespace-qualified (#657) the alias can no
    /// longer ride the simple-name registry, so it is explicitly aliased to the target's builder.
    /// </summary>
    private readonly List<(string ConsumerPath, Stmt.ImportAlias Alias)> _pendingNamespaceImportAliasFunctions = [];

    /// <summary>
    /// Qualified state-machine function name -> enclosing namespace path, recorded at define
    /// time for generators / async / async-generators declared in a namespace. Their MoveNext
    /// bodies are emitted in dedicated later phases (<see cref="EmitGeneratorStateMachineBodies"/>
    /// et al.) that iterate by qualified name, long after <see cref="_currentNamespacePath"/> has
    /// been cleared — so those phases restore it from this map to keep namespace var resolution
    /// working for state-machine members (#567). Plain sync functions and class methods don't
    /// need this: they are emitted inline while <see cref="_currentNamespacePath"/> is still live.
    /// </summary>
    private readonly Dictionary<string, string> _functionDefinitionNamespace = [];

    /// <summary>
    /// Defines static fields for a namespace and its nested namespaces.
    /// Called during the definition phase to create module-level fields.
    /// </summary>
    private void DefineNamespaceFields(Stmt.Namespace ns, string parentPath = "")
    {
        string path = string.IsNullOrEmpty(parentPath)
            ? ns.Name.Lexeme
            : $"{parentPath}.{ns.Name.Lexeme}";

        // Create static field for this namespace if it doesn't exist
        if (!_namespaceFields.ContainsKey(path))
        {
            var field = _programType.DefineField(
                $"$ns_{path.Replace(".", "_")}",
                _runtime.TSNamespaceType,
                FieldAttributes.Public | FieldAttributes.Static);
            _namespaceFields[path] = field;
        }

        // Record the enclosing namespace during the define phase too: DefineFunction routes
        // generators/async/async-generators through RegisterStateMachineFunctionModule, which
        // captures _currentNamespacePath into _functionDefinitionNamespace so the later
        // state-machine emission phases can restore it (#567). Saved/restored for nesting.
        var savedNamespacePath = _currentNamespacePath;
        _currentNamespacePath = path;
        try
        {
            // Define namespace members (functions, classes, enums, nested namespaces)
            foreach (var member in ns.Members)
            {
                var actualMember = member;
                // Unwrap export
                if (member is Stmt.Export { Declaration: not null } exp)
                {
                    actualMember = exp.Declaration;
                }

                switch (actualMember)
                {
                    case Stmt.Namespace nested:
                        DefineNamespaceFields(nested, path);
                        break;

                    case Stmt.Function funcStmt when funcStmt.Body != null:
                        // Define functions inside namespace
                        DefineFunction(funcStmt);
                        break;

                    case Stmt.Class classStmt:
                        // Define classes inside namespace
                        DefineClass(classStmt);
                        break;

                    case Stmt.Enum enumStmt:
                        // Define enums inside namespace
                        DefineEnum(enumStmt);
                        break;

                    // A namespace-level variable needs a static backing field so functions
                    // declared in the namespace can resolve the bare name (#567).
                    case Stmt.Var varStmt:
                        DefineNamespaceVarField(path, varStmt.Name.Lexeme);
                        break;

                    case Stmt.Const constStmt:
                        DefineNamespaceVarField(path, constStmt.Name.Lexeme);
                        break;

                    // Function import aliases are resolved after every namespace is defined so the
                    // target (possibly in a sibling namespace declared later) is already in the
                    // function registry. See _pendingNamespaceImportAliasFunctions (#657).
                    case Stmt.ImportAlias importAlias:
                        _pendingNamespaceImportAliasFunctions.Add((path, importAlias));
                        break;
                }
            }
        }
        finally
        {
            _currentNamespacePath = savedNamespacePath;
        }
    }

    /// <summary>
    /// Resolves namespace function import aliases collected during the define phase: for each
    /// <c>import alias = Target.member</c> whose target is a namespace member function, aliases the
    /// consumer namespace's qualified key to the target's <see cref="System.Reflection.Emit.MethodBuilder"/>
    /// (plus its rest/overload metadata) so a call to <c>alias()</c> inside the consumer
    /// namespace's bodies dispatches to the target. Runs after all namespaces are defined (#657).
    /// </summary>
    private void ResolveNamespaceImportAliasFunctions()
    {
        foreach (var (consumerPath, importAlias) in _pendingNamespaceImportAliasFunctions)
        {
            var qpath = importAlias.QualifiedPath;
            if (qpath.Count < 2)
                continue; // need at least Namespace.member to denote a namespace function

            string targetNsPath = string.Join(".", qpath.Take(qpath.Count - 1).Select(t => t.Lexeme));
            string targetMember = qpath[^1].Lexeme;

            // Qualify the target member under the target namespace, and the alias name under the
            // consumer namespace, reusing GetQualifiedFunctionName via the definition context.
            var savedNs = _currentNamespacePath;
            _currentNamespacePath = targetNsPath;
            string targetKey = GetDefinitionContext().GetQualifiedFunctionName(targetMember);
            _currentNamespacePath = consumerPath;
            string aliasKey = GetDefinitionContext().GetQualifiedFunctionName(importAlias.AliasName.Lexeme);
            _currentNamespacePath = savedNs;

            if (_functions.Builders.TryGetValue(targetKey, out var targetBuilder)
                && !_functions.Builders.ContainsKey(aliasKey))
            {
                _functions.Builders[aliasKey] = targetBuilder;
                if (_functions.RestParams.TryGetValue(targetKey, out var rp))
                    _functions.RestParams[aliasKey] = rp;
                if (_functions.Overloads.TryGetValue(targetKey, out var ov))
                    _functions.Overloads[aliasKey] = ov;
            }
        }
    }

    /// <summary>
    /// Defines (once) the static backing field for a namespace-level variable.
    /// </summary>
    private void DefineNamespaceVarField(string nsPath, string varName)
    {
        if (!_namespaceVarFields.TryGetValue(nsPath, out var fields))
        {
            fields = [];
            _namespaceVarFields[nsPath] = fields;
        }
        if (!fields.ContainsKey(varName))
        {
            fields[varName] = _programType.DefineField(
                $"$nsvar_{nsPath.Replace(".", "_")}_{varName}",
                _types.Object,
                FieldAttributes.Public | FieldAttributes.Static);
        }
    }

    /// <summary>
    /// Returns module top-level static vars augmented with the static backing fields of every
    /// namespace enclosing <paramref name="nsPath"/> (innermost wins). Used to make a namespace
    /// function body resolve bare references to namespace-level variables (#567).
    /// </summary>
    private Dictionary<string, FieldBuilder>? BuildNamespaceScopedStaticVars(
        Dictionary<string, FieldBuilder>? moduleVars, string nsPath)
    {
        var merged = moduleVars != null
            ? new Dictionary<string, FieldBuilder>(moduleVars)
            : [];

        // Walk outermost → innermost ("N", then "N.M") so an inner namespace's binding
        // shadows an outer one, and any namespace binding shadows a same-named module var.
        string prefix = "";
        foreach (var part in nsPath.Split('.'))
        {
            prefix = prefix.Length == 0 ? part : $"{prefix}.{part}";
            if (_namespaceVarFields.TryGetValue(prefix, out var fields))
            {
                foreach (var (name, field) in fields)
                    merged[name] = field;
            }
        }

        return merged;
    }

    /// <summary>
    /// Emits method bodies for functions and classes inside a namespace.
    /// Called during Phase 7 (method body emission).
    /// </summary>
    private void EmitNamespaceMemberBodies(Stmt.Namespace ns, string parentPath = "")
    {
        string path = string.IsNullOrEmpty(parentPath)
            ? ns.Name.Lexeme
            : $"{parentPath}.{ns.Name.Lexeme}";

        // Record the enclosing namespace so EmitFunctionBody can surface this namespace's
        // var fields to the function's resolver (#567). Saved/restored to support nesting.
        var savedNamespacePath = _currentNamespacePath;
        _currentNamespacePath = path;
        try
        {
            foreach (var member in ns.Members)
            {
                var actualMember = member;
                // Unwrap export
                if (member is Stmt.Export { Declaration: not null } exp)
                {
                    actualMember = exp.Declaration;
                }

                switch (actualMember)
                {
                    case Stmt.Namespace nested:
                        EmitNamespaceMemberBodies(nested, path);
                        break;

                    case Stmt.Function funcStmt when funcStmt.Body != null:
                        EmitFunctionBody(funcStmt);
                        break;

                    case Stmt.Class classStmt:
                        EmitClassMethods(classStmt);
                        break;
                }
            }
        }
        finally
        {
            _currentNamespacePath = savedNamespacePath;
        }
    }

    /// <summary>
    /// Initializes all namespace fields at the start of Main().
    /// Must be called before any namespace member access.
    /// </summary>
    private void InitializeNamespaceFields(ILGenerator il)
    {
        // Initialize namespace fields ordered by nesting depth (parents first)
        // This ensures parent namespaces exist before children are added
        foreach (var (nsPath, field) in _namespaceFields.OrderBy(kv => kv.Key.Count(c => c == '.')))
        {
            // Get simple name (last part of path)
            string simpleName = nsPath.Contains('.')
                ? nsPath[(nsPath.LastIndexOf('.') + 1)..]
                : nsPath;

            // new $TSNamespace(simpleName)
            il.Emit(OpCodes.Ldstr, simpleName);
            il.Emit(OpCodes.Newobj, _runtime.TSNamespaceCtor);
            il.Emit(OpCodes.Stsfld, field);

            // If nested, add to parent: parent.Set(childName, child)
            int dotIndex = nsPath.LastIndexOf('.');
            if (dotIndex > 0)
            {
                string parentPath = nsPath[..dotIndex];
                if (_namespaceFields.TryGetValue(parentPath, out var parentField))
                {
                    il.Emit(OpCodes.Ldsfld, parentField);
                    il.Emit(OpCodes.Ldstr, simpleName);
                    il.Emit(OpCodes.Ldsfld, field);
                    il.Emit(OpCodes.Call, _runtime.TSNamespaceSet);
                }
            }
        }
    }
}
