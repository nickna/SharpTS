using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// IncomingMessage for the interpreter HTTPS server (#1049). A Readable backed by the request
/// parsed off the TLS stream by <see cref="HttpProtocol"/>.
/// </summary>
public class SharpTSHttpsServerRequest : SharpTSReadable
{
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly HttpProtocol.ParsedRequest _parsed;
    private bool _bodyDelivered;

    public SharpTSHttpsServerRequest(HttpProtocol.ParsedRequest parsed) => _parsed = parsed;

    public override object? GetMember(string name)
    {
        return name switch
        {
            "method" => _parsed.Method,
            "url" => _parsed.Url,
            "httpVersion" => _parsed.HttpVersion,
            "httpVersionMajor" => (double)ParseVersion(0),
            "httpVersionMinor" => (double)ParseVersion(1),
            "headers" => new SharpTSObject(new Dictionary<string, object?>(_parsed.HeadersLower)),
            "rawHeaders" => new SharpTSArray(new List<object?>(_parsed.RawHeaders)),
            "trailers" => new SharpTSObject(new Dictionary<string, object?>()),
            "rawTrailers" => new SharpTSArray(new List<object?>()),
            "statusCode" => SharpTSUndefined.Instance,
            "statusMessage" => SharpTSUndefined.Instance,
            "complete" => _bodyDelivered,
            "aborted" => false,
            "socket" or "connection" => new SharpTSObject(new Dictionary<string, object?> { ["encrypted"] = true }),
            _ => base.GetMember(name)
        };
    }

    private int ParseVersion(int idx)
    {
        var parts = _parsed.HttpVersion.Split('.');
        return idx < parts.Length && int.TryParse(parts[idx], out var v) ? v : 1;
    }

    /// <summary>Pushes the already-read body into the Readable after listeners are attached.</summary>
    internal void DeliverBody(Interpreter interpreter, byte[] body)
    {
        var push = base.GetMember("push") as BuiltInMethod;
        var bound = push?.Bind(this);
        if (body.Length > 0)
            bound?.Call(interpreter, [new SharpTSBuffer(body)]);
        _bodyDelivered = true;
        bound?.Call(interpreter, new List<object?> { null });
    }

    public override string ToString() => $"IncomingMessage {{ method: '{_parsed.Method}', url: '{_parsed.Url}' }}";
}

/// <summary>
/// ServerResponse for the interpreter HTTPS server (#1049). A Writable that serializes an
/// HTTP/1.1 response over the TLS stream and closes the connection (Connection: close).
/// </summary>
public class SharpTSHttpsServerResponse : SharpTSWritable
{
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly SslStream _stream;
    private readonly TcpClient _tcpClient;
    private int _statusCode = 200;
    private string? _statusMessage;
    private bool _headersSent;
    private bool _finished;
    private readonly List<byte> _body = new();
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public SharpTSHttpsServerResponse(SslStream stream, TcpClient tcpClient)
    {
        _stream = stream;
        _tcpClient = tcpClient;
    }

    public override object? GetMember(string name)
    {
        return name switch
        {
            "statusCode" => (double)_statusCode,
            "statusMessage" => (object?)_statusMessage ?? StatusText(_statusCode),
            "headersSent" => _headersSent,
            "finished" => _finished,
            "writableEnded" => _finished,
            "sendDate" => true,
            "writeHead" => BuiltInMethod.CreateV2("writeHead", 1, 3, (_, receiver, args) =>
                receiver.ToObject() is SharpTSHttpsServerResponse r ? r.WriteHead(args) : receiver).Bind(this),
            "write" => BuiltInMethod.CreateV2("write", 1, 3, (interp, receiver, args) =>
                receiver.ToObject() is SharpTSHttpsServerResponse r ? r.Write(interp, args) : RuntimeValue.True).Bind(this),
            "end" => BuiltInMethod.CreateV2("end", 0, 3, (interp, receiver, args) =>
                receiver.ToObject() is SharpTSHttpsServerResponse r ? r.End(interp, args) : receiver).Bind(this),
            "setHeader" => BuiltInMethod.CreateV2("setHeader", 2, (_, receiver, args) =>
                receiver.ToObject() is SharpTSHttpsServerResponse r ? r.SetHeader(args) : receiver).Bind(this),
            "getHeader" => BuiltInMethod.CreateV2("getHeader", 1, (_, receiver, args) =>
            {
                var n = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                return n != null && _headers.TryGetValue(n, out var v) ? RuntimeValue.FromString(v) : RuntimeValue.Undefined;
            }).Bind(this),
            "removeHeader" => BuiltInMethod.CreateV2("removeHeader", 1, (_, receiver, args) =>
            {
                if (args.Length > 0 && args[0].ToObject()?.ToString() is { } n) _headers.Remove(n);
                return receiver;
            }).Bind(this),
            "hasHeader" => BuiltInMethod.CreateV2("hasHeader", 1, (_, _, args) =>
            {
                var n = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoolean(n != null && _headers.ContainsKey(n));
            }).Bind(this),
            "getHeaderNames" => BuiltInMethod.CreateV2("getHeaderNames", 0, (_, _, _) =>
                RuntimeValue.FromObject(new SharpTSArray(_headers.Keys.Select(k => (object?)k.ToLowerInvariant()).ToList()))).Bind(this),
            "flushHeaders" => BuiltInMethod.CreateV2("flushHeaders", 0, (_, receiver, _) => receiver).Bind(this),
            "writeContinue" => BuiltInMethod.CreateV2("writeContinue", 0, (_, _, _) => RuntimeValue.Undefined).Bind(this),
            "writeProcessing" => BuiltInMethod.CreateV2("writeProcessing", 0, (_, _, _) => RuntimeValue.Undefined).Bind(this),
            "addTrailers" => BuiltInMethod.CreateV2("addTrailers", 1, (_, _, _) => RuntimeValue.Undefined).Bind(this),
            _ => base.GetMember(name)
        };
    }

    public void SetMember(string name, object? value)
    {
        switch (name)
        {
            case "statusCode": if (value is double d) _statusCode = (int)d; break;
            case "statusMessage": if (value is string s) _statusMessage = s; break;
        }
    }

    private RuntimeValue WriteHead(ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].IsNumber) _statusCode = (int)args[0].AsNumberUnsafe();
        int headersIdx = 1;
        if (args.Length > 1 && args[1].IsString) { _statusMessage = args[1].AsStringUnsafe(); headersIdx = 2; }
        if (args.Length > headersIdx && args[headersIdx].ToObject() is SharpTSObject headers)
            foreach (var kv in headers.Fields) _headers[kv.Key] = kv.Value?.ToString() ?? "";
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue SetHeader(ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length >= 2 && args[0].ToObject()?.ToString() is { } n)
            _headers[n] = args[1].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Write(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].ToObject() is not ISharpTSCallable)
            _body.AddRange(Encode(args));
        return RuntimeValue.True;
    }

    private RuntimeValue End(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_finished) return RuntimeValue.FromObject(this);
        if (args.Length > 0 && args[0].ToObject() is { } first && first is not ISharpTSCallable && first is not SharpTSUndefined)
            _body.AddRange(Encode(args));

        var bodyBytes = _body.ToArray();
        _headers["Content-Length"] = bodyBytes.Length.ToString();
        if (!_headers.ContainsKey("Connection")) _headers["Connection"] = "close";
        if (!_headers.ContainsKey("Date")) _headers["Date"] = DateTime.UtcNow.ToString("R");

        try
        {
            _headersSent = true;
            var payload = HttpProtocol.SerializeResponse(_statusCode, _statusMessage ?? StatusText(_statusCode), _headers, bodyBytes);
            _stream.Write(payload, 0, payload.Length);
            _stream.Flush();
        }
        catch { /* client may have disconnected */ }
        finally
        {
            try { _stream.Dispose(); } catch { }
            try { _tcpClient.Close(); } catch { }
        }

        _finished = true;
        foreach (var arg in args)
            if (arg.ToObject() is ISharpTSCallable cb) { cb.Call(interpreter, []); break; }

        EmitEvent(interpreter, "finish", []);
        EmitEvent(interpreter, "close", []);
        return RuntimeValue.FromObject(this);
    }

    private static byte[] Encode(ReadOnlySpan<RuntimeValue> args)
    {
        return args[0].ToObject() switch
        {
            string str => Encoding.UTF8.GetBytes(str),
            SharpTSBuffer buf => buf.Data,
            null => Array.Empty<byte>(),
            var o => Encoding.UTF8.GetBytes(o.ToString() ?? "")
        };
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK", 201 => "Created", 204 => "No Content", 301 => "Moved Permanently",
        302 => "Found", 304 => "Not Modified", 400 => "Bad Request", 401 => "Unauthorized",
        403 => "Forbidden", 404 => "Not Found", 500 => "Internal Server Error", _ => "OK"
    };

    public override string ToString() => $"ServerResponse {{ statusCode: {_statusCode} }}";
}
