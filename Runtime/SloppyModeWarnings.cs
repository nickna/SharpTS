namespace SharpTS.Runtime;

/// <summary>
/// Provides debug warnings for operations that silently fail in non-strict ("sloppy") mode.
/// These warnings help developers identify code that would throw in strict mode.
/// </summary>
public static class SloppyModeWarnings
{
    /// <summary>
    /// Emits a warning to stderr when an operation silently fails in sloppy mode.
    /// </summary>
    /// <param name="operation">The operation that failed (e.g., "delete variable", "write to frozen")</param>
    /// <param name="details">Additional context about what was ignored</param>
    public static void Warn(string operation, string details)
    {
        Console.Error.WriteLine($"[Warning] Silent failure: {operation} - {details}");
    }

    /// <summary>
    /// Warns and returns a value. Useful for expression contexts.
    /// </summary>
    public static T WarnAndReturn<T>(T value, string operation, string details)
    {
        Warn(operation, details);
        return value;
    }
}
