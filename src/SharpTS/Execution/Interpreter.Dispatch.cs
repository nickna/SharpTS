using SharpTS.Parsing;
using SharpTS.Runtime;

namespace SharpTS.Execution;

/// <summary>
/// Hand-maintained dispatch switches routing AST nodes to their Visit*/Execute* handlers.
/// This is the interpreter's replacement for the reflection-built <c>NodeRegistry</c> tables:
/// the registry resolved handlers via <c>GetMethod</c> + <c>Expression.Lambda().Compile()</c>,
/// which is both slower than a type switch under the JIT and catastrophically slower under
/// Native AOT, where compiled expression trees degrade to LightLambda interpretation (#1324).
/// </summary>
/// <remarks>
/// Exhaustiveness is guarded the same way as <see cref="Parsing.Visitors.AstVisitorBase"/>'s
/// switch: the default arms throw, and <c>AstDispatchTests</c> probes every node type in
/// <see cref="Parsing.Visitors.AstNodeCatalog"/> against them (#1243). A new AST node that is
/// not wired here fails those tests, not a production run.
/// Within each switch, the hottest node kinds are listed first — C# lowers these type
/// switches to sequential type tests, so arm order is dispatch cost.
/// </remarks>
public partial class Interpreter
{
    internal RuntimeValue DispatchExpr(Expr expr) => expr switch
    {
        // Hot tier: identifiers, literals, arithmetic, calls, member/index access.
        Expr.Variable e => VisitVariable(e),
        Expr.Literal e => VisitLiteral(e),
        Expr.Binary e => VisitBinary(e),
        Expr.Call e => VisitCall(e),
        Expr.Get e => VisitGet(e),
        Expr.GetIndex e => VisitGetIndex(e),
        Expr.Assign e => VisitAssign(e),
        Expr.Logical e => VisitLogical(e),
        Expr.CompoundAssign e => VisitCompoundAssign(e),
        Expr.PostfixIncrement e => VisitPostfixIncrement(e),
        Expr.PrefixIncrement e => VisitPrefixIncrement(e),
        Expr.Unary e => VisitUnary(e),
        Expr.This e => VisitThis(e),
        Expr.ObjectLiteral e => VisitObjectLiteral(e),
        Expr.ArrayLiteral e => VisitArrayLiteral(e),
        Expr.Ternary e => VisitTernary(e),
        Expr.TemplateLiteral e => VisitTemplateLiteral(e),
        Expr.Set e => VisitSet(e),
        Expr.SetIndex e => VisitSetIndex(e),
        Expr.ArrowFunction e => VisitArrowFunction(e),
        Expr.New e => VisitNew(e),
        Expr.Grouping e => VisitGrouping(e),
        Expr.NullishCoalescing e => VisitNullishCoalescing(e),
        Expr.Await e => VisitAwait(e),
        // Cold tier.
        Expr.Comma e => VisitComma(e),
        Expr.DestructuringAssign e => VisitDestructuringAssign(e),
        Expr.Delete e => VisitDelete(e),
        Expr.GetPrivate e => VisitGetPrivate(e),
        Expr.SetPrivate e => VisitSetPrivate(e),
        Expr.CallPrivate e => VisitCallPrivate(e),
        Expr.Super e => VisitSuper(e),
        Expr.CompoundSet e => VisitCompoundSet(e),
        Expr.CompoundSetIndex e => VisitCompoundSetIndex(e),
        Expr.LogicalAssign e => VisitLogicalAssign(e),
        Expr.LogicalSet e => VisitLogicalSet(e),
        Expr.LogicalSetIndex e => VisitLogicalSetIndex(e),
        Expr.TaggedTemplateLiteral e => VisitTaggedTemplateLiteral(e),
        Expr.Spread e => VisitSpread(e),
        Expr.TypeAssertion e => VisitTypeAssertion(e),
        Expr.Satisfies e => VisitSatisfies(e),
        Expr.NonNullAssertion e => VisitNonNullAssertion(e),
        Expr.DynamicImport e => VisitDynamicImport(e),
        Expr.ImportMeta e => VisitImportMeta(e),
        Expr.Yield e => VisitYield(e),
        Expr.RegexLiteral e => VisitRegexLiteral(e),
        Expr.ClassExpr e => VisitClassExpr(e),
        _ => throw new NotSupportedException(
            $"Interpreter has no dispatch case for expression node '{expr.GetType().Name}'. " +
            "Add a case to Interpreter.DispatchExpr and DispatchExprAsync (#1243)."),
    };

    internal ExecutionResult DispatchStmt(Stmt stmt) => stmt switch
    {
        // Hot tier.
        Stmt.Expression s => VisitExpression(s),
        Stmt.Block s => VisitBlock(s),
        Stmt.If s => VisitIf(s),
        Stmt.Var s => VisitVar(s),
        Stmt.Return s => VisitReturn(s),
        Stmt.Const s => VisitConst(s),
        Stmt.For s => VisitFor(s),
        Stmt.While s => VisitWhile(s),
        Stmt.ForOf s => VisitForOf(s),
        Stmt.Sequence s => VisitSequence(s),
        // Cold tier.
        Stmt.Function s => VisitFunction(s),
        Stmt.Field s => VisitField(s),
        Stmt.Accessor s => VisitAccessor(s),
        Stmt.AutoAccessor s => VisitAutoAccessor(s),
        Stmt.Class s => VisitClass(s),
        Stmt.StaticBlock s => VisitStaticBlock(s),
        Stmt.Interface s => VisitInterface(s),
        Stmt.DoWhile s => VisitDoWhile(s),
        Stmt.ForIn s => VisitForIn(s),
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
            $"Interpreter has no dispatch case for statement node '{stmt.GetType().Name}'. " +
            "Add a case to Interpreter.DispatchStmt (#1243)."),
    };

    /// <summary>
    /// Async expression dispatch. Every expression kind has an explicit async handler (#930) so
    /// that adding a new Expr type forces an explicit async decision here rather than silently
    /// reusing the sync path; the exhaustiveness probe covers this switch too.
    /// </summary>
    internal ValueTask<RuntimeValue> DispatchExprAsync(Expr expr) => expr switch
    {
        // Hot tier (same ordering rationale as DispatchExpr).
        Expr.Variable e => VisitVariableAsync(e),
        Expr.Literal e => VisitLiteralAsync(e),
        Expr.Binary e => VisitBinaryAsync(e),
        Expr.Call e => VisitCallAsync(e),
        Expr.Get e => VisitGetAsync(e),
        Expr.GetIndex e => VisitGetIndexAsync(e),
        Expr.Assign e => VisitAssignAsync(e),
        Expr.Logical e => VisitLogicalAsync(e),
        Expr.CompoundAssign e => VisitCompoundAssignAsync(e),
        Expr.PostfixIncrement e => VisitPostfixIncrementAsync(e),
        Expr.PrefixIncrement e => VisitPrefixIncrementAsync(e),
        Expr.Unary e => VisitUnaryAsync(e),
        Expr.This e => VisitThisAsync(e),
        Expr.ObjectLiteral e => VisitObjectLiteralAsync(e),
        Expr.ArrayLiteral e => VisitArrayLiteralAsync(e),
        Expr.Ternary e => VisitTernaryAsync(e),
        Expr.TemplateLiteral e => VisitTemplateLiteralAsync(e),
        Expr.Set e => VisitSetAsync(e),
        Expr.SetIndex e => VisitSetIndexAsync(e),
        Expr.ArrowFunction e => VisitArrowFunctionAsync(e),
        Expr.New e => VisitNewAsync(e),
        Expr.Grouping e => VisitGroupingAsync(e),
        Expr.NullishCoalescing e => VisitNullishCoalescingAsync(e),
        Expr.Await e => VisitAwaitAsync(e),
        // Cold tier.
        Expr.Comma e => VisitCommaAsync(e),
        Expr.DestructuringAssign e => VisitDestructuringAssignAsync(e),
        Expr.Delete e => VisitDeleteAsync(e),
        Expr.GetPrivate e => VisitGetPrivateAsync(e),
        Expr.SetPrivate e => VisitSetPrivateAsync(e),
        Expr.CallPrivate e => VisitCallPrivateAsync(e),
        Expr.Super e => VisitSuperAsync(e),
        Expr.CompoundSet e => VisitCompoundSetAsync(e),
        Expr.CompoundSetIndex e => VisitCompoundSetIndexAsync(e),
        Expr.LogicalAssign e => VisitLogicalAssignAsync(e),
        Expr.LogicalSet e => VisitLogicalSetAsync(e),
        Expr.LogicalSetIndex e => VisitLogicalSetIndexAsync(e),
        Expr.TaggedTemplateLiteral e => VisitTaggedTemplateLiteralAsync(e),
        Expr.Spread e => VisitSpreadAsync(e),
        Expr.TypeAssertion e => VisitTypeAssertionAsync(e),
        Expr.Satisfies e => VisitSatisfiesAsync(e),
        Expr.NonNullAssertion e => VisitNonNullAssertionAsync(e),
        Expr.DynamicImport e => VisitDynamicImportAsync(e),
        Expr.ImportMeta e => VisitImportMetaAsync(e),
        Expr.Yield e => VisitYieldAsync(e),
        Expr.RegexLiteral e => VisitRegexLiteralAsync(e),
        Expr.ClassExpr e => VisitClassExprAsync(e),
        _ => throw new NotSupportedException(
            $"Interpreter has no dispatch case for expression node '{expr.GetType().Name}'. " +
            "Add a case to Interpreter.DispatchExpr and DispatchExprAsync (#1243)."),
    };

    /// <summary>
    /// Async statement dispatch. Only the statement kinds that need async behavior (await inside
    /// their bodies/initializers) have async handlers; everything else falls back to the sync
    /// handler wrapped in a completed ValueTask — the exact semantics the registry's
    /// <c>DispatchStmtAsync</c> had.
    /// </summary>
    internal ValueTask<ExecutionResult> DispatchStmtAsync(Stmt stmt) => stmt switch
    {
        Stmt.Block s => ExecuteBlockAsyncVT(s),
        Stmt.Sequence s => ExecuteSequenceAsyncVT(s),
        Stmt.Expression s => ExecuteExpressionAsyncVT(s),
        Stmt.If s => ExecuteIfAsyncVT(s),
        Stmt.While s => ExecuteWhileAsyncVT(s),
        Stmt.DoWhile s => ExecuteDoWhileAsyncVT(s),
        Stmt.For s => ExecuteForAsyncVT(s),
        Stmt.ForOf s => ExecuteForOfAsyncVT(s),
        Stmt.ForIn s => ExecuteForInAsyncVT(s),
        // #728: route a labeled `for await` (and any labeled loop in async code) through the async
        // path so the for-await async-iterator lowering runs and labels are parked for the loop.
        Stmt.LabeledStatement s => ExecuteLabeledStatementAsyncVT(s),
        Stmt.Switch s => ExecuteSwitchAsyncVT(s),
        Stmt.TryCatch s => ExecuteTryCatchAsyncVT(s),
        Stmt.Throw s => ExecuteThrowAsyncVT(s),
        Stmt.Var s => ExecuteVarAsyncVT(s),
        Stmt.Const s => ExecuteConstAsyncVT(s),
        Stmt.Return s => ExecuteReturnAsyncVT(s),
        _ => new ValueTask<ExecutionResult>(DispatchStmt(stmt)),
    };
}
