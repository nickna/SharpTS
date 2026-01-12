using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Parser-level tests for the non-null assertion operator (postfix !).
/// Verifies correct AST construction.
/// </summary>
public class NonNullAssertionParsingTests
{
    private List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private Expr GetExpressionFromStmt(Stmt stmt)
    {
        return ((Stmt.Expression)stmt).Expr;
    }

    private Expr GetInitializerFromVar(Stmt stmt)
    {
        return ((Stmt.Var)stmt).Initializer!;
    }

    #region Basic Parsing

    [Fact]
    public void Parser_NonNullAssertion_OnVariable()
    {
        var source = "x!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.NonNullAssertion>(expr);
        var nna = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.Variable>(nna.Expression);
        var variable = (Expr.Variable)nna.Expression;
        Assert.Equal("x", variable.Name.Lexeme);
    }

    [Fact]
    public void Parser_NonNullAssertion_OnPropertyAccess()
    {
        var source = "obj.prop!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.NonNullAssertion>(expr);
        var nna = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.Get>(nna.Expression);
    }

    [Fact]
    public void Parser_NonNullAssertion_OnMethodCall()
    {
        var source = "getValue()!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.NonNullAssertion>(expr);
        var nna = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.Call>(nna.Expression);
    }

    [Fact]
    public void Parser_NonNullAssertion_OnArrayIndex()
    {
        var source = "arr[0]!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.NonNullAssertion>(expr);
        var nna = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.GetIndex>(nna.Expression);
    }

    #endregion

    #region Chained Operations

    [Fact]
    public void Parser_NonNullAssertion_BeforePropertyAccess()
    {
        var source = "x!.length;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        // Should be: Get(NonNullAssertion(Variable))
        Assert.IsType<Expr.Get>(expr);
        var get = (Expr.Get)expr;

        Assert.IsType<Expr.NonNullAssertion>(get.Object);
        var nna = (Expr.NonNullAssertion)get.Object;

        Assert.IsType<Expr.Variable>(nna.Expression);
    }

    [Fact]
    public void Parser_NonNullAssertion_BeforeMethodCall()
    {
        var source = "x!.toString();";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        // Should be: Call(Get(NonNullAssertion(Variable)))
        Assert.IsType<Expr.Call>(expr);
        var call = (Expr.Call)expr;

        Assert.IsType<Expr.Get>(call.Callee);
        var get = (Expr.Get)call.Callee;

        Assert.IsType<Expr.NonNullAssertion>(get.Object);
    }

    [Fact]
    public void Parser_NonNullAssertion_Multiple()
    {
        var source = "a!.b!.c;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        // Should be: Get(NonNullAssertion(Get(NonNullAssertion(Variable))))
        Assert.IsType<Expr.Get>(expr);
        var get1 = (Expr.Get)expr;
        Assert.Equal("c", get1.Name.Lexeme);

        Assert.IsType<Expr.NonNullAssertion>(get1.Object);
        var nna1 = (Expr.NonNullAssertion)get1.Object;

        Assert.IsType<Expr.Get>(nna1.Expression);
        var get2 = (Expr.Get)nna1.Expression;
        Assert.Equal("b", get2.Name.Lexeme);

        Assert.IsType<Expr.NonNullAssertion>(get2.Object);
        var nna2 = (Expr.NonNullAssertion)get2.Object;

        Assert.IsType<Expr.Variable>(nna2.Expression);
    }

    [Fact]
    public void Parser_NonNullAssertion_Consecutive()
    {
        var source = "x!!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        // Should be: NonNullAssertion(NonNullAssertion(Variable))
        Assert.IsType<Expr.NonNullAssertion>(expr);
        var outer = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.NonNullAssertion>(outer.Expression);
        var inner = (Expr.NonNullAssertion)outer.Expression;

        Assert.IsType<Expr.Variable>(inner.Expression);
    }

    #endregion

    #region Precedence

    [Fact]
    public void Parser_NonNullAssertion_InBinaryExpression()
    {
        var source = "let z = x! + y!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        // Should be: Binary(NonNullAssertion(x), +, NonNullAssertion(y))
        Assert.IsType<Expr.Binary>(init);
        var binary = (Expr.Binary)init;

        Assert.IsType<Expr.NonNullAssertion>(binary.Left);
        Assert.IsType<Expr.NonNullAssertion>(binary.Right);
    }

    [Fact]
    public void Parser_NonNullAssertion_AfterTypeAssertion()
    {
        var source = "(x as string)!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        // Should be: NonNullAssertion(Grouping(TypeAssertion(Variable)))
        Assert.IsType<Expr.NonNullAssertion>(expr);
        var nna = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.Grouping>(nna.Expression);
        var group = (Expr.Grouping)nna.Expression;

        Assert.IsType<Expr.TypeAssertion>(group.Expression);
    }

    [Fact]
    public void Parser_NonNullAssertion_BeforePostfixIncrement_Fails()
    {
        // x!++ is invalid - can't increment the result of non-null assertion
        // But x! followed by something else should parse
        var source = "x!; y++;";
        var statements = Parse(source);

        Assert.Equal(2, statements.Count);
        Assert.IsType<Expr.NonNullAssertion>(GetExpressionFromStmt(statements[0]));
        Assert.IsType<Expr.PostfixIncrement>(GetExpressionFromStmt(statements[1]));
    }

    #endregion

    #region Combined with Other Operators

    [Fact]
    public void Parser_NonNullAssertion_InTernary()
    {
        var source = "let z = x! > 0 ? x! : y!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.Ternary>(init);
        var ternary = (Expr.Ternary)init;

        // Condition should contain NonNullAssertion
        Assert.IsType<Expr.Binary>(ternary.Condition);
        var cond = (Expr.Binary)ternary.Condition;
        Assert.IsType<Expr.NonNullAssertion>(cond.Left);

        // Then and else branches should be NonNullAssertions
        Assert.IsType<Expr.NonNullAssertion>(ternary.ThenBranch);
        Assert.IsType<Expr.NonNullAssertion>(ternary.ElseBranch);
    }

    [Fact]
    public void Parser_NonNullAssertion_WithOptionalChaining()
    {
        var source = "obj?.prop!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        // Should be: NonNullAssertion(Get(Variable, Optional=true))
        Assert.IsType<Expr.NonNullAssertion>(expr);
        var nna = (Expr.NonNullAssertion)expr;

        Assert.IsType<Expr.Get>(nna.Expression);
        var get = (Expr.Get)nna.Expression;
        Assert.True(get.Optional);
    }

    [Fact]
    public void Parser_NonNullAssertion_InNullishCoalescing()
    {
        var source = "let z = x! ?? y;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        // Should be: NullishCoalescing(NonNullAssertion(x), y)
        Assert.IsType<Expr.NullishCoalescing>(init);
        var nc = (Expr.NullishCoalescing)init;

        Assert.IsType<Expr.NonNullAssertion>(nc.Left);
    }

    #endregion

    #region In Various Contexts

    [Fact]
    public void Parser_NonNullAssertion_InArrayLiteral()
    {
        var source = "let arr = [x!, y!];";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.ArrayLiteral>(init);
        var array = (Expr.ArrayLiteral)init;

        Assert.Equal(2, array.Elements.Count);
        Assert.IsType<Expr.NonNullAssertion>(array.Elements[0]);
        Assert.IsType<Expr.NonNullAssertion>(array.Elements[1]);
    }

    [Fact]
    public void Parser_NonNullAssertion_InObjectLiteral()
    {
        var source = "let obj = { a: x!, b: y! };";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.ObjectLiteral>(init);
        var obj = (Expr.ObjectLiteral)init;

        Assert.Equal(2, obj.Properties.Count);
        Assert.IsType<Expr.NonNullAssertion>(obj.Properties[0].Value);
        Assert.IsType<Expr.NonNullAssertion>(obj.Properties[1].Value);
    }

    [Fact]
    public void Parser_NonNullAssertion_InFunctionCall()
    {
        var source = "fn(x!, y!);";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.Call>(expr);
        var call = (Expr.Call)expr;

        Assert.Equal(2, call.Arguments.Count);
        Assert.IsType<Expr.NonNullAssertion>(call.Arguments[0]);
        Assert.IsType<Expr.NonNullAssertion>(call.Arguments[1]);
    }

    [Fact]
    public void Parser_NonNullAssertion_InArrowFunction()
    {
        var source = "let fn = (x: string | null) => x!.length;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.ArrowFunction>(init);
        var arrow = (Expr.ArrowFunction)init;

        Assert.NotNull(arrow.ExpressionBody);

        // Expression body should be Get(NonNullAssertion(Variable))
        Assert.IsType<Expr.Get>(arrow.ExpressionBody);
        var get = (Expr.Get)arrow.ExpressionBody;

        Assert.IsType<Expr.NonNullAssertion>(get.Object);
    }

    [Fact]
    public void Parser_NonNullAssertion_InSingleParamArrowWithoutParens()
    {
        // Single-parameter arrow function without parentheses
        var source = "let fn = x => x!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.ArrowFunction>(init);
        var arrow = (Expr.ArrowFunction)init;

        Assert.Single(arrow.Parameters);
        Assert.Equal("x", arrow.Parameters[0].Name.Lexeme);
        Assert.NotNull(arrow.ExpressionBody);
        Assert.IsType<Expr.NonNullAssertion>(arrow.ExpressionBody);
    }

    [Fact]
    public void Parser_NonNullAssertion_InSingleParamArrowWithBinaryOp()
    {
        // Single-parameter arrow with non-null assertion followed by binary operator
        var source = "let fn = x => x! > 5;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.ArrowFunction>(init);
        var arrow = (Expr.ArrowFunction)init;

        Assert.NotNull(arrow.ExpressionBody);
        Assert.IsType<Expr.Binary>(arrow.ExpressionBody);
        var binary = (Expr.Binary)arrow.ExpressionBody;

        Assert.IsType<Expr.NonNullAssertion>(binary.Left);
    }

    [Fact]
    public void Parser_SingleParamArrowInFunctionCall()
    {
        // Single-parameter arrow function as argument to function call
        var source = "arr.filter(x => x! > 2);";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.Call>(expr);
        var call = (Expr.Call)expr;

        Assert.Single(call.Arguments);
        Assert.IsType<Expr.ArrowFunction>(call.Arguments[0]);
    }

    [Fact]
    public void Parser_NonNullAssertion_InTemplateLiteral()
    {
        var source = "let str = `Hello ${name!}`;";
        var statements = Parse(source);

        Assert.Single(statements);
        var init = GetInitializerFromVar(statements[0]);

        Assert.IsType<Expr.TemplateLiteral>(init);
        var template = (Expr.TemplateLiteral)init;

        Assert.Single(template.Expressions);
        Assert.IsType<Expr.NonNullAssertion>(template.Expressions[0]);
    }

    #endregion

    #region Distinction from Logical NOT

    [Fact]
    public void Parser_LogicalNot_Prefix_VsNonNullAssertion_Postfix()
    {
        // Verify that !x (logical NOT) and x! (non-null assertion) parse differently
        var source1 = "!x;";
        var source2 = "x!;";

        var statements1 = Parse(source1);
        var statements2 = Parse(source2);

        var expr1 = GetExpressionFromStmt(statements1[0]);
        var expr2 = GetExpressionFromStmt(statements2[0]);

        Assert.IsType<Expr.Unary>(expr1);  // Logical NOT
        Assert.IsType<Expr.NonNullAssertion>(expr2);  // Non-null assertion
    }

    [Fact]
    public void Parser_BothLogicalNotAndNonNullAssertion()
    {
        // !x! means logical NOT of (x!)
        var source = "!x!;";
        var statements = Parse(source);

        Assert.Single(statements);
        var expr = GetExpressionFromStmt(statements[0]);

        Assert.IsType<Expr.Unary>(expr);
        var unary = (Expr.Unary)expr;
        Assert.Equal(TokenType.BANG, unary.Operator.Type);

        Assert.IsType<Expr.NonNullAssertion>(unary.Right);
    }

    #endregion
}
