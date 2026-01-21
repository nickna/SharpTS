using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Central resolver for binary operator semantics.
/// Provides a single source of truth for operator classification across
/// TypeChecker, Interpreter, and ILEmitter.
/// </summary>
public static class SemanticOperatorResolver
{
    /// <summary>
    /// Resolves a token type to its operator descriptor with IL opcode information.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>An OperatorDescriptor describing the operator's semantics.</returns>
    public static OperatorDescriptor Resolve(TokenType op) => op switch
    {
        // Plus - special for string concatenation
        TokenType.PLUS => new OperatorDescriptor.Plus(),

        // Arithmetic with direct IL opcodes
        TokenType.MINUS => new OperatorDescriptor.Arithmetic(OpCodes.Sub),
        TokenType.STAR => new OperatorDescriptor.Arithmetic(OpCodes.Mul),
        TokenType.SLASH => new OperatorDescriptor.Arithmetic(OpCodes.Div),
        TokenType.PERCENT => new OperatorDescriptor.Arithmetic(OpCodes.Rem),

        // Power - requires Math.Pow
        TokenType.STAR_STAR => new OperatorDescriptor.Power(),

        // Comparison operators
        TokenType.LESS => new OperatorDescriptor.Comparison(OpCodes.Clt),
        TokenType.GREATER => new OperatorDescriptor.Comparison(OpCodes.Cgt),
        TokenType.LESS_EQUAL => new OperatorDescriptor.Comparison(OpCodes.Cgt, Negated: true),
        TokenType.GREATER_EQUAL => new OperatorDescriptor.Comparison(OpCodes.Clt, Negated: true),

        // Equality operators
        TokenType.EQUAL_EQUAL => new OperatorDescriptor.Equality(IsStrict: false, IsNegated: false),
        TokenType.EQUAL_EQUAL_EQUAL => new OperatorDescriptor.Equality(IsStrict: true, IsNegated: false),
        TokenType.BANG_EQUAL => new OperatorDescriptor.Equality(IsStrict: false, IsNegated: true),
        TokenType.BANG_EQUAL_EQUAL => new OperatorDescriptor.Equality(IsStrict: true, IsNegated: true),

        // Bitwise operators
        TokenType.AMPERSAND => new OperatorDescriptor.Bitwise(OpCodes.And),
        TokenType.PIPE => new OperatorDescriptor.Bitwise(OpCodes.Or),
        TokenType.CARET => new OperatorDescriptor.Bitwise(OpCodes.Xor),

        // Bitwise shift operators
        TokenType.LESS_LESS => new OperatorDescriptor.BitwiseShift(OpCodes.Shl),
        TokenType.GREATER_GREATER => new OperatorDescriptor.BitwiseShift(OpCodes.Shr),

        // Unsigned right shift - special case, no bigint support
        TokenType.GREATER_GREATER_GREATER => new OperatorDescriptor.UnsignedRightShift(),

        // Special operators
        TokenType.IN => new OperatorDescriptor.In(),
        TokenType.INSTANCEOF => new OperatorDescriptor.InstanceOf(),

        _ => new OperatorDescriptor.Unknown()
    };

    /// <summary>
    /// Gets the operator category for a token type.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>The operator category.</returns>
    /// <exception cref="ArgumentException">Thrown for unknown operators.</exception>
    public static OperatorCategory GetCategory(TokenType op) => op switch
    {
        TokenType.PLUS => OperatorCategory.Plus,

        TokenType.MINUS or TokenType.STAR or TokenType.SLASH or
        TokenType.PERCENT or TokenType.STAR_STAR => OperatorCategory.Arithmetic,

        TokenType.LESS or TokenType.LESS_EQUAL or
        TokenType.GREATER or TokenType.GREATER_EQUAL => OperatorCategory.Comparison,

        TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL or
        TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL => OperatorCategory.Equality,

        TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET or
        TokenType.LESS_LESS or TokenType.GREATER_GREATER => OperatorCategory.Bitwise,

        TokenType.GREATER_GREATER_GREATER => OperatorCategory.UnsignedShift,

        TokenType.IN or TokenType.INSTANCEOF => OperatorCategory.Special,

        _ => throw new ArgumentException($"Unknown operator: {op}")
    };

    /// <summary>
    /// Returns true if the operator supports BigInt operands.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>True if BigInt is supported; false for unsigned right shift.</returns>
    public static bool SupportsBigInt(TokenType op) => op != TokenType.GREATER_GREATER_GREATER;

    /// <summary>
    /// Returns true if the operator returns a boolean result.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>True for comparison, equality, and special operators.</returns>
    public static bool ReturnsBoolean(TokenType op) => GetCategory(op) is
        OperatorCategory.Comparison or OperatorCategory.Equality or OperatorCategory.Special;

    /// <summary>
    /// Returns true if the operator requires numeric operands (number or bigint).
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>True for arithmetic, comparison, bitwise, and unsigned shift operators.</returns>
    public static bool RequiresNumeric(TokenType op) => GetCategory(op) is
        OperatorCategory.Arithmetic or OperatorCategory.Comparison or
        OperatorCategory.Bitwise or OperatorCategory.UnsignedShift;

    /// <summary>
    /// Returns true if the operator is an equality check (==, ===, !=, !==).
    /// </summary>
    public static bool IsEqualityOperator(TokenType op) => op is
        TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL or
        TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;

    /// <summary>
    /// Returns true if the operator is a comparison (&lt;, &lt;=, &gt;, &gt;=).
    /// </summary>
    public static bool IsComparisonOperator(TokenType op) => op is
        TokenType.LESS or TokenType.LESS_EQUAL or
        TokenType.GREATER or TokenType.GREATER_EQUAL;

    /// <summary>
    /// Returns true if the operator is a bitwise operation.
    /// </summary>
    public static bool IsBitwiseOperator(TokenType op) => op is
        TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET or
        TokenType.LESS_LESS or TokenType.GREATER_GREATER or
        TokenType.GREATER_GREATER_GREATER;
}
