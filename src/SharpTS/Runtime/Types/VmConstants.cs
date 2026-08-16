namespace SharpTS.Runtime.Types;

/// <summary>
/// Holds the process-wide sentinel values exported as <c>vm.constants</c>.
/// These are opaque, identity-comparable Symbols (matching Node, where they are
/// used as marker values):
/// <list type="bullet">
/// <item><c>USE_MAIN_CONTEXT_DEFAULT_LOADER</c> — passed as <c>importModuleDynamically</c>
/// to ask the default ESM loader to resolve dynamic imports (see vm #1156).</item>
/// <item><c>DONT_CONTEXTIFY</c> — passed as the context object to <c>vm.createContext()</c>
/// to create a context without contextifying a sandbox (see vm #1153).</item>
/// </list>
/// Being static singletons, they round-trip identically through interpreter and
/// compiled (reflection-delegated) execution.
/// </summary>
public static class VmConstants
{
    /// <summary>vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER</summary>
    public static readonly SharpTSSymbol UseMainContextDefaultLoader =
        new("vm.constants.USE_MAIN_CONTEXT_DEFAULT_LOADER");

    /// <summary>vm.constants.DONT_CONTEXTIFY</summary>
    public static readonly SharpTSSymbol DontContextify =
        new("vm.constants.DONT_CONTEXTIFY");

    /// <summary>
    /// Builds a fresh <c>vm.constants</c> object. Returned as a plain dictionary so
    /// both the interpreter (IDictionary property access) and compiled mode
    /// (GetFieldsProperty dictionary dispatch) can read its members.
    /// </summary>
    public static Dictionary<string, object?> Create()
    {
        return new Dictionary<string, object?>
        {
            ["USE_MAIN_CONTEXT_DEFAULT_LOADER"] = UseMainContextDefaultLoader,
            ["DONT_CONTEXTIFY"] = DontContextify,
        };
    }
}
