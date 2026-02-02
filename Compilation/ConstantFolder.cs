using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Performs compile-time evaluation of constant expressions.
/// </summary>
/// <remarks>
/// Evaluates binary and unary operations on literal values at compile time,
/// reducing runtime overhead and enabling further optimizations. Supports
/// numeric arithmetic, string concatenation, comparison, and bitwise operations.
/// </remarks>
public static class ConstantFolder
{
    /// <summary>
    /// Attempts to fold a binary expression with literal operands.
    /// </summary>
    /// <param name="binary">The binary expression to fold.</param>
    /// <param name="result">The folded result if successful.</param>
    /// <returns>True if the expression was folded; false otherwise.</returns>
    public static bool TryFoldBinary(Expr.Binary binary, out object? result)
    {
        result = null;

        // Both operands must be literals
        if (binary.Left is not Expr.Literal left || binary.Right is not Expr.Literal right)
            return false;

        // Handle numeric operations
        if (left.Value is double leftNum && right.Value is double rightNum)
        {
            return TryFoldNumeric(binary.Operator.Type, leftNum, rightNum, out result);
        }

        // Handle string concatenation
        if (binary.Operator.Type == TokenType.PLUS)
        {
            if (left.Value is string || right.Value is string)
            {
                result = Stringify(left.Value) + Stringify(right.Value);
                return true;
            }
        }

        // Handle string comparison
        if (left.Value is string leftStr && right.Value is string rightStr)
        {
            return TryFoldStringComparison(binary.Operator.Type, leftStr, rightStr, out result);
        }

        // Handle boolean operations
        if (left.Value is bool leftBool && right.Value is bool rightBool)
        {
            return TryFoldBoolean(binary.Operator.Type, leftBool, rightBool, out result);
        }

        // Handle null/undefined equality
        if (IsNullish(left.Value) || IsNullish(right.Value))
        {
            return TryFoldNullishEquality(binary.Operator.Type, left.Value, right.Value, out result);
        }

        return false;
    }

    /// <summary>
    /// Attempts to fold a unary expression with a literal operand.
    /// </summary>
    /// <param name="unary">The unary expression to fold.</param>
    /// <param name="result">The folded result if successful.</param>
    /// <returns>True if the expression was folded; false otherwise.</returns>
    public static bool TryFoldUnary(Expr.Unary unary, out object? result)
    {
        result = null;

        if (unary.Right is not Expr.Literal lit)
            return false;

        switch (unary.Operator.Type)
        {
            case TokenType.MINUS when lit.Value is double d:
                result = -d;
                return true;

            case TokenType.BANG:
                result = !IsTruthy(lit.Value);
                return true;

            case TokenType.TILDE when lit.Value is double d:
                result = (double)~(int)d;
                return true;

            case TokenType.TYPEOF:
                result = TypeOf(lit.Value);
                return true;

            default:
                return false;
        }
    }

    private static bool TryFoldNumeric(TokenType op, double left, double right, out object? result)
    {
        result = op switch
        {
            // Arithmetic
            TokenType.PLUS => left + right,
            TokenType.MINUS => left - right,
            TokenType.STAR => left * right,
            TokenType.SLASH => left / right,
            TokenType.PERCENT => left % right,
            TokenType.STAR_STAR => Math.Pow(left, right),

            // Comparison
            TokenType.LESS => left < right,
            TokenType.GREATER => left > right,
            TokenType.LESS_EQUAL => left <= right,
            TokenType.GREATER_EQUAL => left >= right,

            // Equality
            TokenType.EQUAL_EQUAL => left == right,
            TokenType.EQUAL_EQUAL_EQUAL => left == right,
            TokenType.BANG_EQUAL => left != right,
            TokenType.BANG_EQUAL_EQUAL => left != right,

            // Bitwise
            TokenType.AMPERSAND => (double)((int)left & (int)right),
            TokenType.PIPE => (double)((int)left | (int)right),
            TokenType.CARET => (double)((int)left ^ (int)right),
            TokenType.LESS_LESS => (double)((int)left << ((int)right & 0x1F)),
            TokenType.GREATER_GREATER => (double)((int)left >> ((int)right & 0x1F)),
            TokenType.GREATER_GREATER_GREATER => (double)((uint)(int)left >> ((int)right & 0x1F)),

            _ => null
        };

        return result != null;
    }

    private static bool TryFoldStringComparison(TokenType op, string left, string right, out object? result)
    {
        result = op switch
        {
            TokenType.EQUAL_EQUAL => left == right,
            TokenType.EQUAL_EQUAL_EQUAL => left == right,
            TokenType.BANG_EQUAL => left != right,
            TokenType.BANG_EQUAL_EQUAL => left != right,
            TokenType.LESS => string.Compare(left, right, StringComparison.Ordinal) < 0,
            TokenType.GREATER => string.Compare(left, right, StringComparison.Ordinal) > 0,
            TokenType.LESS_EQUAL => string.Compare(left, right, StringComparison.Ordinal) <= 0,
            TokenType.GREATER_EQUAL => string.Compare(left, right, StringComparison.Ordinal) >= 0,
            _ => null
        };

        return result != null;
    }

    private static bool TryFoldBoolean(TokenType op, bool left, bool right, out object? result)
    {
        result = op switch
        {
            TokenType.EQUAL_EQUAL => left == right,
            TokenType.EQUAL_EQUAL_EQUAL => left == right,
            TokenType.BANG_EQUAL => left != right,
            TokenType.BANG_EQUAL_EQUAL => left != right,
            TokenType.AMPERSAND => left & right,
            TokenType.PIPE => left | right,
            TokenType.CARET => left ^ right,
            _ => null
        };

        return result != null;
    }

    private static bool TryFoldNullishEquality(TokenType op, object? left, object? right, out object? result)
    {
        bool leftNullish = IsNullish(left);
        bool rightNullish = IsNullish(right);

        result = op switch
        {
            // Loose equality: null == undefined
            TokenType.EQUAL_EQUAL => leftNullish && rightNullish,
            TokenType.BANG_EQUAL => !(leftNullish && rightNullish),

            // Strict equality: null !== undefined
            TokenType.EQUAL_EQUAL_EQUAL => (left == null && right == null) ||
                                           (IsUndefined(left) && IsUndefined(right)),
            TokenType.BANG_EQUAL_EQUAL => !((left == null && right == null) ||
                                            (IsUndefined(left) && IsUndefined(right))),
            _ => null
        };

        return result != null;
    }

    /// <summary>
    /// Attempts to fold a logical expression (&&, ||) with literal operands.
    /// </summary>
    public static bool TryFoldLogical(Expr.Logical logical, out object? result)
    {
        result = null;

        if (logical.Left is not Expr.Literal left)
            return false;

        bool leftTruthy = IsTruthy(left.Value);

        // Short-circuit evaluation
        if (logical.Operator.Type == TokenType.AND_AND)
        {
            // && returns left if falsy, otherwise right
            if (!leftTruthy)
            {
                result = left.Value;
                return true;
            }
            // Left is truthy, result depends on right
            if (logical.Right is Expr.Literal right)
            {
                result = right.Value;
                return true;
            }
        }
        else if (logical.Operator.Type == TokenType.OR_OR)
        {
            // || returns left if truthy, otherwise right
            if (leftTruthy)
            {
                result = left.Value;
                return true;
            }
            // Left is falsy, result depends on right
            if (logical.Right is Expr.Literal right)
            {
                result = right.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to fold a nullish coalescing expression (??) with literal operands.
    /// </summary>
    public static bool TryFoldNullishCoalescing(Expr.NullishCoalescing nc, out object? result)
    {
        result = null;

        if (nc.Left is not Expr.Literal left)
            return false;

        // ?? returns left if not nullish, otherwise right
        if (!IsNullish(left.Value))
        {
            result = left.Value;
            return true;
        }

        if (nc.Right is Expr.Literal right)
        {
            result = right.Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to fold a ternary expression with a literal condition.
    /// </summary>
    public static bool TryFoldTernary(Expr.Ternary ternary, out Expr? result)
    {
        result = null;

        if (ternary.Condition is not Expr.Literal condition)
            return false;

        // Select branch based on condition truthiness
        result = IsTruthy(condition.Value) ? ternary.ThenBranch : ternary.ElseBranch;
        return true;
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        SharpTSUndefined => false,
        bool b => b,
        double d => d != 0.0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };

    private static bool IsNullish(object? value) =>
        value == null || IsUndefined(value);

    private static bool IsUndefined(object? value) =>
        value is SharpTSUndefined;

    private static string Stringify(object? value) => value switch
    {
        null => "null",
        SharpTSUndefined => "undefined",
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        string s => s,
        _ => value.ToString() ?? ""
    };

    private static string FormatNumber(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }

    private static string TypeOf(object? value) => value switch
    {
        null => "object",
        SharpTSUndefined => "undefined",
        bool => "boolean",
        double => "number",
        string => "string",
        _ => "object"
    };
}
