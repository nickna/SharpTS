namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript SuppressedError.
/// Thrown when disposal fails while another error is pending.
/// TypeScript ES2024 explicit resource management semantics.
/// </summary>
/// <remarks>
/// When using 'using' declarations, if both the block body and disposal throw errors,
/// the disposal error becomes the 'error' property and the prior error becomes
/// the 'suppressed' property. Later cleanup failures wrap the prior completion.
/// </remarks>
public class SharpTSSuppressedError : Exception, ISharpTSOwnPropertyObject
{
    private readonly SharpTSObject _properties = new([]);
    SharpTSObject ISharpTSOwnPropertyObject.OwnProperties => _properties;
    /// <summary>
    /// The latest cleanup error.
    /// </summary>
    public object? Error { get; }

    /// <summary>
    /// The prior error from the block or an earlier cleanup.
    /// </summary>
    public object? Suppressed { get; }

    /// <summary>
    /// Creates a new SuppressedError wrapping both the original and disposal errors.
    /// </summary>
    /// <param name="error">The latest cleanup error.</param>
    /// <param name="suppressed">The prior error being suppressed.</param>
    /// <param name="message">Optional custom message (defaults to standard message).</param>
    public SharpTSSuppressedError(object? error, object? suppressed, string? message = null)
        : base(message ?? "An error was suppressed during disposal")
    {
        Error = error;
        Suppressed = suppressed;
        _properties.DefineProperty("error", new SharpTSPropertyDescriptor(
            error, writable: true, enumerable: false, configurable: true) { HasValue = true });
        _properties.DefineProperty("suppressed", new SharpTSPropertyDescriptor(
            suppressed, writable: true, enumerable: false, configurable: true) { HasValue = true });
    }

    /// <summary>
    /// Returns a string representation of this SuppressedError.
    /// </summary>
    public override string ToString()
    {
        return $"SuppressedError: {Message}\n  error: {Error}\n  suppressed: {Suppressed}";
    }
}
