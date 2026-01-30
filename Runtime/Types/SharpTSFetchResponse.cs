using System.Net;
using System.Text;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a fetch Response object.
/// </summary>
/// <remarks>
/// Wraps an HttpResponseMessage to provide the Web API Response interface.
/// Properties: status, statusText, ok, headers, url
/// Methods: json(), text(), arrayBuffer() - all return Promises
/// </remarks>
public class SharpTSFetchResponse : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly HttpResponseMessage _response;
    private readonly string _url;
    private byte[]? _bodyBytes;
    private bool _bodyConsumed;

    /// <summary>
    /// Creates a new Response wrapping an HttpResponseMessage.
    /// </summary>
    /// <param name="response">The HTTP response to wrap.</param>
    /// <param name="url">The final URL after any redirects.</param>
    public SharpTSFetchResponse(HttpResponseMessage response, string url)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    /// <summary>
    /// HTTP status code (e.g., 200, 404).
    /// </summary>
    public double Status => (double)_response.StatusCode;

    /// <summary>
    /// HTTP status text (e.g., "OK", "Not Found").
    /// </summary>
    public string StatusText => _response.ReasonPhrase ?? GetDefaultStatusText(_response.StatusCode);

    /// <summary>
    /// True if the response status is in the 200-299 range.
    /// </summary>
    public bool Ok => _response.IsSuccessStatusCode;

    /// <summary>
    /// The final URL of the response (after any redirects).
    /// </summary>
    public string Url => _url;

    /// <summary>
    /// Gets a member (property or method) by name.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "status" => Status,
            "statusText" => StatusText,
            "ok" => Ok,
            "url" => Url,
            "headers" => GetHeadersObject(),
            "json" => new BuiltInAsyncMethod("json", 0, JsonImpl).Bind(this),
            "text" => new BuiltInAsyncMethod("text", 0, TextImpl).Bind(this),
            "arrayBuffer" => new BuiltInAsyncMethod("arrayBuffer", 0, ArrayBufferImpl).Bind(this),
            "clone" => new BuiltInMethod("clone", 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSFetchResponse resp)
                    return resp.Clone();
                throw new Exception("Runtime Error: clone requires a Response object");
            }).Bind(this),
            "bodyUsed" => _bodyConsumed,
            _ => SharpTSUndefined.Instance
        };
    }

    /// <summary>
    /// Parses the response body as JSON and returns a Promise.
    /// </summary>
    private async Task<object?> JsonImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        var text = await ReadBodyAsStringAsync();
        try
        {
            return ParseJson(text);
        }
        catch (JsonException ex)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSSyntaxError($"Unexpected token in JSON: {ex.Message}"));
        }
    }

    /// <summary>
    /// Parses JSON text into SharpTS runtime values.
    /// </summary>
    private static object? ParseJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return ConvertJsonElement(doc.RootElement);
    }

    /// <summary>
    /// Converts a JsonElement to SharpTS runtime values.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => new SharpTSArray(
                element.EnumerateArray().Select(ConvertJsonElement).ToList()),
            JsonValueKind.Object => new SharpTSObject(
                element.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => ConvertJsonElement(p.Value))),
            _ => null
        };
    }

    /// <summary>
    /// Returns the response body as text in a Promise.
    /// </summary>
    private async Task<object?> TextImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        return await ReadBodyAsStringAsync();
    }

    /// <summary>
    /// Returns the response body as an ArrayBuffer (Buffer) in a Promise.
    /// </summary>
    private async Task<object?> ArrayBufferImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        var bytes = await ReadBodyAsBytesAsync();
        return new SharpTSBuffer(bytes);
    }

    /// <summary>
    /// Reads the body as bytes, consuming it.
    /// </summary>
    private async Task<byte[]> ReadBodyAsBytesAsync()
    {
        if (_bodyConsumed && _bodyBytes != null)
        {
            return _bodyBytes;
        }

        if (_bodyConsumed)
        {
            throw new Exception("Runtime Error: body stream already read");
        }

        _bodyBytes = await _response.Content.ReadAsByteArrayAsync();
        _bodyConsumed = true;
        return _bodyBytes;
    }

    /// <summary>
    /// Reads the body as a string, consuming it.
    /// </summary>
    private async Task<string> ReadBodyAsStringAsync()
    {
        var bytes = await ReadBodyAsBytesAsync();
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Gets the headers as a SharpTSObject for easy access.
    /// </summary>
    private SharpTSObject GetHeadersObject()
    {
        var headers = new Dictionary<string, object?>();

        // Add response headers
        foreach (var header in _response.Headers)
        {
            headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
        }

        // Add content headers
        if (_response.Content?.Headers != null)
        {
            foreach (var header in _response.Content.Headers)
            {
                headers[header.Key.ToLowerInvariant()] = string.Join(", ", header.Value);
            }
        }

        return new SharpTSObject(headers);
    }

    /// <summary>
    /// Creates a clone of the response (allows body to be read again).
    /// </summary>
    private SharpTSFetchResponse Clone()
    {
        if (_bodyConsumed)
        {
            throw new Exception("Runtime Error: cannot clone Response after body has been consumed");
        }
        // Create a new response wrapper - shares the same underlying response
        return new SharpTSFetchResponse(_response, _url);
    }

    /// <summary>
    /// Gets default status text for common status codes.
    /// </summary>
    private static string GetDefaultStatusText(HttpStatusCode code)
    {
        return code switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.Created => "Created",
            HttpStatusCode.Accepted => "Accepted",
            HttpStatusCode.NoContent => "No Content",
            HttpStatusCode.MovedPermanently => "Moved Permanently",
            HttpStatusCode.Found => "Found",
            HttpStatusCode.NotModified => "Not Modified",
            HttpStatusCode.BadRequest => "Bad Request",
            HttpStatusCode.Unauthorized => "Unauthorized",
            HttpStatusCode.Forbidden => "Forbidden",
            HttpStatusCode.NotFound => "Not Found",
            HttpStatusCode.MethodNotAllowed => "Method Not Allowed",
            HttpStatusCode.InternalServerError => "Internal Server Error",
            HttpStatusCode.NotImplemented => "Not Implemented",
            HttpStatusCode.BadGateway => "Bad Gateway",
            HttpStatusCode.ServiceUnavailable => "Service Unavailable",
            _ => ((int)code).ToString()
        };
    }

    public override string ToString() => $"Response {{ status: {Status}, ok: {Ok} }}";
}
