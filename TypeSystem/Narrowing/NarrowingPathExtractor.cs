using SharpTS.Parsing;

namespace SharpTS.TypeSystem.Narrowing;

/// <summary>
/// Extracts NarrowingPath from AST expressions.
/// </summary>
/// <remarks>
/// Only certain expression forms are "narrowable":
/// - Simple variables: x
/// - Property access chains: x.prop, x.a.b.c
/// - Tuple element access with literal index: x[0]
///
/// Non-narrowable expressions include:
/// - Method calls: getObj().prop
/// - Computed property access: obj[key] (when key is not a literal)
/// - Complex expressions: (condition ? a : b).prop
/// </remarks>
public static class NarrowingPathExtractor
{
    /// <summary>
    /// Attempts to extract a NarrowingPath from an expression.
    /// Returns null if the expression is not narrowable.
    /// </summary>
    public static NarrowingPath? TryExtract(Expr expr)
    {
        return expr switch
        {
            Expr.Variable v => new NarrowingPath.Variable(v.Name.Lexeme),

            Expr.Get get when TryExtract(get.Object) is NarrowingPath basePath =>
                new NarrowingPath.PropertyAccess(basePath, get.Name.Lexeme),

            Expr.GetIndex idx when TryExtract(idx.Object) is NarrowingPath basePath &&
                                   idx.Index is Expr.Literal { Value: double d } &&
                                   d == Math.Floor(d) && d >= 0 =>
                new NarrowingPath.ElementAccess(basePath, (int)d),

            // Parenthesized expressions - unwrap
            Expr.Grouping g => TryExtract(g.Expression),

            // Non-null assertion - unwrap (x!.prop is narrowable if x.prop is)
            Expr.NonNullAssertion nna => TryExtract(nna.Expression),

            _ => null
        };
    }

    /// <summary>
    /// Extracts a NarrowingPath from an expression, throwing if not narrowable.
    /// Use this when the expression is expected to be narrowable.
    /// </summary>
    public static NarrowingPath Extract(Expr expr)
    {
        return TryExtract(expr)
            ?? throw new InvalidOperationException($"Expression {expr.GetType().Name} is not narrowable");
    }

    /// <summary>
    /// Checks if an expression can be narrowed (i.e., has a stable identity).
    /// </summary>
    public static bool IsNarrowable(Expr expr) => TryExtract(expr) != null;

    /// <summary>
    /// Gets the maximum depth we allow for narrowing paths.
    /// This prevents pathological cases like obj.a.b.c.d.e.f.g...
    /// </summary>
    public const int MaxNarrowingDepth = 10;

    /// <summary>
    /// Checks if a path is within the allowed depth for narrowing.
    /// </summary>
    public static bool IsWithinDepthLimit(NarrowingPath path)
    {
        return path.Depth <= MaxNarrowingDepth;
    }
}
