using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes loop bodies to identify array variables whose isinst type check
/// can be hoisted out of the loop. A variable is hoistable when:
/// - It appears as the receiver of GetIndex, SetIndex, or .length access
/// - It is not reassigned within the loop body
/// - It has a known array type in the TypeMap
/// </summary>
public static class ArrayHoistAnalyzer
{
    /// <summary>
    /// Analyzes a loop body and returns the set of array variables that can be hoisted.
    /// </summary>
    public static Dictionary<string, ArrayElementsDescriptor> Analyze(Stmt body, TypeMap? typeMap)
    {
        if (typeMap == null) return new();

        var visitor = new ArrayAccessVisitor(typeMap);
        visitor.Visit(body);

        // Remove any variables that are reassigned within the loop
        foreach (var reassigned in visitor.ReassignedVariables)
            visitor.ArrayVariables.Remove(reassigned);

        return visitor.ArrayVariables;
    }

    /// <summary>
    /// Analyzes a loop body and condition/increment for hoistable arrays.
    /// Used for for-loops where the condition and increment are separate from the body.
    /// </summary>
    public static Dictionary<string, ArrayElementsDescriptor> AnalyzeFor(
        Stmt body, Expr? condition, Expr? increment, TypeMap? typeMap)
    {
        if (typeMap == null) return new();

        var visitor = new ArrayAccessVisitor(typeMap);
        visitor.Visit(body);
        if (condition != null) visitor.VisitExpr(condition);
        if (increment != null) visitor.VisitExpr(increment);

        foreach (var reassigned in visitor.ReassignedVariables)
            visitor.ArrayVariables.Remove(reassigned);

        return visitor.ArrayVariables;
    }

    private sealed class ArrayAccessVisitor : AstVisitorBase
    {
        private readonly TypeMap _typeMap;

        /// <summary>Array variables found with their descriptors.</summary>
        public Dictionary<string, ArrayElementsDescriptor> ArrayVariables { get; } = new();

        /// <summary>Variables that are reassigned within the loop body.</summary>
        public HashSet<string> ReassignedVariables { get; } = new();

        public ArrayAccessVisitor(TypeMap typeMap) => _typeMap = typeMap;

        public void VisitExpr(Expr expr) => Visit(expr);

        protected override void VisitGetIndex(Expr.GetIndex expr)
        {
            TryRegisterArrayVariable(expr.Object);
            base.VisitGetIndex(expr);
        }

        protected override void VisitSetIndex(Expr.SetIndex expr)
        {
            TryRegisterArrayVariable(expr.Object);
            base.VisitSetIndex(expr);
        }

        protected override void VisitGet(Expr.Get expr)
        {
            // Detect .length access on arrays
            if (expr.Name.Lexeme == "length")
                TryRegisterArrayVariable(expr.Object);
            base.VisitGet(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            ReassignedVariables.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            ReassignedVariables.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitVar(Stmt.Var stmt)
        {
            // Variable shadowing — treat as reassignment
            ReassignedVariables.Add(stmt.Name.Lexeme);
            base.VisitVar(stmt);
        }

        private void TryRegisterArrayVariable(Expr receiver)
        {
            if (receiver is not Expr.Variable v) return;
            if (ArrayVariables.ContainsKey(v.Name.Lexeme)) return;

            var typeInfo = _typeMap.Get(receiver);
            var desc = ArrayElements.Resolve(typeInfo);
            if (desc != null)
                ArrayVariables[v.Name.Lexeme] = desc;
        }
    }
}
