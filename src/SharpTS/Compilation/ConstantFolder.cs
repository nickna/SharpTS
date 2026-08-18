using SharpTS.Parsing;

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
    // Cached type for SharpTSUndefined checks (avoids compile-time dependency on SharpTS.dll)
    private static readonly Type? _sharpTSUndefinedType = Type.GetType("SharpTS.Runtime.Types.SharpTSUndefined, SharpTS");

    private static bool IsUndefinedType(object? value) =>
        value != null && _sharpTSUndefinedType?.IsInstanceOfType(value) == true;
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

            case TokenType.PLUS when lit.Value is double dp:
                result = dp;
                return true;

            case TokenType.BANG:
                result = !IsTruthy(lit.Value);
                return true;

            case TokenType.TILDE when lit.Value is double d:
                result = (double)~ToInt32(d);
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
            TokenType.AMPERSAND => (double)(ToInt32(left) & ToInt32(right)),
            TokenType.PIPE => (double)(ToInt32(left) | ToInt32(right)),
            TokenType.CARET => (double)(ToInt32(left) ^ ToInt32(right)),
            TokenType.LESS_LESS => (double)(ToInt32(left) << (ToInt32(right) & 0x1F)),
            TokenType.GREATER_GREATER => (double)(ToInt32(left) >> (ToInt32(right) & 0x1F)),
            TokenType.GREATER_GREATER_GREATER => (double)((uint)ToInt32(left) >> (ToInt32(right) & 0x1F)),

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
            TokenType.AMPERSAND => (double)((left ? 1 : 0) & (right ? 1 : 0)),
            TokenType.PIPE => (double)((left ? 1 : 0) | (right ? 1 : 0)),
            TokenType.CARET => (double)((left ? 1 : 0) ^ (right ? 1 : 0)),
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

    private static bool IsTruthy(object? value) => RuntimeTypes.IsTruthy(value);

    private static bool IsNullish(object? value) =>
        value == null || IsUndefined(value);

    private static bool IsUndefined(object? value) =>
        IsUndefinedType(value);

    private static string Stringify(object? value)
    {
        if (value == null) return "null";
        if (IsUndefinedType(value)) return "undefined";
        return value switch
        {
            bool b => b ? "true" : "false",
            double d => FormatNumber(d),
            string s => s,
            _ => value.ToString() ?? ""
        };
    }

    private static string FormatNumber(double d) => RuntimeTypes.FormatNumber(d);

    private static int ToInt32(double value)
    {
        if (!double.IsFinite(value) || value == 0) return 0;
        double integer = Math.Truncate(value);
        double modulo = integer - Math.Floor(integer / 4294967296.0) * 4294967296.0;
        return modulo >= 2147483648.0
            ? (int)(modulo - 4294967296.0)
            : (int)modulo;
    }

    private static string TypeOf(object? value)
    {
        if (value == null) return "object";
        if (IsUndefinedType(value)) return "undefined";
        return value switch
        {
            bool => "boolean",
            double => "number",
            // A bigint literal (e.g. `typeof 10n`) lexes to a BigInteger-valued
            // Expr.Literal, so the runtime TypeOf emitter never runs — this fold
            // must mirror its `BigInteger => "bigint"` case (RuntimeTypes.TypeOf).
            System.Numerics.BigInteger => "bigint",
            string => "string",
            _ => "object"
        };
    }
}
