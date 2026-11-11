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
/// to previously defined enum members and resolved constants, grouping, unary (+ - ~), binary arithmetic /
/// bitwise / shift, and string concatenation. The single implementation shared by the type
/// checker, the interpreter, and the IL compiler, which previously each carried an exact copy.
/// </summary>
/// <remarks>
/// Failures are reported through the <c>error</c> factory so each phase throws its own exception
/// type (TypeCheckException with a TS code / InterpreterException / CompileException) without
/// this evaluator knowing about any of them. Arithmetic uses doubles and bitwise
/// operators use the JavaScript 32-bit integer conversion.
/// </remarks>
internal static class ConstEnumExpressionEvaluator
{
    private sealed class NonConstantInitializerException : Exception;

    /// <summary>Recognizes numeric literals through grouping and unary signs, without evaluating arbitrary expressions.</summary>
    public static bool TryEvaluateSignedLiteral(Expr? expression, out double value)
    {
        switch (expression)
        {
            case Expr.Literal { Value: double number }:
                value = number;
                return true;
            case Expr.Grouping grouping:
                return TryEvaluateSignedLiteral(grouping.Expression, out value);
            case Expr.Unary { Operator.Type: TokenType.MINUS or TokenType.PLUS } unary
                when TryEvaluateSignedLiteral(unary.Right, out double operand):
                value = unary.Operator.Type == TokenType.MINUS ? -operand : operand;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public static object? EvaluateKnownMember(
        Expr expression, IReadOnlyDictionary<string, object> resolvedMembers, string enumName, bool isConst,
        Func<Expr, object?>? resolveReference = null)
    {
        try
        {
            var value = Evaluate(expression, resolvedMembers, enumName,
                static _ => new NonConstantInitializerException(), regularEnum: !isConst, resolveReference);
            return value is double or string ? value : null;
        }
        catch (NonConstantInitializerException)
        {
            return null;
        }
    }

    public static object Evaluate(
        Expr expression,
        IReadOnlyDictionary<string, object> resolvedMembers,
        string enumName,
        Func<ConstEnumError, Exception> error,
        bool regularEnum = false,
        Func<Expr, object?>? resolveReference = null)
    {
        return expression switch
        {
            Expr.Literal lit => lit.Value ?? throw error(new(
                ConstEnumErrorKind.NullLiteral,
                "Const enum expression cannot be null.")),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                ResolveMember(g.Name.Lexeme, resolvedMembers, error),

            Expr.GetIndex { Object: Expr.Variable receiver, Index: Expr.Literal { Value: string key } }
                when receiver.Name.Lexeme == enumName =>
                ResolveMember(key, resolvedMembers, error),

            Expr.Variable variable when resolvedMembers.TryGetValue(variable.Name.Lexeme, out var memberValue) =>
                memberValue,

            Expr.Variable variable when resolveReference?.Invoke(variable) is { } constant => constant,

            Expr.Variable variable when regularEnum =>
                throw error(new(ConstEnumErrorKind.ForwardReference,
                    $"Const enum member '{variable.Name.Lexeme}' referenced before definition.")),

            Expr.Grouping gr => Evaluate(gr.Expression, resolvedMembers, enumName, error, regularEnum, resolveReference),

            Expr.Unary u => EvaluateUnary(u, resolvedMembers, enumName, error, regularEnum, resolveReference),

            Expr.Binary b => EvaluateBinary(b, resolvedMembers, enumName, error, regularEnum, resolveReference),

            Expr.Get or Expr.GetIndex when resolveReference?.Invoke(expression) is { } value => value,

            _ => throw error(new(
                ConstEnumErrorKind.DisallowedExpression,
                $"Expression type '{expression.GetType().Name}' is not allowed in const enum initializer."))
        };
    }

    private static object ResolveMember(string name, IReadOnlyDictionary<string, object> resolvedMembers,
        Func<ConstEnumError, Exception> error)
    {
        if (resolvedMembers.TryGetValue(name, out var value))
            return value;

        throw error(new(ConstEnumErrorKind.ForwardReference,
            $"Const enum member '{name}' referenced before definition."));
    }

    private static object EvaluateUnary(
        Expr.Unary unary,
        IReadOnlyDictionary<string, object> resolvedMembers,
        string enumName,
        Func<ConstEnumError, Exception> error,
        bool regularEnum,
        Func<Expr, object?>? resolveReference)
    {
        var operand = Evaluate(unary.Right, resolvedMembers, enumName, error, regularEnum, resolveReference);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)~ToInt32(d),
            _ => throw error(new(
                ConstEnumErrorKind.DisallowedOperator,
                $"Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions."))
        };
    }

    private static object EvaluateBinary(
        Expr.Binary binary,
        IReadOnlyDictionary<string, object> resolvedMembers,
        string enumName,
        Func<ConstEnumError, Exception> error,
        bool regularEnum,
        Func<Expr, object?>? resolveReference)
    {
        var left = Evaluate(binary.Left, resolvedMembers, enumName, error, regularEnum, resolveReference);
        var right = Evaluate(binary.Right, resolvedMembers, enumName, error, regularEnum, resolveReference);

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
                TokenType.AMPERSAND => (double)(ToInt32(l) & ToInt32(r)),
                TokenType.PIPE => (double)(ToInt32(l) | ToInt32(r)),
                TokenType.CARET => (double)(ToInt32(l) ^ ToInt32(r)),
                TokenType.LESS_LESS => (double)(ToInt32(l) << ToInt32(r)),
                TokenType.GREATER_GREATER => (double)(ToInt32(l) >> ToInt32(r)),
                TokenType.GREATER_GREATER_GREATER when regularEnum =>
                    (double)(unchecked((uint)ToInt32(l)) >> ToInt32(r)),
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

    private static int ToInt32(double number) =>
        double.IsFinite(number) ? unchecked((int)(long)(Math.Truncate(number) % 4294967296.0)) : 0;
}
