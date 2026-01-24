using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using static SharpTS.TypeSystem.OperatorDescriptor;

namespace SharpTS.Compilation.Emitters.Operators;

/// <summary>
/// Operator emitter for arithmetic binary operations (-, *, /, %).
/// </summary>
/// <remarks>
/// Note: The + operator is handled separately because it can be
/// either numeric addition or string concatenation.
/// </remarks>
public sealed class ArithmeticOperatorEmitter : IOperatorEmitter
{
    /// <summary>
    /// Arithmetic operations have high priority (20) as they are common.
    /// </summary>
    public int Priority => 20;

    /// <inheritdoc/>
    public bool CanHandleBinary(IEmitterContext emitter, Expr.Binary binary)
    {
        var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);
        return desc is Arithmetic;
    }

    /// <inheritdoc/>
    public bool TryEmitBinary(IEmitterContext emitter, Expr.Binary binary)
    {
        var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);

        if (desc is not Arithmetic arith)
            return false;

        var il = emitter.IL;
        var ctx = emitter.Context;

        // Emit left operand as double
        emitter.EmitExpressionAsDouble(binary.Left);

        // Emit right operand as double
        emitter.EmitExpressionAsDouble(binary.Right);

        // Emit the arithmetic opcode (Sub, Mul, Div, Rem)
        il.Emit(arith.Opcode);

        // Box the result
        il.Emit(OpCodes.Box, ctx.Types.Double);
        emitter.SetStackUnknown();

        return true;
    }

    /// <inheritdoc/>
    public bool CanHandleUnary(IEmitterContext emitter, Expr.Unary unary)
    {
        // Arithmetic operations are binary only
        return false;
    }

    /// <inheritdoc/>
    public bool TryEmitUnary(IEmitterContext emitter, Expr.Unary unary)
    {
        return false;
    }
}
