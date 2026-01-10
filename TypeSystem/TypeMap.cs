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
    private readonly Dictionary<string, TypeInfo.Class> _classTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeInfo.Function> _functionTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Associates an expression with its resolved type.
    /// </summary>
    public void Set(Expr expr, TypeInfo type) => _types[expr] = type;

    /// <summary>
    /// Registers a class type by name for later lookup during compilation.
    /// </summary>
    public void SetClassType(string className, TypeInfo.Class classType) => _classTypes[className] = classType;

    /// <summary>
    /// Gets the class type by name, or null if not found.
    /// </summary>
    public TypeInfo.Class? GetClassType(string className) => _classTypes.GetValueOrDefault(className);

    /// <summary>
    /// Registers a top-level function type by name.
    /// </summary>
    public void SetFunctionType(string functionName, TypeInfo.Function functionType) => _functionTypes[functionName] = functionType;

    /// <summary>
    /// Gets the function type by name, or null if not found.
    /// </summary>
    public TypeInfo.Function? GetFunctionType(string functionName) => _functionTypes.GetValueOrDefault(functionName);

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
