using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an HTTP client-side IncomingMessage (the response to a
/// <see cref="SharpTSClientRequest"/>). Extends <see cref="SharpTSReadable"/> so the
/// response body is a Readable stream ('data'/'end'/'error', <c>for await</c>).
/// </summary>
/// <remarks>
/// Built from the data extracted from a .NET <c>HttpResponseMessage</c> rather than
/// holding the message itself, so the underlying response can be disposed. Properties:
/// statusCode, statusMessage, httpVersion/Major/Minor, headers (lowercased),
/// rawHeaders (original case + order), complete, aborted.
/// </remarks>
public class SharpTSClientResponse : SharpTSReadable
{
    /// <inheritdoc />
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly int _statusCode;
    private readonly string _statusMessage;
    private readonly string _httpVersion;
    private readonly Dictionary<string, object?> _headers;
    private readonly List<object?> _rawHeaders;
    private readonly SharpTSObject? _socket;
    private bool _complete;
    private bool _aborted;

    public SharpTSClientResponse(
        int statusCode,
        string statusMessage,
        string httpVersion,
        Dictionary<string, object?> headers,
        List<object?> rawHeaders,
        SharpTSObject? socket = null)
    {
        _statusCode = statusCode;
        _statusMessage = statusMessage;
        _httpVersion = httpVersion;
        _headers = headers;
        _rawHeaders = rawHeaders;
        _socket = socket;
    }

    /// <summary>Marks the body as fully delivered (set when the stream ends).</summary>
    internal void MarkComplete() => _complete = true;

    /// <summary>Marks the response as aborted (request torn down before the body finished).</summary>
    internal void MarkAborted() => _aborted = true;

    public override object? GetMember(string name)
    {
        return name switch
        {
            "statusCode" => (double)_statusCode,
            "statusMessage" => _statusMessage,
            "httpVersion" => _httpVersion,
            "httpVersionMajor" => (double)ParseVersionPart(0),
            "httpVersionMinor" => (double)ParseVersionPart(1),
            "headers" => new SharpTSObject(new Dictionary<string, object?>(_headers)),
            "headersDistinct" => GetHeadersDistinct(),
            "rawHeaders" => new SharpTSArray(new List<object?>(_rawHeaders)),
            "trailers" => new SharpTSObject(new Dictionary<string, object?>()),
            "rawTrailers" => new SharpTSArray(new List<object?>()),
            "complete" => _complete,
            "aborted" => _aborted,
            "url" => "",
            "method" => (object?)null,
            "socket" or "connection" => (object?)_socket ?? new SharpTSObject(new Dictionary<string, object?>()),
            "setTimeout" => BuiltInMethod.CreateV2("setTimeout", 1, 2, (interp, receiver, args) =>
            {
                // IncomingMessage.setTimeout(msecs, cb?) — cb registered as a 'timeout' listener.
                if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb)
                    EmitEvent(interp, "__noop_register_timeout", []); // no-op; timeouts are managed by the request
                return receiver;
            }),

            // Inherit Readable stream methods (on, once, pipe, read, pause, resume, for await, etc.)
            _ => base.GetMember(name)
        };
    }

    private int ParseVersionPart(int index)
    {
        var parts = _httpVersion.Split('.');
        if (index < parts.Length && int.TryParse(parts[index], out var v))
            return v;
        return index == 0 ? 1 : 1;
    }

    private SharpTSObject GetHeadersDistinct()
    {
        // Node's headersDistinct keeps every header value as a string array.
        var distinct = new Dictionary<string, object?>();
        foreach (var (key, value) in _headers)
        {
            var arr = new List<object?> { value };
            distinct[key] = new SharpTSArray(arr);
        }
        return new SharpTSObject(distinct);
    }

    public override string ToString() => $"IncomingMessage {{ statusCode: {_statusCode} }}";
}
