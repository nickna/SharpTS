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
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["createServer"] = new BuiltInMethod("createServer", 0, 1, CreateServer),
            ["request"] = new BuiltInAsyncMethod("request", 1, 2, Request),
            ["get"] = new BuiltInAsyncMethod("get", 1, 2, Get),
            ["METHODS"] = GetMethods(),
            ["STATUS_CODES"] = GetStatusCodes(),
            ["globalAgent"] = CreateGlobalAgent()
        };
    }

    /// <summary>
    /// Creates an HTTP server.
    /// </summary>
    /// <param name="interpreter">The interpreter instance.</param>
    /// <param name="receiver">Not used.</param>
    /// <param name="args">Optional request handler callback.</param>
    /// <returns>A SharpTSHttpServer instance.</returns>
    private static object? CreateServer(Interp interpreter, object? receiver, List<object?> args)
    {
        // The first argument is the request handler: (req, res) => void
        ISharpTSCallable? requestHandler = null;

        if (args.Count > 0)
        {
            // Could be options object or callback
            if (args[0] is ISharpTSCallable callback)
            {
                requestHandler = callback;
            }
            else if (args[0] is SharpTSObject && args.Count > 1 && args[1] is ISharpTSCallable cb)
            {
                // First arg is options, second is callback
                requestHandler = cb;
            }
        }

        // If no handler provided, create a no-op handler
        requestHandler ??= new NoOpHandler();

        return new SharpTSHttpServer(requestHandler);
    }

    /// <summary>
    /// Makes an HTTP request (similar to Node.js http.request).
    /// </summary>
    private static async Task<object?> Request(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError("http.request requires a URL or options object"));

        string url;
        SharpTSObject? options = null;

        // Parse arguments
        if (args[0] is string urlStr)
        {
            url = urlStr;
            if (args.Count > 1 && args[1] is SharpTSObject opts)
            {
                options = opts;
            }
        }
        else if (args[0] is SharpTSObject opts)
        {
            options = opts;
            // Build URL from options
            var protocol = opts.Fields.TryGetValue("protocol", out var p) && p is string ps ? ps : "http:";
            var hostname = opts.Fields.TryGetValue("hostname", out var h) && h is string hs ? hs
                : opts.Fields.TryGetValue("host", out var ho) && ho is string hos ? hos : "localhost";
            var port = opts.Fields.TryGetValue("port", out var po) && po is double pd ? $":{(int)pd}" : "";
            var path = opts.Fields.TryGetValue("path", out var pa) && pa is string pas ? pas : "/";

            protocol = protocol.TrimEnd(':') + ":";
            url = $"{protocol}//{hostname}{port}{path}";
        }
        else
        {
            throw new SharpTSPromiseRejectedException(
                new SharpTSTypeError("http.request: first argument must be URL string or options object"));
        }

        // Delegate to fetch - use Call which returns a Promise
        var promise = FetchBuiltIns.FetchMethod.Call(interpreter, new List<object?> { url, options });
        if (promise is SharpTSPromise fetchPromise)
        {
            return await fetchPromise.GetValueAsync();
        }
        return promise;
    }

    /// <summary>
    /// Shorthand for GET requests.
    /// </summary>
    private static async Task<object?> Get(Interp interpreter, object? receiver, List<object?> args)
    {
        // Ensure method is GET
        if (args.Count > 1 && args[1] is SharpTSObject options)
        {
            options.SetProperty("method", "GET");
        }
        else if (args.Count == 1)
        {
            var opts = new SharpTSObject(new Dictionary<string, object?> { ["method"] = "GET" });
            args = new List<object?> { args[0], opts };
        }

        return await Request(interpreter, receiver, args);
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
    /// Creates a minimal global agent object for compatibility.
    /// </summary>
    private static SharpTSObject CreateGlobalAgent()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["maxSockets"] = double.PositiveInfinity,
            ["maxFreeSockets"] = 256.0,
            ["keepAlive"] = true,
            ["keepAliveMsecs"] = 1000.0
        });
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
