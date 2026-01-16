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
        return parser.Parse();
    }

    private static Stmt.Var ParseVarWithArrow(string source)
    {
        var statements = Parse(source);
        Assert.Single(statements);
        return Assert.IsType<Stmt.Var>(statements[0]);
    }

    #endregion

    #region Single Parameter

    [Fact]
    public void Arrow_SingleParamNoParens()
    {
        var varStmt = ParseVarWithArrow("const fn = x => x + 1;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Single(arrow.Parameters);
        Assert.Equal("x", arrow.Parameters[0].Name.Lexeme);
        Assert.NotNull(arrow.ExpressionBody);
    }

    [Fact]
    public void Arrow_SingleParamWithParens()
    {
        var varStmt = ParseVarWithArrow("const fn = (x) => x + 1;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Single(arrow.Parameters);
    }

    [Fact]
    public void Arrow_SingleParamWithType()
    {
        var varStmt = ParseVarWithArrow("const fn = (x: number) => x * 2;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Single(arrow.Parameters);
        Assert.Equal("number", arrow.Parameters[0].Type);
    }

    #endregion

    #region Multiple Parameters

    [Fact]
    public void Arrow_TwoParams()
    {
        var varStmt = ParseVarWithArrow("const add = (a, b) => a + b;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Equal(2, arrow.Parameters.Count);
    }

    [Fact]
    public void Arrow_ThreeParams()
    {
        var varStmt = ParseVarWithArrow("const sum = (a, b, c) => a + b + c;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Equal(3, arrow.Parameters.Count);
    }

    [Fact]
    public void Arrow_ParamsWithTypes()
    {
        var varStmt = ParseVarWithArrow("const add = (a: number, b: number) => a + b;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Equal(2, arrow.Parameters.Count);
        Assert.Equal("number", arrow.Parameters[0].Type);
        Assert.Equal("number", arrow.Parameters[1].Type);
    }

    #endregion

    #region No Parameters

    [Fact]
    public void Arrow_NoParams()
    {
        var varStmt = ParseVarWithArrow("const fn = () => 42;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Empty(arrow.Parameters);
    }

    [Fact]
    public void Arrow_NoParamsWithBlock()
    {
        var varStmt = ParseVarWithArrow("const fn = () => { return 42; };");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Empty(arrow.Parameters);
        Assert.Null(arrow.ExpressionBody);
        Assert.NotNull(arrow.BlockBody);
    }

    #endregion

    #region Expression Body vs Block Body

    [Fact]
    public void Arrow_ExpressionBody()
    {
        var varStmt = ParseVarWithArrow("const fn = x => x * 2;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.NotNull(arrow.ExpressionBody);
        Assert.Null(arrow.BlockBody);
    }

    [Fact]
    public void Arrow_BlockBody()
    {
        var varStmt = ParseVarWithArrow("const fn = x => { return x * 2; };");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
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
        var varStmt = ParseVarWithArrow(source);
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.NotNull(arrow.BlockBody);
        Assert.Equal(2, arrow.BlockBody.Count);
    }

    #endregion

    #region Return Types

    [Fact]
    public void Arrow_WithReturnType()
    {
        var varStmt = ParseVarWithArrow("const fn = (x: number): number => x * 2;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Equal("number", arrow.ReturnType);
    }

    [Fact]
    public void Arrow_NoReturnType()
    {
        var varStmt = ParseVarWithArrow("const fn = (x) => x * 2;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Null(arrow.ReturnType);
    }

    #endregion

    #region Async Arrow Functions

    [Fact]
    public void Arrow_Async_ExpressionBody()
    {
        var varStmt = ParseVarWithArrow("const fn = async (x) => await fetch(x);");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
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
        var varStmt = ParseVarWithArrow(source);
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.True(arrow.IsAsync);
        Assert.NotNull(arrow.BlockBody);
    }

    [Fact]
    public void Arrow_Async_NoParams()
    {
        var varStmt = ParseVarWithArrow("const fn = async () => await getData();");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.True(arrow.IsAsync);
        Assert.Empty(arrow.Parameters);
    }

    #endregion

    #region Default Parameters

    [Fact]
    public void Arrow_DefaultParam()
    {
        var varStmt = ParseVarWithArrow("const fn = (x = 5) => x * 2;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Single(arrow.Parameters);
        Assert.NotNull(arrow.Parameters[0].DefaultValue);
    }

    [Fact]
    public void Arrow_MultipleDefaultParams()
    {
        var varStmt = ParseVarWithArrow("const fn = (a = 1, b = 2) => a + b;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.NotNull(arrow.Parameters[0].DefaultValue);
        Assert.NotNull(arrow.Parameters[1].DefaultValue);
    }

    #endregion

    #region Rest Parameters

    [Fact]
    public void Arrow_RestParam()
    {
        var varStmt = ParseVarWithArrow("const fn = (...args) => args.length;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
        Assert.Single(arrow.Parameters);
        Assert.True(arrow.Parameters[0].IsRest);
    }

    [Fact]
    public void Arrow_RestParamWithRegular()
    {
        var varStmt = ParseVarWithArrow("const fn = (first, ...rest) => rest;");
        var arrow = Assert.IsType<Expr.ArrowFunction>(varStmt.Initializer);
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
