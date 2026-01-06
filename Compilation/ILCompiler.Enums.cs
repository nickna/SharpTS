using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Enum compilation methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private void DefineEnum(Stmt.Enum enumStmt)
    {
        Dictionary<string, object> members = [];
        Dictionary<double, string> reverse = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;

        foreach (var member in enumStmt.Members)
        {
            if (member.Value is Expr.Literal lit)
            {
                if (lit.Value is double d)
                {
                    members[member.Name.Lexeme] = d;
                    reverse[d] = member.Name.Lexeme;  // Only numeric values get reverse mapping
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (lit.Value is string s)
                {
                    members[member.Name.Lexeme] = s;
                    // No reverse mapping for string values
                    hasString = true;
                }
            }
            else if (enumStmt.IsConst && member.Value != null)
            {
                // Const enums support computed expressions - evaluate at compile time
                var computedValue = EvaluateConstEnumExpression(member.Value, members, enumStmt.Name.Lexeme);
                if (computedValue is double d)
                {
                    members[member.Name.Lexeme] = d;
                    reverse[d] = member.Name.Lexeme;
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (computedValue is string s)
                {
                    members[member.Name.Lexeme] = s;
                    hasString = true;
                }
            }
            else if (member.Value == null)
            {
                // Auto-increment
                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                reverse[currentNumericValue.Value] = member.Name.Lexeme;
                hasNumeric = true;
                currentNumericValue++;
            }
        }

        EnumKind kind = (hasNumeric, hasString) switch
        {
            (true, false) => EnumKind.Numeric,
            (false, true) => EnumKind.String,
            (true, true) => EnumKind.Heterogeneous,
            _ => EnumKind.Numeric
        };

        _enumMembers[enumStmt.Name.Lexeme] = members;
        _enumReverse[enumStmt.Name.Lexeme] = reverse;
        _enumKinds[enumStmt.Name.Lexeme] = kind;

        // Track const enums
        if (enumStmt.IsConst)
        {
            _constEnums.Add(enumStmt.Name.Lexeme);
        }
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members during compilation.
    /// </summary>
    private object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value ?? throw new Exception($"Compile Error: Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new Exception($"Compile Error: Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new Exception($"Compile Error: Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
        };
    }

    private object EvaluateConstEnumUnary(Expr.Unary unary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var operand = EvaluateConstEnumExpression(unary.Right, resolvedMembers, enumName);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)(~(int)d),
            _ => throw new Exception($"Compile Error: Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
        };
    }

    private object EvaluateConstEnumBinary(Expr.Binary binary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var left = EvaluateConstEnumExpression(binary.Left, resolvedMembers, enumName);
        var right = EvaluateConstEnumExpression(binary.Right, resolvedMembers, enumName);

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
                _ => throw new Exception($"Compile Error: Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new Exception($"Compile Error: Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
    }
}
