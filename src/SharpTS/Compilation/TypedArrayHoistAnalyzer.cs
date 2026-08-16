using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes a loop for numeric TypedArray variables whose <c>castclass $XArray</c> can be hoisted
/// out of the loop (#928). A variable qualifies when it is the receiver of an index get / set /
/// compound-set, is statically typed as a <see cref="TypeInfo.TypedArray"/>, and is not reassigned
/// or re-declared anywhere in the loop (so the cast stays valid for every iteration).
///
/// Returns <c>varName → element-type name</c> (e.g. <c>"Int32"</c>); the emit site resolves the
/// concrete <c>$XArray</c> type and skips element types without unboxed accessors (BigInt /
/// Uint8Clamped), whose index sites do not take the unboxed fast path. Mirrors
/// <see cref="ArrayHoistAnalyzer"/>, which performs the equivalent hoist for ordinary arrays.
/// </summary>
public static class TypedArrayHoistAnalyzer
{
    public static Dictionary<string, string> AnalyzeFor(
        Stmt body, Expr? condition, Expr? increment, TypeMap? typeMap)
    {
        if (typeMap == null) return new();

        var visitor = new TypedArrayAccessVisitor(typeMap);
        visitor.Visit(body);
        if (condition != null) visitor.VisitExpr(condition);
        if (increment != null) visitor.VisitExpr(increment);

        foreach (var reassigned in visitor.Reassigned)
            visitor.Candidates.Remove(reassigned);

        return visitor.Candidates;
    }

    private sealed class TypedArrayAccessVisitor : AstVisitorBase
    {
        private readonly TypeMap _typeMap;

        /// <summary>Variable name → TypedArray element-type name (e.g. "Int32").</summary>
        public Dictionary<string, string> Candidates { get; } = new();

        /// <summary>Variables reassigned/re-declared within the loop (disqualified).</summary>
        public HashSet<string> Reassigned { get; } = new();

        public TypedArrayAccessVisitor(TypeMap typeMap) => _typeMap = typeMap;

        public void VisitExpr(Expr expr) => Visit(expr);

        protected override void VisitGetIndex(Expr.GetIndex expr)
        {
            TryRegister(expr.Object);
            base.VisitGetIndex(expr);
        }

        protected override void VisitSetIndex(Expr.SetIndex expr)
        {
            TryRegister(expr.Object);
            base.VisitSetIndex(expr);
        }

        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expr)
        {
            TryRegister(expr.Object);
            base.VisitCompoundSetIndex(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            Reassigned.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Reassigned.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitVar(Stmt.Var stmt)
        {
            // Variable shadowing — treat as reassignment, matching ArrayHoistAnalyzer.
            Reassigned.Add(stmt.Name.Lexeme);
            base.VisitVar(stmt);
        }

        private void TryRegister(Expr receiver)
        {
            if (receiver is not Expr.Variable v) return;
            if (Candidates.ContainsKey(v.Name.Lexeme)) return;
            if (_typeMap.Get(receiver) is TypeInfo.TypedArray ta)
                Candidates[v.Name.Lexeme] = ta.ElementType;
        }
    }
}
