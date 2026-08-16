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
    private readonly List<ICallHandler> _handlers;

    /// <summary>
    /// Creates a new registry with the default set of handlers.
    /// </summary>
    public CallHandlerRegistry()
    {
        _handlers =
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
        ];

        // Sort by priority (lower = earlier)
        _handlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// Attempts to handle the call using registered handlers.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="call">The call expression to handle.</param>
    /// <returns>True if any handler handled the call.</returns>
    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
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
