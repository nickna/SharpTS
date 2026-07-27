using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;

namespace SharpTS.Execution;

/// <summary>
/// Handler registrations for the Interpreter.
/// Configures a NodeRegistry with handlers for all AST node types.
/// </summary>
/// <remarks>
/// This registry provides sync dispatch for expressions and statements,
/// with optional async statement dispatch for statements that need
/// async behavior (e.g., for await...of, try/catch with async body).
/// </remarks>
public static class InterpreterRegistry
{
    /// <summary>
    /// Creates and configures a NodeRegistry for the Interpreter.
    /// Uses reflection-based auto-registration to discover Visit* methods,
    /// with async support enabled for statement dispatch.
    /// </summary>
    /// <returns>A frozen registry ready for dispatch.</returns>
    public static NodeRegistry<Interpreter, RuntimeValue, ExecutionResult> Create()
    {
        return new NodeRegistry<Interpreter, RuntimeValue, ExecutionResult>(supportAsync: true)
            .AutoRegister()
            // Register async statement handlers for statements that need async behavior.
            // These handlers use EvaluateAsync and ExecuteAsync internally.
            .RegisterStmtAsync<Stmt.Block>((s, i) => i.ExecuteBlockAsyncVT(s))
            .RegisterStmtAsync<Stmt.Sequence>((s, i) => i.ExecuteSequenceAsyncVT(s))
            .RegisterStmtAsync<Stmt.Expression>((s, i) => i.ExecuteExpressionAsyncVT(s))
            .RegisterStmtAsync<Stmt.If>((s, i) => i.ExecuteIfAsyncVT(s))
            .RegisterStmtAsync<Stmt.While>((s, i) => i.ExecuteWhileAsyncVT(s))
            .RegisterStmtAsync<Stmt.DoWhile>((s, i) => i.ExecuteDoWhileAsyncVT(s))
            .RegisterStmtAsync<Stmt.For>((s, i) => i.ExecuteForAsyncVT(s))
            .RegisterStmtAsync<Stmt.ForOf>((s, i) => i.ExecuteForOfAsyncVT(s))
            .RegisterStmtAsync<Stmt.ForIn>((s, i) => i.ExecuteForInAsyncVT(s))
            // #728: route a labeled `for await` (and any labeled loop in async code) through the async
            // path so the for-await async-iterator lowering runs and labels are parked for the loop.
            .RegisterStmtAsync<Stmt.LabeledStatement>((s, i) => i.ExecuteLabeledStatementAsyncVT(s))
            .RegisterStmtAsync<Stmt.Switch>((s, i) => i.ExecuteSwitchAsyncVT(s))
            .RegisterStmtAsync<Stmt.TryCatch>((s, i) => i.ExecuteTryCatchAsyncVT(s))
            .RegisterStmtAsync<Stmt.Throw>((s, i) => i.ExecuteThrowAsyncVT(s))
            .RegisterStmtAsync<Stmt.Var>((s, i) => i.ExecuteVarAsyncVT(s))
            .RegisterStmtAsync<Stmt.Const>((s, i) => i.ExecuteConstAsyncVT(s))
            .RegisterStmtAsync<Stmt.Return>((s, i) => i.ExecuteReturnAsyncVT(s))
            // Register async expression handlers for all expression types (#930).
            // Routes EvaluateAsync through NodeRegistry.DispatchExprAsync so the async
            // dispatch path gets the same exhaustiveness guarantees as the sync path.
            .RegisterExprAsync<Expr.Comma>((e, i) => i.VisitCommaAsync(e))
            .RegisterExprAsync<Expr.DestructuringAssign>((e, i) => i.VisitDestructuringAssignAsync(e))
            .RegisterExprAsync<Expr.Binary>((e, i) => i.VisitBinaryAsync(e))
            .RegisterExprAsync<Expr.Logical>((e, i) => i.VisitLogicalAsync(e))
            .RegisterExprAsync<Expr.NullishCoalescing>((e, i) => i.VisitNullishCoalescingAsync(e))
            .RegisterExprAsync<Expr.Ternary>((e, i) => i.VisitTernaryAsync(e))
            .RegisterExprAsync<Expr.Grouping>((e, i) => i.VisitGroupingAsync(e))
            .RegisterExprAsync<Expr.Literal>((e, i) => i.VisitLiteralAsync(e))
            .RegisterExprAsync<Expr.Unary>((e, i) => i.VisitUnaryAsync(e))
            .RegisterExprAsync<Expr.Delete>((e, i) => i.VisitDeleteAsync(e))
            .RegisterExprAsync<Expr.Variable>((e, i) => i.VisitVariableAsync(e))
            .RegisterExprAsync<Expr.Assign>((e, i) => i.VisitAssignAsync(e))
            .RegisterExprAsync<Expr.Call>((e, i) => i.VisitCallAsync(e))
            .RegisterExprAsync<Expr.Get>((e, i) => i.VisitGetAsync(e))
            .RegisterExprAsync<Expr.Set>((e, i) => i.VisitSetAsync(e))
            .RegisterExprAsync<Expr.GetPrivate>((e, i) => i.VisitGetPrivateAsync(e))
            .RegisterExprAsync<Expr.SetPrivate>((e, i) => i.VisitSetPrivateAsync(e))
            .RegisterExprAsync<Expr.CallPrivate>((e, i) => i.VisitCallPrivateAsync(e))
            .RegisterExprAsync<Expr.This>((e, i) => i.VisitThisAsync(e))
            .RegisterExprAsync<Expr.New>((e, i) => i.VisitNewAsync(e))
            .RegisterExprAsync<Expr.ArrayLiteral>((e, i) => i.VisitArrayLiteralAsync(e))
            .RegisterExprAsync<Expr.ObjectLiteral>((e, i) => i.VisitObjectLiteralAsync(e))
            .RegisterExprAsync<Expr.GetIndex>((e, i) => i.VisitGetIndexAsync(e))
            .RegisterExprAsync<Expr.SetIndex>((e, i) => i.VisitSetIndexAsync(e))
            .RegisterExprAsync<Expr.Super>((e, i) => i.VisitSuperAsync(e))
            .RegisterExprAsync<Expr.CompoundAssign>((e, i) => i.VisitCompoundAssignAsync(e))
            .RegisterExprAsync<Expr.CompoundSet>((e, i) => i.VisitCompoundSetAsync(e))
            .RegisterExprAsync<Expr.CompoundSetIndex>((e, i) => i.VisitCompoundSetIndexAsync(e))
            .RegisterExprAsync<Expr.LogicalAssign>((e, i) => i.VisitLogicalAssignAsync(e))
            .RegisterExprAsync<Expr.LogicalSet>((e, i) => i.VisitLogicalSetAsync(e))
            .RegisterExprAsync<Expr.LogicalSetIndex>((e, i) => i.VisitLogicalSetIndexAsync(e))
            .RegisterExprAsync<Expr.PrefixIncrement>((e, i) => i.VisitPrefixIncrementAsync(e))
            .RegisterExprAsync<Expr.PostfixIncrement>((e, i) => i.VisitPostfixIncrementAsync(e))
            .RegisterExprAsync<Expr.ArrowFunction>((e, i) => i.VisitArrowFunctionAsync(e))
            .RegisterExprAsync<Expr.TemplateLiteral>((e, i) => i.VisitTemplateLiteralAsync(e))
            .RegisterExprAsync<Expr.TaggedTemplateLiteral>((e, i) => i.VisitTaggedTemplateLiteralAsync(e))
            .RegisterExprAsync<Expr.Spread>((e, i) => i.VisitSpreadAsync(e))
            .RegisterExprAsync<Expr.TypeAssertion>((e, i) => i.VisitTypeAssertionAsync(e))
            .RegisterExprAsync<Expr.Satisfies>((e, i) => i.VisitSatisfiesAsync(e))
            .RegisterExprAsync<Expr.NonNullAssertion>((e, i) => i.VisitNonNullAssertionAsync(e))
            .RegisterExprAsync<Expr.Await>((e, i) => i.VisitAwaitAsync(e))
            .RegisterExprAsync<Expr.DynamicImport>((e, i) => i.VisitDynamicImportAsync(e))
            .RegisterExprAsync<Expr.ImportMeta>((e, i) => i.VisitImportMetaAsync(e))
            .RegisterExprAsync<Expr.Yield>((e, i) => i.VisitYieldAsync(e))
            .RegisterExprAsync<Expr.RegexLiteral>((e, i) => i.VisitRegexLiteralAsync(e))
            .RegisterExprAsync<Expr.ClassExpr>((e, i) => i.VisitClassExprAsync(e))
            .Freeze()
            .FreezeAsync();
    }
}
