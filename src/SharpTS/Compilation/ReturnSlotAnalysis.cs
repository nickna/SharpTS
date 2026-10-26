using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Decides whether a function/arrow/method body has a return whose value can be the runtime
/// <c>undefined</c> sentinel despite a numeric, boolean or class-instance declared return type.
/// The checker and forwarding analysis flag those expressions in <see cref="TypeMap"/> so the
/// compiler can widen the return slot to <c>object</c>, preserving the value instead of coercing it
/// or failing a class cast.
/// </summary>
internal static class ReturnSlotAnalysis
{
    /// <summary>
    /// True if any <c>return</c> within <paramref name="body"/> — not crossing into a nested
    /// function, arrow, or class — returns a value flagged undefined-reachable by the checker.
    /// Nested functions own their own return slot and are analyzed separately, so traversal
    /// stops at those boundaries.
    /// </summary>
    public static bool BlockReturnsMayBeUndefined(IReadOnlyList<Stmt>? body, TypeMap? typeMap)
    {
        if (body == null || typeMap == null) return false;
        foreach (var stmt in body)
        {
            if (StatementHasFlaggedReturn(stmt, typeMap)) return true;
        }
        return false;
    }

    /// <summary>
    /// True if the expression-arrow body <paramref name="expr"/> was flagged undefined-reachable.
    /// Expression-bodied arrows have no <c>Stmt.Return</c>; the body expression is the return value.
    /// </summary>
    public static bool ExpressionReturnMayBeUndefined(Expr? expr, TypeMap? typeMap) =>
        expr != null && typeMap != null && (typeMap.IsUndefinedReachableReturn(expr) ||
            PrimitiveFieldValueAnalysis.ReturnsRawField(expr, typeMap));

    private static bool StatementHasFlaggedReturn(Stmt stmt, TypeMap typeMap)
    {
        switch (stmt)
        {
            case Stmt.Return ret:
                return ExpressionReturnMayBeUndefined(ret.Value, typeMap);

            case Stmt.Block b:
                return BlockReturnsMayBeUndefined(b.Statements, typeMap);

            case Stmt.If i:
                return StatementHasFlaggedReturn(i.ThenBranch, typeMap)
                    || (i.ElseBranch != null && StatementHasFlaggedReturn(i.ElseBranch, typeMap));

            case Stmt.While w:
                return StatementHasFlaggedReturn(w.Body, typeMap);

            case Stmt.DoWhile dw:
                return StatementHasFlaggedReturn(dw.Body, typeMap);

            case Stmt.For f:
                return StatementHasFlaggedReturn(f.Body, typeMap);

            case Stmt.ForOf fo:
                return StatementHasFlaggedReturn(fo.Body, typeMap);

            case Stmt.ForIn fi:
                return StatementHasFlaggedReturn(fi.Body, typeMap);

            case Stmt.LabeledStatement ls:
                return StatementHasFlaggedReturn(ls.Statement, typeMap);

            case Stmt.Switch sw:
                foreach (var c in sw.Clauses)
                    if (BlockReturnsMayBeUndefined(c.Body, typeMap)) return true;
                return false;

            case Stmt.TryCatch tc:
                return BlockReturnsMayBeUndefined(tc.TryBlock, typeMap)
                    || BlockReturnsMayBeUndefined(tc.CatchBlock, typeMap)
                    || BlockReturnsMayBeUndefined(tc.FinallyBlock, typeMap);

            // Nested function/class declarations own their own return slot — do not descend.
            // Expression statements, variable declarations, throw/break/continue, etc. carry no
            // reachable return for this function (any arrow inside an initializer is likewise a
            // separate slot, flagged independently when its own body is analyzed).
            default:
                return false;
        }
    }
}
