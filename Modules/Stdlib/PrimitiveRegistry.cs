namespace SharpTS.Modules.Stdlib;

/// <summary>
/// Registry of <c>primitive:*</c> modules — the narrow C# interop surface that
/// stdlib TypeScript modules rely on. These are intentionally <em>not</em>
/// importable from user code; only modules loaded via the embedded stdlib
/// provider may resolve <c>primitive:*</c> specifiers.
/// </summary>
/// <remarks>
/// The set is narrow and stable by design. Growing it is a conscious architectural
/// choice — each primitive is an irreducible C# capability that TypeScript cannot
/// express (OS detection, filesystem I/O, environment access, etc.).
/// </remarks>
public static class PrimitiveRegistry
{
    /// <summary>
    /// Sentinel prefix identifying a primitive specifier or virtual path
    /// (e.g. <c>primitive:os</c>).
    /// </summary>
    public const string Prefix = "primitive:";

    private static readonly HashSet<string> _primitives = new(StringComparer.Ordinal)
    {
        Prefix + "os",
        Prefix + "process",
        Prefix + "perf",
        Prefix + "tty",
        Prefix + "async_hooks",
        Prefix + "timers",
        Prefix + "timers/promises",
        Prefix + "readline",
        Prefix + "fs",
        Prefix + "fs/promises",
        Prefix + "zlib",
    };

    /// <summary>
    /// Checks whether a specifier is a known primitive (e.g. <c>primitive:os</c>).
    /// </summary>
    public static bool IsPrimitive(string specifier) => _primitives.Contains(specifier);

    /// <summary>
    /// All registered primitive specifiers.
    /// </summary>
    public static IReadOnlySet<string> GetPrimitives() => _primitives;

    /// <summary>
    /// Extracts the bare primitive name from a specifier or virtual path
    /// (e.g. <c>primitive:os</c> → <c>os</c>). Returns null if not a primitive.
    /// </summary>
    public static string? GetPrimitiveName(string specifier)
    {
        if (!specifier.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        return specifier[Prefix.Length..];
    }
}
