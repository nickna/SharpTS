using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using Xunit;

namespace SharpTS.Tests.ParsingTests;

/// <summary>
/// Tests for AST visitor infrastructure to ensure all node types are handled.
/// </summary>
public class AstVisitorTests
{
    /// <summary>
    /// Tracks visited expression types to verify exhaustive dispatch.
    /// </summary>
    private class ExprTypeCollector : IExprVisitor<Type>
    {
        public Type VisitBinary(Expr.Binary expr) => typeof(Expr.Binary);
        public Type VisitLogical(Expr.Logical expr) => typeof(Expr.Logical);
        public Type VisitNullishCoalescing(Expr.NullishCoalescing expr) => typeof(Expr.NullishCoalescing);
        public Type VisitTernary(Expr.Ternary expr) => typeof(Expr.Ternary);
        public Type VisitGrouping(Expr.Grouping expr) => typeof(Expr.Grouping);
        public Type VisitLiteral(Expr.Literal expr) => typeof(Expr.Literal);
        public Type VisitUnary(Expr.Unary expr) => typeof(Expr.Unary);
        public Type VisitDelete(Expr.Delete expr) => typeof(Expr.Delete);
        public Type VisitVariable(Expr.Variable expr) => typeof(Expr.Variable);
        public Type VisitAssign(Expr.Assign expr) => typeof(Expr.Assign);
        public Type VisitCall(Expr.Call expr) => typeof(Expr.Call);
        public Type VisitGet(Expr.Get expr) => typeof(Expr.Get);
        public Type VisitSet(Expr.Set expr) => typeof(Expr.Set);
        public Type VisitGetPrivate(Expr.GetPrivate expr) => typeof(Expr.GetPrivate);
        public Type VisitSetPrivate(Expr.SetPrivate expr) => typeof(Expr.SetPrivate);
        public Type VisitCallPrivate(Expr.CallPrivate expr) => typeof(Expr.CallPrivate);
        public Type VisitThis(Expr.This expr) => typeof(Expr.This);
        public Type VisitNew(Expr.New expr) => typeof(Expr.New);
        public Type VisitArrayLiteral(Expr.ArrayLiteral expr) => typeof(Expr.ArrayLiteral);
        public Type VisitObjectLiteral(Expr.ObjectLiteral expr) => typeof(Expr.ObjectLiteral);
        public Type VisitGetIndex(Expr.GetIndex expr) => typeof(Expr.GetIndex);
        public Type VisitSetIndex(Expr.SetIndex expr) => typeof(Expr.SetIndex);
        public Type VisitSuper(Expr.Super expr) => typeof(Expr.Super);
        public Type VisitCompoundAssign(Expr.CompoundAssign expr) => typeof(Expr.CompoundAssign);
        public Type VisitCompoundSet(Expr.CompoundSet expr) => typeof(Expr.CompoundSet);
        public Type VisitCompoundSetIndex(Expr.CompoundSetIndex expr) => typeof(Expr.CompoundSetIndex);
        public Type VisitLogicalAssign(Expr.LogicalAssign expr) => typeof(Expr.LogicalAssign);
        public Type VisitLogicalSet(Expr.LogicalSet expr) => typeof(Expr.LogicalSet);
        public Type VisitLogicalSetIndex(Expr.LogicalSetIndex expr) => typeof(Expr.LogicalSetIndex);
        public Type VisitPrefixIncrement(Expr.PrefixIncrement expr) => typeof(Expr.PrefixIncrement);
        public Type VisitPostfixIncrement(Expr.PostfixIncrement expr) => typeof(Expr.PostfixIncrement);
        public Type VisitArrowFunction(Expr.ArrowFunction expr) => typeof(Expr.ArrowFunction);
        public Type VisitTemplateLiteral(Expr.TemplateLiteral expr) => typeof(Expr.TemplateLiteral);
        public Type VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expr) => typeof(Expr.TaggedTemplateLiteral);
        public Type VisitSpread(Expr.Spread expr) => typeof(Expr.Spread);
        public Type VisitTypeAssertion(Expr.TypeAssertion expr) => typeof(Expr.TypeAssertion);
        public Type VisitSatisfies(Expr.Satisfies expr) => typeof(Expr.Satisfies);
        public Type VisitAwait(Expr.Await expr) => typeof(Expr.Await);
        public Type VisitDynamicImport(Expr.DynamicImport expr) => typeof(Expr.DynamicImport);
        public Type VisitImportMeta(Expr.ImportMeta expr) => typeof(Expr.ImportMeta);
        public Type VisitYield(Expr.Yield expr) => typeof(Expr.Yield);
        public Type VisitRegexLiteral(Expr.RegexLiteral expr) => typeof(Expr.RegexLiteral);
        public Type VisitNonNullAssertion(Expr.NonNullAssertion expr) => typeof(Expr.NonNullAssertion);
        public Type VisitClassExpr(Expr.ClassExpr expr) => typeof(Expr.ClassExpr);
    }

    /// <summary>
    /// Tracks visited statement types to verify exhaustive dispatch.
    /// </summary>
    private class StmtTypeCollector : IStmtVisitor<Type>
    {
        public Type VisitExpression(Stmt.Expression stmt) => typeof(Stmt.Expression);
        public Type VisitVar(Stmt.Var stmt) => typeof(Stmt.Var);
        public Type VisitConst(Stmt.Const stmt) => typeof(Stmt.Const);
        public Type VisitFunction(Stmt.Function stmt) => typeof(Stmt.Function);
        public Type VisitField(Stmt.Field stmt) => typeof(Stmt.Field);
        public Type VisitAccessor(Stmt.Accessor stmt) => typeof(Stmt.Accessor);
        public Type VisitAutoAccessor(Stmt.AutoAccessor stmt) => typeof(Stmt.AutoAccessor);
        public Type VisitClass(Stmt.Class stmt) => typeof(Stmt.Class);
        public Type VisitStaticBlock(Stmt.StaticBlock stmt) => typeof(Stmt.StaticBlock);
        public Type VisitInterface(Stmt.Interface stmt) => typeof(Stmt.Interface);
        public Type VisitBlock(Stmt.Block stmt) => typeof(Stmt.Block);
        public Type VisitSequence(Stmt.Sequence stmt) => typeof(Stmt.Sequence);
        public Type VisitReturn(Stmt.Return stmt) => typeof(Stmt.Return);
        public Type VisitWhile(Stmt.While stmt) => typeof(Stmt.While);
        public Type VisitFor(Stmt.For stmt) => typeof(Stmt.For);
        public Type VisitDoWhile(Stmt.DoWhile stmt) => typeof(Stmt.DoWhile);
        public Type VisitForOf(Stmt.ForOf stmt) => typeof(Stmt.ForOf);
        public Type VisitForIn(Stmt.ForIn stmt) => typeof(Stmt.ForIn);
        public Type VisitIf(Stmt.If stmt) => typeof(Stmt.If);
        public Type VisitPrint(Stmt.Print stmt) => typeof(Stmt.Print);
        public Type VisitBreak(Stmt.Break stmt) => typeof(Stmt.Break);
        public Type VisitContinue(Stmt.Continue stmt) => typeof(Stmt.Continue);
        public Type VisitLabeledStatement(Stmt.LabeledStatement stmt) => typeof(Stmt.LabeledStatement);
        public Type VisitSwitch(Stmt.Switch stmt) => typeof(Stmt.Switch);
        public Type VisitTryCatch(Stmt.TryCatch stmt) => typeof(Stmt.TryCatch);
        public Type VisitThrow(Stmt.Throw stmt) => typeof(Stmt.Throw);
        public Type VisitTypeAlias(Stmt.TypeAlias stmt) => typeof(Stmt.TypeAlias);
        public Type VisitEnum(Stmt.Enum stmt) => typeof(Stmt.Enum);
        public Type VisitNamespace(Stmt.Namespace stmt) => typeof(Stmt.Namespace);
        public Type VisitImportAlias(Stmt.ImportAlias stmt) => typeof(Stmt.ImportAlias);
        public Type VisitImportRequire(Stmt.ImportRequire stmt) => typeof(Stmt.ImportRequire);
        public Type VisitImport(Stmt.Import stmt) => typeof(Stmt.Import);
        public Type VisitExport(Stmt.Export stmt) => typeof(Stmt.Export);
        public Type VisitFileDirective(Stmt.FileDirective stmt) => typeof(Stmt.FileDirective);
        public Type VisitDirective(Stmt.Directive stmt) => typeof(Stmt.Directive);
        public Type VisitDeclareModule(Stmt.DeclareModule stmt) => typeof(Stmt.DeclareModule);
        public Type VisitDeclareGlobal(Stmt.DeclareGlobal stmt) => typeof(Stmt.DeclareGlobal);
        public Type VisitUsing(Stmt.Using stmt) => typeof(Stmt.Using);
    }

    private static Token DummyToken => new(TokenType.IDENTIFIER, "dummy", null, 1);

    [Fact]
    public void Accept_DispatchesAllExprTypes()
    {
        var visitor = new ExprTypeCollector();

        // Create one instance of each Expr type and verify dispatch
        var testCases = new (Expr expr, Type expected)[]
        {
            (new Expr.Binary(new Expr.Literal(1), DummyToken, new Expr.Literal(2)), typeof(Expr.Binary)),
            (new Expr.Logical(new Expr.Literal(true), DummyToken, new Expr.Literal(false)), typeof(Expr.Logical)),
            (new Expr.NullishCoalescing(new Expr.Literal(null), new Expr.Literal(1)), typeof(Expr.NullishCoalescing)),
            (new Expr.Ternary(new Expr.Literal(true), new Expr.Literal(1), new Expr.Literal(2)), typeof(Expr.Ternary)),
            (new Expr.Grouping(new Expr.Literal(1)), typeof(Expr.Grouping)),
            (new Expr.Literal(42), typeof(Expr.Literal)),
            (new Expr.Unary(DummyToken, new Expr.Literal(1)), typeof(Expr.Unary)),
            (new Expr.Delete(DummyToken, new Expr.Variable(DummyToken)), typeof(Expr.Delete)),
            (new Expr.Variable(DummyToken), typeof(Expr.Variable)),
            (new Expr.Assign(DummyToken, new Expr.Literal(1)), typeof(Expr.Assign)),
            (new Expr.Call(new Expr.Variable(DummyToken), DummyToken, null, []), typeof(Expr.Call)),
            (new Expr.Get(new Expr.Variable(DummyToken), DummyToken), typeof(Expr.Get)),
            (new Expr.Set(new Expr.Variable(DummyToken), DummyToken, new Expr.Literal(1)), typeof(Expr.Set)),
            (new Expr.GetPrivate(new Expr.Variable(DummyToken), DummyToken), typeof(Expr.GetPrivate)),
            (new Expr.SetPrivate(new Expr.Variable(DummyToken), DummyToken, new Expr.Literal(1)), typeof(Expr.SetPrivate)),
            (new Expr.CallPrivate(new Expr.Variable(DummyToken), DummyToken, []), typeof(Expr.CallPrivate)),
            (new Expr.This(DummyToken), typeof(Expr.This)),
            (new Expr.New(new Expr.Variable(DummyToken), null, []), typeof(Expr.New)),
            (new Expr.ArrayLiteral([]), typeof(Expr.ArrayLiteral)),
            (new Expr.ObjectLiteral([]), typeof(Expr.ObjectLiteral)),
            (new Expr.GetIndex(new Expr.Variable(DummyToken), new Expr.Literal(0)), typeof(Expr.GetIndex)),
            (new Expr.SetIndex(new Expr.Variable(DummyToken), new Expr.Literal(0), new Expr.Literal(1)), typeof(Expr.SetIndex)),
            (new Expr.Super(DummyToken, null), typeof(Expr.Super)),
            (new Expr.CompoundAssign(DummyToken, DummyToken, new Expr.Literal(1)), typeof(Expr.CompoundAssign)),
            (new Expr.CompoundSet(new Expr.Variable(DummyToken), DummyToken, DummyToken, new Expr.Literal(1)), typeof(Expr.CompoundSet)),
            (new Expr.CompoundSetIndex(new Expr.Variable(DummyToken), new Expr.Literal(0), DummyToken, new Expr.Literal(1)), typeof(Expr.CompoundSetIndex)),
            (new Expr.LogicalAssign(DummyToken, DummyToken, new Expr.Literal(1)), typeof(Expr.LogicalAssign)),
            (new Expr.LogicalSet(new Expr.Variable(DummyToken), DummyToken, DummyToken, new Expr.Literal(1)), typeof(Expr.LogicalSet)),
            (new Expr.LogicalSetIndex(new Expr.Variable(DummyToken), new Expr.Literal(0), DummyToken, new Expr.Literal(1)), typeof(Expr.LogicalSetIndex)),
            (new Expr.PrefixIncrement(DummyToken, new Expr.Variable(DummyToken)), typeof(Expr.PrefixIncrement)),
            (new Expr.PostfixIncrement(new Expr.Variable(DummyToken), DummyToken), typeof(Expr.PostfixIncrement)),
            (new Expr.ArrowFunction(null, null, null, [], new Expr.Literal(1), null, null), typeof(Expr.ArrowFunction)),
            (new Expr.TemplateLiteral([""], []), typeof(Expr.TemplateLiteral)),
            (new Expr.TaggedTemplateLiteral(new Expr.Variable(DummyToken), [""], [""], []), typeof(Expr.TaggedTemplateLiteral)),
            (new Expr.Spread(new Expr.Variable(DummyToken)), typeof(Expr.Spread)),
            (new Expr.TypeAssertion(new Expr.Literal(1), "number"), typeof(Expr.TypeAssertion)),
            (new Expr.Satisfies(new Expr.Literal(1), "number"), typeof(Expr.Satisfies)),
            (new Expr.Await(DummyToken, new Expr.Variable(DummyToken)), typeof(Expr.Await)),
            (new Expr.DynamicImport(DummyToken, new Expr.Literal("./mod")), typeof(Expr.DynamicImport)),
            (new Expr.ImportMeta(DummyToken), typeof(Expr.ImportMeta)),
            (new Expr.Yield(DummyToken, null, false), typeof(Expr.Yield)),
            (new Expr.RegexLiteral(".*", "g"), typeof(Expr.RegexLiteral)),
            (new Expr.NonNullAssertion(new Expr.Variable(DummyToken)), typeof(Expr.NonNullAssertion)),
            (new Expr.ClassExpr(null, null, null, null, [], []), typeof(Expr.ClassExpr)),
        };

        foreach (var (expr, expected) in testCases)
        {
            var result = Expr.Accept(expr, visitor);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Accept_DispatchesAllStmtTypes()
    {
        var visitor = new StmtTypeCollector();

        var testCases = new (Stmt stmt, Type expected)[]
        {
            (new Stmt.Expression(new Expr.Literal(1)), typeof(Stmt.Expression)),
            (new Stmt.Var(DummyToken, null, null), typeof(Stmt.Var)),
            (new Stmt.Const(DummyToken, null, new Expr.Literal(1)), typeof(Stmt.Const)),
            (new Stmt.Function(DummyToken, null, null, [], [], null), typeof(Stmt.Function)),
            (new Stmt.Field(DummyToken, null, null), typeof(Stmt.Field)),
            (new Stmt.Accessor(DummyToken, DummyToken, null, [], null), typeof(Stmt.Accessor)),
            (new Stmt.AutoAccessor(DummyToken, null, null), typeof(Stmt.AutoAccessor)),
            (new Stmt.Class(DummyToken, null, null, null, [], []), typeof(Stmt.Class)),
            (new Stmt.StaticBlock([]), typeof(Stmt.StaticBlock)),
            (new Stmt.Interface(DummyToken, null, []), typeof(Stmt.Interface)),
            (new Stmt.Block([]), typeof(Stmt.Block)),
            (new Stmt.Sequence([]), typeof(Stmt.Sequence)),
            (new Stmt.Return(DummyToken, null), typeof(Stmt.Return)),
            (new Stmt.While(new Expr.Literal(true), new Stmt.Block([])), typeof(Stmt.While)),
            (new Stmt.For(null, null, null, new Stmt.Block([])), typeof(Stmt.For)),
            (new Stmt.DoWhile(new Stmt.Block([]), new Expr.Literal(true)), typeof(Stmt.DoWhile)),
            (new Stmt.ForOf(DummyToken, null, new Expr.Variable(DummyToken), new Stmt.Block([])), typeof(Stmt.ForOf)),
            (new Stmt.ForIn(DummyToken, null, new Expr.Variable(DummyToken), new Stmt.Block([])), typeof(Stmt.ForIn)),
            (new Stmt.If(new Expr.Literal(true), new Stmt.Block([]), null), typeof(Stmt.If)),
            (new Stmt.Print(new Expr.Literal("test")), typeof(Stmt.Print)),
            (new Stmt.Break(DummyToken), typeof(Stmt.Break)),
            (new Stmt.Continue(DummyToken), typeof(Stmt.Continue)),
            (new Stmt.LabeledStatement(DummyToken, new Stmt.Block([])), typeof(Stmt.LabeledStatement)),
            (new Stmt.Switch(new Expr.Literal(1), [], null), typeof(Stmt.Switch)),
            (new Stmt.TryCatch([], null, null, null), typeof(Stmt.TryCatch)),
            (new Stmt.Throw(DummyToken, new Expr.Literal("error")), typeof(Stmt.Throw)),
            (new Stmt.TypeAlias(DummyToken, "number"), typeof(Stmt.TypeAlias)),
            (new Stmt.Enum(DummyToken, []), typeof(Stmt.Enum)),
            (new Stmt.Namespace(DummyToken, []), typeof(Stmt.Namespace)),
            (new Stmt.ImportAlias(DummyToken, DummyToken, [DummyToken]), typeof(Stmt.ImportAlias)),
            (new Stmt.ImportRequire(DummyToken, DummyToken, "./mod"), typeof(Stmt.ImportRequire)),
            (new Stmt.Import(DummyToken, null, null, null, "./mod"), typeof(Stmt.Import)),
            (new Stmt.Export(DummyToken, null, null, null, null, false), typeof(Stmt.Export)),
            (new Stmt.FileDirective([]), typeof(Stmt.FileDirective)),
            (new Stmt.Directive("use strict", DummyToken), typeof(Stmt.Directive)),
            (new Stmt.DeclareModule(DummyToken, "./mod", []), typeof(Stmt.DeclareModule)),
            (new Stmt.DeclareGlobal(DummyToken, []), typeof(Stmt.DeclareGlobal)),
            (new Stmt.Using(DummyToken, [new Stmt.UsingBinding(DummyToken, null, null, new Expr.Variable(DummyToken))], false), typeof(Stmt.Using)),
        };

        foreach (var (stmt, expected) in testCases)
        {
            var result = Stmt.Accept(stmt, visitor);
            Assert.Equal(expected, result);
        }
    }

    [Fact]
    public void Accept_CoverageMatchesReflectedTypes()
    {
        // Verify all Expr subtypes are covered using reflection
        var exprType = typeof(Expr);
        var exprSubtypes = exprType.GetNestedTypes()
            .Where(t => t.IsClass && !t.IsAbstract && exprType.IsAssignableFrom(t))
            .ToHashSet();

        // The interface should have one method per subtype
        var visitorMethods = typeof(IExprVisitor<>).GetMethods()
            .Where(m => m.Name.StartsWith("Visit"))
            .Select(m => m.GetParameters()[0].ParameterType)
            .ToHashSet();

        // Verify every Expr subtype has a visitor method
        foreach (var subtype in exprSubtypes)
        {
            Assert.Contains(subtype, visitorMethods);
        }

        // Verify every visitor method has a corresponding subtype
        foreach (var methodType in visitorMethods)
        {
            Assert.Contains(methodType, exprSubtypes);
        }
    }

    [Fact]
    public void StmtAccept_CoverageMatchesReflectedTypes()
    {
        // Verify all Stmt subtypes (that are visitable) are covered using reflection
        var stmtType = typeof(Stmt);
        var stmtSubtypes = stmtType.GetNestedTypes()
            .Where(t => t.IsClass && !t.IsAbstract && stmtType.IsAssignableFrom(t))
            .ToHashSet();

        // The interface should have one method per subtype
        var visitorMethods = typeof(IStmtVisitor<>).GetMethods()
            .Where(m => m.Name.StartsWith("Visit"))
            .Select(m => m.GetParameters()[0].ParameterType)
            .ToHashSet();

        // Verify every visitable Stmt subtype has a visitor method
        foreach (var subtype in stmtSubtypes)
        {
            Assert.Contains(subtype, visitorMethods);
        }

        // Verify every visitor method has a corresponding subtype
        foreach (var methodType in visitorMethods)
        {
            Assert.Contains(methodType, stmtSubtypes);
        }
    }
}
