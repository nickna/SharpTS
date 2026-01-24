using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Properties;

/// <summary>
/// Strategy interface for emitting IL code for property get/set operations.
/// Each implementation handles a specific category of property access
/// (enum, static member, external type, etc.).
/// </summary>
public interface IPropertyEmitterStrategy
{
    /// <summary>
    /// Priority for strategy selection. Lower values are tried first.
    /// Default is 100.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// Determines if this strategy can handle a property get operation.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="get">The property get expression.</param>
    /// <returns>True if this strategy can handle the get operation.</returns>
    bool CanHandleGet(IEmitterContext emitter, Expr.Get get);

    /// <summary>
    /// Attempts to emit IL for a property get operation.
    /// Only called if CanHandleGet returns true.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="get">The property get expression.</param>
    /// <returns>True if the get was successfully emitted.</returns>
    bool TryEmitGet(IEmitterContext emitter, Expr.Get get);

    /// <summary>
    /// Determines if this strategy can handle a property set operation.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="set">The property set expression.</param>
    /// <returns>True if this strategy can handle the set operation.</returns>
    bool CanHandleSet(IEmitterContext emitter, Expr.Set set);

    /// <summary>
    /// Attempts to emit IL for a property set operation.
    /// Only called if CanHandleSet returns true.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="set">The property set expression.</param>
    /// <returns>True if the set was successfully emitted.</returns>
    bool TryEmitSet(IEmitterContext emitter, Expr.Set set);
}
