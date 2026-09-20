using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Registry that manages and executes call handlers in priority order.
/// Implements Chain of Responsibility pattern for call emission.
/// Works with IEmitterContext so it can be used by all emitter types.
/// </summary>
public class CallHandlerRegistry
{
    private readonly List<ICallHandler> _handlers = [];

    public bool IsComplete { get; private set; }

    /// <summary>
    /// Creates a new registry with the default set of handlers.
    /// </summary>
    public CallHandlerRegistry() : this(
        [
            new SuperConstructorHandler(),   // Priority 10 - super() calls
            new ObjectRestHandler(),         // Priority 15 - Internal helpers first
            new ArrayDestructureHandler(),   // Priority 16 - Internal helper (#685)
            new ConsoleMethodHandler(),      // Priority 20 - Console methods
            new StaticTypeHandler(),         // Priority 30 - Math, JSON, Object, Array, etc.
            new GlobalThisChainHandler(),    // Priority 32 - globalThis.X.Y()
            new DateStaticHandler(),         // Priority 35 - Date.now()
            new BuiltInModuleHandler(),      // Priority 40 - path, os, fs modules
            new ProcessStreamHandler(),      // Priority 43 - process.stdin/stdout/stderr
            new TimerHandler(),              // Priority 45 - setTimeout, clearTimeout
            new CookieJarHandler(),          // Priority 44 - fetch.cookieJar.{getCookies,setCookie,clear}
            new FetchHandler(),              // Priority 46 - fetch()
            new GlobalFunctionHandler(),     // Priority 50 - parseInt, parseFloat, isNaN, isFinite
            new BuiltInConstructorHandler(), // Priority 60 - Symbol, BigInt, Date()
            new ImportedClassStaticHandler(),// Priority 72 - imported class statics
            new ClassExprStaticHandler(),    // Priority 74 - class expression statics
            new ThisStaticContextHandler(),  // Priority 76 - this.method() in static context
            new AsyncFunctionCallHandler(),  // Priority 80 - async function dispatch
        ])
    {
    }

    /// <summary>Creates a registry with a checked snapshot of the supplied handlers.</summary>
    public CallHandlerRegistry(IEnumerable<ICallHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        foreach (var handler in handlers)
            Register(handler);
    }

    /// <summary>Creates the completed, stateless default dispatch chain.</summary>
    public static CallHandlerRegistry CreateDefault()
    {
        var registry = new CallHandlerRegistry();
        registry.CompleteRegistration();
        return registry;
    }

    /// <summary>Freezes handler membership and order before dispatch begins.</summary>
    public void CompleteRegistration()
    {
        EnsureRegistrationOpen();
        IsComplete = true;
    }

    private void EnsureRegistrationOpen()
    {
        if (IsComplete)
            throw new InvalidOperationException("Call handler registration is complete.");
    }

    /// <summary>
    /// Attempts to handle the call using registered handlers.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="call">The call expression to handle.</param>
    /// <returns>True if any handler handled the call.</returns>
    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (!IsComplete)
            throw new InvalidOperationException("Call handler registration must complete before dispatch.");

        foreach (var handler in _handlers)
        {
            if (handler.TryHandle(emitter, call))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Registers a custom handler before completion. Equal priorities retain registration order.
    /// </summary>
    public void Register(ICallHandler handler)
    {
        EnsureRegistrationOpen();
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.Any(existing => ReferenceEquals(existing, handler)))
            throw new InvalidOperationException("The call handler is already registered.");

        int index = _handlers.FindIndex(existing => existing.Priority > handler.Priority);
        if (index < 0)
            _handlers.Add(handler);
        else
            _handlers.Insert(index, handler);
    }
}
