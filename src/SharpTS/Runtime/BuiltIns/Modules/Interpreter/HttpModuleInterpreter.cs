using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'http' module.
/// </summary>
/// <remarks>
/// Provides HTTP functionality including:
/// - createServer() - create an HTTP server
/// - request() - make an HTTP request (alias for fetch with different signature)
/// - get() - shorthand for GET requests
/// - METHODS - array of supported HTTP methods
/// - STATUS_CODES - map of status codes to messages
/// </remarks>
public static class HttpModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the http module.
    /// </summary>
    public static Dictionary<string, object?> GetExports() => GetExports(isHttps: false);

    /// <summary>
    /// Gets all exported values for the http (or https) module. The <paramref name="isHttps"/>
    /// flag selects TLS for the client request path (used by the https module wiring).
    /// </summary>
    public static Dictionary<string, object?> GetExports(bool isHttps)
    {
        return new Dictionary<string, object?>
        {
            ["createServer"] = BuiltInMethod.CreateV2("createServer", 0, 2, CreateServer),
            ["request"] = BuiltInMethod.CreateV2("request", 1, 3, (interp, _, args) =>
                RuntimeValue.FromObject(CreateClientRequest(interp, args, forceMethod: null, forceHttps: isHttps))),
            ["get"] = BuiltInMethod.CreateV2("get", 1, 3, (interp, _, args) =>
            {
                var req = CreateClientRequest(interp, args, forceMethod: "GET", forceHttps: isHttps);
                req.EndNow();
                return RuntimeValue.FromObject(req);
            }),
            ["METHODS"] = GetMethods(),
            ["STATUS_CODES"] = GetStatusCodes(),
            ["globalAgent"] = SharpTSAgent.GlobalAgent,
            ["Agent"] = BuiltInMethod.CreateV2("Agent", 0, 1, ConstructAgent),
            // Utilities + constants (#1052).
            ["validateHeaderName"] = BuiltInMethod.CreateV2("validateHeaderName", 1, 2, ValidateHeaderName),
            ["validateHeaderValue"] = BuiltInMethod.CreateV2("validateHeaderValue", 2, ValidateHeaderValue),
            ["maxHeaderSize"] = (double)HttpHeaderValidation.MaxHeaderSize,
            ["setMaxIdleHTTPParsers"] = BuiltInMethod.CreateV2("setMaxIdleHTTPParsers", 1,
                (_, _, _) => RuntimeValue.Undefined)
        };
    }

    private static RuntimeValue ValidateHeaderName(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var name = args.Length > 0 ? args[0].ToObject() as string : null;
        if (name == null || !HttpHeaderValidation.IsValidHttpToken(name))
        {
            var shown = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "undefined" : "undefined";
            throw new Runtime.Exceptions.ThrowException(
                new SharpTSTypeError($"Header name must be a valid HTTP token [\"{shown}\"]") { Code = "ERR_INVALID_HTTP_TOKEN" });
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue ValidateHeaderValue(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var name = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
        if (args.Length < 2 || args[1].IsUndefined)
        {
            throw new Runtime.Exceptions.ThrowException(
                new SharpTSTypeError($"Invalid value \"undefined\" for header \"{name}\"") { Code = "ERR_HTTP_INVALID_HEADER_VALUE" });
        }
        var value = args[1].ToObject()?.ToString() ?? "";
        if (HttpHeaderValidation.HasInvalidHeaderChar(value))
        {
            throw new Runtime.Exceptions.ThrowException(
                new SharpTSTypeError($"Invalid character in header content [\"{name}\"]") { Code = "ERR_INVALID_CHAR" });
        }
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Gets the exports for the 'https' module: the http client surface over TLS (#1050) plus a
    /// real TLS-terminating createServer (#1049, interpreter).
    /// </summary>
    public static Dictionary<string, object?> GetHttpsExports()
    {
        var exports = GetExports(isHttps: true);
        exports["createServer"] = BuiltInMethod.CreateV2("createServer", 0, 2, CreateHttpsServer);
        // https.Agent / globalAgent are TLS-aware aliases (same Agent surface; #1050).
        return exports;
    }

    /// <summary>
    /// Creates a real HTTPS server (#1049): builds <see cref="SharpTSHttpsServer"/> over the
    /// tls #1032 server, terminating TLS and running the HTTP pipeline over each connection.
    /// </summary>
    private static RuntimeValue CreateHttpsServer(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        SharpTSObject? options = null;
        ISharpTSCallable? handler = null;
        if (args.Length > 0)
        {
            if (args[0].ToObject() is SharpTSObject o) options = o;
            else if (args[0].ToObject() is ISharpTSCallable c) handler = c;
        }
        if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable c2) handler = c2;
        return RuntimeValue.FromObject(new SharpTSHttpsServer(options, handler));
    }

    /// <summary>
    /// Creates an HTTP server.
    /// </summary>
    /// <param name="interpreter">The interpreter instance.</param>
    /// <param name="receiver">Not used.</param>
    /// <param name="args">Optional request handler callback.</param>
    /// <returns>A SharpTSHttpServer instance.</returns>
    private static RuntimeValue CreateServer(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // The first argument is the request handler: (req, res) => void
        ISharpTSCallable? requestHandler = null;

        if (args.Length > 0)
        {
            // Could be options object or callback
            if (args[0].ToObject() is ISharpTSCallable callback)
            {
                requestHandler = callback;
            }
            else if (args[0].ToObject() is SharpTSObject && args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb)
            {
                // First arg is options, second is callback
                requestHandler = cb;
            }
        }

        // If no handler provided, create a no-op handler
        requestHandler ??= new NoOpHandler();

        return RuntimeValue.FromObject(new SharpTSHttpServer(requestHandler));
    }

    /// <summary>
    /// Builds a <see cref="SharpTSClientRequest"/> from <c>http.request</c>/<c>http.get</c> arguments.
    /// Accepts <c>(url[, options][, cb])</c> or <c>(options[, cb])</c>. The response callback (the
    /// last function argument) is registered as a 'response' listener.
    /// </summary>
    internal static SharpTSClientRequest CreateClientRequest(
        Interp interpreter, ReadOnlySpan<RuntimeValue> args, string? forceMethod, bool forceHttps)
    {
        string? urlStr = null;
        SharpTSObject? options = null;
        ISharpTSCallable? callback = null;

        if (args.Length > 0)
        {
            var a0 = args[0].ToObject();
            if (a0 is string s) urlStr = s;
            else if (a0 is SharpTSObject o) options = o;
        }
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i].ToObject();
            if (a is ISharpTSCallable cb) callback = cb;
            else if (a is SharpTSObject o && options == null) options = o;
        }

        bool isHttps = forceHttps;
        if (urlStr != null && urlStr.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            isHttps = true;
        if (options != null && options.Fields.TryGetValue("protocol", out var p) && p is string ps
            && ps.TrimEnd(':').Equals("https", StringComparison.OrdinalIgnoreCase))
            isHttps = true;
        if (forceHttps) isHttps = true;

        string url = urlStr ?? BuildUrlFromOptions(options, isHttps);

        string method = forceMethod
            ?? (options != null && options.Fields.TryGetValue("method", out var m) && m is string ms ? ms : "GET");

        var req = new SharpTSClientRequest(interpreter, method, url, options, isHttps);
        if (callback != null)
            req.RegisterResponseCallback(callback);
        return req;
    }

    /// <summary>
    /// Builds a URL from a request options object (protocol/host/hostname/port/path).
    /// </summary>
    private static string BuildUrlFromOptions(SharpTSObject? opts, bool isHttps)
    {
        string defaultScheme = isHttps ? "https" : "http";
        if (opts == null) return $"{defaultScheme}://localhost/";

        string protocol = opts.Fields.TryGetValue("protocol", out var p) && p is string ps
            ? ps.TrimEnd(':') : defaultScheme;
        string host = opts.Fields.TryGetValue("hostname", out var h) && h is string hs ? hs
            : opts.Fields.TryGetValue("host", out var ho) && ho is string hos ? hos : "localhost";

        string portStr = "";
        if (opts.Fields.TryGetValue("port", out var po))
        {
            if (po is double pd) portStr = ":" + (int)pd;
            else if (po is string pss && int.TryParse(pss, out var pii)) portStr = ":" + pii;
        }

        string path = opts.Fields.TryGetValue("path", out var pa) && pa is string pas ? pas : "/";
        if (!path.StartsWith("/")) path = "/" + path;
        return $"{protocol}://{host}{portStr}{path}";
    }

    /// <summary>
    /// Gets the array of supported HTTP methods.
    /// </summary>
    private static SharpTSArray GetMethods()
    {
        return new SharpTSArray(new List<object?>
        {
            "ACL", "BIND", "CHECKOUT", "CONNECT", "COPY", "DELETE", "GET", "HEAD",
            "LINK", "LOCK", "M-SEARCH", "MERGE", "MKACTIVITY", "MKCALENDAR", "MKCOL",
            "MOVE", "NOTIFY", "OPTIONS", "PATCH", "POST", "PROPFIND", "PROPPATCH",
            "PURGE", "PUT", "REBIND", "REPORT", "SEARCH", "SOURCE", "SUBSCRIBE",
            "TRACE", "UNBIND", "UNLINK", "UNLOCK", "UNSUBSCRIBE"
        });
    }

    /// <summary>
    /// Gets the map of status codes to their messages.
    /// </summary>
    private static SharpTSObject GetStatusCodes()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["100"] = "Continue",
            ["101"] = "Switching Protocols",
            ["102"] = "Processing",
            ["103"] = "Early Hints",
            ["200"] = "OK",
            ["201"] = "Created",
            ["202"] = "Accepted",
            ["203"] = "Non-Authoritative Information",
            ["204"] = "No Content",
            ["205"] = "Reset Content",
            ["206"] = "Partial Content",
            ["207"] = "Multi-Status",
            ["208"] = "Already Reported",
            ["226"] = "IM Used",
            ["300"] = "Multiple Choices",
            ["301"] = "Moved Permanently",
            ["302"] = "Found",
            ["303"] = "See Other",
            ["304"] = "Not Modified",
            ["305"] = "Use Proxy",
            ["307"] = "Temporary Redirect",
            ["308"] = "Permanent Redirect",
            ["400"] = "Bad Request",
            ["401"] = "Unauthorized",
            ["402"] = "Payment Required",
            ["403"] = "Forbidden",
            ["404"] = "Not Found",
            ["405"] = "Method Not Allowed",
            ["406"] = "Not Acceptable",
            ["407"] = "Proxy Authentication Required",
            ["408"] = "Request Timeout",
            ["409"] = "Conflict",
            ["410"] = "Gone",
            ["411"] = "Length Required",
            ["412"] = "Precondition Failed",
            ["413"] = "Payload Too Large",
            ["414"] = "URI Too Long",
            ["415"] = "Unsupported Media Type",
            ["416"] = "Range Not Satisfiable",
            ["417"] = "Expectation Failed",
            ["418"] = "I'm a Teapot",
            ["421"] = "Misdirected Request",
            ["422"] = "Unprocessable Entity",
            ["423"] = "Locked",
            ["424"] = "Failed Dependency",
            ["425"] = "Too Early",
            ["426"] = "Upgrade Required",
            ["428"] = "Precondition Required",
            ["429"] = "Too Many Requests",
            ["431"] = "Request Header Fields Too Large",
            ["451"] = "Unavailable For Legal Reasons",
            ["500"] = "Internal Server Error",
            ["501"] = "Not Implemented",
            ["502"] = "Bad Gateway",
            ["503"] = "Service Unavailable",
            ["504"] = "Gateway Timeout",
            ["505"] = "HTTP Version Not Supported",
            ["506"] = "Variant Also Negotiates",
            ["507"] = "Insufficient Storage",
            ["508"] = "Loop Detected",
            ["509"] = "Bandwidth Limit Exceeded",
            ["510"] = "Not Extended",
            ["511"] = "Network Authentication Required"
        });
    }

    /// <summary>
    /// Constructs a new http.Agent from options.
    /// </summary>
    private static RuntimeValue ConstructAgent(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var options = args.Length > 0 ? args[0].ToObject() as SharpTSObject : null;
        return RuntimeValue.FromObject(SharpTSAgent.FromOptions(options));
    }

    /// <summary>
    /// A no-op request handler for servers created without a callback.
    /// </summary>
    private class NoOpHandler : ISharpTSCallable
    {
        public int Arity() => 2;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            // Do nothing - user must add 'request' event listener
            return null;
        }
    }
}
