using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// A lightweight mock HTTP server for testing fetch functionality.
/// Uses HttpListener to serve predefined routes.
/// </summary>
public class MockHttpServer : IDisposable
{
    private HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly Dictionary<string, Func<HttpListenerRequest, (int StatusCode, string ContentType, byte[] Body)>> _routes;
    private Task? _listenerTask;
    private bool _disposed;
    private static readonly object _portLock = new();
    private static readonly HashSet<int> _portsInUse = new();

    public int Port { get; private set; }
    public string BaseUrl => $"http://localhost:{Port}/";

    public MockHttpServer()
    {
        _listener = new HttpListener();
        _cts = new CancellationTokenSource();
        _routes = new Dictionary<string, Func<HttpListenerRequest, (int, string, byte[])>>(StringComparer.OrdinalIgnoreCase);

        // Find and reserve an available port with retry logic
        Port = FindAndReservePort();
        _listener.Prefixes.Add(BaseUrl);
    }

    private static int FindAndReservePort()
    {
        lock (_portLock)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var port = FindAvailablePort();
                if (!_portsInUse.Contains(port))
                {
                    _portsInUse.Add(port);
                    return port;
                }
            }
            throw new InvalidOperationException("Could not find an available port after 10 attempts");
        }
    }

    private static void ReleasePort(int port)
    {
        lock (_portLock)
        {
            _portsInUse.Remove(port);
        }
    }

    private static int FindAvailablePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    /// <summary>
    /// Adds a route that returns JSON data.
    /// </summary>
    public void AddJsonRoute(string path, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var body = Encoding.UTF8.GetBytes(json);
        _routes[path] = _ => (200, "application/json", body);
    }

    /// <summary>
    /// Adds a route that returns plain text.
    /// </summary>
    public void AddTextRoute(string path, string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        _routes[path] = _ => (200, "text/plain", body);
    }

    /// <summary>
    /// Adds a route that echoes the request information.
    /// </summary>
    public void AddEchoRoute(string path)
    {
        _routes[path] = request =>
        {
            var echo = new Dictionary<string, object?>
            {
                ["method"] = request.HttpMethod,
                ["url"] = request.Url?.ToString(),
                ["headers"] = request.Headers.AllKeys.ToDictionary(k => k ?? "", k => request.Headers[k]),
                ["body"] = null as object
            };

            // Read request body if present
            if (request.HasEntityBody)
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var bodyText = reader.ReadToEnd();
                echo["body"] = bodyText;

                // Try to parse as JSON
                if (request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                {
                    try
                    {
                        echo["json"] = JsonSerializer.Deserialize<object>(bodyText);
                    }
                    catch
                    {
                        // Keep as string
                    }
                }
            }

            var json = JsonSerializer.Serialize(echo);
            var body = Encoding.UTF8.GetBytes(json);
            return (200, "application/json", body);
        };
    }

    /// <summary>
    /// Adds a route that returns binary data.
    /// </summary>
    public void AddBinaryRoute(string path, byte[] data)
    {
        _routes[path] = _ => (200, "application/octet-stream", data);
    }

    /// <summary>
    /// Adds a route that delays before responding (for abort/timeout tests).
    /// </summary>
    public void AddDelayRoute(string path, int delayMs, string body = "delayed")
    {
        _routes[path] = _ =>
        {
            Thread.Sleep(delayMs);
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            return (200, "text/plain", bodyBytes);
        };
    }

    /// <summary>
    /// Adds a route that redirects to another path.
    /// </summary>
    public void AddRedirectRoute(string path, string targetPath, int statusCode = 302)
    {
        _routes[path] = _ => (statusCode, "text/plain", Array.Empty<byte>());
        // We need to set the Location header in the response handler, so use a custom handler
        _routes.Remove(path);
        _redirectRoutes[path] = (targetPath, statusCode);
    }

    private readonly Dictionary<string, (string TargetPath, int StatusCode)> _redirectRoutes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Custom routes that get full access to the response context (for setting arbitrary
    /// headers like Set-Cookie or for echoing request headers into the response body).
    /// </summary>
    private readonly Dictionary<string, Action<HttpListenerContext>> _customRoutes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a route with a specific status code.
    /// </summary>
    public void AddStatusRoute(string path, int statusCode, string body = "")
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        _routes[path] = _ => (statusCode, "text/plain", bodyBytes);
    }

    /// <summary>
    /// Adds a route that emits one or more <c>Set-Cookie</c> response headers and a
    /// 200 OK with empty body. Each entry in <paramref name="cookies"/> becomes its own
    /// <c>Set-Cookie</c> header line — this is the only way to send multi-value
    /// Set-Cookie since HttpListener treats them specially.
    /// </summary>
    public void AddSetCookieRoute(string path, params string[] cookies)
    {
        _customRoutes[path] = ctx =>
        {
            foreach (var cookie in cookies)
            {
                ctx.Response.Headers.Add("Set-Cookie", cookie);
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = 0;
        };
    }

    /// <summary>
    /// Adds a route that echoes the incoming request's <c>Cookie:</c> header verbatim
    /// into the response body. Used to verify which cookies a fetch sent.
    /// </summary>
    public void AddCookieEchoRoute(string path)
    {
        _customRoutes[path] = ctx =>
        {
            var cookieHeader = ctx.Request.Headers["Cookie"] ?? "";
            var bodyBytes = Encoding.UTF8.GetBytes(cookieHeader);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = bodyBytes.Length;
            ctx.Response.OutputStream.Write(bodyBytes, 0, bodyBytes.Length);
        };
    }

    /// <summary>
    /// Adds a redirect route that also sets a Set-Cookie header on the redirect response.
    /// Used to verify cookies are stored from intermediate redirect responses.
    /// </summary>
    public void AddSetCookieRedirectRoute(string path, string targetPath, string cookie, int statusCode = 302)
    {
        _customRoutes[path] = ctx =>
        {
            ctx.Response.Headers.Add("Set-Cookie", cookie);
            ctx.Response.StatusCode = statusCode;
            ctx.Response.RedirectLocation = $"http://localhost:{Port}{targetPath}";
            ctx.Response.ContentLength64 = 0;
        };
    }

    /// <summary>
    /// Starts the server. Each attempt binds the listener, launches the accept loop, then probes
    /// the server with a real loopback request before returning. A bind exception OR a server that
    /// binds but never answers (a port that was reserved but is unhealthy, or an accept loop that
    /// hasn't begun serving) re-rolls the port and retries. This makes <see cref="Start"/> not
    /// return until the server has actually responded, eliminating the accept-timing race and
    /// catching silent bad binds that the old bind-exception-only retry missed.
    /// </summary>
    public void Start()
    {
        const int maxAttempts = 5;
        Exception? lastError = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                lastError = ex;
                RebuildOnNewPort();
                continue;
            }

            StartAcceptLoop();

            if (ProbeReady(TimeSpan.FromSeconds(2)))
            {
                return; // Server is bound AND answering requests.
            }

            // Bound but never became ready: tear this listener down (which makes the accept loop's
            // GetContextAsync throw HttpListenerException and exit) and re-roll the port.
            lastError = new InvalidOperationException($"MockHttpServer on port {Port} never became ready");
            try { _listener.Stop(); } catch { /* ignore */ }
            RebuildOnNewPort();
        }

        throw lastError ?? new InvalidOperationException("Failed to start MockHttpServer");
    }

    /// <summary>
    /// Releases the current port and rebuilds a fresh listener on a newly reserved port.
    /// Used between failed start attempts (bind exception or failed readiness probe).
    /// </summary>
    private void RebuildOnNewPort()
    {
        ReleasePort(Port);
        try { _listener.Close(); } catch { /* ignore */ }
        _listener = new HttpListener();
        Port = FindAndReservePort();
        _listener.Prefixes.Add(BaseUrl);
        Thread.Sleep(10); // Small delay before retry
    }

    /// <summary>
    /// Launches the background accept loop for the current listener. The loop exits on cancellation
    /// (<see cref="Dispose"/>) or when the listener is stopped (a failed start attempt).
    /// </summary>
    private void StartAcceptLoop()
    {
        _listenerTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                    _ = HandleRequestAsync(context);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    break;
                }
            }
        }, _cts.Token);
    }

    /// <summary>
    /// Probes the server with a real loopback HTTP request until it answers or the budget elapses.
    /// Any completed HTTP response (even a 404 for the sentinel path) proves the accept loop is
    /// serving, so this never depends on a specific route being registered, and it deliberately
    /// avoids user routes (including the /slow delay route).
    /// </summary>
    private bool ProbeReady(TimeSpan budget)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) };
        var probeUrl = BaseUrl + "__ready__";
        var deadline = DateTime.UtcNow + budget;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var resp = client.GetAsync(probeUrl).GetAwaiter().GetResult();
                return true; // Got an HTTP status back -> the server is serving.
            }
            catch
            {
                // Connection refused / reset / timeout: not ready yet.
            }
            Thread.Sleep(25);
        }

        return false;
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            // Check custom routes first (full context access)
            if (_customRoutes.TryGetValue(path, out var customHandler))
            {
                customHandler(context);
            }
            // Check redirect routes
            else if (_redirectRoutes.TryGetValue(path, out var redirect))
            {
                context.Response.StatusCode = redirect.StatusCode;
                context.Response.RedirectLocation = $"http://localhost:{Port}{redirect.TargetPath}";
                context.Response.ContentLength64 = 0;
            }
            else if (_routes.TryGetValue(path, out var handler))
            {
                var (statusCode, contentType, body) = handler(context.Request);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
            }
            else
            {
                context.Response.StatusCode = 404;
                var body = Encoding.UTF8.GetBytes("Not Found");
                context.Response.ContentType = "text/plain";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
            }
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>
    /// Stops the server and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Ignore cancellation errors
        }

        try
        {
            _listener.Stop();
        }
        catch
        {
            // Ignore stop errors
        }

        // Let the accept loop observe the cancellation/stop and exit before the listener and
        // token source are torn down under it. Bounded so a wedged request can't hang teardown.
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Cancellation surfaces here as AggregateException; the loop is done either way
        }

        try
        {
            _listener.Close();
        }
        catch
        {
            // Ignore close errors (can happen during parallel test cleanup)
        }

        try
        {
            _cts.Dispose();
        }
        catch
        {
            // Ignore dispose errors
        }

        // Release the port reservation
        ReleasePort(Port);
    }
}
