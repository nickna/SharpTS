using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Optimizes string concatenation chains by flattening nested + operations
/// into a single String.Concat call.
/// </summary>
/// <remarks>
/// Detects patterns like: "a" + b + "c" + d
/// Which form a left-associative tree:
///        (+)
///       /   \
///     (+)    d
///    /   \
///  (+)   "c"
///  /  \
/// "a"  b
///
/// Flattens to: ["a", b, "c", d] and emits String.Concat(object[])
/// </remarks>
public static class StringConcatOptimizer
{
    /// <summary>
    /// Minimum number of parts required to optimize.
    /// Below this threshold, individual + operations may be faster.
    /// </summary>
    private const int MinPartsForOptimization = 3;

    /// <summary>
    /// Attempts to detect and flatten a string concatenation chain.
    /// </summary>
    /// <param name="binary">The binary expression to analyze.</param>
    /// <param name="parts">The flattened list of expressions if successful.</param>
    /// <returns>True if this is an optimizable string concatenation chain.</returns>
    public static bool TryFlattenConcatChain(Expr.Binary binary, out List<Expr> parts)
    {
        parts = [];

        // Must be a + operator
        if (binary.Operator.Type != TokenType.PLUS)
            return false;

        // Flatten the chain
        FlattenPlusChain(binary, parts);

        // Only optimize if we have enough parts and at least one is a string
        if (parts.Count < MinPartsForOptimization)
            return false;

        // Check if this looks like a string concatenation (at least one string literal)
        bool hasStringLiteral = false;
        foreach (var part in parts)
        {
            if (part is Expr.Literal { Value: string })
            {
                hasStringLiteral = true;
                break;
            }
            if (part is Expr.TemplateLiteral)
            {
                hasStringLiteral = true;
                break;
            }
        }

        return hasStringLiteral;
    }

    /// <summary>
    /// Recursively flattens a left-associative chain of + operations.
    /// </summary>
    private static void FlattenPlusChain(Expr expr, List<Expr> parts)
    {
        if (expr is Expr.Binary binary && binary.Operator.Type == TokenType.PLUS)
        {
            // Recurse into left side first (left-associative)
            FlattenPlusChain(binary.Left, parts);
            // Then add right side
            FlattenPlusChain(binary.Right, parts);
        }
        else
        {
            // Base case: not a + operation, add to parts
            parts.Add(expr);
        }
    }

    /// <summary>
    /// Checks if all parts can be constant-folded into a single string.
    /// </summary>
    public static bool TryFoldAllToString(List<Expr> parts, out string? result)
    {
        result = null;
        var sb = new System.Text.StringBuilder();

        foreach (var part in parts)
        {
            if (part is Expr.Literal lit)
            {
                sb.Append(StringifyLiteral(lit.Value));
            }
            else
            {
                // Non-literal found, can't fully fold
                return false;
            }
        }

        result = sb.ToString();
        return true;
    }

    private static string StringifyLiteral(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        string s => s,
        SharpTS.Runtime.Types.SharpTSUndefined => "undefined",
        _ => value.ToString() ?? ""
    };

    private static string FormatNumber(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }
}
