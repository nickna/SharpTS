using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for operator precedence in the parser.
/// Verifies that expressions are parsed with correct precedence and associativity.
/// </summary>
public class OperatorPrecedenceTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    /// <summary>
    /// Parses a single expression statement and returns the expression.
    /// </summary>
    private static Expr ParseExpression(string source)
    {
        var statements = Parse(source);
        Assert.Single(statements);
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        return exprStmt.Expr;
    }

    /// <summary>
    /// Verifies the structure of a binary expression.
    /// </summary>
    private static (Expr Left, TokenType Operator, Expr Right) GetBinary(Expr expr)
    {
        var binary = Assert.IsType<Expr.Binary>(expr);
        return (binary.Left, binary.Operator.Type, binary.Right);
    }

    #endregion

    #region Multiplicative vs Additive

    [Fact]
    public void Precedence_MultiplyBeforeAdd()
    {
        // 2 + 3 * 4 should parse as 2 + (3 * 4)
        var expr = ParseExpression("2 + 3 * 4;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.PLUS, op);
        Assert.IsType<Expr.Literal>(left); // 2
        var rightBinary = Assert.IsType<Expr.Binary>(right);
        Assert.Equal(TokenType.STAR, rightBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_DivideBeforeSubtract()
    {
        // 10 - 6 / 2 should parse as 10 - (6 / 2)
        var expr = ParseExpression("10 - 6 / 2;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.MINUS, op);
        Assert.IsType<Expr.Literal>(left);
        Assert.IsType<Expr.Binary>(right);
    }

    [Fact]
    public void Precedence_ModuloBeforeAdd()
    {
        // 5 + 10 % 3 should parse as 5 + (10 % 3)
        var expr = ParseExpression("5 + 10 % 3;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.PLUS, op);
        Assert.IsType<Expr.Literal>(left);
        var rightBinary = Assert.IsType<Expr.Binary>(right);
        Assert.Equal(TokenType.PERCENT, rightBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_MultipleMultiplicative()
    {
        // 2 * 3 / 4 should parse left-to-right as (2 * 3) / 4
        var expr = ParseExpression("2 * 3 / 4;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.SLASH, op);
        var leftBinary = Assert.IsType<Expr.Binary>(left);
        Assert.Equal(TokenType.STAR, leftBinary.Operator.Type);
    }

    #endregion

    #region Exponentiation

    [Fact]
    public void Precedence_ExponentiationBeforeMultiply()
    {
        // 2 * 3 ** 2 should parse as 2 * (3 ** 2)
        var expr = ParseExpression("2 * 3 ** 2;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.STAR, op);
        Assert.IsType<Expr.Literal>(left);
        var rightBinary = Assert.IsType<Expr.Binary>(right);
        Assert.Equal(TokenType.STAR_STAR, rightBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_ExponentiationRightAssociative()
    {
        // 2 ** 3 ** 2 should parse as 2 ** (3 ** 2), not (2 ** 3) ** 2
        var expr = ParseExpression("2 ** 3 ** 2;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.STAR_STAR, op);
        Assert.IsType<Expr.Literal>(left); // 2
        var rightBinary = Assert.IsType<Expr.Binary>(right);
        Assert.Equal(TokenType.STAR_STAR, rightBinary.Operator.Type);
    }

    #endregion

    #region Comparison vs Additive

    [Fact]
    public void Precedence_AddBeforeComparison()
    {
        // 1 + 2 < 3 + 4 should parse as (1 + 2) < (3 + 4)
        var expr = ParseExpression("1 + 2 < 3 + 4;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.LESS, op);
        Assert.IsType<Expr.Binary>(left);
        Assert.IsType<Expr.Binary>(right);
    }

    [Fact]
    public void Precedence_MultiplyBeforeGreater()
    {
        // 2 * 3 > 5 should parse as (2 * 3) > 5
        var expr = ParseExpression("2 * 3 > 5;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.GREATER, op);
        var leftBinary = Assert.IsType<Expr.Binary>(left);
        Assert.Equal(TokenType.STAR, leftBinary.Operator.Type);
    }

    #endregion

    #region Equality vs Comparison

    [Fact]
    public void Precedence_ComparisonBeforeEquality()
    {
        // a < b == c < d should parse as (a < b) == (c < d)
        var expr = ParseExpression("a < b == c < d;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.EQUAL_EQUAL, op);
        var leftBinary = Assert.IsType<Expr.Binary>(left);
        Assert.Equal(TokenType.LESS, leftBinary.Operator.Type);
        var rightBinary = Assert.IsType<Expr.Binary>(right);
        Assert.Equal(TokenType.LESS, rightBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_StrictEquality()
    {
        // 1 + 2 === 3 should parse as (1 + 2) === 3
        var expr = ParseExpression("1 + 2 === 3;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.EQUAL_EQUAL_EQUAL, op);
        Assert.IsType<Expr.Binary>(left);
        Assert.IsType<Expr.Literal>(right);
    }

    #endregion

    #region Logical Operators

    [Fact]
    public void Precedence_EqualityBeforeLogicalAnd()
    {
        // a == b && c == d should parse as (a == b) && (c == d)
        var expr = ParseExpression("a == b && c == d;");
        var logical = Assert.IsType<Expr.Logical>(expr);
        Assert.Equal(TokenType.AND_AND, logical.Operator.Type);
        Assert.IsType<Expr.Binary>(logical.Left);
    }

    [Fact]
    public void Precedence_LogicalAndBeforeOr()
    {
        // a && b || c && d should parse as (a && b) || (c && d)
        var expr = ParseExpression("a && b || c && d;");
        var logical = Assert.IsType<Expr.Logical>(expr);
        Assert.Equal(TokenType.OR_OR, logical.Operator.Type);
        var leftLogical = Assert.IsType<Expr.Logical>(logical.Left);
        Assert.Equal(TokenType.AND_AND, leftLogical.Operator.Type);
        var rightLogical = Assert.IsType<Expr.Logical>(logical.Right);
        Assert.Equal(TokenType.AND_AND, rightLogical.Operator.Type);
    }

    [Fact]
    public void Precedence_NullishCoalescingLowestOfLogical()
    {
        // a || b ?? c should parse as (a || b) ?? c
        var expr = ParseExpression("a || b ?? c;");
        var nullish = Assert.IsType<Expr.NullishCoalescing>(expr);
        Assert.IsType<Expr.Logical>(nullish.Left);
        Assert.IsType<Expr.Variable>(nullish.Right);
    }

    #endregion

    #region Bitwise Operators

    [Fact]
    public void Precedence_ShiftBeforeComparison()
    {
        // a << 2 < b should parse as (a << 2) < b
        var expr = ParseExpression("a << 2 < b;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.LESS, op);
        var leftBinary = Assert.IsType<Expr.Binary>(left);
        Assert.Equal(TokenType.LESS_LESS, leftBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_BitwiseAndBeforeBitwiseOr()
    {
        // a & b | c should parse as (a & b) | c
        var expr = ParseExpression("a & b | c;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.PIPE, op);
        var leftBinary = Assert.IsType<Expr.Binary>(left);
        Assert.Equal(TokenType.AMPERSAND, leftBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_BitwiseXorBetweenAndOr()
    {
        // a & b ^ c | d should parse as ((a & b) ^ c) | d
        var expr = ParseExpression("a & b ^ c | d;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.PIPE, op);
        var leftBinary = Assert.IsType<Expr.Binary>(left);
        Assert.Equal(TokenType.CARET, leftBinary.Operator.Type);
    }

    #endregion

    #region Parentheses Override

    [Fact]
    public void Precedence_ParenthesesOverrideAddMultiply()
    {
        // (2 + 3) * 4 should parse with addition first
        var expr = ParseExpression("(2 + 3) * 4;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.STAR, op);
        var leftGrouping = Assert.IsType<Expr.Grouping>(left);
        var innerBinary = Assert.IsType<Expr.Binary>(leftGrouping.Expression);
        Assert.Equal(TokenType.PLUS, innerBinary.Operator.Type);
    }

    [Fact]
    public void Precedence_ParenthesesOverrideExponentiation()
    {
        // (2 ** 3) ** 2 should parse with left exponentiation first
        var expr = ParseExpression("(2 ** 3) ** 2;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.STAR_STAR, op);
        var leftGrouping = Assert.IsType<Expr.Grouping>(left);
        Assert.IsType<Expr.Binary>(leftGrouping.Expression);
    }

    [Fact]
    public void Precedence_NestedParentheses()
    {
        // ((1 + 2) * 3) + 4
        var expr = ParseExpression("((1 + 2) * 3) + 4;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.PLUS, op);
        var leftGrouping = Assert.IsType<Expr.Grouping>(left);
        Assert.IsType<Expr.Binary>(leftGrouping.Expression);
    }

    #endregion

    #region Unary Operators

    [Fact]
    public void Precedence_UnaryBeforeBinary()
    {
        // -a + b should parse as (-a) + b
        var expr = ParseExpression("-a + b;");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.PLUS, op);
        Assert.IsType<Expr.Unary>(left);
    }

    [Fact]
    public void Precedence_MultipleUnary()
    {
        // --a parses as decrement (prefix)
        var expr = ParseExpression("--a;");
        var prefix = Assert.IsType<Expr.PrefixIncrement>(expr);
        Assert.Equal(TokenType.MINUS_MINUS, prefix.Operator.Type);
        Assert.IsType<Expr.Variable>(prefix.Operand);
    }

    [Fact]
    public void Precedence_UnaryNot()
    {
        // !a && b should parse as (!a) && b
        var expr = ParseExpression("!a && b;");
        var logical = Assert.IsType<Expr.Logical>(expr);
        Assert.Equal(TokenType.AND_AND, logical.Operator.Type);
        Assert.IsType<Expr.Unary>(logical.Left);
    }

    [Fact]
    public void Precedence_TypeofBeforeBinary()
    {
        // typeof x == "string"
        var expr = ParseExpression("typeof x == \"string\";");
        var (left, op, right) = GetBinary(expr);

        Assert.Equal(TokenType.EQUAL_EQUAL, op);
        Assert.IsType<Expr.Unary>(left);
    }

    #endregion

    #region Ternary Operator

    [Fact]
    public void Precedence_TernaryLowestPrecedence()
    {
        // a + b ? c : d should parse as (a + b) ? c : d
        var expr = ParseExpression("a + b ? c : d;");
        var ternary = Assert.IsType<Expr.Ternary>(expr);
        Assert.IsType<Expr.Binary>(ternary.Condition);
    }

    [Fact]
    public void Precedence_TernaryNested()
    {
        // a ? b ? c : d : e parses with right-nested ternaries
        var expr = ParseExpression("a ? b ? c : d : e;");
        var outer = Assert.IsType<Expr.Ternary>(expr);
        Assert.IsType<Expr.Variable>(outer.Condition);
        Assert.IsType<Expr.Ternary>(outer.ThenBranch);
    }

    [Fact]
    public void Precedence_TernaryWithLogical()
    {
        // a || b ? c && d : e should parse correctly
        var expr = ParseExpression("a || b ? c && d : e;");
        var ternary = Assert.IsType<Expr.Ternary>(expr);
        Assert.IsType<Expr.Logical>(ternary.Condition);
        Assert.IsType<Expr.Logical>(ternary.ThenBranch);
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void Precedence_ComplexMixedExpression()
    {
        // 1 + 2 * 3 - 4 / 2 should parse as ((1 + (2 * 3)) - (4 / 2))
        var expr = ParseExpression("1 + 2 * 3 - 4 / 2;");
        // Result should be: (1 + (2*3)) - (4/2)
        var (left, op, right) = GetBinary(expr);
        Assert.Equal(TokenType.MINUS, op);
    }

    [Fact]
    public void Precedence_AllArithmeticLevels()
    {
        // 2 ** 3 * 4 + 5 - 6 / 2
        // Should parse as: (((2 ** 3) * 4) + 5) - (6 / 2)
        var expr = ParseExpression("2 ** 3 * 4 + 5 - 6 / 2;");
        var (left, op, right) = GetBinary(expr);
        Assert.Equal(TokenType.MINUS, op);
    }

    [Fact]
    public void Precedence_LogicalWithComparison()
    {
        // a > b && c < d || e == f
        // Should parse as: ((a > b) && (c < d)) || (e == f)
        var expr = ParseExpression("a > b && c < d || e == f;");
        var logical = Assert.IsType<Expr.Logical>(expr);
        Assert.Equal(TokenType.OR_OR, logical.Operator.Type);
        var leftLogical = Assert.IsType<Expr.Logical>(logical.Left);
        Assert.Equal(TokenType.AND_AND, leftLogical.Operator.Type);
    }

    #endregion
}
