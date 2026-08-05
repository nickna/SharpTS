using System.Net;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an HTTP IncomingMessage (request).
/// Extends SharpTSReadable for full Readable stream semantics (data, end, error events).
/// </summary>
/// <remarks>
/// Wraps an HttpListenerRequest to provide the Node.js IncomingMessage interface.
/// Properties: method, url, headers, httpVersion, statusCode, statusMessage
/// Inherits stream methods: on, once, pipe, read, pause, resume, etc.
/// </remarks>
public class SharpTSHttpRequest : SharpTSReadable
{
    /// <inheritdoc />
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly HttpListenerRequest _request;
    private bool _bodyRead;
    private bool _endEmitted;

    /// <summary>
    /// Creates a new IncomingMessage wrapping an HttpListenerRequest.
    /// </summary>
    public SharpTSHttpRequest(HttpListenerRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>
    /// HTTP method (GET, POST, etc.).
    /// </summary>
    public string Method => _request.HttpMethod;

    /// <summary>
    /// Request URL path (e.g., "/api/users").
    /// </summary>
    public string Url => _request.RawUrl ?? "/";

    /// <summary>
    /// HTTP version string (e.g., "1.1").
    /// </summary>
    public string HttpVersion => $"{_request.ProtocolVersion.Major}.{_request.ProtocolVersion.Minor}";

    private bool _aborted;

    /// <summary>Marks the request as aborted (peer disconnected before the body finished).</summary>
    internal void MarkAborted() => _aborted = true;

    /// <summary>
    /// Gets a member (property or method) by name.
    /// Overrides Readable.GetMember to add HTTP-specific properties.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            // HTTP-specific properties
            "method" => Method,
            "url" => Url,
            "httpVersion" => HttpVersion,
            "httpVersionMajor" => (double)_request.ProtocolVersion.Major,
            "httpVersionMinor" => (double)_request.ProtocolVersion.Minor,
            "headers" => GetHeadersObject(),
            "headersDistinct" => GetHeadersDistinct(),
            "rawHeaders" => GetRawHeaders(),
            "socket" or "connection" => CreateSocketObject(),
            "complete" => _bodyRead && _endEmitted,
            // Server-side IncomingMessage has no statusCode/statusMessage (those are client-side).
            "statusCode" => SharpTSUndefined.Instance,
            "statusMessage" => SharpTSUndefined.Instance,
            "trailers" => new SharpTSObject(new Dictionary<string, object?>()),
            "rawTrailers" => new SharpTSArray(new List<object?>()),
            "aborted" => _aborted,
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, (interp, receiver, args) =>
            {
                if (receiver.ToObject() is SharpTSHttpRequest request)
                    return request.DestroyRequest(interp, args);
                return receiver;
            }).Bind(this),

            // Inherit Readable stream methods (on, once, pipe, read, pause, resume, etc.)
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue DestroyRequest(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_aborted)
        {
            _aborted = true;
            try { _request.InputStream.Close(); } catch { /* peer may already have closed */ }
            EmitEvent(interpreter, "aborted", []);
        }

        // Preserve the base Readable destroy/error/close lifecycle as well.
        var baseDestroy = base.GetMember("destroy") as BuiltInMethod;
        return baseDestroy?.Bind(this).CallV2(interpreter, args) ?? RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Emits data and end events by reading the request body.
    /// Called internally when the request is processed.
    /// </summary>
    internal async Task EmitDataEventsAsync(Interpreter interpreter)
    {
        if (_bodyRead) return;
        _bodyRead = true;

        try
        {
            var pushMethod = base.GetMember("push") as BuiltInMethod;
            var bound = pushMethod?.Bind(this);

            // On-demand streaming (#1048): read the request body in chunks and push each as it
            // arrives, rather than buffering the whole body then emitting one 'data'. Listeners
            // were attached synchronously by the handler before this runs.
            var buffer = new byte[16384];
            int n;
            while (!_aborted &&
                   (n = await _request.InputStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = new byte[n];
                Array.Copy(buffer, chunk, n);
                bound?.Call(interpreter, [new SharpTSBuffer(chunk)]);
            }

            // destroy() closes InputStream from inside a data listener. Depending
            // on the platform that can make the pending read return EOF instead
            // of throwing; either way an aborted request must never emit 'end'.
            if (_aborted) return;

            // Make complete observable from inside the synchronous end listener.
            _endEmitted = true;
            bound?.Call(interpreter, new List<object?> { null });
        }
        catch (Exception ex)
        {
            // Closing InputStream is the expected destroy() wake-up path. The
            // base Readable destroy method already emitted any supplied error.
            if (_aborted) return;

            _aborted = true;
            EmitEvent(interpreter, "aborted", []);
            // Emit 'error' event through the Readable stream
            EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
        }
    }

    /// <summary>
    /// Node's headersDistinct — every header value as a string array.
    /// </summary>
    private SharpTSObject GetHeadersDistinct()
    {
        var distinct = new Dictionary<string, object?>();
        foreach (string? key in _request.Headers.AllKeys)
        {
            if (key != null)
                distinct[key.ToLowerInvariant()] = new SharpTSArray(new List<object?> { _request.Headers[key] });
        }
        return new SharpTSObject(distinct);
    }

    /// <summary>
    /// Gets the headers as a SharpTSObject.
    /// </summary>
    private SharpTSObject GetHeadersObject()
    {
        var headers = new Dictionary<string, object?>();
        foreach (string? key in _request.Headers.AllKeys)
        {
            if (key != null)
            {
                headers[key.ToLowerInvariant()] = _request.Headers[key];
            }
        }
        return new SharpTSObject(headers);
    }

    /// <summary>
    /// Gets raw headers as alternating [name, value] array.
    /// </summary>
    private SharpTSArray GetRawHeaders()
    {
        var raw = new List<object?>();
        foreach (string? key in _request.Headers.AllKeys)
        {
            if (key != null)
            {
                raw.Add(key);
                raw.Add(_request.Headers[key]);
            }
        }
        return new SharpTSArray(raw);
    }

    /// <summary>
    /// Creates a minimal socket object for compatibility.
    /// </summary>
    private SharpTSObject CreateSocketObject()
    {
        var remote = _request.RemoteEndPoint;
        string family = remote?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["remoteAddress"] = remote?.Address?.ToString(),
            ["remotePort"] = (double?)remote?.Port,
            ["remoteFamily"] = family,
            ["localAddress"] = _request.LocalEndPoint?.Address?.ToString(),
            ["localPort"] = (double?)_request.LocalEndPoint?.Port,
            ["family"] = family,
            ["encrypted"] = _request.IsSecureConnection
        });
    }

    public override string ToString() => $"IncomingMessage {{ method: '{Method}', url: '{Url}' }}";
}
