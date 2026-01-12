namespace SharpTS.LspBridge.Protocol;

/// <summary>
/// Response message from the LSP bridge to the IDE.
/// </summary>
public sealed class BridgeResponse
{
    /// <summary>
    /// Sequence number matching the request.
    /// </summary>
    public int Seq { get; set; }

    /// <summary>
    /// Whether the command succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Command-specific response body.
    /// </summary>
    public object? Body { get; init; }

    /// <summary>
    /// Creates a successful response with the given body.
    /// </summary>
    public static BridgeResponse Ok(object? body = null) => new()
    {
        Success = true,
        Body = body
    };

    /// <summary>
    /// Creates an error response with the given message.
    /// </summary>
    public static BridgeResponse Error(string message) => new()
    {
        Success = false,
        Message = message
    };
}
