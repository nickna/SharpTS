using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.TypeSystem;

/// <summary>
/// Hand-maintained dispatch switches routing AST nodes to their Visit* handlers.
/// Replaces the reflection-built <c>NodeRegistry</c> tables (see
/// <c>Execution/Interpreter.Dispatch.cs</c> for the rationale — #1324).
/// </summary>
/// <remarks>
/// Exhaustiveness is guarded like <see cref="AstVisitorBase"/>'s switch: the default arms throw,
/// and <c>AstDispatchTests</c> probes every node type in <see cref="AstNodeCatalog"/> against
/// them (#1243). The type checker visits each node once per program, so arms are kept in the
/// same order as <see cref="AstVisitorBase"/> for readability rather than hot-path ordering.
/// </remarks>
public partial class TypeChecker
{
    internal TypeInfo DispatchExpr(Expr expr) => expr switch
    {
        Expr.Comma e => VisitComma(e),
        Expr.DestructuringAssign e => VisitDestructuringAssign(e),
        Expr.Binary e => VisitBinary(e),
        Expr.Logical e => VisitLogical(e),
        Expr.NullishCoalescing e => VisitNullishCoalescing(e),
        Expr.Ternary e => VisitTernary(e),
        Expr.Grouping e => VisitGrouping(e),
        Expr.Literal e => VisitLiteral(e),
        Expr.Unary e => VisitUnary(e),
        Expr.Delete e => VisitDelete(e),
        Expr.Variable e => VisitVariable(e),
        Expr.Assign e => VisitAssign(e),
        Expr.Call e => VisitCall(e),
        Expr.Get e => VisitGet(e),
        Expr.Set e => VisitSet(e),
        Expr.GetPrivate e => VisitGetPrivate(e),
        Expr.SetPrivate e => VisitSetPrivate(e),
        Expr.CallPrivate e => VisitCallPrivate(e),
        Expr.This e => VisitThis(e),
        Expr.New e => VisitNew(e),
        Expr.ArrayLiteral e => VisitArrayLiteral(e),
        Expr.ObjectLiteral e => VisitObjectLiteral(e),
        Expr.GetIndex e => VisitGetIndex(e),
        Expr.SetIndex e => VisitSetIndex(e),
        Expr.Super e => VisitSuper(e),
        Expr.CompoundAssign e => VisitCompoundAssign(e),
        Expr.CompoundSet e => VisitCompoundSet(e),
        Expr.CompoundSetIndex e => VisitCompoundSetIndex(e),
        Expr.LogicalAssign e => VisitLogicalAssign(e),
        Expr.LogicalSet e => VisitLogicalSet(e),
        Expr.LogicalSetIndex e => VisitLogicalSetIndex(e),
        Expr.PrefixIncrement e => VisitPrefixIncrement(e),
        Expr.PostfixIncrement e => VisitPostfixIncrement(e),
        Expr.ArrowFunction e => VisitArrowFunction(e),
        Expr.TemplateLiteral e => VisitTemplateLiteral(e),
        Expr.TaggedTemplateLiteral e => VisitTaggedTemplateLiteral(e),
        Expr.Spread e => VisitSpread(e),
        Expr.TypeAssertion e => VisitTypeAssertion(e),
        Expr.Satisfies e => VisitSatisfies(e),
        Expr.NonNullAssertion e => VisitNonNullAssertion(e),
        Expr.Await e => VisitAwait(e),
        Expr.DynamicImport e => VisitDynamicImport(e),
        Expr.ImportMeta e => VisitImportMeta(e),
        Expr.Yield e => VisitYield(e),
        Expr.RegexLiteral e => VisitRegexLiteral(e),
        Expr.ClassExpr e => VisitClassExpr(e),
        _ => throw new NotSupportedException(
            $"TypeChecker has no dispatch case for expression node '{expr.GetType().Name}'. " +
            "Add a case to TypeChecker.DispatchExpr (#1243)."),
    };

    internal VoidResult DispatchStmt(Stmt stmt) => stmt switch
    {
        Stmt.Expression s => VisitExpression(s),
        Stmt.Var s => VisitVar(s),
        Stmt.Const s => VisitConst(s),
        Stmt.Function s => VisitFunction(s),
        Stmt.Field s => VisitField(s),
        Stmt.Accessor s => VisitAccessor(s),
        Stmt.AutoAccessor s => VisitAutoAccessor(s),
        Stmt.Class s => VisitClass(s),
        Stmt.StaticBlock s => VisitStaticBlock(s),
        Stmt.Interface s => VisitInterface(s),
        Stmt.Block s => VisitBlock(s),
        Stmt.Sequence s => VisitSequence(s),
        Stmt.Return s => VisitReturn(s),
        Stmt.While s => VisitWhile(s),
        Stmt.DoWhile s => VisitDoWhile(s),
        Stmt.ForOf s => VisitForOf(s),
        Stmt.ForIn s => VisitForIn(s),
        Stmt.For s => VisitFor(s),
        Stmt.If s => VisitIf(s),
        Stmt.Break s => VisitBreak(s),
        Stmt.Continue s => VisitContinue(s),
        Stmt.LabeledStatement s => VisitLabeledStatement(s),
        Stmt.Switch s => VisitSwitch(s),
        Stmt.TryCatch s => VisitTryCatch(s),
        Stmt.Throw s => VisitThrow(s),
        Stmt.TypeAlias s => VisitTypeAlias(s),
        Stmt.Enum s => VisitEnum(s),
        Stmt.Namespace s => VisitNamespace(s),
        Stmt.ImportAlias s => VisitImportAlias(s),
        Stmt.ImportRequire s => VisitImportRequire(s),
        Stmt.Import s => VisitImport(s),
        Stmt.Export s => VisitExport(s),
        Stmt.FileDirective s => VisitFileDirective(s),
        Stmt.Directive s => VisitDirective(s),
        Stmt.DeclareModule s => VisitDeclareModule(s),
        Stmt.DeclareGlobal s => VisitDeclareGlobal(s),
        Stmt.Using s => VisitUsing(s),
        _ => throw new NotSupportedException(
            $"TypeChecker has no dispatch case for statement node '{stmt.GetType().Name}'. " +
            "Add a case to TypeChecker.DispatchStmt (#1243)."),
    };
}
