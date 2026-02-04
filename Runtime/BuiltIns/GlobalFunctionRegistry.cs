using SharpTS.Execution;
using SharpTS.Parsing;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Registry for global JavaScript functions (Symbol, BigInt, parseInt, setTimeout, etc.).
/// Provides centralized dispatch similar to BuiltInRegistry for namespace methods.
/// </summary>
/// <remarks>
/// This registry handles global functions that are called directly without a namespace prefix:
/// - Constructors callable without 'new': Symbol(), BigInt(), Date(), Error types
/// - Parsing functions: parseInt(), parseFloat()
/// - Type checking functions: isNaN(), isFinite()
/// - Utility functions: structuredClone()
/// - Timer functions: setTimeout(), clearTimeout(), setInterval(), clearInterval()
/// - Internal helpers: __objectRest
/// </remarks>
public sealed class GlobalFunctionRegistry
{
    /// <summary>
    /// The singleton instance of the registry with all global functions registered.
    /// </summary>
    public static GlobalFunctionRegistry Instance { get; } = CreateDefault();

    /// <summary>
    /// Handler delegate for global functions.
    /// </summary>
    /// <param name="evaluateArg">Function to evaluate a single argument expression.</param>
    /// <param name="arguments">The argument expressions (unevaluated).</param>
    /// <param name="interpreter">The interpreter instance.</param>
    /// <returns>The function result.</returns>
    public delegate ValueTask<object?> GlobalFunctionHandler(
        Func<Expr, ValueTask<object?>> evaluateArg,
        IReadOnlyList<Expr> arguments,
        Interpreter interpreter);

    private readonly Dictionary<string, GlobalFunctionHandler> _handlers = new(StringComparer.Ordinal);

    private GlobalFunctionRegistry() { }

    /// <summary>
    /// Tries to get a handler for a global function by name.
    /// </summary>
    /// <param name="name">The function name (e.g., "Symbol", "parseInt")</param>
    /// <param name="handler">The handler if found</param>
    /// <returns>True if a handler exists for this function name</returns>
    public bool TryGetHandler(string name, out GlobalFunctionHandler? handler)
        => _handlers.TryGetValue(name, out handler);

    /// <summary>
    /// Registers a handler for a global function.
    /// </summary>
    /// <param name="name">The function name</param>
    /// <param name="handler">The handler to invoke when the function is called</param>
    public void Register(string name, GlobalFunctionHandler handler)
        => _handlers[name] = handler;

    /// <summary>
    /// Creates the default registry with all built-in global functions registered.
    /// </summary>
    private static GlobalFunctionRegistry CreateDefault()
    {
        var registry = new GlobalFunctionRegistry();

        // Register all global functions
        GlobalFunctionHandlers.RegisterAll(registry);

        return registry;
    }
}
