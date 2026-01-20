using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for parsing the 'satisfies' operator (TypeScript 4.9+).
/// </summary>
public class SatisfiesParsingTests
{
    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    [Fact]
    public void Satisfies_ParsesBasicExpression()
    {
        var source = "const x = 42 satisfies number;";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var sat = (Expr.Satisfies)constStmt.Initializer;
        Assert.Equal("number", sat.ConstraintType);
    }

    [Fact]
    public void Satisfies_ParsesObjectType()
    {
        var source = "const obj = { x: 1 } satisfies { x: number };";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var sat = (Expr.Satisfies)constStmt.Initializer;
        Assert.Contains("x", sat.ConstraintType);
        Assert.Contains("number", sat.ConstraintType);
    }

    [Fact]
    public void Satisfies_ParsesUnionType()
    {
        var source = "const x = \"hello\" satisfies string | number;";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var sat = (Expr.Satisfies)constStmt.Initializer;
        Assert.Contains("string", sat.ConstraintType);
        Assert.Contains("number", sat.ConstraintType);
    }

    [Fact]
    public void Satisfies_ParsesChained()
    {
        var source = "const x = 42 satisfies number satisfies number;";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];

        // Outer should be satisfies
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var outerSat = (Expr.Satisfies)constStmt.Initializer;

        // Inner should also be satisfies
        Assert.IsType<Expr.Satisfies>(outerSat.Expression);
    }

    [Fact]
    public void Satisfies_ParsesArrayType()
    {
        var source = "const arr = [1, 2] satisfies number[];";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var sat = (Expr.Satisfies)constStmt.Initializer;
        Assert.Contains("number[]", sat.ConstraintType);
    }

    [Fact]
    public void Satisfies_InnerExpressionPreserved()
    {
        var source = "const x = 42 satisfies number;";
        var statements = Parse(source);

        var constStmt = (Stmt.Const)statements[0];
        var sat = (Expr.Satisfies)constStmt.Initializer;
        Assert.IsType<Expr.Literal>(sat.Expression);
        var literal = (Expr.Literal)sat.Expression;
        Assert.Equal(42.0, literal.Value);
    }

    [Fact]
    public void Satisfies_ParsesAfterAs()
    {
        // 'as const' followed by 'satisfies' - should parse correctly
        var source = "const x = [1, 2] as const satisfies number[];";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];

        // Outer should be satisfies
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var sat = (Expr.Satisfies)constStmt.Initializer;

        // Inner should be TypeAssertion (as const)
        Assert.IsType<Expr.TypeAssertion>(sat.Expression);
    }

    [Fact]
    public void Satisfies_ParsesGenericType()
    {
        var source = "const arr = [1, 2] satisfies Array<number>;";
        var statements = Parse(source);

        Assert.Single(statements);
        Assert.IsType<Stmt.Const>(statements[0]);
        var constStmt = (Stmt.Const)statements[0];
        Assert.IsType<Expr.Satisfies>(constStmt.Initializer);
        var sat = (Expr.Satisfies)constStmt.Initializer;
        Assert.Contains("Array<number>", sat.ConstraintType);
    }
}
