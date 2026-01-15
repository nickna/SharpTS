using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Interface for handling specific types of function calls in IL emission.
/// Implements Chain of Responsibility pattern to simplify EmitCall.
/// </summary>
public interface ICallHandler
{
    /// <summary>
    /// Attempts to handle the call expression. Returns true if handled, false to try next handler.
    /// </summary>
    /// <param name="emitter">The IL emitter instance for emitting IL code.</param>
    /// <param name="call">The call expression to handle.</param>
    /// <returns>True if this handler handled the call, false to try next handler.</returns>
    bool TryHandle(ILEmitter emitter, Expr.Call call);

    /// <summary>
    /// Priority order for handler execution. Lower values run first.
    /// Default handlers (fallback) should use high values like 1000.
    /// </summary>
    int Priority => 100;
}
