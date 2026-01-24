using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using static SharpTS.TypeSystem.OperatorDescriptor;

namespace SharpTS.Compilation.Emitters.Operators;

/// <summary>
/// Operator emitter for numeric comparison operations (>, <, >=, <=).
/// </summary>
public sealed class ComparisonOperatorEmitter : IOperatorEmitter
{
    /// <summary>
    /// Comparison operations have high priority (25).
    /// </summary>
    public int Priority => 25;

    /// <inheritdoc/>
    public bool CanHandleBinary(IEmitterContext emitter, Expr.Binary binary)
    {
        var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);
        return desc is Comparison;
    }

    /// <inheritdoc/>
    public bool TryEmitBinary(IEmitterContext emitter, Expr.Binary binary)
    {
        var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);

        if (desc is not Comparison cmp)
            return false;

        var il = emitter.IL;
        var ctx = emitter.Context;

        // Emit both operands as doubles
        emitter.EmitExpressionAsDouble(binary.Left);
        emitter.EmitExpressionAsDouble(binary.Right);

        // Emit the comparison opcode
        il.Emit(cmp.Opcode);

        if (cmp.Negated)
        {
            // For <= and >=, we use the inverse opcode and negate
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
        }

        // Box the boolean result
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        emitter.SetStackUnknown();

        return true;
    }

    /// <inheritdoc/>
    public bool CanHandleUnary(IEmitterContext emitter, Expr.Unary unary)
    {
        // Comparison operations are binary only
        return false;
    }

    /// <inheritdoc/>
    public bool TryEmitUnary(IEmitterContext emitter, Expr.Unary unary)
    {
        return false;
    }
}
