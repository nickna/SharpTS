using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase

    #region Abstract Implementations

    protected override void EmitSuper(Expr.Super s)
    {
        // Not implemented in generator context - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Not implemented in generator context - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitUnknownExpression(Expr expr)
    {
        // Unsupported expression - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    #endregion
}
