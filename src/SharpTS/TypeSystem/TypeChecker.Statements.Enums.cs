using SharpTS.TypeSystem.Exceptions;
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
        RegisterValueDeclaration(enumStmt.Name, mergeWithLocal: true);

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
                        throw new TypeCheckException($" Enum member '{member.Name.Lexeme}' must be a string or number literal.", tsCode: "TS2553");
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
                        throw new TypeCheckException($" Const enum member '{member.Name.Lexeme}' must evaluate to a string or number.", tsCode: "TS2553");
                    }
                }
                else
                {
                    throw new TypeCheckException($" Enum member '{member.Name.Lexeme}' must be a literal value.", tsCode: "TS2553");
                }
            }
            else
            {
                // No initializer - use auto-increment if active
                if (!autoIncrementActive)
                {
                    throw new TypeCheckException($" Enum member '{member.Name.Lexeme}' must have an initializer " +
                                        "(string enum members cannot use auto-increment).", tsCode: "TS1061");
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

        var enumType = new TypeInfo.Enum(enumStmt.Name.Lexeme, members.ToFrozenDictionary(), kind, enumStmt.IsConst);
        DeclareValue(enumStmt.Name, enumType, mergeWithLocal: true);
        _environment.DefineType(enumStmt.Name.Lexeme, enumType);
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members via the shared
    /// <see cref="ConstEnumExpressionEvaluator"/>, mapping failures onto TypeCheckExceptions
    /// with the TS code each error kind has always carried here.
    /// </summary>
    private static object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return ConstEnumExpressionEvaluator.Evaluate(expr, resolvedMembers, enumName, static e =>
            new TypeCheckException($" {e.Message}", tsCode: e.Kind switch
            {
                ConstEnumErrorKind.ForwardReference => "TS2474",
                ConstEnumErrorKind.InvalidOperandTypes => "TS2362",
                _ => "TS2553",
            }));
    }
}
