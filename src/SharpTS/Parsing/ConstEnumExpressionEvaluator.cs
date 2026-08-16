namespace SharpTS.Parsing;

/// <summary>
/// Classifies why a const-enum initializer expression failed to evaluate, so each phase can map
/// the failure onto its own diagnostic (the type checker picks the TS code from this kind).
/// </summary>
internal enum ConstEnumErrorKind
{
    /// <summary>A literal with a null value (neither number nor string).</summary>
    NullLiteral,
    /// <summary>A reference to an enum member that has not been defined yet.</summary>
    ForwardReference,
    /// <summary>An expression form that const enum initializers do not allow.</summary>
    DisallowedExpression,
    /// <summary>A unary or binary operator that const enum initializers do not allow.</summary>
    DisallowedOperator,
    /// <summary>Operand types the operator cannot combine (e.g. string * string).</summary>
    InvalidOperandTypes,
}

/// <summary>A structured const-enum evaluation failure: the kind plus a neutral, formatted message.</summary>
internal readonly record struct ConstEnumError(ConstEnumErrorKind Kind, string Message);

/// <summary>
/// Phase-neutral evaluation of const-enum member initializer expressions — literals, references
/// to previously defined members of the same enum, grouping, unary (+ - ~), binary arithmetic /
/// bitwise / shift, and string concatenation. The single implementation shared by the type
/// checker, the interpreter, and the IL compiler, which previously each carried an exact copy.
/// </summary>
/// <remarks>
/// Failures are reported through the <c>error</c> factory so each phase throws its own exception
/// type (TypeCheckException with a TS code / InterpreterException / CompileException) without
/// this evaluator knowing about any of them. Numeric semantics are preserved exactly: all
/// arithmetic is double, bitwise/shift operators cast through int and back to double.
/// </remarks>
internal static class ConstEnumExpressionEvaluator
{
    public static object Evaluate(
        Expr expression,
        IReadOnlyDictionary<string, object> resolvedMembers,
        string enumName,
        Func<ConstEnumError, Exception> error)
    {
        return expression switch
        {
            Expr.Literal lit => lit.Value ?? throw error(new(
                ConstEnumErrorKind.NullLiteral,
                "Const enum expression cannot be null.")),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw error(new(
                        ConstEnumErrorKind.ForwardReference,
                        $"Const enum member '{g.Name.Lexeme}' referenced before definition.")),

            Expr.Grouping gr => Evaluate(gr.Expression, resolvedMembers, enumName, error),

            Expr.Unary u => EvaluateUnary(u, resolvedMembers, enumName, error),

            Expr.Binary b => EvaluateBinary(b, resolvedMembers, enumName, error),

            _ => throw error(new(
                ConstEnumErrorKind.DisallowedExpression,
                $"Expression type '{expression.GetType().Name}' is not allowed in const enum initializer."))
        };
    }

    private static object EvaluateUnary(
        Expr.Unary unary,
        IReadOnlyDictionary<string, object> resolvedMembers,
        string enumName,
        Func<ConstEnumError, Exception> error)
    {
        var operand = Evaluate(unary.Right, resolvedMembers, enumName, error);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)(~(int)d),
            _ => throw error(new(
                ConstEnumErrorKind.DisallowedOperator,
                $"Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions."))
        };
    }

    private static object EvaluateBinary(
        Expr.Binary binary,
        IReadOnlyDictionary<string, object> resolvedMembers,
        string enumName,
        Func<ConstEnumError, Exception> error)
    {
        var left = Evaluate(binary.Left, resolvedMembers, enumName, error);
        var right = Evaluate(binary.Right, resolvedMembers, enumName, error);

        if (left is double l && right is double r)
        {
            return binary.Operator.Type switch
            {
                TokenType.PLUS => l + r,
                TokenType.MINUS => l - r,
                TokenType.STAR => l * r,
                TokenType.SLASH => l / r,
                TokenType.PERCENT => l % r,
                TokenType.STAR_STAR => Math.Pow(l, r),
                TokenType.AMPERSAND => (double)((int)l & (int)r),
                TokenType.PIPE => (double)((int)l | (int)r),
                TokenType.CARET => (double)((int)l ^ (int)r),
                TokenType.LESS_LESS => (double)((int)l << (int)r),
                TokenType.GREATER_GREATER => (double)((int)l >> (int)r),
                _ => throw error(new(
                    ConstEnumErrorKind.DisallowedOperator,
                    $"Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions."))
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw error(new(
            ConstEnumErrorKind.InvalidOperandTypes,
            $"Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression."));
    }
}
