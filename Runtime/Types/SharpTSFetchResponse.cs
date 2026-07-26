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
    private readonly bool _redirected;
    private byte[]? _bodyBytes;
    private bool _bodyConsumed;
    private SharpTSReadable? _bodyStream;

    /// <summary>
    /// Creates a new Response wrapping an HttpResponseMessage.
    /// </summary>
    /// <param name="response">The HTTP response to wrap.</param>
    /// <param name="url">The final URL after any redirects.</param>
    /// <param name="redirected">Whether the request was redirected.</param>
    public SharpTSFetchResponse(HttpResponseMessage response, string url, bool redirected = false)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _redirected = redirected;
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
            "redirected" => _redirected,
            "type" => "basic",
            "headers" => GetHeadersObject(),
            // Body readers don't opt into refsEventLoopWhileInFlight (the #320 native-I/O
            // liveness audit): fetch uses the default HttpCompletionOption.ResponseContentRead,
            // so the body is already fully buffered by the time fetch()'s promise resolves
            // (the in-flight network leg is Ref'd inside FetchBuiltIns). These reads drain that
            // in-memory buffer and complete synchronously, so a Ref here would never fire. If
            // fetch ever switches to ResponseHeadersRead / streamed bodies, these three must
            // opt in (the read would then be real in-flight I/O that can outrun the 250ms
            // quiescence window). See FetchBuiltIns.FetchImpl and BuiltInAsyncMethod.
            "json" => new BuiltInAsyncMethod("json", 0, JsonImpl).Bind(this),
            "text" => new BuiltInAsyncMethod("text", 0, TextImpl).Bind(this),
            "arrayBuffer" => new BuiltInAsyncMethod("arrayBuffer", 0, ArrayBufferImpl).Bind(this),
            "clone" => BuiltInMethod.CreateV2("clone", 0, static (_, receiver, _) =>
            {
                if (receiver.ToObject() is SharpTSFetchResponse resp)
                    return RuntimeValue.FromObject(resp.Clone());
                throw new Exception("Runtime Error: clone requires a Response object");
            }).Bind(this),
            "body" => GetBodyReadable(),
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
            return RuntimeJson.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSSyntaxError($"Unexpected token in JSON: {ex.Message}"));
        }
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
    /// Gets the headers as a SharpTSHeaders object with full Headers API.
    /// </summary>
    private SharpTSHeaders GetHeadersObject()
    {
        return new SharpTSHeaders(_response);
    }

    /// <summary>
    /// Gets the response body as a Readable stream.
    /// The stream pushes the full body as a single Buffer chunk, then signals EOF.
    /// Accessing body marks the body as consumed (same as calling json/text/arrayBuffer).
    /// </summary>
    private SharpTSReadable GetBodyReadable()
    {
        if (_bodyStream != null)
            return _bodyStream;

        _bodyStream = new SharpTSReadable();

        // Load body bytes eagerly and push into the readable
        // This follows the existing pattern where body is fully loaded
        Task.Run(async () =>
        {
            try
            {
                var bytes = await ReadBodyAsBytesAsync();
                var buf = new SharpTSBuffer(bytes);

                // Push data and EOF into the readable's internal buffer
                // The readable will drain when a 'data' listener is added
                var pushMethod = _bodyStream.GetMember("push") as BuiltInMethod;
                pushMethod?.Bind(_bodyStream).Call(null!, new List<object?> { buf });
                pushMethod?.Bind(_bodyStream).Call(null!, new List<object?> { null }); // EOF
            }
            catch (Exception)
            {
                // Body already consumed or error - push EOF
                var pushMethod = _bodyStream.GetMember("push") as BuiltInMethod;
                pushMethod?.Bind(_bodyStream).Call(null!, new List<object?> { null });
            }
        }).GetAwaiter().GetResult();

        return _bodyStream;
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
