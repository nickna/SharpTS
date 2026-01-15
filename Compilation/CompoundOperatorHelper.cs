using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Centralized mapping from compound assignment operators to IL opcodes.
/// Eliminates duplication across ILEmitter, AsyncMoveNextEmitter, and GeneratorMoveNextEmitter.
/// </summary>
public static class CompoundOperatorHelper
{
    /// <summary>
    /// Gets the IL opcode for a compound assignment operator.
    /// </summary>
    /// <param name="op">The compound assignment token type.</param>
    /// <returns>The corresponding IL opcode, or null for unsupported operators.</returns>
    public static OpCode? GetOpcode(TokenType op) => op switch
    {
        TokenType.PLUS_EQUAL => OpCodes.Add,
        TokenType.MINUS_EQUAL => OpCodes.Sub,
        TokenType.STAR_EQUAL => OpCodes.Mul,
        TokenType.SLASH_EQUAL => OpCodes.Div,
        TokenType.PERCENT_EQUAL => OpCodes.Rem,
        TokenType.AMPERSAND_EQUAL => OpCodes.And,
        TokenType.PIPE_EQUAL => OpCodes.Or,
        TokenType.CARET_EQUAL => OpCodes.Xor,
        TokenType.LESS_LESS_EQUAL => OpCodes.Shl,
        TokenType.GREATER_GREATER_EQUAL => OpCodes.Shr,
        _ => null
    };

    /// <summary>
    /// Checks if the operator is a bitwise compound assignment.
    /// </summary>
    public static bool IsBitwise(TokenType op) => op is
        TokenType.AMPERSAND_EQUAL or
        TokenType.PIPE_EQUAL or
        TokenType.CARET_EQUAL or
        TokenType.LESS_LESS_EQUAL or
        TokenType.GREATER_GREATER_EQUAL;

    /// <summary>
    /// Checks if the operator is an arithmetic compound assignment.
    /// </summary>
    public static bool IsArithmetic(TokenType op) => op is
        TokenType.PLUS_EQUAL or
        TokenType.MINUS_EQUAL or
        TokenType.STAR_EQUAL or
        TokenType.SLASH_EQUAL or
        TokenType.PERCENT_EQUAL;
}
