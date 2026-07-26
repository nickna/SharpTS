using System.Text;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a standalone Response object.
/// </summary>
/// <remarks>
/// Provides the Web API Response constructor for creating HTTP responses.
/// Unlike SharpTSFetchResponse which wraps HttpResponseMessage (lazy network body),
/// this wraps in-memory body data.
/// Properties: status, statusText, ok, url, type, headers, body, bodyUsed, redirected
/// Methods: json(), text(), arrayBuffer(), clone()
/// </remarks>
public class SharpTSResponse : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly double _status;
    private readonly string _statusText;
    private readonly SharpTSHeaders _headers;
    private readonly byte[] _bodyBytes;
    private readonly string _type;
    private bool _bodyConsumed;

    /// <summary>
    /// Creates a new Response with the given body and optional init object.
    /// </summary>
    public SharpTSResponse(object? body, SharpTSObject? init)
    {
        _status = 200;
        _statusText = "";
        _headers = new SharpTSHeaders();
        _type = "default";

        if (init != null)
        {
            if (init.Fields.TryGetValue("status", out var statusObj) && statusObj is double s)
                _status = s;

            if (init.Fields.TryGetValue("statusText", out var stObj) && stObj is string st)
                _statusText = st;

            if (init.Fields.TryGetValue("headers", out var headersObj))
            {
                _headers = headersObj switch
                {
                    SharpTSHeaders h => h,
                    SharpTSObject obj => new SharpTSHeaders(obj),
                    _ => new SharpTSHeaders()
                };
            }
        }

        _bodyBytes = body switch
        {
            string s => Encoding.UTF8.GetBytes(s),
            SharpTSBuffer buf => buf.Data,
            null => [],
            _ => Encoding.UTF8.GetBytes(body.ToString() ?? "")
        };
    }

    /// <summary>
    /// Internal constructor for static factory methods and clone.
    /// </summary>
    internal SharpTSResponse(double status, string statusText, SharpTSHeaders headers, byte[] bodyBytes, string type)
    {
        _status = status;
        _statusText = statusText;
        _headers = headers;
        _bodyBytes = bodyBytes;
        _type = type;
    }

    public double Status => _status;
    public string StatusText => _statusText;
    public bool Ok => _status >= 200 && _status <= 299;
    public string Url => "";
    public string Type => _type;
    public bool Redirected => false;
    public SharpTSHeaders Headers => _headers;
    public bool BodyUsed => _bodyConsumed;

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
            "type" => Type,
            "redirected" => Redirected,
            "headers" => Headers,
            "body" => (object?)null, // simplified; no ReadableStream
            "bodyUsed" => BodyUsed,
            "json" => new BuiltInAsyncMethod("json", 0, JsonImpl).Bind(this),
            "text" => new BuiltInAsyncMethod("text", 0, TextImpl).Bind(this),
            "arrayBuffer" => new BuiltInAsyncMethod("arrayBuffer", 0, ArrayBufferImpl).Bind(this),
            "clone" => BuiltInMethod.CreateV2("clone", 0, static (_, receiver, _) =>
            {
                if (receiver.ToObject() is SharpTSResponse resp)
                    return RuntimeValue.FromObject(resp.Clone());
                throw new Exception("Runtime Error: clone requires a Response object");
            }).Bind(this),
            _ => SharpTSUndefined.Instance
        };
    }

    private async Task<object?> JsonImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        var text = ReadBodyAsString();
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

    private async Task<object?> TextImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        return ReadBodyAsString();
    }

    private async Task<object?> ArrayBufferImpl(Interpreter interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSBuffer(ReadBodyAsBytes());
    }

    private byte[] ReadBodyAsBytes()
    {
        if (_bodyConsumed)
            return _bodyBytes;

        _bodyConsumed = true;
        return _bodyBytes;
    }

    private string ReadBodyAsString()
    {
        var bytes = ReadBodyAsBytes();
        return Encoding.UTF8.GetString(bytes);
    }

    private SharpTSResponse Clone()
    {
        if (_bodyConsumed)
            throw new Exception("Runtime Error: cannot clone Response after body has been consumed");

        return new SharpTSResponse(_status, _statusText, _headers, _bodyBytes, _type);
    }

    public override string ToString() => $"Response {{ status: {Status}, ok: {Ok} }}";
}
