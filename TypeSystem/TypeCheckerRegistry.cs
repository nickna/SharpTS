using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.TypeSystem;

/// <summary>
/// Handler registrations for the TypeChecker.
/// Configures a NodeRegistry with handlers for all AST node types.
/// </summary>
public static class TypeCheckerRegistry
{
    /// <summary>
    /// Creates and configures a NodeRegistry for the TypeChecker.
    /// </summary>
    /// <returns>A frozen registry ready for dispatch.</returns>
    public static NodeRegistry<TypeChecker, TypeInfo, VoidResult> Create()
    {
        return new NodeRegistry<TypeChecker, TypeInfo, VoidResult>(supportAsync: false)
            // ========== Expression Handlers ==========
            // Literals and basic values
            .RegisterExpr<Expr.Literal>((e, t) => t.VisitLiteral(e))
            .RegisterExpr<Expr.Variable>((e, t) => t.VisitVariable(e))
            .RegisterExpr<Expr.Grouping>((e, t) => t.VisitGrouping(e))
            .RegisterExpr<Expr.This>((e, t) => t.VisitThis(e))
            .RegisterExpr<Expr.Super>((e, t) => t.VisitSuper(e))
            .RegisterExpr<Expr.RegexLiteral>((e, t) => t.VisitRegexLiteral(e))
            .RegisterExpr<Expr.TemplateLiteral>((e, t) => t.VisitTemplateLiteral(e))
            .RegisterExpr<Expr.TaggedTemplateLiteral>((e, t) => t.VisitTaggedTemplateLiteral(e))

            // Operators
            .RegisterExpr<Expr.Binary>((e, t) => t.VisitBinary(e))
            .RegisterExpr<Expr.Logical>((e, t) => t.VisitLogical(e))
            .RegisterExpr<Expr.Unary>((e, t) => t.VisitUnary(e))
            .RegisterExpr<Expr.Ternary>((e, t) => t.VisitTernary(e))
            .RegisterExpr<Expr.NullishCoalescing>((e, t) => t.VisitNullishCoalescing(e))

            // Assignment
            .RegisterExpr<Expr.Assign>((e, t) => t.VisitAssign(e))
            .RegisterExpr<Expr.CompoundAssign>((e, t) => t.VisitCompoundAssign(e))
            .RegisterExpr<Expr.CompoundSet>((e, t) => t.VisitCompoundSet(e))
            .RegisterExpr<Expr.CompoundSetIndex>((e, t) => t.VisitCompoundSetIndex(e))
            .RegisterExpr<Expr.LogicalAssign>((e, t) => t.VisitLogicalAssign(e))
            .RegisterExpr<Expr.LogicalSet>((e, t) => t.VisitLogicalSet(e))
            .RegisterExpr<Expr.LogicalSetIndex>((e, t) => t.VisitLogicalSetIndex(e))
            .RegisterExpr<Expr.PrefixIncrement>((e, t) => t.VisitPrefixIncrement(e))
            .RegisterExpr<Expr.PostfixIncrement>((e, t) => t.VisitPostfixIncrement(e))

            // Property access
            .RegisterExpr<Expr.Get>((e, t) => t.VisitGet(e))
            .RegisterExpr<Expr.Set>((e, t) => t.VisitSet(e))
            .RegisterExpr<Expr.GetPrivate>((e, t) => t.VisitGetPrivate(e))
            .RegisterExpr<Expr.SetPrivate>((e, t) => t.VisitSetPrivate(e))
            .RegisterExpr<Expr.GetIndex>((e, t) => t.VisitGetIndex(e))
            .RegisterExpr<Expr.SetIndex>((e, t) => t.VisitSetIndex(e))

            // Calls
            .RegisterExpr<Expr.Call>((e, t) => t.VisitCall(e))
            .RegisterExpr<Expr.CallPrivate>((e, t) => t.VisitCallPrivate(e))
            .RegisterExpr<Expr.New>((e, t) => t.VisitNew(e))

            // Collections
            .RegisterExpr<Expr.ArrayLiteral>((e, t) => t.VisitArrayLiteral(e))
            .RegisterExpr<Expr.ObjectLiteral>((e, t) => t.VisitObjectLiteral(e))
            .RegisterExpr<Expr.Spread>((e, t) => t.VisitSpread(e))

            // Functions
            .RegisterExpr<Expr.ArrowFunction>((e, t) => t.VisitArrowFunction(e))
            .RegisterExpr<Expr.ClassExpr>((e, t) => t.VisitClassExpr(e))

            // Type operations
            .RegisterExpr<Expr.TypeAssertion>((e, t) => t.VisitTypeAssertion(e))
            .RegisterExpr<Expr.Satisfies>((e, t) => t.VisitSatisfies(e))
            .RegisterExpr<Expr.NonNullAssertion>((e, t) => t.VisitNonNullAssertion(e))
            .RegisterExpr<Expr.Delete>((e, t) => t.VisitDelete(e))

            // Async/Generator
            .RegisterExpr<Expr.Await>((e, t) => t.VisitAwait(e))
            .RegisterExpr<Expr.Yield>((e, t) => t.VisitYield(e))

            // Module
            .RegisterExpr<Expr.DynamicImport>((e, t) => t.VisitDynamicImport(e))
            .RegisterExpr<Expr.ImportMeta>((e, t) => t.VisitImportMeta(e))

            // ========== Statement Handlers ==========
            // Declarations
            .RegisterStmt<Stmt.Var>((s, t) => t.VisitVar(s))
            .RegisterStmt<Stmt.Const>((s, t) => t.VisitConst(s))
            .RegisterStmt<Stmt.Function>((s, t) => t.VisitFunction(s))
            .RegisterStmt<Stmt.Class>((s, t) => t.VisitClass(s))
            .RegisterStmt<Stmt.Interface>((s, t) => t.VisitInterface(s))
            .RegisterStmt<Stmt.TypeAlias>((s, t) => t.VisitTypeAlias(s))
            .RegisterStmt<Stmt.Enum>((s, t) => t.VisitEnum(s))
            .RegisterStmt<Stmt.Namespace>((s, t) => t.VisitNamespace(s))

            // Class members
            .RegisterStmt<Stmt.Field>((s, t) => t.VisitField(s))
            .RegisterStmt<Stmt.Accessor>((s, t) => t.VisitAccessor(s))
            .RegisterStmt<Stmt.AutoAccessor>((s, t) => t.VisitAutoAccessor(s))
            .RegisterStmt<Stmt.StaticBlock>((s, t) => t.VisitStaticBlock(s))

            // Control flow
            .RegisterStmt<Stmt.Block>((s, t) => t.VisitBlock(s))
            .RegisterStmt<Stmt.Sequence>((s, t) => t.VisitSequence(s))
            .RegisterStmt<Stmt.If>((s, t) => t.VisitIf(s))
            .RegisterStmt<Stmt.While>((s, t) => t.VisitWhile(s))
            .RegisterStmt<Stmt.DoWhile>((s, t) => t.VisitDoWhile(s))
            .RegisterStmt<Stmt.For>((s, t) => t.VisitFor(s))
            .RegisterStmt<Stmt.ForOf>((s, t) => t.VisitForOf(s))
            .RegisterStmt<Stmt.ForIn>((s, t) => t.VisitForIn(s))
            .RegisterStmt<Stmt.Switch>((s, t) => t.VisitSwitch(s))
            .RegisterStmt<Stmt.LabeledStatement>((s, t) => t.VisitLabeledStatement(s))

            // Control transfer
            .RegisterStmt<Stmt.Return>((s, t) => t.VisitReturn(s))
            .RegisterStmt<Stmt.Break>((s, t) => t.VisitBreak(s))
            .RegisterStmt<Stmt.Continue>((s, t) => t.VisitContinue(s))
            .RegisterStmt<Stmt.Throw>((s, t) => t.VisitThrow(s))

            // Error handling
            .RegisterStmt<Stmt.TryCatch>((s, t) => t.VisitTryCatch(s))

            // Expression statement
            .RegisterStmt<Stmt.Expression>((s, t) => t.VisitExpression(s))
            .RegisterStmt<Stmt.Print>((s, t) => t.VisitPrint(s))

            // Modules
            .RegisterStmt<Stmt.Import>((s, t) => t.VisitImport(s))
            .RegisterStmt<Stmt.ImportAlias>((s, t) => t.VisitImportAlias(s))
            .RegisterStmt<Stmt.ImportRequire>((s, t) => t.VisitImportRequire(s))
            .RegisterStmt<Stmt.Export>((s, t) => t.VisitExport(s))

            // Directives and declarations
            .RegisterStmt<Stmt.Directive>((s, t) => t.VisitDirective(s))
            .RegisterStmt<Stmt.FileDirective>((s, t) => t.VisitFileDirective(s))
            .RegisterStmt<Stmt.DeclareModule>((s, t) => t.VisitDeclareModule(s))
            .RegisterStmt<Stmt.DeclareGlobal>((s, t) => t.VisitDeclareGlobal(s))

            // Resource management
            .RegisterStmt<Stmt.Using>((s, t) => t.VisitUsing(s))

            // Freeze and validate exhaustiveness
            .Freeze();
    }
}
