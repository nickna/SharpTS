namespace SharpTS.Runtime;

/// <summary>
/// Provides debug warnings for operations that silently fail in non-strict ("sloppy") mode.
/// These warnings help developers identify code that would throw in strict mode.
/// </summary>
public static class SloppyModeWarnings
{
    /// <summary>
    /// Controls whether sloppy-mode warnings are emitted. Off by default: these
    /// silent failures are spec-legal JavaScript and Node prints nothing for
    /// them, so warning on stderr polluted ordinary program output. Opt in for
    /// debugging with SHARPTS_SLOPPY_WARNINGS=1 (or set this when embedding).
    /// </summary>
    public static bool Enabled { get; set; } =
        Environment.GetEnvironmentVariable("SHARPTS_SLOPPY_WARNINGS") is "1" or "true";

    /// <summary>
    /// Emits a warning to stderr when an operation silently fails in sloppy mode.
    /// </summary>
    /// <param name="operation">The operation that failed (e.g., "delete variable", "write to frozen")</param>
    /// <param name="details">Additional context about what was ignored</param>
    public static void Warn(string operation, string details)
    {
        if (!Enabled) return;
        Console.Error.WriteLine($"[Warning] Silent failure: {operation} - {details}");
    }

    /// <summary>
    /// Warns and returns a value. Useful for expression contexts.
    /// </summary>
    public static T WarnAndReturn<T>(T value, string operation, string details)
    {
        if (!Enabled) return value;
        Warn(operation, details);
        return value;
    }
}
