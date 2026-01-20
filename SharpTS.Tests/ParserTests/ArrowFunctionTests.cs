using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for arrow function parsing.
/// Covers various arrow function forms: single param, multiple params, expression body, block body.
/// </summary>
public class ArrowFunctionTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    private static Expr.ArrowFunction ParseArrowFromVarOrConst(string source)
    {
        var statements = Parse(source);
        Assert.Single(statements);
        var initializer = statements[0] switch
        {
            Stmt.Var v => v.Initializer,
            Stmt.Const c => c.Initializer,
            _ => throw new InvalidOperationException($"Expected Var or Const, got {statements[0].GetType()}")
        };
        return Assert.IsType<Expr.ArrowFunction>(initializer);
    }

    #endregion

    #region Single Parameter

    [Fact]
    public void Arrow_SingleParamNoParens()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = x => x + 1;");
        Assert.Single(arrow.Parameters);
        Assert.Equal("x", arrow.Parameters[0].Name.Lexeme);
        Assert.NotNull(arrow.ExpressionBody);
    }

    [Fact]
    public void Arrow_SingleParamWithParens()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (x) => x + 1;");
        Assert.Single(arrow.Parameters);
    }

    [Fact]
    public void Arrow_SingleParamWithType()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (x: number) => x * 2;");
        Assert.Single(arrow.Parameters);
        Assert.Equal("number", arrow.Parameters[0].Type);
    }

    #endregion

    #region Multiple Parameters

    [Fact]
    public void Arrow_TwoParams()
    {
        var arrow = ParseArrowFromVarOrConst("const add = (a, b) => a + b;");
        Assert.Equal(2, arrow.Parameters.Count);
    }

    [Fact]
    public void Arrow_ThreeParams()
    {
        var arrow = ParseArrowFromVarOrConst("const sum = (a, b, c) => a + b + c;");
        Assert.Equal(3, arrow.Parameters.Count);
    }

    [Fact]
    public void Arrow_ParamsWithTypes()
    {
        var arrow = ParseArrowFromVarOrConst("const add = (a: number, b: number) => a + b;");
        Assert.Equal(2, arrow.Parameters.Count);
        Assert.Equal("number", arrow.Parameters[0].Type);
        Assert.Equal("number", arrow.Parameters[1].Type);
    }

    #endregion

    #region No Parameters

    [Fact]
    public void Arrow_NoParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = () => 42;");
        Assert.Empty(arrow.Parameters);
    }

    [Fact]
    public void Arrow_NoParamsWithBlock()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = () => { return 42; };");
        Assert.Empty(arrow.Parameters);
        Assert.Null(arrow.ExpressionBody);
        Assert.NotNull(arrow.BlockBody);
    }

    #endregion

    #region Expression Body vs Block Body

    [Fact]
    public void Arrow_ExpressionBody()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = x => x * 2;");
        Assert.NotNull(arrow.ExpressionBody);
        Assert.Null(arrow.BlockBody);
    }

    [Fact]
    public void Arrow_BlockBody()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = x => { return x * 2; };");
        Assert.Null(arrow.ExpressionBody);
        Assert.NotNull(arrow.BlockBody);
    }

    [Fact]
    public void Arrow_BlockBodyMultipleStatements()
    {
        var source = """
            const fn = (x) => {
                const y = x * 2;
                return y + 1;
            };
            """;
        var arrow = ParseArrowFromVarOrConst(source);
        Assert.NotNull(arrow.BlockBody);
        Assert.Equal(2, arrow.BlockBody.Count);
    }

    #endregion

    #region Return Types

    [Fact]
    public void Arrow_WithReturnType()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (x: number): number => x * 2;");
        Assert.Equal("number", arrow.ReturnType);
    }

    [Fact]
    public void Arrow_NoReturnType()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (x) => x * 2;");
        Assert.Null(arrow.ReturnType);
    }

    #endregion

    #region Async Arrow Functions

    [Fact]
    public void Arrow_Async_ExpressionBody()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = async (x) => await fetch(x);");
        Assert.True(arrow.IsAsync);
    }

    [Fact]
    public void Arrow_Async_BlockBody()
    {
        var source = """
            const fn = async (url) => {
                const response = await fetch(url);
                return response.json();
            };
            """;
        var arrow = ParseArrowFromVarOrConst(source);
        Assert.True(arrow.IsAsync);
        Assert.NotNull(arrow.BlockBody);
    }

    [Fact]
    public void Arrow_Async_NoParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = async () => await getData();");
        Assert.True(arrow.IsAsync);
        Assert.Empty(arrow.Parameters);
    }

    #endregion

    #region Default Parameters

    [Fact]
    public void Arrow_DefaultParam()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (x = 5) => x * 2;");
        Assert.Single(arrow.Parameters);
        Assert.NotNull(arrow.Parameters[0].DefaultValue);
    }

    [Fact]
    public void Arrow_MultipleDefaultParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (a = 1, b = 2) => a + b;");
        Assert.NotNull(arrow.Parameters[0].DefaultValue);
        Assert.NotNull(arrow.Parameters[1].DefaultValue);
    }

    #endregion

    #region Rest Parameters

    [Fact]
    public void Arrow_RestParam()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (...args) => args.length;");
        Assert.Single(arrow.Parameters);
        Assert.True(arrow.Parameters[0].IsRest);
    }

    [Fact]
    public void Arrow_RestParamWithRegular()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (first, ...rest) => rest;");
        Assert.Equal(2, arrow.Parameters.Count);
        Assert.False(arrow.Parameters[0].IsRest);
        Assert.True(arrow.Parameters[1].IsRest);
    }

    #endregion

    #region Arrow Functions as Arguments

    [Fact]
    public void Arrow_AsCallbackArgument()
    {
        var statements = Parse("arr.map(x => x * 2);");
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        var call = Assert.IsType<Expr.Call>(exprStmt.Expr);
        Assert.Single(call.Arguments);
        Assert.IsType<Expr.ArrowFunction>(call.Arguments[0]);
    }

    [Fact]
    public void Arrow_MultipleCallbackArguments()
    {
        var statements = Parse("arr.reduce((acc, x) => acc + x, 0);");
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        var call = Assert.IsType<Expr.Call>(exprStmt.Expr);
        Assert.Equal(2, call.Arguments.Count);
        Assert.IsType<Expr.ArrowFunction>(call.Arguments[0]);
        Assert.IsType<Expr.Literal>(call.Arguments[1]);
    }

    #endregion
}
