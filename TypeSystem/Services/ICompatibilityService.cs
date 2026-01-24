using SharpTS.Parsing;

namespace SharpTS.TypeSystem.Services;

/// <summary>
/// Service interface for type compatibility checking.
/// </summary>
/// <remarks>
/// This service encapsulates all type compatibility logic previously spread across
/// TypeChecker.Compatibility*.cs files. It handles structural typing, type guards,
/// template literal matching, tuple/array compatibility, and callable/constructable
/// interface matching.
/// </remarks>
public interface ICompatibilityService
{
    /// <summary>
    /// Checks if an actual type is assignable to an expected type.
    /// Uses memoization for performance.
    /// </summary>
    /// <param name="expected">The expected (target) type.</param>
    /// <param name="actual">The actual (source) type.</param>
    /// <returns>True if actual is assignable to expected.</returns>
    bool IsCompatible(TypeInfo expected, TypeInfo actual);

    /// <summary>
    /// Checks structural compatibility between required interface members and an actual type.
    /// Used for duck typing and interface implementation checking.
    /// </summary>
    /// <param name="requiredMembers">The required member types from an interface.</param>
    /// <param name="actual">The actual type to check.</param>
    /// <param name="optionalMembers">Set of optional member names.</param>
    /// <returns>True if actual structurally matches the required members.</returns>
    bool CheckStructuralCompatibility(
        IReadOnlyDictionary<string, TypeInfo> requiredMembers,
        TypeInfo actual,
        IReadOnlySet<string>? optionalMembers = null);

    /// <summary>
    /// Gets the type of a member from a type (property, method, or field).
    /// Handles records, interfaces, classes, strings, arrays, and tuples.
    /// </summary>
    /// <param name="type">The type to look up the member on.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member type, or null if not found.</returns>
    TypeInfo? GetMemberType(TypeInfo type, string name);

    /// <summary>
    /// Analyzes a condition expression for type guard patterns.
    /// Returns information about how to narrow types in then/else branches.
    /// </summary>
    /// <param name="condition">The condition expression.</param>
    /// <param name="environment">The type environment for variable lookup.</param>
    /// <param name="checkExpr">Delegate to check expression types.</param>
    /// <returns>Tuple of (variable name, narrowed type for then-branch, excluded type for else-branch).</returns>
    (string? VarName, TypeInfo? NarrowedType, TypeInfo? ExcludedType) AnalyzeTypeGuard(
        Expr condition,
        TypeEnvironment environment,
        Func<Expr, TypeInfo> checkExpr);

    /// <summary>
    /// Checks excess properties in a fresh object literal.
    /// TypeScript performs this check to catch typos and enforce exact object shapes.
    /// </summary>
    /// <param name="actual">The actual object record type from the literal.</param>
    /// <param name="expected">The expected type from the variable declaration.</param>
    /// <param name="sourceExpr">The source expression for error context.</param>
    /// <exception cref="TypeSystem.Exceptions.TypeCheckException">Thrown when excess properties are found.</exception>
    void CheckExcessProperties(TypeInfo.Record actual, TypeInfo expected, Expr sourceExpr);
}

/// <summary>
/// Helper type predicates for compatibility checking.
/// </summary>
public interface ITypePredicates
{
    /// <summary>
    /// Checks if a type is a number type (including literal types and unions).
    /// </summary>
    bool IsNumber(TypeInfo type);

    /// <summary>
    /// Checks if a type is a string type (including literal types and unions).
    /// </summary>
    bool IsString(TypeInfo type);

    /// <summary>
    /// Checks if a type is a bigint type (including unions).
    /// </summary>
    bool IsBigInt(TypeInfo type);

    /// <summary>
    /// Checks if a type is a primitive (not valid as WeakMap key or WeakSet value).
    /// </summary>
    bool IsPrimitiveType(TypeInfo type);
}
