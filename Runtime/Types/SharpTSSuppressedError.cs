namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript SuppressedError.
/// Thrown when disposal fails while another error is pending.
/// TypeScript ES2024 explicit resource management semantics.
/// </summary>
/// <remarks>
/// When using 'using' declarations, if both the block body and disposal throw errors,
/// the disposal error becomes the 'suppressed' property and the original error becomes
/// the 'error' property. If multiple disposals fail, errors chain as:
/// SuppressedError(SuppressedError(original, first), second)
/// </remarks>
public class SharpTSSuppressedError : Exception
{
    /// <summary>
    /// Original error from the block (or a previously wrapped SuppressedError).
    /// </summary>
    public object? Error { get; }

    /// <summary>
    /// Suppressed error from the disposal that failed.
    /// </summary>
    public object? Suppressed { get; }

    /// <summary>
    /// Creates a new SuppressedError wrapping both the original and disposal errors.
    /// </summary>
    /// <param name="error">The original error from the block.</param>
    /// <param name="suppressed">The suppressed error from disposal.</param>
    /// <param name="message">Optional custom message (defaults to standard message).</param>
    public SharpTSSuppressedError(object? error, object? suppressed, string? message = null)
        : base(message ?? "An error was suppressed during disposal")
    {
        Error = error;
        Suppressed = suppressed;
    }

    /// <summary>
    /// Returns a string representation of this SuppressedError.
    /// </summary>
    public override string ToString()
    {
        return $"SuppressedError: {Message}\n  error: {Error}\n  suppressed: {Suppressed}";
    }
}
