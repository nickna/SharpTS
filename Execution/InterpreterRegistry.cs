using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// Handler registrations for the Interpreter.
/// Configures a NodeRegistry with handlers for all AST node types.
/// </summary>
/// <remarks>
/// This registry provides sync dispatch for expressions and statements.
/// Async dispatch continues to use the existing switch-based EvaluateAsync method
/// in Interpreter.Expressions.cs, which may be migrated to registry-based dispatch
/// in a future update.
/// </remarks>
public static class InterpreterRegistry
{
    /// <summary>
    /// Creates and configures a NodeRegistry for the Interpreter.
    /// </summary>
    /// <returns>A frozen registry ready for dispatch.</returns>
    public static NodeRegistry<Interpreter, object?, ExecutionResult> Create()
    {
        return new NodeRegistry<Interpreter, object?, ExecutionResult>(supportAsync: false)
            // ========== Expression Handlers ==========
            // Literals and basic values
            .RegisterExpr<Expr.Literal>((e, i) => i.VisitLiteral(e))
            .RegisterExpr<Expr.Variable>((e, i) => i.VisitVariable(e))
            .RegisterExpr<Expr.Grouping>((e, i) => i.VisitGrouping(e))
            .RegisterExpr<Expr.This>((e, i) => i.VisitThis(e))
            .RegisterExpr<Expr.Super>((e, i) => i.VisitSuper(e))
            .RegisterExpr<Expr.RegexLiteral>((e, i) => i.VisitRegexLiteral(e))
            .RegisterExpr<Expr.TemplateLiteral>((e, i) => i.VisitTemplateLiteral(e))
            .RegisterExpr<Expr.TaggedTemplateLiteral>((e, i) => i.VisitTaggedTemplateLiteral(e))

            // Operators
            .RegisterExpr<Expr.Binary>((e, i) => i.VisitBinary(e))
            .RegisterExpr<Expr.Logical>((e, i) => i.VisitLogical(e))
            .RegisterExpr<Expr.Unary>((e, i) => i.VisitUnary(e))
            .RegisterExpr<Expr.Ternary>((e, i) => i.VisitTernary(e))
            .RegisterExpr<Expr.NullishCoalescing>((e, i) => i.VisitNullishCoalescing(e))

            // Assignment
            .RegisterExpr<Expr.Assign>((e, i) => i.VisitAssign(e))
            .RegisterExpr<Expr.CompoundAssign>((e, i) => i.VisitCompoundAssign(e))
            .RegisterExpr<Expr.CompoundSet>((e, i) => i.VisitCompoundSet(e))
            .RegisterExpr<Expr.CompoundSetIndex>((e, i) => i.VisitCompoundSetIndex(e))
            .RegisterExpr<Expr.LogicalAssign>((e, i) => i.VisitLogicalAssign(e))
            .RegisterExpr<Expr.LogicalSet>((e, i) => i.VisitLogicalSet(e))
            .RegisterExpr<Expr.LogicalSetIndex>((e, i) => i.VisitLogicalSetIndex(e))
            .RegisterExpr<Expr.PrefixIncrement>((e, i) => i.VisitPrefixIncrement(e))
            .RegisterExpr<Expr.PostfixIncrement>((e, i) => i.VisitPostfixIncrement(e))

            // Property access
            .RegisterExpr<Expr.Get>((e, i) => i.VisitGet(e))
            .RegisterExpr<Expr.Set>((e, i) => i.VisitSet(e))
            .RegisterExpr<Expr.GetPrivate>((e, i) => i.VisitGetPrivate(e))
            .RegisterExpr<Expr.SetPrivate>((e, i) => i.VisitSetPrivate(e))
            .RegisterExpr<Expr.GetIndex>((e, i) => i.VisitGetIndex(e))
            .RegisterExpr<Expr.SetIndex>((e, i) => i.VisitSetIndex(e))

            // Calls
            .RegisterExpr<Expr.Call>((e, i) => i.VisitCall(e))
            .RegisterExpr<Expr.CallPrivate>((e, i) => i.VisitCallPrivate(e))
            .RegisterExpr<Expr.New>((e, i) => i.VisitNew(e))

            // Collections
            .RegisterExpr<Expr.ArrayLiteral>((e, i) => i.VisitArrayLiteral(e))
            .RegisterExpr<Expr.ObjectLiteral>((e, i) => i.VisitObjectLiteral(e))
            .RegisterExpr<Expr.Spread>((e, i) => i.VisitSpread(e))

            // Functions
            .RegisterExpr<Expr.ArrowFunction>((e, i) => i.VisitArrowFunction(e))
            .RegisterExpr<Expr.ClassExpr>((e, i) => i.VisitClassExpr(e))

            // Type operations (pass-through at runtime)
            .RegisterExpr<Expr.TypeAssertion>((e, i) => i.VisitTypeAssertion(e))
            .RegisterExpr<Expr.Satisfies>((e, i) => i.VisitSatisfies(e))
            .RegisterExpr<Expr.NonNullAssertion>((e, i) => i.VisitNonNullAssertion(e))
            .RegisterExpr<Expr.Delete>((e, i) => i.VisitDelete(e))

            // Async/Generator
            .RegisterExpr<Expr.Await>((e, i) => i.VisitAwait(e))
            .RegisterExpr<Expr.Yield>((e, i) => i.VisitYield(e))

            // Module
            .RegisterExpr<Expr.DynamicImport>((e, i) => i.VisitDynamicImport(e))
            .RegisterExpr<Expr.ImportMeta>((e, i) => i.VisitImportMeta(e))

            // ========== Statement Handlers ==========
            // Declarations
            .RegisterStmt<Stmt.Var>((s, i) => i.VisitVar(s))
            .RegisterStmt<Stmt.Const>((s, i) => i.VisitConst(s))
            .RegisterStmt<Stmt.Function>((s, i) => i.VisitFunction(s))
            .RegisterStmt<Stmt.Class>((s, i) => i.VisitClass(s))
            .RegisterStmt<Stmt.Interface>((s, i) => i.VisitInterface(s))
            .RegisterStmt<Stmt.TypeAlias>((s, i) => i.VisitTypeAlias(s))
            .RegisterStmt<Stmt.Enum>((s, i) => i.VisitEnum(s))
            .RegisterStmt<Stmt.Namespace>((s, i) => i.VisitNamespace(s))

            // Class members (handled within class processing)
            .RegisterStmt<Stmt.Field>((s, i) => i.VisitField(s))
            .RegisterStmt<Stmt.Accessor>((s, i) => i.VisitAccessor(s))
            .RegisterStmt<Stmt.AutoAccessor>((s, i) => i.VisitAutoAccessor(s))
            .RegisterStmt<Stmt.StaticBlock>((s, i) => i.VisitStaticBlock(s))

            // Control flow
            .RegisterStmt<Stmt.Block>((s, i) => i.VisitBlock(s))
            .RegisterStmt<Stmt.Sequence>((s, i) => i.VisitSequence(s))
            .RegisterStmt<Stmt.If>((s, i) => i.VisitIf(s))
            .RegisterStmt<Stmt.While>((s, i) => i.VisitWhile(s))
            .RegisterStmt<Stmt.DoWhile>((s, i) => i.VisitDoWhile(s))
            .RegisterStmt<Stmt.For>((s, i) => i.VisitFor(s))
            .RegisterStmt<Stmt.ForOf>((s, i) => i.VisitForOf(s))
            .RegisterStmt<Stmt.ForIn>((s, i) => i.VisitForIn(s))
            .RegisterStmt<Stmt.Switch>((s, i) => i.VisitSwitch(s))
            .RegisterStmt<Stmt.LabeledStatement>((s, i) => i.VisitLabeledStatement(s))

            // Control transfer
            .RegisterStmt<Stmt.Return>((s, i) => i.VisitReturn(s))
            .RegisterStmt<Stmt.Break>((s, i) => i.VisitBreak(s))
            .RegisterStmt<Stmt.Continue>((s, i) => i.VisitContinue(s))
            .RegisterStmt<Stmt.Throw>((s, i) => i.VisitThrow(s))

            // Error handling
            .RegisterStmt<Stmt.TryCatch>((s, i) => i.VisitTryCatch(s))

            // Expression statement
            .RegisterStmt<Stmt.Expression>((s, i) => i.VisitExpression(s))
            .RegisterStmt<Stmt.Print>((s, i) => i.VisitPrint(s))

            // Modules
            .RegisterStmt<Stmt.Import>((s, i) => i.VisitImport(s))
            .RegisterStmt<Stmt.ImportAlias>((s, i) => i.VisitImportAlias(s))
            .RegisterStmt<Stmt.ImportRequire>((s, i) => i.VisitImportRequire(s))
            .RegisterStmt<Stmt.Export>((s, i) => i.VisitExport(s))

            // Directives and declarations
            .RegisterStmt<Stmt.Directive>((s, i) => i.VisitDirective(s))
            .RegisterStmt<Stmt.FileDirective>((s, i) => i.VisitFileDirective(s))
            .RegisterStmt<Stmt.DeclareModule>((s, i) => i.VisitDeclareModule(s))
            .RegisterStmt<Stmt.DeclareGlobal>((s, i) => i.VisitDeclareGlobal(s))

            // Resource management
            .RegisterStmt<Stmt.Using>((s, i) => i.VisitUsing(s))

            // Freeze and validate exhaustiveness
            .Freeze();
    }
}
