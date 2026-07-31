using System.Reflection;
using SharpTS.Parsing;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Discovers CLR operator methods for TypeScript operator tokens. The bridge only consults
/// this resolver when at least one operand is a known CLR interop value/type, preserving
/// JavaScript coercion for ordinary TypeScript values.
/// </summary>
internal static class DotNetOperatorResolver
{
    internal static MethodInfo[] GetBinaryCandidates(TokenType token, Type? leftType, Type? rightType)
    {
        string? name = GetBinaryMethodName(token);
        if (name == null)
            return [];

        return EnumerateDeclaringTypes(leftType, rightType)
            .SelectMany(type => ManagedDotNetInterop.GetMethods(
                type,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(method =>
                method.IsSpecialName &&
                method.Name == name &&
                method.GetParameters().Length == 2 &&
                !method.ContainsGenericParameters)
            .Distinct()
            .ToArray();
    }

    internal static MethodInfo[] GetUnaryCandidates(TokenType token, Type operandType)
    {
        string? name = token switch
        {
            TokenType.PLUS => "op_UnaryPlus",
            TokenType.MINUS => "op_UnaryNegation",
            TokenType.BANG => "op_LogicalNot",
            TokenType.TILDE => "op_OnesComplement",
            _ => null
        };
        if (name == null)
            return [];

        return ManagedDotNetInterop.GetMethods(
                operandType,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(method =>
                method.IsSpecialName &&
                method.Name == name &&
                method.GetParameters().Length == 1 &&
                !method.ContainsGenericParameters)
            .ToArray();
    }

    internal static MethodInfo[] GetIncrementCandidates(TokenType token, Type operandType)
    {
        string? name = token switch
        {
            TokenType.PLUS_PLUS => "op_Increment",
            TokenType.MINUS_MINUS => "op_Decrement",
            _ => null
        };
        if (name == null)
            return [];

        return ManagedDotNetInterop.GetMethods(
                operandType,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(method =>
                method.IsSpecialName &&
                method.Name == name &&
                method.GetParameters().Length == 1 &&
                !method.ContainsGenericParameters)
            .ToArray();
    }

    internal static TokenType? GetBinaryTokenForCompound(TokenType token) => token switch
    {
        TokenType.PLUS_EQUAL => TokenType.PLUS,
        TokenType.MINUS_EQUAL => TokenType.MINUS,
        TokenType.STAR_EQUAL => TokenType.STAR,
        TokenType.SLASH_EQUAL => TokenType.SLASH,
        TokenType.PERCENT_EQUAL => TokenType.PERCENT,
        TokenType.AMPERSAND_EQUAL => TokenType.AMPERSAND,
        TokenType.PIPE_EQUAL => TokenType.PIPE,
        TokenType.CARET_EQUAL => TokenType.CARET,
        TokenType.LESS_LESS_EQUAL => TokenType.LESS_LESS,
        TokenType.GREATER_GREATER_EQUAL => TokenType.GREATER_GREATER,
        TokenType.GREATER_GREATER_GREATER_EQUAL => TokenType.GREATER_GREATER_GREATER,
        _ => null
    };

    private static IEnumerable<Type> EnumerateDeclaringTypes(Type? leftType, Type? rightType)
    {
        if (leftType != null)
            yield return leftType;
        if (rightType != null && rightType != leftType)
            yield return rightType;
    }

    private static string? GetBinaryMethodName(TokenType token) => token switch
    {
        TokenType.PLUS => "op_Addition",
        TokenType.MINUS => "op_Subtraction",
        TokenType.STAR => "op_Multiply",
        TokenType.SLASH => "op_Division",
        TokenType.PERCENT => "op_Modulus",
        TokenType.AMPERSAND => "op_BitwiseAnd",
        TokenType.PIPE => "op_BitwiseOr",
        TokenType.CARET => "op_ExclusiveOr",
        TokenType.LESS_LESS => "op_LeftShift",
        TokenType.GREATER_GREATER => "op_RightShift",
        TokenType.GREATER_GREATER_GREATER => "op_UnsignedRightShift",
        TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL => "op_Equality",
        TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL => "op_Inequality",
        TokenType.LESS => "op_LessThan",
        TokenType.LESS_EQUAL => "op_LessThanOrEqual",
        TokenType.GREATER => "op_GreaterThan",
        TokenType.GREATER_EQUAL => "op_GreaterThanOrEqual",
        _ => null
    };
}
