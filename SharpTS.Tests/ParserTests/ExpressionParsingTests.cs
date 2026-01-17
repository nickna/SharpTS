using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for expression parsing in the parser.
/// Covers unary, assignment, call, member access, and other expression types.
/// </summary>
public class ExpressionParsingTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    private static Expr ParseExpression(string source)
    {
        var statements = Parse(source);
        Assert.Single(statements);
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        return exprStmt.Expr;
    }

    #endregion

    #region Unary Expressions

    [Fact]
    public void Unary_LogicalNot()
    {
        var expr = ParseExpression("!x;");
        var unary = Assert.IsType<Expr.Unary>(expr);
        Assert.Equal(TokenType.BANG, unary.Operator.Type);
        var variable = Assert.IsType<Expr.Variable>(unary.Right);
        Assert.Equal("x", variable.Name.Lexeme);
    }

    [Fact]
    public void Unary_Negation()
    {
        var expr = ParseExpression("-x;");
        var unary = Assert.IsType<Expr.Unary>(expr);
        Assert.Equal(TokenType.MINUS, unary.Operator.Type);
    }

    [Fact]
    public void Unary_BitwiseNot()
    {
        var expr = ParseExpression("~x;");
        var unary = Assert.IsType<Expr.Unary>(expr);
        Assert.Equal(TokenType.TILDE, unary.Operator.Type);
    }

    [Fact]
    public void Unary_PrefixIncrement()
    {
        var expr = ParseExpression("++x;");
        var prefix = Assert.IsType<Expr.PrefixIncrement>(expr);
        Assert.Equal(TokenType.PLUS_PLUS, prefix.Operator.Type);
        Assert.IsType<Expr.Variable>(prefix.Operand);
    }

    [Fact]
    public void Unary_PrefixDecrement()
    {
        var expr = ParseExpression("--x;");
        var prefix = Assert.IsType<Expr.PrefixIncrement>(expr);
        Assert.Equal(TokenType.MINUS_MINUS, prefix.Operator.Type);
    }

    [Fact]
    public void Unary_PostfixIncrement()
    {
        var expr = ParseExpression("x++;");
        var postfix = Assert.IsType<Expr.PostfixIncrement>(expr);
        Assert.Equal(TokenType.PLUS_PLUS, postfix.Operator.Type);
    }

    [Fact]
    public void Unary_PostfixDecrement()
    {
        var expr = ParseExpression("x--;");
        var postfix = Assert.IsType<Expr.PostfixIncrement>(expr);
        Assert.Equal(TokenType.MINUS_MINUS, postfix.Operator.Type);
    }

    [Fact]
    public void Unary_Typeof()
    {
        var expr = ParseExpression("typeof x;");
        var unary = Assert.IsType<Expr.Unary>(expr);
        Assert.Equal(TokenType.TYPEOF, unary.Operator.Type);
    }

    [Fact]
    public void Unary_DoubleNot()
    {
        var expr = ParseExpression("!!x;");
        var outer = Assert.IsType<Expr.Unary>(expr);
        Assert.Equal(TokenType.BANG, outer.Operator.Type);
        var inner = Assert.IsType<Expr.Unary>(outer.Right);
        Assert.Equal(TokenType.BANG, inner.Operator.Type);
    }

    #endregion

    #region Assignment Expressions

    [Fact]
    public void Assignment_Simple()
    {
        var expr = ParseExpression("x = 5;");
        var assign = Assert.IsType<Expr.Assign>(expr);
        Assert.Equal("x", assign.Name.Lexeme);
        Assert.IsType<Expr.Literal>(assign.Value);
    }

    [Fact]
    public void Assignment_Compound_PlusEqual()
    {
        var expr = ParseExpression("x += 5;");
        var assign = Assert.IsType<Expr.CompoundAssign>(expr);
        Assert.Equal("x", assign.Name.Lexeme);
        Assert.Equal(TokenType.PLUS_EQUAL, assign.Operator.Type);
    }

    [Fact]
    public void Assignment_Compound_MinusEqual()
    {
        var expr = ParseExpression("x -= 5;");
        var assign = Assert.IsType<Expr.CompoundAssign>(expr);
        Assert.Equal(TokenType.MINUS_EQUAL, assign.Operator.Type);
    }

    [Fact]
    public void Assignment_ToProperty()
    {
        var expr = ParseExpression("obj.prop = 5;");
        var set = Assert.IsType<Expr.Set>(expr);
        Assert.Equal("prop", set.Name.Lexeme);
    }

    [Fact]
    public void Assignment_ToIndex()
    {
        var expr = ParseExpression("arr[0] = 5;");
        var setIndex = Assert.IsType<Expr.SetIndex>(expr);
        Assert.IsType<Expr.Variable>(setIndex.Object);
    }

    #endregion

    #region Call Expressions

    [Fact]
    public void Call_NoArguments()
    {
        var expr = ParseExpression("foo();");
        var call = Assert.IsType<Expr.Call>(expr);
        var callee = Assert.IsType<Expr.Variable>(call.Callee);
        Assert.Equal("foo", callee.Name.Lexeme);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void Call_SingleArgument()
    {
        var expr = ParseExpression("foo(x);");
        var call = Assert.IsType<Expr.Call>(expr);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void Call_MultipleArguments()
    {
        var expr = ParseExpression("foo(a, b, c);");
        var call = Assert.IsType<Expr.Call>(expr);
        Assert.Equal(3, call.Arguments.Count);
    }

    [Fact]
    public void Call_WithSpread()
    {
        var expr = ParseExpression("foo(...args);");
        var call = Assert.IsType<Expr.Call>(expr);
        Assert.Single(call.Arguments);
        Assert.IsType<Expr.Spread>(call.Arguments[0]);
    }

    [Fact]
    public void Call_Chained()
    {
        var expr = ParseExpression("foo()();");
        var outerCall = Assert.IsType<Expr.Call>(expr);
        Assert.IsType<Expr.Call>(outerCall.Callee);
    }

    [Fact]
    public void Call_OnMethod()
    {
        var expr = ParseExpression("obj.method();");
        var call = Assert.IsType<Expr.Call>(expr);
        Assert.IsType<Expr.Get>(call.Callee);
    }

    [Fact]
    public void Call_OptionalChaining()
    {
        var expr = ParseExpression("obj?.method();");
        var call = Assert.IsType<Expr.Call>(expr);
        var get = Assert.IsType<Expr.Get>(call.Callee);
        Assert.True(get.Optional);
    }

    #endregion

    #region Member Access

    [Fact]
    public void MemberAccess_Dot()
    {
        var expr = ParseExpression("obj.prop;");
        var get = Assert.IsType<Expr.Get>(expr);
        Assert.Equal("prop", get.Name.Lexeme);
        Assert.False(get.Optional);
    }

    [Fact]
    public void MemberAccess_OptionalChaining()
    {
        var expr = ParseExpression("obj?.prop;");
        var get = Assert.IsType<Expr.Get>(expr);
        Assert.Equal("prop", get.Name.Lexeme);
        Assert.True(get.Optional);
    }

    [Fact]
    public void MemberAccess_Computed()
    {
        var expr = ParseExpression("obj[key];");
        var getIndex = Assert.IsType<Expr.GetIndex>(expr);
        Assert.IsType<Expr.Variable>(getIndex.Object);
        Assert.IsType<Expr.Variable>(getIndex.Index);
    }

    [Fact]
    public void MemberAccess_Chained()
    {
        var expr = ParseExpression("a.b.c;");
        var outerGet = Assert.IsType<Expr.Get>(expr);
        Assert.Equal("c", outerGet.Name.Lexeme);
        var innerGet = Assert.IsType<Expr.Get>(outerGet.Object);
        Assert.Equal("b", innerGet.Name.Lexeme);
    }

    #endregion

    #region Type Assertions

    [Fact]
    public void TypeAssertion_As()
    {
        var expr = ParseExpression("x as number;");
        var asExpr = Assert.IsType<Expr.TypeAssertion>(expr);
        Assert.IsType<Expr.Variable>(asExpr.Expression);
        Assert.Equal("number", asExpr.TargetType);
    }

    [Fact]
    public void TypeAssertion_NonNullAssertion()
    {
        var expr = ParseExpression("x!;");
        var nonNull = Assert.IsType<Expr.NonNullAssertion>(expr);
        Assert.IsType<Expr.Variable>(nonNull.Expression);
    }

    #endregion

    #region Literal Expressions

    [Fact]
    public void Literal_Number()
    {
        var expr = ParseExpression("42;");
        var literal = Assert.IsType<Expr.Literal>(expr);
        Assert.Equal(42.0, literal.Value);
    }

    [Fact]
    public void Literal_String()
    {
        // String literals at the start of a file are parsed as directive prologue (like "use strict")
        // This is correct JavaScript/TypeScript behavior
        var statements = Parse("\"hello\";");
        Assert.Single(statements);
        var directive = Assert.IsType<Stmt.Directive>(statements[0]);
        Assert.Equal("hello", directive.Value);
    }

    [Fact]
    public void Literal_String_InExpression()
    {
        // Test string literal in a non-directive position (after a non-directive statement)
        var statements = Parse("0; \"hello\";");
        Assert.Equal(2, statements.Count);
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[1]);
        var literal = Assert.IsType<Expr.Literal>(exprStmt.Expr);
        Assert.Equal("hello", literal.Value);
    }

    [Fact]
    public void Literal_True()
    {
        var expr = ParseExpression("true;");
        var literal = Assert.IsType<Expr.Literal>(expr);
        Assert.Equal(true, literal.Value);
    }

    [Fact]
    public void Literal_False()
    {
        var expr = ParseExpression("false;");
        var literal = Assert.IsType<Expr.Literal>(expr);
        Assert.Equal(false, literal.Value);
    }

    [Fact]
    public void Literal_Null()
    {
        var expr = ParseExpression("null;");
        var literal = Assert.IsType<Expr.Literal>(expr);
        Assert.Null(literal.Value);
    }

    #endregion

    #region Array Literals

    [Fact]
    public void ArrayLiteral_Empty()
    {
        var expr = ParseExpression("[];");
        var arr = Assert.IsType<Expr.ArrayLiteral>(expr);
        Assert.Empty(arr.Elements);
    }

    [Fact]
    public void ArrayLiteral_MultipleElements()
    {
        var expr = ParseExpression("[1, 2, 3];");
        var arr = Assert.IsType<Expr.ArrayLiteral>(expr);
        Assert.Equal(3, arr.Elements.Count);
    }

    [Fact]
    public void ArrayLiteral_WithSpread()
    {
        var expr = ParseExpression("[...arr, 4];");
        var arr = Assert.IsType<Expr.ArrayLiteral>(expr);
        Assert.Equal(2, arr.Elements.Count);
        Assert.IsType<Expr.Spread>(arr.Elements[0]);
    }

    #endregion

    #region Object Literals

    [Fact]
    public void ObjectLiteral_Empty()
    {
        var expr = ParseExpression("({});");
        var grouping = Assert.IsType<Expr.Grouping>(expr);
        Assert.IsType<Expr.ObjectLiteral>(grouping.Expression);
    }

    [Fact]
    public void ObjectLiteral_SingleProperty()
    {
        var expr = ParseExpression("({ x: 1 });");
        var grouping = Assert.IsType<Expr.Grouping>(expr);
        var obj = Assert.IsType<Expr.ObjectLiteral>(grouping.Expression);
        Assert.Single(obj.Properties);
    }

    #endregion

    #region New Expressions

    [Fact]
    public void New_Simple()
    {
        var expr = ParseExpression("new Foo();");
        var newExpr = Assert.IsType<Expr.New>(expr);
        Assert.Equal("Foo", newExpr.ClassName.Lexeme);
    }

    [Fact]
    public void New_WithArguments()
    {
        var expr = ParseExpression("new Foo(1, 2);");
        var newExpr = Assert.IsType<Expr.New>(expr);
        Assert.Equal(2, newExpr.Arguments.Count);
    }

    #endregion

    #region This and Super

    [Fact]
    public void This_Simple()
    {
        var expr = ParseExpression("this;");
        Assert.IsType<Expr.This>(expr);
    }

    [Fact]
    public void This_PropertyAccess()
    {
        var expr = ParseExpression("this.prop;");
        var get = Assert.IsType<Expr.Get>(expr);
        Assert.IsType<Expr.This>(get.Object);
    }

    [Fact]
    public void Super_PropertyAccess()
    {
        var expr = ParseExpression("super.method;");
        var superExpr = Assert.IsType<Expr.Super>(expr);
        Assert.Equal("method", superExpr.Method!.Lexeme);
    }

    #endregion

    #region Template Literals

    [Fact]
    public void TemplateLiteral_Simple()
    {
        var expr = ParseExpression("`hello`;");
        var template = Assert.IsType<Expr.TemplateLiteral>(expr);
        Assert.Single(template.Strings);
        Assert.Empty(template.Expressions);
    }

    [Fact]
    public void TemplateLiteral_WithInterpolation()
    {
        var expr = ParseExpression("`hello ${name}`;");
        var template = Assert.IsType<Expr.TemplateLiteral>(expr);
        Assert.Equal(2, template.Strings.Count);
        Assert.Single(template.Expressions);
    }

    #endregion

    #region Await and Yield

    [Fact]
    public void Await_Simple()
    {
        var expr = ParseExpression("await promise;");
        var awaitExpr = Assert.IsType<Expr.Await>(expr);
        Assert.IsType<Expr.Variable>(awaitExpr.Expression);
    }

    [Fact]
    public void Yield_Simple()
    {
        var expr = ParseExpression("yield x;");
        var yieldExpr = Assert.IsType<Expr.Yield>(expr);
        Assert.IsType<Expr.Variable>(yieldExpr.Value);
    }

    [Fact]
    public void Yield_Delegate()
    {
        var expr = ParseExpression("yield* gen;");
        var yieldExpr = Assert.IsType<Expr.Yield>(expr);
        Assert.True(yieldExpr.IsDelegating);
    }

    #endregion
}
