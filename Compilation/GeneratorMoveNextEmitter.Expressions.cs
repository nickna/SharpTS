using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase

    #region Abstract Implementations - Pass-through expressions

    protected override void EmitGrouping(Expr.Grouping g)
    {
        // Grouping just evaluates its inner expression
        EmitExpression(g.Expression);
    }

    protected override void EmitTypeAssertion(Expr.TypeAssertion ta)
    {
        // Type assertions are compile-time only, just emit the inner expression
        EmitExpression(ta.Expression);
    }

    protected override void EmitNonNullAssertion(Expr.NonNullAssertion nna)
    {
        // Non-null assertions are compile-time only, just emit the inner expression
        EmitExpression(nna.Expression);
    }

    protected override void EmitSpread(Expr.Spread sp)
    {
        // Spread expressions are handled in context (arrays, objects, calls)
        // If we get here directly, just emit the inner expression
        EmitExpression(sp.Expression);
    }

    protected override void EmitSuper(Expr.Super s)
    {
        // Not implemented in generator context - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitRegexLiteral(Expr.RegexLiteral re)
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
