using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Function Compilation State
    // ============================================

    public Dictionary<string, MethodBuilder> Functions { get; }

    /// <summary>
    /// The $Program type where top-level functions are defined.
    /// Used for GetMethodFromHandle to properly resolve MethodBuilder tokens in persisted assemblies.
    /// </summary>
    public TypeBuilder? ProgramType { get; set; }

    // Rest parameter info: function name -> (restParamIndex, regularParamCount)
    // If a function has a rest param, restParamIndex is its index, regularParamCount is non-rest param count
    public Dictionary<string, (int RestParamIndex, int RegularParamCount)>? FunctionRestParams { get; set; }

    // Function overloads for default parameters: function name -> list of overload methods
    public Dictionary<string, List<MethodBuilder>>? FunctionOverloads { get; set; }

    // Method overloads for default parameters: class name -> method name -> list of overload methods
    public Dictionary<string, Dictionary<string, List<MethodBuilder>>>? MethodOverloads { get; set; }

    // Track generic params per function for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? FunctionGenericParams { get; set; }

    // Track which functions are generic definitions
    public Dictionary<string, bool>? IsGenericFunction { get; set; }

    /// <summary>
    /// Qualified names of functions whose body references JS <c>arguments</c> (directly,
    /// or through a nested arrow — arrows inherit their enclosing non-arrow's binding).
    /// Direct call sites to any of these must publish the caller's raw arg array to the
    /// <c>$TSFunction._currentArguments</c> thread-static before emitting <c>OpCodes.Call</c>,
    /// so the prologue can materialize <c>arguments</c> including values past the declared
    /// arity (lodash overRest pattern; #64). Indirect calls through <c>$TSFunction.Invoke</c>
    /// publish automatically — this set only matters for in-module direct dispatch.
    /// </summary>
    public HashSet<string>? FunctionsCapturingArguments { get; set; }

    /// <summary>
    /// Applies the namespace prefix to an already module-qualified function name. A namespace
    /// member function lives in its own registry slot keyed by the namespace path, so two
    /// namespaces declaring a same-named function (and a same-named module/global function) no
    /// longer collide on the simple/module name (#657). Kept distinct from the namespace FIELD
    /// name (<c>$ns_…</c>) and var-backing field (<c>$nsvar_…</c>) by its own <c>$nsfn_</c> prefix.
    /// </summary>
    private static string ApplyNamespacePrefix(string namespacePath, string baseName)
        => $"$nsfn_{namespacePath.Replace('.', '_')}_{baseName}";

    private string ModuleQualify(string simpleFunctionName)
        => CurrentModulePath == null
            ? simpleFunctionName
            : $"$M_{SanitizeModuleName(Path.GetFileNameWithoutExtension(CurrentModulePath))}_{simpleFunctionName}";

    /// <summary>
    /// Resolves a simple function name to its qualified name for lookup in the Functions dictionary.
    /// </summary>
    public string ResolveFunctionName(string simpleFunctionName)
    {
        // Namespace-scoped resolution: a function declared in the current namespace (or an
        // enclosing one) shadows a same-named module/global function. Walk innermost → outermost
        // and return the first namespace-qualified key that actually exists, so a sibling/self
        // call inside a namespace member body reaches its own namespace's function (#657). When
        // none matches (the name is a plain module/global function), fall through.
        if (CurrentNamespacePath != null)
        {
            string moduleBase = ModuleQualify(simpleFunctionName);
            var parts = CurrentNamespacePath.Split('.');
            for (int i = parts.Length; i >= 1; i--)
            {
                string nsPrefix = string.Join('.', parts.Take(i));
                string key = ApplyNamespacePrefix(nsPrefix, moduleBase);
                if (Functions.ContainsKey(key))
                    return key;
            }
        }

        if (FunctionToModule != null && FunctionToModule.TryGetValue(simpleFunctionName, out var modulePath))
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(modulePath));
            return $"$M_{sanitizedModule}_{simpleFunctionName}";
        }
        return simpleFunctionName;
    }

    /// <summary>
    /// Resolves a bare identifier to a namespace's static backing field. Namespace fields are keyed
    /// by full dotted path (e.g. <c>O.A</c>), so a nested/enclosing namespace referenced by its
    /// simple name from inside a namespace member body — <c>A</c> inside <c>O.B.f</c>, where
    /// <c>A</c> is <c>O.A</c> — does not match a direct lookup. Walks the current namespace path
    /// innermost → outermost (<c>O.B.A</c>, then <c>O.A</c>) and returns the first existing field,
    /// mirroring <see cref="ResolveFunctionName"/>'s namespace-scoped function resolution so a
    /// nested namespace can reference an enclosing namespace's namespace-typed member (#665). The
    /// namespace-scoped pass runs first so an inner namespace shadows a same-named top-level one;
    /// the direct lookup then covers a top-level namespace reference (or any reference outside a
    /// namespace). Returns null when no namespace matches.
    /// </summary>
    public FieldBuilder? ResolveNamespaceField(string name)
    {
        if (NamespaceFields == null)
            return null;

        if (CurrentNamespacePath != null)
        {
            var parts = CurrentNamespacePath.Split('.');
            for (int i = parts.Length; i >= 1; i--)
            {
                string nsPrefix = string.Join('.', parts.Take(i));
                if (NamespaceFields.TryGetValue($"{nsPrefix}.{name}", out var scoped))
                    return scoped;
            }
        }

        return NamespaceFields.TryGetValue(name, out var direct) ? direct : null;
    }

    /// <summary>
    /// Gets the qualified function name for the current module + namespace context. Module
    /// qualification (#418) keeps same-named functions in different modules distinct; namespace
    /// qualification (#657) keeps same-named members of different namespaces distinct. Both are
    /// gated: a plain top-level function in single-file compilation keeps its simple name.
    /// </summary>
    public string GetQualifiedFunctionName(string simpleFunctionName)
    {
        string baseName = ModuleQualify(simpleFunctionName);
        return CurrentNamespacePath == null
            ? baseName
            : ApplyNamespacePrefix(CurrentNamespacePath, baseName);
    }
}
