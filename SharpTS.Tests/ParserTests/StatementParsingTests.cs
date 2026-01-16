using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for statement parsing in the parser.
/// Covers control flow, loops, switch, try/catch, and other statement types.
/// </summary>
public class StatementParsingTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    #endregion

    #region If Statements

    [Fact]
    public void If_Simple()
    {
        var statements = Parse("if (true) x = 1;");
        Assert.Single(statements);
        var ifStmt = Assert.IsType<Stmt.If>(statements[0]);
        Assert.IsType<Expr.Literal>(ifStmt.Condition);
        Assert.Null(ifStmt.ElseBranch);
    }

    [Fact]
    public void If_WithBlock()
    {
        var statements = Parse("if (true) { x = 1; }");
        var ifStmt = Assert.IsType<Stmt.If>(statements[0]);
        Assert.IsType<Stmt.Block>(ifStmt.ThenBranch);
    }

    [Fact]
    public void If_WithElse()
    {
        var statements = Parse("if (true) x = 1; else x = 2;");
        var ifStmt = Assert.IsType<Stmt.If>(statements[0]);
        Assert.NotNull(ifStmt.ElseBranch);
    }

    [Fact]
    public void If_ElseIf()
    {
        var statements = Parse("if (a) x = 1; else if (b) x = 2; else x = 3;");
        var ifStmt = Assert.IsType<Stmt.If>(statements[0]);
        Assert.NotNull(ifStmt.ElseBranch);
        var elseIf = Assert.IsType<Stmt.If>(ifStmt.ElseBranch);
        Assert.NotNull(elseIf.ElseBranch);
    }

    [Fact]
    public void If_NestedCondition()
    {
        var statements = Parse("if (x > 5 && y < 10) { z = 1; }");
        var ifStmt = Assert.IsType<Stmt.If>(statements[0]);
        Assert.IsType<Expr.Logical>(ifStmt.Condition);
    }

    #endregion

    #region While Loops

    [Fact]
    public void While_Simple()
    {
        var statements = Parse("while (true) x++;");
        Assert.Single(statements);
        var whileStmt = Assert.IsType<Stmt.While>(statements[0]);
        Assert.IsType<Expr.Literal>(whileStmt.Condition);
    }

    [Fact]
    public void While_WithBlock()
    {
        var statements = Parse("while (i < 10) { i++; }");
        var whileStmt = Assert.IsType<Stmt.While>(statements[0]);
        Assert.IsType<Stmt.Block>(whileStmt.Body);
    }

    #endregion

    #region Do-While Loops

    [Fact]
    public void DoWhile_Simple()
    {
        var statements = Parse("do x++; while (x < 10);");
        Assert.Single(statements);
        var doWhile = Assert.IsType<Stmt.DoWhile>(statements[0]);
        Assert.IsType<Expr.Binary>(doWhile.Condition);
    }

    [Fact]
    public void DoWhile_WithBlock()
    {
        var statements = Parse("do { x++; } while (x < 10);");
        var doWhile = Assert.IsType<Stmt.DoWhile>(statements[0]);
        Assert.IsType<Stmt.Block>(doWhile.Body);
    }

    #endregion

    #region For Loops

    [Fact]
    public void For_Traditional()
    {
        // Note: Parser desugars for loops into while loops
        var statements = Parse("for (let i = 0; i < 10; i++) { }");
        // After desugaring, this becomes a block or sequence
        Assert.Single(statements);
    }

    #endregion

    #region For-Of Loops

    [Fact]
    public void ForOf_Simple()
    {
        var statements = Parse("for (let x of arr) { }");
        Assert.Single(statements);
        var forOf = Assert.IsType<Stmt.ForOf>(statements[0]);
        Assert.Equal("x", forOf.Variable.Lexeme);
    }

    #endregion

    #region For-In Loops

    [Fact]
    public void ForIn_Simple()
    {
        var statements = Parse("for (let key in obj) { }");
        Assert.Single(statements);
        var forIn = Assert.IsType<Stmt.ForIn>(statements[0]);
        Assert.Equal("key", forIn.Variable.Lexeme);
    }

    #endregion

    #region Switch Statements

    [Fact]
    public void Switch_Simple()
    {
        var source = """
            switch (x) {
                case 1:
                    y = 1;
                    break;
            }
            """;
        var statements = Parse(source);
        var switchStmt = Assert.IsType<Stmt.Switch>(statements[0]);
        Assert.Single(switchStmt.Cases);
    }

    [Fact]
    public void Switch_MultipleCases()
    {
        var source = """
            switch (x) {
                case 1:
                    break;
                case 2:
                    break;
            }
            """;
        var statements = Parse(source);
        var switchStmt = Assert.IsType<Stmt.Switch>(statements[0]);
        Assert.Equal(2, switchStmt.Cases.Count);
    }

    [Fact]
    public void Switch_DefaultCase()
    {
        var source = """
            switch (x) {
                default:
                    y = 0;
            }
            """;
        var statements = Parse(source);
        var switchStmt = Assert.IsType<Stmt.Switch>(statements[0]);
        Assert.NotNull(switchStmt.DefaultBody);
    }

    #endregion

    #region Try-Catch-Finally

    [Fact]
    public void Try_CatchOnly()
    {
        var source = """
            try {
                throw new Error();
            } catch (e) {
                console.log(e);
            }
            """;
        var statements = Parse(source);
        var tryStmt = Assert.IsType<Stmt.TryCatch>(statements[0]);
        Assert.NotNull(tryStmt.CatchBlock);
        Assert.Null(tryStmt.FinallyBlock);
    }

    [Fact]
    public void Try_FinallyOnly()
    {
        var source = """
            try {
                doSomething();
            } finally {
                cleanup();
            }
            """;
        var statements = Parse(source);
        var tryStmt = Assert.IsType<Stmt.TryCatch>(statements[0]);
        Assert.Null(tryStmt.CatchBlock);
        Assert.NotNull(tryStmt.FinallyBlock);
    }

    [Fact]
    public void Try_CatchAndFinally()
    {
        var source = """
            try {
                doSomething();
            } catch (e) {
                handleError(e);
            } finally {
                cleanup();
            }
            """;
        var statements = Parse(source);
        var tryStmt = Assert.IsType<Stmt.TryCatch>(statements[0]);
        Assert.NotNull(tryStmt.CatchBlock);
        Assert.NotNull(tryStmt.FinallyBlock);
    }

    [Fact]
    public void Try_CatchParameter()
    {
        var source = """
            try {
                throw new Error();
            } catch (error) {
                console.log(error);
            }
            """;
        var statements = Parse(source);
        var tryStmt = Assert.IsType<Stmt.TryCatch>(statements[0]);
        Assert.Equal("error", tryStmt.CatchParam!.Lexeme);
    }

    #endregion

    #region Jump Statements

    [Fact]
    public void Break_Simple()
    {
        var statements = Parse("break;");
        var breakStmt = Assert.IsType<Stmt.Break>(statements[0]);
        Assert.Null(breakStmt.Label);
    }

    [Fact]
    public void Break_WithLabel()
    {
        var statements = Parse("break outer;");
        var breakStmt = Assert.IsType<Stmt.Break>(statements[0]);
        Assert.Equal("outer", breakStmt.Label!.Lexeme);
    }

    [Fact]
    public void Continue_Simple()
    {
        var statements = Parse("continue;");
        var continueStmt = Assert.IsType<Stmt.Continue>(statements[0]);
        Assert.Null(continueStmt.Label);
    }

    [Fact]
    public void Continue_WithLabel()
    {
        var statements = Parse("continue loop;");
        var continueStmt = Assert.IsType<Stmt.Continue>(statements[0]);
        Assert.Equal("loop", continueStmt.Label!.Lexeme);
    }

    [Fact]
    public void Return_Empty()
    {
        var statements = Parse("return;");
        var returnStmt = Assert.IsType<Stmt.Return>(statements[0]);
        Assert.Null(returnStmt.Value);
    }

    [Fact]
    public void Return_WithValue()
    {
        var statements = Parse("return 42;");
        var returnStmt = Assert.IsType<Stmt.Return>(statements[0]);
        Assert.NotNull(returnStmt.Value);
        Assert.IsType<Expr.Literal>(returnStmt.Value);
    }

    [Fact]
    public void Throw_Simple()
    {
        var statements = Parse("throw new Error();");
        var throwStmt = Assert.IsType<Stmt.Throw>(statements[0]);
        Assert.IsType<Expr.New>(throwStmt.Value);
    }

    #endregion

    #region Labeled Statements

    [Fact]
    public void Labeled_Simple()
    {
        var statements = Parse("outer: while (true) { }");
        var labeled = Assert.IsType<Stmt.LabeledStatement>(statements[0]);
        Assert.Equal("outer", labeled.Label.Lexeme);
        Assert.IsType<Stmt.While>(labeled.Statement);
    }

    #endregion

    #region Block Statements

    [Fact]
    public void Block_Empty()
    {
        var statements = Parse("{ }");
        var block = Assert.IsType<Stmt.Block>(statements[0]);
        Assert.Empty(block.Statements);
    }

    [Fact]
    public void Block_SingleStatement()
    {
        var statements = Parse("{ x = 1; }");
        var block = Assert.IsType<Stmt.Block>(statements[0]);
        Assert.Single(block.Statements);
    }

    [Fact]
    public void Block_MultipleStatements()
    {
        var statements = Parse("{ x = 1; y = 2; z = 3; }");
        var block = Assert.IsType<Stmt.Block>(statements[0]);
        Assert.Equal(3, block.Statements.Count);
    }

    #endregion

    #region Variable Declarations

    [Fact]
    public void Var_Let()
    {
        var statements = Parse("let x = 5;");
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Equal("x", varStmt.Name.Lexeme);
    }

    [Fact]
    public void Var_Const()
    {
        var statements = Parse("const x = 5;");
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.NotNull(varStmt.Initializer);
    }

    [Fact]
    public void Var_WithType()
    {
        var statements = Parse("let x: number = 5;");
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Equal("number", varStmt.TypeAnnotation);
    }

    [Fact]
    public void Var_NoInitializer()
    {
        var statements = Parse("let x: number;");
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Null(varStmt.Initializer);
    }

    #endregion

    #region Expression Statements

    [Fact]
    public void ExpressionStatement_Call()
    {
        var statements = Parse("console.log(\"hello\");");
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        Assert.IsType<Expr.Call>(exprStmt.Expr);
    }

    [Fact]
    public void ExpressionStatement_Assignment()
    {
        var statements = Parse("x = 5;");
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        Assert.IsType<Expr.Assign>(exprStmt.Expr);
    }

    #endregion
}
