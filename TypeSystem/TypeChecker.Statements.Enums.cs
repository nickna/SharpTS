using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Enum declaration type checking - handles enum statements including const enums with computed values.
/// </summary>
public partial class TypeChecker
{
    private void CheckEnumDeclaration(Stmt.Enum enumStmt)
    {
        Dictionary<string, object> members = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;
        bool autoIncrementActive = true;

        foreach (var member in enumStmt.Members)
        {
            if (member.Value != null)
            {
                // For literals, do normal type checking
                // For const enum computed expressions, skip CheckExpr (enum not yet defined)
                if (member.Value is Expr.Literal lit)
                {
                    if (lit.Value is double d)
                    {
                        // Numeric literal - enable auto-increment from this value
                        members[member.Name.Lexeme] = d;
                        currentNumericValue = d + 1;
                        hasNumeric = true;
                        autoIncrementActive = true;
                    }
                    else if (lit.Value is string s)
                    {
                        // String literal - disable auto-increment
                        members[member.Name.Lexeme] = s;
                        hasString = true;
                        autoIncrementActive = false;
                    }
                    else
                    {
                        throw new Exception($"Type Error: Enum member '{member.Name.Lexeme}' must be a string or number literal.");
                    }
                }
                else if (enumStmt.IsConst)
                {
                    // Const enums support computed values (e.g., B = A * 2)
                    var computedValue = EvaluateConstEnumExpression(member.Value, members, enumStmt.Name.Lexeme);
                    if (computedValue is double d)
                    {
                        members[member.Name.Lexeme] = d;
                        currentNumericValue = d + 1;
                        hasNumeric = true;
                        autoIncrementActive = true;
                    }
                    else if (computedValue is string s)
                    {
                        members[member.Name.Lexeme] = s;
                        hasString = true;
                        autoIncrementActive = false;
                    }
                    else
                    {
                        throw new Exception($"Type Error: Const enum member '{member.Name.Lexeme}' must evaluate to a string or number.");
                    }
                }
                else
                {
                    throw new Exception($"Type Error: Enum member '{member.Name.Lexeme}' must be a literal value.");
                }
            }
            else
            {
                // No initializer - use auto-increment if active
                if (!autoIncrementActive)
                {
                    throw new Exception($"Type Error: Enum member '{member.Name.Lexeme}' must have an initializer " +
                                        "(string enum members cannot use auto-increment).");
                }

                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                hasNumeric = true;
                currentNumericValue++;
            }
        }

        // Determine enum kind
        EnumKind kind = (hasNumeric, hasString) switch
        {
            (true, false) => EnumKind.Numeric,
            (false, true) => EnumKind.String,
            (true, true) => EnumKind.Heterogeneous,
            _ => EnumKind.Numeric  // Empty enum defaults to numeric
        };

        _environment.Define(enumStmt.Name.Lexeme, new TypeInfo.Enum(enumStmt.Name.Lexeme, members.ToFrozenDictionary(), kind, enumStmt.IsConst));
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members.
    /// Supports literals, references to other enum members, and arithmetic operations.
    /// </summary>
    private object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value ?? throw new Exception($"Type Error: Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new Exception($"Type Error: Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new Exception($"Type Error: Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
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
            _ => throw new Exception($"Type Error: Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
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
                _ => throw new Exception($"Type Error: Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new Exception($"Type Error: Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
    }
}
