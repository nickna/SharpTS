using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for Automatic Semicolon Insertion (ASI) — ECMAScript §12.9.
/// Verifies that the parser correctly inserts virtual semicolons at
/// newlines, before '}', and at EOF.
/// </summary>
public class AsiTests
{
    #region Helpers

    private static ParseDiagnosticResult ParseWithDiagnostics(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #endregion

    #region Basic ASI — Newline before offending token

    [Fact]
    public void TwoDeclarations_SeparatedByNewline()
    {
        var stmts = TestHarness.ParseOrThrow("let x = 5\nlet y = 10");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<Stmt.Var>(stmts[0]);
        Assert.IsType<Stmt.Var>(stmts[1]);
    }

    [Fact]
    public void TwoExpressionStatements_SeparatedByNewline()
    {
        var stmts = TestHarness.ParseOrThrow("x = 1\ny = 2");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<Stmt.Expression>(stmts[0]);
        Assert.IsType<Stmt.Expression>(stmts[1]);
    }

    [Fact]
    public void MultipleStatements_NoSemicolons()
    {
        var stmts = TestHarness.ParseOrThrow("let a = 1\nlet b = 2\nlet c = 3");
        Assert.Equal(3, stmts.Count);
    }

    [Fact]
    public void ConstDeclaration_NoSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("const x = 42\nconst y = 'hello'");
        Assert.Equal(2, stmts.Count);
    }

    #endregion

    #region ASI before closing brace

    [Fact]
    public void FunctionBody_NoSemicolonBeforeBrace()
    {
        var stmts = TestHarness.ParseOrThrow("function f() { return 1 }");
        Assert.Single(stmts);
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        Assert.Single(func.Body!);
    }

    [Fact]
    public void IfBlock_NoSemicolonBeforeBrace()
    {
        var stmts = TestHarness.ParseOrThrow("if (true) { let x = 1 }");
        Assert.Single(stmts);
    }

    [Fact]
    public void Block_MultipleStatements_NoSemicolons()
    {
        var stmts = TestHarness.ParseOrThrow("function f() { let x = 1\nlet y = 2 }");
        Assert.Single(stmts);
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        Assert.Equal(2, func.Body!.Count);
    }

    #endregion

    #region ASI at EOF

    [Fact]
    public void SingleDeclaration_AtEOF()
    {
        var stmts = TestHarness.ParseOrThrow("let x = 5");
        Assert.Single(stmts);
        Assert.IsType<Stmt.Var>(stmts[0]);
    }

    [Fact]
    public void ExpressionStatement_AtEOF()
    {
        var stmts = TestHarness.ParseOrThrow("x = 1");
        Assert.Single(stmts);
    }

    #endregion

    #region Restricted production: return

    [Fact]
    public void Return_WithNewline_ReturnsUndefined()
    {
        // return\n1 → return; 1;  (returns undefined, 1 is separate expression statement)
        var stmts = TestHarness.ParseOrThrow("function f() { return\n1 }");
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        var body = func.Body!;
        Assert.Equal(2, body.Count);
        var ret = Assert.IsType<Stmt.Return>(body[0]);
        Assert.Null(ret.Value); // returns undefined
        Assert.IsType<Stmt.Expression>(body[1]); // 1 is a separate statement
    }

    [Fact]
    public void Return_SameLine_ReturnsExpression()
    {
        var stmts = TestHarness.ParseOrThrow("function f() { return 42 }");
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        var body = func.Body!;
        Assert.Single(body);
        var ret = Assert.IsType<Stmt.Return>(body[0]);
        Assert.NotNull(ret.Value);
    }

    [Fact]
    public void Return_Bare_InBlock()
    {
        var stmts = TestHarness.ParseOrThrow("function f() { return }");
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        var body = func.Body!;
        Assert.Single(body);
        var ret = Assert.IsType<Stmt.Return>(body[0]);
        Assert.Null(ret.Value);
    }

    #endregion

    #region Restricted production: throw

    [Fact]
    public void Throw_WithNewline_IsError()
    {
        // throw\n1 → syntax error (throw must have expression on same line)
        var result = ParseWithDiagnostics("function f() { throw\n1 }");
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Throw_SameLine_IsValid()
    {
        var stmts = TestHarness.ParseOrThrow("function f() { throw new Error('x') }");
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        var body = func.Body!;
        Assert.Single(body);
        Assert.IsType<Stmt.Throw>(body[0]);
    }

    #endregion

    #region Restricted production: break / continue

    [Fact]
    public void Break_WithNewline_BeforeLabel_BreaksWithoutLabel()
    {
        // break\nlabel → break; label;
        var stmts = TestHarness.ParseOrThrow("while (true) { break\nlabel }");
        var whileStmt = Assert.IsType<Stmt.While>(stmts[0]);
        var block = Assert.IsType<Stmt.Block>(whileStmt.Body);
        Assert.Equal(2, block.Statements.Count);
        var brk = Assert.IsType<Stmt.Break>(block.Statements[0]);
        Assert.Null(brk.Label);
    }

    [Fact]
    public void Continue_WithNewline_BeforeLabel_ContinuesWithoutLabel()
    {
        // continue\nlabel → continue; label;
        var stmts = TestHarness.ParseOrThrow("while (true) { continue\nlabel }");
        var whileStmt = Assert.IsType<Stmt.While>(stmts[0]);
        var block = Assert.IsType<Stmt.Block>(whileStmt.Body);
        Assert.Equal(2, block.Statements.Count);
        var cont = Assert.IsType<Stmt.Continue>(block.Statements[0]);
        Assert.Null(cont.Label);
    }

    [Fact]
    public void Break_WithLabel_SameLine()
    {
        var stmts = TestHarness.ParseOrThrow("outer: while (true) { break outer }");
        var labeled = Assert.IsType<Stmt.LabeledStatement>(stmts[0]);
        var whileStmt = Assert.IsType<Stmt.While>(labeled.Statement);
        var block = Assert.IsType<Stmt.Block>(whileStmt.Body);
        var brk = Assert.IsType<Stmt.Break>(block.Statements[0]);
        Assert.NotNull(brk.Label);
        Assert.Equal("outer", brk.Label!.Lexeme);
    }

    #endregion

    #region Restricted production: postfix ++/--

    [Fact]
    public void PostfixIncrement_OnNewLine_BecomesPrefix()
    {
        // x\n++\ny → x; ++y;
        var stmts = TestHarness.ParseOrThrow("let x = 0\nlet y = 0\nx\n++\ny");
        // Should be: let x = 0; let y = 0; x; ++y;
        Assert.Equal(4, stmts.Count);
    }

    #endregion

    #region Restricted production: yield

    [Fact]
    public void Yield_WithNewline_YieldsUndefined()
    {
        // yield\n1 → yield; 1;
        var stmts = TestHarness.ParseOrThrow("function* gen() { yield\n1 }");
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        var body = func.Body!;
        Assert.Equal(2, body.Count);
        var yieldExpr = Assert.IsType<Expr.Yield>(((Stmt.Expression)body[0]).Expr);
        Assert.Null(yieldExpr.Value); // yields undefined
    }

    [Fact]
    public void Yield_SameLine_YieldsExpression()
    {
        var stmts = TestHarness.ParseOrThrow("function* gen() { yield 42 }");
        var func = Assert.IsType<Stmt.Function>(stmts[0]);
        var body = func.Body!;
        Assert.Single(body);
        var yieldExpr = Assert.IsType<Expr.Yield>(((Stmt.Expression)body[0]).Expr);
        Assert.NotNull(yieldExpr.Value);
    }

    #endregion

    #region No-insert cases (valid continuations)

    [Fact]
    public void BinaryOperator_OnNewLine_IsContinuation()
    {
        // let x = 1\n+ 2 → let x = 1 + 2 (NOT let x = 1; +2)
        var stmts = TestHarness.ParseOrThrow("let x = 1\n+ 2");
        Assert.Single(stmts);
        var varStmt = Assert.IsType<Stmt.Var>(stmts[0]);
        Assert.IsType<Expr.Binary>(varStmt.Initializer);
    }

    [Fact]
    public void MethodChain_AcrossLines()
    {
        // f()\n.method() → f().method()
        var stmts = TestHarness.ParseOrThrow("f()\n.toString()");
        Assert.Single(stmts);
    }

    #endregion

    #region For-loop headers remain strict

    [Fact]
    public void ForLoop_RequiresExplicitSemicolons()
    {
        // for (let i = 0; i < 10; i++) {} must work with explicit semicolons
        var stmts = TestHarness.ParseOrThrow("for (let i = 0; i < 10; i++) {}");
        Assert.Single(stmts);
        Assert.IsType<Stmt.For>(stmts[0]);
    }

    [Fact]
    public void ForLoop_NewlineInHeader_IsError()
    {
        // for (let i = 0\ni < 10\ni++) {} → error (for-header requires explicit ;)
        var result = ParseWithDiagnostics("for (let i = 0\ni < 10\ni++) {}");
        Assert.False(result.IsSuccess);
    }

    #endregion

    #region Interface members — newline as separator

    [Fact]
    public void Interface_NewlineSeparatedMembers()
    {
        var stmts = TestHarness.ParseOrThrow("interface I {\n  x: number\n  y: string\n}");
        Assert.Single(stmts);
        var iface = Assert.IsType<Stmt.Interface>(stmts[0]);
        Assert.Equal(2, iface.Members.Count);
    }

    [Fact]
    public void Interface_CommaSeparatedMembers()
    {
        var stmts = TestHarness.ParseOrThrow("interface I { x: number, y: string }");
        Assert.Single(stmts);
        var iface = Assert.IsType<Stmt.Interface>(stmts[0]);
        Assert.Equal(2, iface.Members.Count);
    }

    [Fact]
    public void Interface_SemicolonSeparatedMembers()
    {
        var stmts = TestHarness.ParseOrThrow("interface I { x: number; y: string; }");
        Assert.Single(stmts);
        var iface = Assert.IsType<Stmt.Interface>(stmts[0]);
        Assert.Equal(2, iface.Members.Count);
    }

    #endregion

    #region Class fields — newline as separator

    [Fact]
    public void ClassFields_NoSemicolons()
    {
        var stmts = TestHarness.ParseOrThrow("class C {\n  x: number = 5\n  y: string = ''\n}");
        Assert.Single(stmts);
    }

    #endregion

    #region Import/export without semicolons

    [Fact]
    public void ImportDeclaration_NoSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("import { foo } from './bar'\nexport const x = 1");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<Stmt.Import>(stmts[0]);
        Assert.IsType<Stmt.Export>(stmts[1]);
    }

    [Fact]
    public void SideEffectImport_NoSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("import './polyfill'\nlet x = 1");
        Assert.Equal(2, stmts.Count);
    }

    #endregion

    #region Do-while without trailing semicolon

    [Fact]
    public void DoWhile_NoTrailingSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("do { x = 1 } while (true)\nlet y = 2");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<Stmt.DoWhile>(stmts[0]);
        Assert.IsType<Stmt.Var>(stmts[1]);
    }

    #endregion

    #region Type alias without semicolon

    [Fact]
    public void TypeAlias_NoSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("type Foo = string\ntype Bar = number");
        Assert.Equal(2, stmts.Count);
        Assert.IsType<Stmt.TypeAlias>(stmts[0]);
        Assert.IsType<Stmt.TypeAlias>(stmts[1]);
    }

    #endregion

    #region Explicit semicolons still work

    [Fact]
    public void ExplicitSemicolons_StillValid()
    {
        var stmts = TestHarness.ParseOrThrow("let x = 5;\nlet y = 10;\nconsole.log(x + y);");
        Assert.Equal(3, stmts.Count);
    }

    [Fact]
    public void MixedSemicolonsAndASI()
    {
        var stmts = TestHarness.ParseOrThrow("let x = 5;\nlet y = 10\nlet z = 15;");
        Assert.Equal(3, stmts.Count);
    }

    #endregion

    #region Destructuring without semicolons

    [Fact]
    public void ArrayDestructuring_NoSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("let [a, b] = [1, 2]\nlet x = 3");
        Assert.Equal(2, stmts.Count);
    }

    [Fact]
    public void ObjectDestructuring_NoSemicolon()
    {
        var stmts = TestHarness.ParseOrThrow("let { a, b } = { a: 1, b: 2 }\nlet x = 3");
        Assert.Equal(2, stmts.Count);
    }

    #endregion
}
