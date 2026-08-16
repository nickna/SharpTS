using SharpTS.Diagnostics.Exceptions;
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
        var ctx = GetDefinitionContext();

        // Get qualified enum name (module-prefixed in multi-module compilation)
        string qualifiedEnumName = ctx.GetQualifiedEnumName(enumStmt.Name.Lexeme);

        // Track simple name -> module mapping for later lookups
        if (_modules.CurrentPath != null)
        {
            _modules.EnumToModule[enumStmt.Name.Lexeme] = _modules.CurrentPath;
        }

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

        _enums.Members[qualifiedEnumName] = members;
        _enums.Reverse[qualifiedEnumName] = reverse;
        _enums.Kinds[qualifiedEnumName] = kind;

        // Track const enums
        if (enumStmt.IsConst)
        {
            _enums.ConstEnums.Add(qualifiedEnumName);
        }
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members during compilation via the shared
    /// <see cref="ConstEnumExpressionEvaluator"/>, surfacing failures as CompileExceptions.
    /// </summary>
    private static object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return ConstEnumExpressionEvaluator.Evaluate(expr, resolvedMembers, enumName,
            static e => new CompileException(e.Message));
    }
}
