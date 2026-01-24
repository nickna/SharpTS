using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Operators;

/// <summary>
/// Strategy interface for emitting IL code for operators.
/// Each implementation handles a category of operators
/// (arithmetic, comparison, equality, bitwise, etc.).
/// </summary>
public interface IOperatorEmitter
{
    /// <summary>
    /// Priority for strategy selection. Lower values are tried first.
    /// Default is 100.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// Determines if this emitter can handle the given binary expression.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="binary">The binary expression.</param>
    /// <returns>True if this emitter can handle the expression.</returns>
    bool CanHandleBinary(IEmitterContext emitter, Expr.Binary binary);

    /// <summary>
    /// Attempts to emit IL for a binary expression.
    /// Only called if CanHandleBinary returns true.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="binary">The binary expression.</param>
    /// <returns>True if the expression was successfully emitted.</returns>
    bool TryEmitBinary(IEmitterContext emitter, Expr.Binary binary);

    /// <summary>
    /// Determines if this emitter can handle the given unary expression.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="unary">The unary expression.</param>
    /// <returns>True if this emitter can handle the expression.</returns>
    bool CanHandleUnary(IEmitterContext emitter, Expr.Unary unary);

    /// <summary>
    /// Attempts to emit IL for a unary expression.
    /// Only called if CanHandleUnary returns true.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="unary">The unary expression.</param>
    /// <returns>True if the expression was successfully emitted.</returns>
    bool TryEmitUnary(IEmitterContext emitter, Expr.Unary unary);
}
