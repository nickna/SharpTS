using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Registry that manages and executes call handlers in priority order.
/// Implements Chain of Responsibility pattern for call emission.
/// </summary>
public class CallHandlerRegistry
{
    private readonly List<ICallHandler> _handlers;

    /// <summary>
    /// Creates a new registry with the default set of handlers.
    /// </summary>
    public CallHandlerRegistry()
    {
        _handlers =
        [
            new ObjectRestHandler(),         // Priority 15 - Internal helpers first
            new ConsoleMethodHandler(),      // Priority 20 - Console methods
            new StaticTypeHandler(),         // Priority 30 - Math, JSON, Object, Array, etc.
            new DateStaticHandler(),         // Priority 35 - Date.now()
            new BuiltInModuleHandler(),      // Priority 40 - path, os, fs modules
            new TimerHandler(),              // Priority 45 - setTimeout, clearTimeout
            new FetchHandler(),              // Priority 46 - fetch()
            new GlobalFunctionHandler(),     // Priority 50 - parseInt, parseFloat, isNaN, isFinite
            new BuiltInConstructorHandler(), // Priority 60 - Symbol, BigInt, Date()
        ];

        // Sort by priority (lower = earlier)
        _handlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Attempts to handle the call using registered handlers.
    /// </summary>
    /// <param name="emitter">The IL emitter instance.</param>
    /// <param name="call">The call expression to handle.</param>
    /// <returns>True if any handler handled the call.</returns>
    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        foreach (var handler in _handlers)
        {
            if (handler.TryHandle(emitter, call))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Registers a custom handler.
    /// </summary>
    public void Register(ICallHandler handler)
    {
        _handlers.Add(handler);
        _handlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }
}
