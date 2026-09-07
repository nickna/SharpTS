using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Discovers writes to variable bindings while leaving property/index mutation policies to
/// consumers. Destructuring writes reach these methods through AstVisitorBase's traversal of
/// the parser's lowered assignments, including nested patterns, defaults, and rest targets.
/// </summary>
internal abstract class VariableWriteVisitor : AstVisitorBase
{
    protected abstract void OnVariableWrite(Token name);

    protected override void VisitAssign(Expr.Assign expression)
    {
        OnVariableWrite(expression.Name);
        base.VisitAssign(expression);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
    {
        OnVariableWrite(expression.Name);
        base.VisitCompoundAssign(expression);
    }

    protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
    {
        OnVariableWrite(expression.Name);
        base.VisitLogicalAssign(expression);
    }

    protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
    {
        if (expression.Operand is Expr.Variable variable)
            OnVariableWrite(variable.Name);
        base.VisitPrefixIncrement(expression);
    }

    protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
    {
        if (expression.Operand is Expr.Variable variable)
            OnVariableWrite(variable.Name);
        base.VisitPostfixIncrement(expression);
    }
}
