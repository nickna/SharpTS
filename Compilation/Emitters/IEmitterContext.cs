using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Abstraction over IL emitters that allows type emitter strategies to work with
/// both ILEmitter (sync code) and AsyncMoveNextEmitter (async state machine code).
/// </summary>
public interface IEmitterContext
{
    /// <summary>
    /// Gets the compilation context containing types, runtime methods, and other compilation state.
    /// </summary>
    CompilationContext Context { get; }

    /// <summary>
    /// Emits IL for an expression, leaving the result on the evaluation stack.
    /// </summary>
    /// <param name="expr">The expression to emit.</param>
    void EmitExpression(Expr expr);

    /// <summary>
    /// Boxes the value on the stack if the expression type requires it.
    /// Value types (numbers, booleans) need boxing to become object references.
    /// </summary>
    /// <param name="expr">The expression whose result may need boxing.</param>
    void EmitBoxIfNeeded(Expr expr);

    /// <summary>
    /// Emits an expression and ensures the result is an unboxed double on the stack.
    /// Used when a numeric value is required (e.g., Date setter arguments).
    /// </summary>
    /// <param name="expr">The expression to emit as a double.</param>
    void EmitExpressionAsDouble(Expr expr);
}
