namespace SharpTS.Parsing.Visitors;

/// <summary>
/// Visitor interface for Expr nodes with strongly-typed return values.
/// Implement all methods to ensure exhaustive handling of all expression types.
/// </summary>
/// <remarks>
/// This interface provides compile-time safety when adding new expression types.
/// Adding a new Expr subtype requires adding a corresponding Visit method here,
/// which will cause compilation errors in all implementers until they handle it.
/// </remarks>
/// <typeparam name="TResult">The return type for all visit methods.</typeparam>
public interface IExprVisitor<out TResult>
{
    TResult VisitBinary(Expr.Binary expr);
    TResult VisitLogical(Expr.Logical expr);
    TResult VisitNullishCoalescing(Expr.NullishCoalescing expr);
    TResult VisitTernary(Expr.Ternary expr);
    TResult VisitGrouping(Expr.Grouping expr);
    TResult VisitLiteral(Expr.Literal expr);
    TResult VisitUnary(Expr.Unary expr);
    TResult VisitDelete(Expr.Delete expr);
    TResult VisitVariable(Expr.Variable expr);
    TResult VisitAssign(Expr.Assign expr);
    TResult VisitCall(Expr.Call expr);
    TResult VisitGet(Expr.Get expr);
    TResult VisitSet(Expr.Set expr);
    TResult VisitGetPrivate(Expr.GetPrivate expr);
    TResult VisitSetPrivate(Expr.SetPrivate expr);
    TResult VisitCallPrivate(Expr.CallPrivate expr);
    TResult VisitThis(Expr.This expr);
    TResult VisitNew(Expr.New expr);
    TResult VisitArrayLiteral(Expr.ArrayLiteral expr);
    TResult VisitObjectLiteral(Expr.ObjectLiteral expr);
    TResult VisitGetIndex(Expr.GetIndex expr);
    TResult VisitSetIndex(Expr.SetIndex expr);
    TResult VisitSuper(Expr.Super expr);
    TResult VisitCompoundAssign(Expr.CompoundAssign expr);
    TResult VisitCompoundSet(Expr.CompoundSet expr);
    TResult VisitCompoundSetIndex(Expr.CompoundSetIndex expr);
    TResult VisitLogicalAssign(Expr.LogicalAssign expr);
    TResult VisitLogicalSet(Expr.LogicalSet expr);
    TResult VisitLogicalSetIndex(Expr.LogicalSetIndex expr);
    TResult VisitPrefixIncrement(Expr.PrefixIncrement expr);
    TResult VisitPostfixIncrement(Expr.PostfixIncrement expr);
    TResult VisitArrowFunction(Expr.ArrowFunction expr);
    TResult VisitTemplateLiteral(Expr.TemplateLiteral expr);
    TResult VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expr);
    TResult VisitSpread(Expr.Spread expr);
    TResult VisitTypeAssertion(Expr.TypeAssertion expr);
    TResult VisitSatisfies(Expr.Satisfies expr);
    TResult VisitAwait(Expr.Await expr);
    TResult VisitDynamicImport(Expr.DynamicImport expr);
    TResult VisitImportMeta(Expr.ImportMeta expr);
    TResult VisitYield(Expr.Yield expr);
    TResult VisitRegexLiteral(Expr.RegexLiteral expr);
    TResult VisitNonNullAssertion(Expr.NonNullAssertion expr);
    TResult VisitClassExpr(Expr.ClassExpr expr);
}
