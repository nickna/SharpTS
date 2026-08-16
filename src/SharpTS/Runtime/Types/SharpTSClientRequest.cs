using System.Net.Http;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js http.ClientRequest — the object returned by
/// <c>http.request()</c>/<c>http.get()</c> (and the https variants). It is a Writable
/// stream: the request body is produced by <c>write()</c>/<c>end()</c>. When the request
/// completes it fires <c>'response'</c> with a <see cref="SharpTSClientResponse"/>
/// (IncomingMessage) plus the lifecycle events <c>'socket'</c>/<c>'error'</c>/<c>'abort'</c>/
/// <c>'close'</c>/<c>'timeout'</c>.
/// </summary>
/// <remarks>
/// Wraps a BCL <see cref="HttpClient"/> (no auto-redirect/decompression, Node semantics) under
/// the hood but is a first-class object model: headers can be manipulated before send, and the
/// body is streamed. The HTTP send runs on a thread-pool task and delivers its result back onto
/// the interpreter's event-loop thread via <c>ScheduleTimer(0)</c>, keeping the loop alive with
/// Ref/Unref while the request is in flight (mirrors <see cref="SharpTSSocket"/>).
/// </remarks>
public class SharpTSClientRequest : SharpTSWritable
{
    /// <inheritdoc />
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly Interpreter _interpreter;
    private readonly string _method;
    private readonly string _url;
    private readonly bool _isHttps;
    private bool _rejectUnauthorized = true;

    // Headers keyed by lowercase name → (originalName, value). value may be a string,
    // number (double), or SharpTSArray of strings (multi-value header).
    private readonly Dictionary<string, (string Name, object? Value)> _headers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<byte[]> _bodyChunks = new();
    private bool _requestEnded;
    private bool _sent;
    private bool _aborted;
    private bool _responseReceived;
    private int _timeoutMs;
    private readonly CancellationTokenSource _cts = new();
    private SharpTSObject? _socketObject;
    private SharpTSAgent? _agent;        // null when options.agent === false (no pooling)
    private bool _agentTracked;

    public SharpTSClientRequest(Interpreter interpreter, string method, string url, SharpTSObject? options, bool isHttps)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _method = string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant();
        _url = url;
        _isHttps = isHttps;
        _agent = SharpTSAgent.GlobalAgent; // default pool (Node semantics); overridden below

        if (options != null)
        {
            // Initial headers from options.headers.
            if (options.Fields.TryGetValue("headers", out var h) && h is SharpTSObject headerObj)
            {
                foreach (var kv in headerObj.Fields)
                    _headers[kv.Key] = (kv.Key, kv.Value);
            }
            if (options.Fields.TryGetValue("timeout", out var to) && to is double tov)
                _timeoutMs = (int)tov;
            if (options.Fields.TryGetValue("rejectUnauthorized", out var ru) && ru is bool rub)
                _rejectUnauthorized = rub;
            // Resolve the connection agent (#1051): an explicit SharpTSAgent, false for no
            // pooling, or the global agent by default.
            if (options.Fields.TryGetValue("agent", out var ag))
            {
                if (ag is SharpTSAgent customAgent) _agent = customAgent;
                else if (ag is false) _agent = null;
            }
            // options.auth ("user:pass") → Basic Authorization header (Node semantics).
            if (options.Fields.TryGetValue("auth", out var au) && au is string authStr && authStr.Length > 0
                && !_headers.ContainsKey("authorization"))
            {
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(authStr));
                _headers["authorization"] = ("Authorization", "Basic " + encoded);
            }
        }
    }

    /// <summary>Ends the request with no further body (used by http.get, which auto-ends).</summary>
    internal void EndNow()
    {
        if (_requestEnded) return;
        _requestEnded = true;
        SendAsync();
    }

    /// <summary>Registers the response callback (the cb passed to http.request) as a 'response' listener.</summary>
    public void RegisterResponseCallback(ISharpTSCallable callback)
    {
        var on = GetEventEmitterMember("on") as BuiltInMethod;
        on?.Bind(this).Call(_interpreter, ["response", callback]);
    }

    public override object? GetMember(string name)
    {
        return name switch
        {
            // Body production (override Writable's defaults to buffer + trigger the send).
            "write" => BuiltInMethod.CreateV2("write", 1, 3, (interp, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).WriteBody(interp, args)).Bind(this),
            "end" => BuiltInMethod.CreateV2("end", 0, 3, (interp, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).EndRequest(interp, args)).Bind(this),

            // Header manipulation (before send).
            "setHeader" => BuiltInMethod.CreateV2("setHeader", 2, (_, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).SetHeader(args)).Bind(this),
            "getHeader" => BuiltInMethod.CreateV2("getHeader", 1, (_, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).GetHeader(args)).Bind(this),
            "removeHeader" => BuiltInMethod.CreateV2("removeHeader", 1, (_, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).RemoveHeader(args)).Bind(this),
            "hasHeader" => BuiltInMethod.CreateV2("hasHeader", 1, (_, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).HasHeader(args)).Bind(this),
            "getHeaders" => BuiltInMethod.CreateV2("getHeaders", 0, (_, receiver, _) =>
                ((SharpTSClientRequest)receiver.ToObject()!).GetHeaders()).Bind(this),
            "getHeaderNames" => BuiltInMethod.CreateV2("getHeaderNames", 0, (_, receiver, _) =>
                ((SharpTSClientRequest)receiver.ToObject()!).GetHeaderNames()).Bind(this),
            "getRawHeaderNames" => BuiltInMethod.CreateV2("getRawHeaderNames", 0, (_, receiver, _) =>
                ((SharpTSClientRequest)receiver.ToObject()!).GetRawHeaderNames()).Bind(this),
            "flushHeaders" => BuiltInMethod.CreateV2("flushHeaders", 0, (_, receiver, _) => receiver).Bind(this),

            // Connection control.
            "abort" => BuiltInMethod.CreateV2("abort", 0, (interp, receiver, _) =>
                ((SharpTSClientRequest)receiver.ToObject()!).Abort(interp)).Bind(this),
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, (interp, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).DestroyRequest(interp, args)).Bind(this),
            "setTimeout" => BuiltInMethod.CreateV2("setTimeout", 1, 2, (interp, receiver, args) =>
                ((SharpTSClientRequest)receiver.ToObject()!).SetTimeout(interp, args)).Bind(this),
            "setNoDelay" => BuiltInMethod.CreateV2("setNoDelay", 0, 1, (_, receiver, _) => receiver).Bind(this),
            "setSocketKeepAlive" => BuiltInMethod.CreateV2("setSocketKeepAlive", 0, 2, (_, receiver, _) => receiver).Bind(this),

            // Properties.
            "method" => _method,
            "path" => GetPath(),
            "host" => GetHostName(),
            "protocol" => _isHttps ? "https:" : "http:",
            "aborted" => _aborted,
            "finished" => _requestEnded,
            "reusedSocket" => false,
            "writableEnded" => _requestEnded,
            "socket" or "connection" => (object?)_socketObject,

            // Inherit Writable/EventEmitter (on, once, off, emit, cork, uncork, etc.)
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue WriteBody(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_requestEnded)
        {
            EmitEvent(interpreter, "error", [new SharpTSError("write after end")]);
            return RuntimeValue.False;
        }
        AppendChunk(args, out ISharpTSCallable? cb);
        cb?.Call(interpreter, []);
        return RuntimeValue.True;
    }

    private RuntimeValue EndRequest(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_requestEnded) return RuntimeValue.FromObject(this);

        // end([chunk][, encoding][, callback])
        ISharpTSCallable? endCb = null;
        if (args.Length > 0 && args[0].ToObject() is not ISharpTSCallable && !args[0].IsUndefined && !args[0].IsNull)
        {
            AppendChunk(args, out endCb);
        }
        else if (args.Length > 0 && args[0].ToObject() is ISharpTSCallable cb0)
        {
            endCb = cb0;
        }

        _requestEnded = true;
        if (endCb != null)
            RegisterFinishCallback(endCb);

        SendAsync();
        return RuntimeValue.FromObject(this);
    }

    private void RegisterFinishCallback(ISharpTSCallable cb)
    {
        var once = GetEventEmitterMember("once") as BuiltInMethod;
        once?.Bind(this).Call(_interpreter, ["finish", cb]);
    }

    private void AppendChunk(ReadOnlySpan<RuntimeValue> args, out ISharpTSCallable? callback)
    {
        callback = null;
        if (args.Length == 0) return;

        var first = args[0].ToObject();
        var encoding = args.Length > 1 && args[1].IsString ? args[1].AsStringUnsafe() : "utf8";

        // trailing callback
        for (int i = 1; i < args.Length; i++)
            if (args[i].ToObject() is ISharpTSCallable cb) { callback = cb; break; }

        byte[] bytes = first switch
        {
            string s => GetEncoding(encoding).GetBytes(s),
            SharpTSBuffer buf => buf.Data,
            null => Array.Empty<byte>(),
            _ => Encoding.UTF8.GetBytes(first.ToString() ?? "")
        };
        if (bytes.Length > 0)
            _bodyChunks.Add(bytes);
    }

    private RuntimeValue SetHeader(ReadOnlySpan<RuntimeValue> args)
    {
        if (_sent) throw new InterpreterException("Cannot set headers after they are sent to the client");
        if (args.Length < 2) return RuntimeValue.FromObject(this);
        var name = args[0].ToObject()?.ToString();
        if (name == null) return RuntimeValue.FromObject(this);
        _headers[name] = (name, args[1].ToObject());
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue GetHeader(ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.Undefined;
        var name = args[0].ToObject()?.ToString();
        if (name != null && _headers.TryGetValue(name, out var entry))
            return RuntimeValue.FromBoxed(entry.Value);
        return RuntimeValue.Undefined;
    }

    private RuntimeValue RemoveHeader(ReadOnlySpan<RuntimeValue> args)
    {
        if (_sent) throw new InterpreterException("Cannot remove headers after they are sent to the client");
        if (args.Length > 0 && args[0].ToObject()?.ToString() is { } name)
            _headers.Remove(name);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue HasHeader(ReadOnlySpan<RuntimeValue> args)
    {
        var name = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
        return RuntimeValue.FromBoolean(name != null && _headers.ContainsKey(name));
    }

    private RuntimeValue GetHeaders()
    {
        var obj = new Dictionary<string, object?>();
        foreach (var (key, entry) in _headers)
            obj[key.ToLowerInvariant()] = entry.Value;
        return RuntimeValue.FromObject(new SharpTSObject(obj));
    }

    private RuntimeValue GetHeaderNames()
    {
        var names = _headers.Keys.Select(k => (object?)k.ToLowerInvariant()).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(names));
    }

    private RuntimeValue GetRawHeaderNames()
    {
        var names = _headers.Values.Select(e => (object?)e.Name).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(names));
    }

    private RuntimeValue SetTimeout(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].IsNumber)
            _timeoutMs = (int)args[0].AsNumberUnsafe();
        if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb)
        {
            var on = GetEventEmitterMember("on") as BuiltInMethod;
            on?.Bind(this).Call(interpreter, ["timeout", cb]);
        }
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Abort(Interpreter interpreter)
    {
        if (_aborted) return RuntimeValue.FromObject(this);
        _aborted = true;
        try { _cts.Cancel(); } catch { }
        EmitEvent(interpreter, "abort", []);
        EmitEvent(interpreter, "close", []);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue DestroyRequest(Interpreter interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_aborted) return RuntimeValue.FromObject(this);
        _aborted = true;
        try { _cts.Cancel(); } catch { }
        if (args.Length > 0 && args[0].ToObject() is { } err && err is not SharpTSUndefined)
            EmitEvent(interpreter, "error", [err]);
        EmitEvent(interpreter, "close", []);
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Performs the actual HTTP exchange on a thread-pool task and delivers the response
    /// (or error) back on the event-loop thread.
    /// </summary>
    private void SendAsync()
    {
        if (_sent) return;
        _sent = true;

        // Register with the connection agent's pool (#1051), unless agent:false.
        if (_agent != null)
        {
            _agent.TrackSocketStart(PoolOrigin());
            _agentTracked = true;
        }

        var bodyBytes = ConcatBody();
        _interpreter.Ref();

        _ = Task.Run(async () =>
        {
            try
            {
                var client = HttpClientRequestHelpers.GetClient(_rejectUnauthorized);
                using var request = BuildRequestMessage(bodyBytes);

                // Optional non-aborting timeout: emit 'timeout' if the request runs long.
                Task? timeoutTask = _timeoutMs > 0 ? Task.Delay(_timeoutMs, _cts.Token) : null;
                var sendTask = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);

                if (timeoutTask != null)
                {
                    var completed = await Task.WhenAny(sendTask, timeoutTask);
                    if (completed == timeoutTask && !sendTask.IsCompleted)
                    {
                        _interpreter.ScheduleTimer(0, 0,
                            () => { if (!_responseReceived) EmitEvent(_interpreter, "timeout", []); },
                            isInterval: false);
                    }
                }

                using var response = await sendTask;

                // Persist Set-Cookie into the shared jar (parity with the fetch client path).
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                {
                    foreach (var c in setCookies)
                        SharpTSCookieJar.Instance.SetCookie(c, _url);
                }

                var (status, reason, version, headersLower, raw) = ExtractResponse(response);
                var body = await response.Content.ReadAsByteArrayAsync(_cts.Token);

                EndAgentTracking(keepAlive: true);
                _interpreter.ScheduleTimer(0, 0, () =>
                {
                    DeliverResponse(status, reason, version, headersLower, raw, body);
                    _interpreter.Unref();
                }, isInterval: false);
            }
            catch (OperationCanceledException)
            {
                // Aborted/destroyed — 'abort'/'close' were already emitted synchronously.
                EndAgentTracking(keepAlive: false);
                _interpreter.ScheduleTimer(0, 0, () => _interpreter.Unref(), isInterval: false);
            }
            catch (Exception ex)
            {
                EndAgentTracking(keepAlive: false);
                _interpreter.ScheduleTimer(0, 0, () =>
                {
                    if (!_aborted)
                        EmitEvent(_interpreter, "error", [MakeRequestError(ex)]);
                    _interpreter.Unref();
                }, isInterval: false);
            }
        });
    }

    private void DeliverResponse(int status, string reason, string version,
        Dictionary<string, object?> headersLower, List<object?> raw, byte[] body)
    {
        if (_aborted) return;
        _responseReceived = true;

        _socketObject = BuildSocketObject();
        EmitEvent(_interpreter, "socket", [_socketObject]);

        var response = new SharpTSClientResponse(status, reason, version, headersLower, raw, _socketObject);

        // 1xx informational responses surface as 'information'/'continue', not 'response'.
        EmitEvent(_interpreter, "response", [response]);

        // Push the body into the response Readable; user listeners were attached
        // synchronously inside the 'response' handler.
        var push = ((SharpTSReadable)response).GetMember("push") as BuiltInMethod;
        var bound = push?.Bind(response);
        if (body.Length > 0)
            bound?.Call(_interpreter, [new SharpTSBuffer(body)]);
        // Mark complete BEFORE the EOF push so res.complete is true inside the 'end' handler.
        response.MarkComplete();
        bound?.Call(_interpreter, new List<object?> { null });
    }

    private HttpRequestMessage BuildRequestMessage(byte[] bodyBytes)
    {
        var request = new HttpRequestMessage(new HttpMethod(_method), _url);
        bool hasBody = bodyBytes.Length > 0;
        HttpContent? content = hasBody ? new ByteArrayContent(bodyBytes) : null;
        if (content != null)
            content.Headers.Clear();

        foreach (var (_, entry) in _headers)
        {
            var name = entry.Name;
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue; // managed automatically by HttpContent

            foreach (var value in EnumerateHeaderValues(entry.Value))
            {
                bool added = request.Headers.TryAddWithoutValidation(name, value);
                if (!added)
                {
                    content ??= new ByteArrayContent(Array.Empty<byte>());
                    content.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }

        // Attach cookies from the shared jar unless the caller set Cookie explicitly
        // (parity with the fetch client path).
        if (!_headers.ContainsKey("cookie"))
        {
            var cookieHeader = SharpTSCookieJar.Instance.GetCookieHeader(_url);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        request.Content = content;
        return request;
    }

    private static IEnumerable<string> EnumerateHeaderValues(object? value)
    {
        if (value is SharpTSArray arr)
        {
            foreach (var v in arr)
                yield return v?.ToString() ?? "";
        }
        else
        {
            yield return value?.ToString() ?? "";
        }
    }

    private static (int status, string reason, string version,
        Dictionary<string, object?> headersLower, List<object?> raw) ExtractResponse(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var reason = response.ReasonPhrase ?? "";
        var version = $"{response.Version.Major}.{response.Version.Minor}";
        var headersLower = new Dictionary<string, object?>();
        var raw = new List<object?>();

        void Collect(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            foreach (var header in headers)
            {
                var lower = header.Key.ToLowerInvariant();
                var values = header.Value.ToList();
                foreach (var v in values)
                {
                    raw.Add(header.Key);
                    raw.Add(v);
                }
                if (lower == "set-cookie")
                    headersLower[lower] = new SharpTSArray(values.Select(v => (object?)v).ToList());
                else
                    headersLower[lower] = string.Join(", ", values);
            }
        }

        Collect(response.Headers);
        if (response.Content?.Headers != null)
            Collect(response.Content.Headers);

        return (status, reason, version, headersLower, raw);
    }

    private SharpTSObject MakeRequestError(Exception ex)
    {
        string code = ex switch
        {
            HttpRequestException hre when hre.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) => "ECONNREFUSED",
            HttpRequestException => "ECONNRESET",
            TaskCanceledException => "ECONNRESET",
            _ => "ECONNRESET"
        };
        var err = new SharpTSError(ex.Message) { Code = code };
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["message"] = err.Message,
            ["code"] = code,
            ["name"] = "Error"
        });
    }

    private SharpTSObject BuildSocketObject()
    {
        string host = GetHostName();
        int port = GetPort();
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["remoteAddress"] = host,
            ["remotePort"] = (double)port,
            ["localAddress"] = "127.0.0.1",
            ["localPort"] = (double)0,
            ["encrypted"] = _isHttps,
            ["writable"] = true,
            ["readable"] = true
        });
    }

    private void EndAgentTracking(bool keepAlive)
    {
        if (_agentTracked && _agent != null)
        {
            _agent.TrackSocketEnd(PoolOrigin(), keepAlive);
            _agentTracked = false;
        }
    }

    private string PoolOrigin() => $"{GetHostName()}:{GetPort()}";

    private byte[] ConcatBody()
    {
        if (_bodyChunks.Count == 0) return Array.Empty<byte>();
        if (_bodyChunks.Count == 1) return _bodyChunks[0];
        var total = _bodyChunks.Sum(c => c.Length);
        var result = new byte[total];
        int offset = 0;
        foreach (var chunk in _bodyChunks)
        {
            Array.Copy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    private string GetPath()
    {
        try { var u = new Uri(_url); return u.PathAndQuery; } catch { return "/"; }
    }

    private string GetHostName()
    {
        try { return new Uri(_url).Host; } catch { return "localhost"; }
    }

    private int GetPort()
    {
        try { var u = new Uri(_url); return u.Port; } catch { return _isHttps ? 443 : 80; }
    }

    private static Encoding GetEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf8" or "utf-8" => Encoding.UTF8,
        "ascii" => Encoding.ASCII,
        "latin1" or "binary" => Encoding.Latin1,
        "utf16le" or "ucs2" or "ucs-2" => Encoding.Unicode,
        "base64" => Encoding.UTF8,
        _ => Encoding.UTF8
    };

    public override string ToString() => $"ClientRequest {{ method: '{_method}' }}";
}
