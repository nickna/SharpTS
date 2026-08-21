using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

internal static class ParameterAssignmentAnalyzer
{
    public static HashSet<string> FindAssigned(IEnumerable<Stmt> body)
    {
        var visitor = new AssignmentVisitor();
        foreach (var statement in body)
            visitor.Visit(statement);
        return visitor.Assigned;
    }

    public static HashSet<string> FindAssigned(Expr expression)
    {
        var visitor = new AssignmentVisitor();
        visitor.Visit(expression);
        return visitor.Assigned;
    }

    private sealed class AssignmentVisitor : AstVisitorBase
    {
        public HashSet<string> Assigned { get; } = new(StringComparer.Ordinal);

        protected override void VisitAssign(Expr.Assign expr)
        {
            Assigned.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Assigned.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            Assigned.Add(expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (expr.Operand is Expr.Variable variable)
                Assigned.Add(variable.Name.Lexeme);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (expr.Operand is Expr.Variable variable)
                Assigned.Add(variable.Name.Lexeme);
            base.VisitPostfixIncrement(expr);
        }
    }
}
