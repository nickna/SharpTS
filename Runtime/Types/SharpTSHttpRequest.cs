using System.Net;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an HTTP IncomingMessage (request).
/// </summary>
/// <remarks>
/// Wraps an HttpListenerRequest to provide the Node.js IncomingMessage interface.
/// Properties: method, url, headers, httpVersion
/// Events: on('data', callback), on('end', callback)
/// </remarks>
public class SharpTSHttpRequest : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly HttpListenerRequest _request;
    private readonly List<(string Event, ISharpTSCallable Callback)> _listeners = new();
    private byte[]? _body;
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

    /// <summary>
    /// Gets a member (property or method) by name.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "method" => Method,
            "url" => Url,
            "httpVersion" => HttpVersion,
            "headers" => GetHeadersObject(),
            "on" => new BuiltInMethod("on", 2, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpRequest req)
                    return req.On(interp, args);
                return receiver;
            }).Bind(this),
            "once" => new BuiltInMethod("once", 2, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpRequest req)
                    return req.Once(interp, args);
                return receiver;
            }).Bind(this),
            "socket" => CreateSocketObject(),
            "complete" => _bodyRead && _endEmitted,
            _ => SharpTSUndefined.Instance
        };
    }

    /// <summary>
    /// Registers an event listener.
    /// </summary>
    private object? On(Interpreter interpreter, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("Runtime Error: on() requires event name and callback");

        var eventName = args[0]?.ToString() ?? throw new Exception("Runtime Error: event name must be a string");
        var callback = args[1] as ISharpTSCallable ?? throw new Exception("Runtime Error: callback must be a function");

        _listeners.Add((eventName, callback));
        return this;
    }

    /// <summary>
    /// Registers a one-time event listener.
    /// </summary>
    private object? Once(Interpreter interpreter, List<object?> args)
    {
        // For simplicity, once is handled the same as on since we only emit each event once anyway
        return On(interpreter, args);
    }

    /// <summary>
    /// Emits events to listeners and reads the body.
    /// Called internally when the request is processed.
    /// </summary>
    internal async Task EmitDataEventsAsync(Interpreter interpreter)
    {
        if (_bodyRead) return;

        try
        {
            using var ms = new MemoryStream();
            await _request.InputStream.CopyToAsync(ms);
            _body = ms.ToArray();
            _bodyRead = true;

            // Emit 'data' event with the body as a Buffer
            if (_body.Length > 0)
            {
                var dataListeners = _listeners.Where(l => l.Event == "data").ToList();
                foreach (var (_, callback) in dataListeners)
                {
                    callback.Call(interpreter, new List<object?> { new SharpTSBuffer(_body) });
                }
            }

            // Emit 'end' event
            _endEmitted = true;
            var endListeners = _listeners.Where(l => l.Event == "end").ToList();
            foreach (var (_, callback) in endListeners)
            {
                callback.Call(interpreter, new List<object?>());
            }
        }
        catch (Exception ex)
        {
            // Emit 'error' event
            var errorListeners = _listeners.Where(l => l.Event == "error").ToList();
            foreach (var (_, callback) in errorListeners)
            {
                callback.Call(interpreter, new List<object?> { new SharpTSError(ex.Message) });
            }
        }
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
    /// Creates a minimal socket object for compatibility.
    /// </summary>
    private SharpTSObject CreateSocketObject()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["remoteAddress"] = _request.RemoteEndPoint?.Address?.ToString(),
            ["remotePort"] = (double?)_request.RemoteEndPoint?.Port,
            ["localAddress"] = _request.LocalEndPoint?.Address?.ToString(),
            ["localPort"] = (double?)_request.LocalEndPoint?.Port
        });
    }

    public override string ToString() => $"IncomingMessage {{ method: '{Method}', url: '{Url}' }}";
}
