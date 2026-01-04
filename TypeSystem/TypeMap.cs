using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Stores the resolved TypeInfo for each expression in the AST.
/// Built by TypeChecker during static analysis, consumed by ILCompiler and Interpreter.
/// </summary>
/// <remarks>
/// Uses ReferenceEqualityComparer because C# records use structural equality by default.
/// Two Expr.Literal(42) instances would otherwise be considered equal even if they
/// appear at different locations in the AST.
/// </remarks>
public class TypeMap
{
    private readonly Dictionary<Expr, TypeInfo> _types = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Associates an expression with its resolved type.
    /// </summary>
    public void Set(Expr expr, TypeInfo type) => _types[expr] = type;

    /// <summary>
    /// Gets the resolved type for an expression, or null if not found.
    /// </summary>
    public TypeInfo? Get(Expr expr) => _types.GetValueOrDefault(expr);

    /// <summary>
    /// Tries to get the resolved type for an expression.
    /// </summary>
    public bool TryGet(Expr expr, out TypeInfo? type) => _types.TryGetValue(expr, out type);

    /// <summary>
    /// Checks if the expression is typed as a string.
    /// </summary>
    public bool IsString(Expr expr) => Get(expr) is TypeInfo.Primitive { Type: TokenType.TYPE_STRING };

    /// <summary>
    /// Checks if the expression is typed as an array.
    /// </summary>
    public bool IsArray(Expr expr) => Get(expr) is TypeInfo.Array;

    /// <summary>
    /// Checks if the expression is typed as a number.
    /// </summary>
    public bool IsNumber(Expr expr) => Get(expr) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER };

    /// <summary>
    /// Checks if the expression is typed as a boolean.
    /// </summary>
    public bool IsBoolean(Expr expr) => Get(expr) is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN };

    /// <summary>
    /// Returns the number of expressions with resolved types.
    /// </summary>
    public int Count => _types.Count;
}
