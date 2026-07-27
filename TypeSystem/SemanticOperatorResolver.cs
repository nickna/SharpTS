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
    // Interned, immutable operator descriptors. Every OperatorDescriptor variant is an
    // immutable record (private ctor; payloads are OpCode structs / bools), and every consumer
    // only pattern-matches the result or reads its readonly OpCode — never mutates it or relies
    // on reference distinctness. So a single shared instance per operator is observationally
    // identical to a fresh allocation, while avoiding a per-binary-op heap allocation on the
    // interpreter's number/number fast path (Resolve is called per +, per <, per i++, ...).
    private static readonly OperatorDescriptor Plus = new OperatorDescriptor.Plus();
    private static readonly OperatorDescriptor Subtract = new OperatorDescriptor.Arithmetic(OpCodes.Sub);
    private static readonly OperatorDescriptor Multiply = new OperatorDescriptor.Arithmetic(OpCodes.Mul);
    private static readonly OperatorDescriptor Divide = new OperatorDescriptor.Arithmetic(OpCodes.Div);
    private static readonly OperatorDescriptor Remainder = new OperatorDescriptor.Arithmetic(OpCodes.Rem);
    private static readonly OperatorDescriptor Power = new OperatorDescriptor.Power();
    private static readonly OperatorDescriptor Less = new OperatorDescriptor.Comparison(OpCodes.Clt);
    private static readonly OperatorDescriptor Greater = new OperatorDescriptor.Comparison(OpCodes.Cgt);
    private static readonly OperatorDescriptor LessEqual = new OperatorDescriptor.Comparison(OpCodes.Cgt, Negated: true);
    private static readonly OperatorDescriptor GreaterEqual = new OperatorDescriptor.Comparison(OpCodes.Clt, Negated: true);
    private static readonly OperatorDescriptor LooseEqual = new OperatorDescriptor.Equality(IsStrict: false, IsNegated: false);
    private static readonly OperatorDescriptor StrictEqual = new OperatorDescriptor.Equality(IsStrict: true, IsNegated: false);
    private static readonly OperatorDescriptor LooseNotEqual = new OperatorDescriptor.Equality(IsStrict: false, IsNegated: true);
    private static readonly OperatorDescriptor StrictNotEqual = new OperatorDescriptor.Equality(IsStrict: true, IsNegated: true);
    private static readonly OperatorDescriptor BitwiseAnd = new OperatorDescriptor.Bitwise(OpCodes.And);
    private static readonly OperatorDescriptor BitwiseOr = new OperatorDescriptor.Bitwise(OpCodes.Or);
    private static readonly OperatorDescriptor BitwiseXor = new OperatorDescriptor.Bitwise(OpCodes.Xor);
    private static readonly OperatorDescriptor ShiftLeft = new OperatorDescriptor.BitwiseShift(OpCodes.Shl);
    private static readonly OperatorDescriptor ShiftRight = new OperatorDescriptor.BitwiseShift(OpCodes.Shr);
    private static readonly OperatorDescriptor UnsignedShiftRight = new OperatorDescriptor.UnsignedRightShift();
    private static readonly OperatorDescriptor InOp = new OperatorDescriptor.In();
    private static readonly OperatorDescriptor InstanceOfOp = new OperatorDescriptor.InstanceOf();
    private static readonly OperatorDescriptor UnknownOp = new OperatorDescriptor.Unknown();

    /// <summary>
    /// Resolves a token type to its operator descriptor with IL opcode information.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>An OperatorDescriptor describing the operator's semantics.</returns>
    public static OperatorDescriptor Resolve(TokenType op) => op switch
    {
        // Plus - special for string concatenation
        TokenType.PLUS => Plus,

        // Arithmetic with direct IL opcodes
        TokenType.MINUS => Subtract,
        TokenType.STAR => Multiply,
        TokenType.SLASH => Divide,
        TokenType.PERCENT => Remainder,

        // Power - requires Math.Pow
        TokenType.STAR_STAR => Power,

        // Comparison operators
        TokenType.LESS => Less,
        TokenType.GREATER => Greater,
        TokenType.LESS_EQUAL => LessEqual,
        TokenType.GREATER_EQUAL => GreaterEqual,

        // Equality operators
        TokenType.EQUAL_EQUAL => LooseEqual,
        TokenType.EQUAL_EQUAL_EQUAL => StrictEqual,
        TokenType.BANG_EQUAL => LooseNotEqual,
        TokenType.BANG_EQUAL_EQUAL => StrictNotEqual,

        // Bitwise operators
        TokenType.AMPERSAND => BitwiseAnd,
        TokenType.PIPE => BitwiseOr,
        TokenType.CARET => BitwiseXor,

        // Bitwise shift operators
        TokenType.LESS_LESS => ShiftLeft,
        TokenType.GREATER_GREATER => ShiftRight,

        // Unsigned right shift - special case, no bigint support
        TokenType.GREATER_GREATER_GREATER => UnsignedShiftRight,

        // Special operators
        TokenType.IN => InOp,
        TokenType.INSTANCEOF => InstanceOfOp,

        _ => UnknownOp
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
    /// Returns true if the operator returns a boolean result.
    /// </summary>
    /// <param name="op">The operator token type.</param>
    /// <returns>True for comparison, equality, and special operators.</returns>
    public static bool ReturnsBoolean(TokenType op) => GetCategory(op) is
        OperatorCategory.Comparison or OperatorCategory.Equality or OperatorCategory.Special;

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
