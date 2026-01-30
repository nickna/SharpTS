using System.Net;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an HTTP ServerResponse.
/// </summary>
/// <remarks>
/// Wraps an HttpListenerResponse to provide the Node.js ServerResponse interface.
/// Properties: statusCode, statusMessage, headersSent
/// Methods: writeHead(status, headers?), write(data), end(data?), setHeader(name, value)
/// </remarks>
public class SharpTSHttpResponse : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly HttpListenerResponse _response;
    private bool _headersSent;
    private bool _finished;
    private readonly List<byte> _bodyBuffer = new();

    /// <summary>
    /// Creates a new ServerResponse wrapping an HttpListenerResponse.
    /// </summary>
    public SharpTSHttpResponse(HttpListenerResponse response)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));
    }

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    public double StatusCode
    {
        get => _response.StatusCode;
        set => _response.StatusCode = (int)value;
    }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string StatusMessage
    {
        get => _response.StatusDescription;
        set => _response.StatusDescription = value;
    }

    /// <summary>
    /// Whether headers have been sent.
    /// </summary>
    public bool HeadersSent => _headersSent;

    /// <summary>
    /// Whether the response has finished.
    /// </summary>
    public bool Finished => _finished;

    /// <summary>
    /// Gets a member (property or method) by name.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "statusCode" => StatusCode,
            "statusMessage" => StatusMessage,
            "headersSent" => HeadersSent,
            "finished" => Finished,
            "writeHead" => new BuiltInMethod("writeHead", 1, 2, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpResponse res)
                    return res.WriteHead(args);
                return receiver;
            }).Bind(this),
            "write" => new BuiltInMethod("write", 1, 2, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpResponse res)
                    return res.Write(args);
                return true;
            }).Bind(this),
            "end" => new BuiltInMethod("end", 0, 2, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpResponse res)
                    return res.End(args);
                return receiver;
            }).Bind(this),
            "setHeader" => new BuiltInMethod("setHeader", 2, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpResponse res)
                    return res.SetHeader(args);
                return receiver;
            }).Bind(this),
            "getHeader" => new BuiltInMethod("getHeader", 1, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpResponse res)
                    return res.GetHeader(args);
                return SharpTSUndefined.Instance;
            }).Bind(this),
            "removeHeader" => new BuiltInMethod("removeHeader", 1, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpResponse res)
                    return res.RemoveHeader(args);
                return receiver;
            }).Bind(this),
            "on" => new BuiltInMethod("on", 2, (_, receiver, _) => receiver).Bind(this),
            _ => SharpTSUndefined.Instance
        };
    }

    /// <summary>
    /// Sets a member (property) by name.
    /// </summary>
    public void SetMember(string name, object? value)
    {
        switch (name)
        {
            case "statusCode":
                if (value is double d)
                    StatusCode = d;
                break;
            case "statusMessage":
                if (value is string s)
                    StatusMessage = s;
                break;
        }
    }

    /// <summary>
    /// Writes the status code and headers.
    /// </summary>
    private object? WriteHead(List<object?> args)
    {
        if (_headersSent)
            throw new Exception("Runtime Error: Cannot call writeHead after headers have been sent");

        if (args.Count == 0 || args[0] is not double statusCode)
            throw new Exception("Runtime Error: writeHead requires a status code");

        _response.StatusCode = (int)statusCode;

        // Optional headers
        if (args.Count > 1 && args[1] is SharpTSObject headers)
        {
            foreach (var kv in headers.Fields)
            {
                var headerName = kv.Key;
                var headerValue = kv.Value?.ToString() ?? "";
                SetResponseHeader(headerName, headerValue);
            }
        }

        return this;
    }

    /// <summary>
    /// Writes data to the response body.
    /// </summary>
    private object? Write(List<object?> args)
    {
        if (_finished)
            throw new Exception("Runtime Error: Cannot write after response has ended");

        if (args.Count == 0) return true;

        byte[] data;
        var encoding = args.Count > 1 && args[1] is string enc ? enc : "utf8";

        switch (args[0])
        {
            case string str:
                data = GetEncoding(encoding).GetBytes(str);
                break;
            case SharpTSBuffer buffer:
                data = buffer.Data;
                break;
            default:
                data = Encoding.UTF8.GetBytes(args[0]?.ToString() ?? "");
                break;
        }

        _bodyBuffer.AddRange(data);
        return true;
    }

    /// <summary>
    /// Ends the response, optionally writing final data.
    /// </summary>
    private object? End(List<object?> args)
    {
        if (_finished) return this;

        // Write any final data
        if (args.Count > 0 && args[0] != null)
        {
            Write(args);
        }

        // Actually send the response
        try
        {
            _headersSent = true;
            _response.ContentLength64 = _bodyBuffer.Count;
            if (_bodyBuffer.Count > 0)
            {
                _response.OutputStream.Write(_bodyBuffer.ToArray(), 0, _bodyBuffer.Count);
            }
            _response.OutputStream.Close();
        }
        catch
        {
            // Client may have disconnected
        }

        _finished = true;
        return this;
    }

    /// <summary>
    /// Sets a single header value.
    /// </summary>
    private object? SetHeader(List<object?> args)
    {
        if (_headersSent)
            throw new Exception("Runtime Error: Cannot set header after headers have been sent");

        if (args.Count < 2)
            throw new Exception("Runtime Error: setHeader requires name and value");

        var name = args[0]?.ToString() ?? throw new Exception("Runtime Error: header name must be a string");
        var value = args[1]?.ToString() ?? "";

        SetResponseHeader(name, value);
        return this;
    }

    /// <summary>
    /// Gets a header value.
    /// </summary>
    private object? GetHeader(List<object?> args)
    {
        if (args.Count == 0) return SharpTSUndefined.Instance;

        var name = args[0]?.ToString();
        if (name == null) return SharpTSUndefined.Instance;

        var headerValue = _response.Headers[name];
        return string.IsNullOrEmpty(headerValue) ? SharpTSUndefined.Instance : (object)headerValue;
    }

    /// <summary>
    /// Removes a header.
    /// </summary>
    private object? RemoveHeader(List<object?> args)
    {
        if (_headersSent)
            throw new Exception("Runtime Error: Cannot remove header after headers have been sent");

        if (args.Count == 0) return this;

        var name = args[0]?.ToString();
        if (name != null)
        {
            _response.Headers.Remove(name);
        }
        return this;
    }

    /// <summary>
    /// Sets a response header, handling special cases.
    /// </summary>
    private void SetResponseHeader(string name, string value)
    {
        // Some headers must be set via properties
        if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            _response.ContentType = value;
        }
        else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(value, out var length))
                _response.ContentLength64 = length;
        }
        else
        {
            _response.Headers[name] = value;
        }
    }

    /// <summary>
    /// Gets the encoding by name.
    /// </summary>
    private static Encoding GetEncoding(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => Encoding.UTF8,
            "ascii" => Encoding.ASCII,
            "latin1" or "binary" => Encoding.Latin1,
            "utf16le" or "ucs2" or "ucs-2" => Encoding.Unicode,
            _ => Encoding.UTF8
        };
    }

    public override string ToString() => $"ServerResponse {{ statusCode: {StatusCode} }}";
}
