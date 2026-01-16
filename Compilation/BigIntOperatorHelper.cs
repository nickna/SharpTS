using System.Numerics;
using System.Reflection;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Indicates the result type of a BigInt operation for IL emission.
/// </summary>
public enum BigIntResultType
{
    /// <summary>Result is a BigInt value (arithmetic, bitwise) - no boxing needed.</summary>
    Value,
    /// <summary>Result is a boolean (comparison, equality) - needs boolean boxing.</summary>
    Boolean,
    /// <summary>Result is negated boolean (!=, !==) - needs negation then boolean boxing.</summary>
    NegatedBoolean,
    /// <summary>Operator is not supported for BigInt.</summary>
    Unsupported
}

/// <summary>
/// Centralized BigInt operator handling for both IL emission and interpretation.
/// Eliminates duplication between ILEmitter.Operators.cs and Interpreter.Calls.cs.
/// </summary>
public static class BigIntOperatorHelper
{
    /// <summary>
    /// Gets the runtime method and result type for a BigInt binary operator.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <param name="runtime">The emitted runtime containing BigInt methods.</param>
    /// <returns>A tuple of (method, resultType). Method is null for unsupported operators.</returns>
    public static (MethodInfo? Method, BigIntResultType ResultType) GetRuntimeMethod(TokenType op, EmittedRuntime runtime)
    {
        return op switch
        {
            // Arithmetic - return BigInt value
            TokenType.PLUS => (runtime.BigIntAdd, BigIntResultType.Value),
            TokenType.MINUS => (runtime.BigIntSubtract, BigIntResultType.Value),
            TokenType.STAR => (runtime.BigIntMultiply, BigIntResultType.Value),
            TokenType.SLASH => (runtime.BigIntDivide, BigIntResultType.Value),
            TokenType.PERCENT => (runtime.BigIntRemainder, BigIntResultType.Value),
            TokenType.STAR_STAR => (runtime.BigIntPow, BigIntResultType.Value),

            // Comparison - return boolean
            TokenType.LESS => (runtime.BigIntLessThan, BigIntResultType.Boolean),
            TokenType.LESS_EQUAL => (runtime.BigIntLessThanOrEqual, BigIntResultType.Boolean),
            TokenType.GREATER => (runtime.BigIntGreaterThan, BigIntResultType.Boolean),
            TokenType.GREATER_EQUAL => (runtime.BigIntGreaterThanOrEqual, BigIntResultType.Boolean),

            // Equality - return boolean
            TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL => (runtime.BigIntEquals, BigIntResultType.Boolean),

            // Inequality - return negated boolean
            TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL => (runtime.BigIntEquals, BigIntResultType.NegatedBoolean),

            // Bitwise - return BigInt value
            TokenType.AMPERSAND => (runtime.BigIntBitwiseAnd, BigIntResultType.Value),
            TokenType.PIPE => (runtime.BigIntBitwiseOr, BigIntResultType.Value),
            TokenType.CARET => (runtime.BigIntBitwiseXor, BigIntResultType.Value),
            TokenType.LESS_LESS => (runtime.BigIntLeftShift, BigIntResultType.Value),
            TokenType.GREATER_GREATER => (runtime.BigIntRightShift, BigIntResultType.Value),

            // Unsigned right shift not supported
            TokenType.GREATER_GREATER_GREATER => (null, BigIntResultType.Unsupported),

            _ => (null, BigIntResultType.Unsupported)
        };
    }

    /// <summary>
    /// Evaluates a BigInt binary operation at runtime (for interpreter).
    /// Assumes both operands are valid BigIntegers.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <param name="left">The left BigInteger operand.</param>
    /// <param name="right">The right BigInteger operand.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="Exception">Thrown for unsupported operators.</exception>
    public static object EvaluateBinary(TokenType op, BigInteger left, BigInteger right)
    {
        return op switch
        {
            // Arithmetic
            TokenType.PLUS => new SharpTSBigInt(left + right),
            TokenType.MINUS => new SharpTSBigInt(left - right),
            TokenType.STAR => new SharpTSBigInt(left * right),
            TokenType.SLASH => new SharpTSBigInt(BigInteger.Divide(left, right)),
            TokenType.PERCENT => new SharpTSBigInt(BigInteger.Remainder(left, right)),
            TokenType.STAR_STAR => SharpTSBigInt.Pow(new SharpTSBigInt(left), new SharpTSBigInt(right)),

            // Comparison
            TokenType.GREATER => left > right,
            TokenType.GREATER_EQUAL => left >= right,
            TokenType.LESS => left < right,
            TokenType.LESS_EQUAL => left <= right,

            // Equality
            TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL => left == right,
            TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL => left != right,

            // Bitwise
            TokenType.AMPERSAND => new SharpTSBigInt(left & right),
            TokenType.PIPE => new SharpTSBigInt(left | right),
            TokenType.CARET => new SharpTSBigInt(left ^ right),
            TokenType.LESS_LESS => new SharpTSBigInt(left << (int)right),
            TokenType.GREATER_GREATER => new SharpTSBigInt(left >> (int)right),
            TokenType.GREATER_GREATER_GREATER => throw new Exception(
                "Runtime Error: Unsigned right shift (>>>) is not supported for bigint."),

            _ => throw new Exception($"Runtime Error: Operator {op} not supported for bigint.")
        };
    }

    /// <summary>
    /// Returns true if the operator returns a boolean result (comparison/equality).
    /// </summary>
    public static bool ReturnsBoolean(TokenType op) => op is
        TokenType.GREATER or TokenType.GREATER_EQUAL or
        TokenType.LESS or TokenType.LESS_EQUAL or
        TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL or
        TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;

    /// <summary>
    /// Returns true if the operator is an equality check.
    /// </summary>
    public static bool IsEqualityOperator(TokenType op) => op is
        TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL or
        TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;

    /// <summary>
    /// Returns true if the operator is a comparison (non-equality).
    /// </summary>
    public static bool IsComparisonOperator(TokenType op) => op is
        TokenType.GREATER or TokenType.GREATER_EQUAL or
        TokenType.LESS or TokenType.LESS_EQUAL;

    /// <summary>
    /// Returns true if the operator is a bitwise operation.
    /// </summary>
    public static bool IsBitwiseOperator(TokenType op) => op is
        TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET or
        TokenType.LESS_LESS or TokenType.GREATER_GREATER or
        TokenType.GREATER_GREATER_GREATER;
}
