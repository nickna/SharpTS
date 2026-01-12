using System.Text.Json;

namespace SharpTS.LspBridge.Protocol;

/// <summary>
/// Request message from the IDE to the LSP bridge.
/// </summary>
public sealed class BridgeRequest
{
    /// <summary>
    /// Sequence number for request/response correlation.
    /// </summary>
    public int Seq { get; init; }

    /// <summary>
    /// The command to execute (e.g., "resolve-type", "list-attributes").
    /// </summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// Command-specific arguments as a JSON element for deferred parsing.
    /// </summary>
    public JsonElement? Arguments { get; init; }

    /// <summary>
    /// Gets a typed argument value from the arguments object.
    /// </summary>
    public T? GetArgument<T>(string name)
    {
        if (Arguments is not { ValueKind: JsonValueKind.Object } args)
            return default;

        if (!args.TryGetProperty(name, out var prop))
            return default;

        return JsonSerializer.Deserialize<T>(prop.GetRawText());
    }

    /// <summary>
    /// Gets a string argument value.
    /// </summary>
    public string? GetStringArgument(string name)
    {
        if (Arguments is not { ValueKind: JsonValueKind.Object } args)
            return null;

        if (!args.TryGetProperty(name, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
