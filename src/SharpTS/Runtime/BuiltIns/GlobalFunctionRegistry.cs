using SharpTS.Execution;
using SharpTS.Parsing;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Registry for global JavaScript functions (Symbol, BigInt, parseInt, setTimeout, etc.).
/// Provides centralized dispatch similar to BuiltInRegistry for namespace methods.
/// </summary>
public sealed class GlobalFunctionRegistry
{
    /// <summary>
    /// The singleton instance of the registry with all global functions registered.
    /// </summary>
    public static GlobalFunctionRegistry Instance { get; } = CreateDefault();

    /// <summary>
    /// V2 handler delegate for global functions (RuntimeValue — no boxing).
    /// </summary>
    public delegate ValueTask<RuntimeValue> GlobalFunctionHandlerV2(
        Func<Expr, ValueTask<RuntimeValue>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter);

    private readonly Dictionary<string, GlobalFunctionHandlerV2> _handlersV2 = new(StringComparer.Ordinal);

    private GlobalFunctionRegistry() { }

    /// <summary>
    /// Tries to get a V2 handler for a global function by name.
    /// </summary>
    public bool TryGetHandlerV2(string name, out GlobalFunctionHandlerV2? handler)
        => _handlersV2.TryGetValue(name, out handler);

    /// <summary>
    /// Registers a V2 handler for a global function.
    /// </summary>
    public void RegisterV2(string name, GlobalFunctionHandlerV2 handler)
        => _handlersV2[name] = handler;

    /// <summary>
    /// Creates the default registry with all built-in global functions registered.
    /// </summary>
    private static GlobalFunctionRegistry CreateDefault()
    {
        var registry = new GlobalFunctionRegistry();
        GlobalFunctionHandlers.RegisterAll(registry);
        return registry;
    }
}
