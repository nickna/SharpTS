using System.Net;
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
    /// Adds a route with a specific status code.
    /// </summary>
    public void AddStatusRoute(string path, int statusCode, string body = "")
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        _routes[path] = _ => (statusCode, "text/plain", bodyBytes);
    }

    /// <summary>
    /// Starts the server with retry logic for port binding.
    /// </summary>
    public void Start()
    {
        const int maxRetries = 3;
        Exception? lastException = null;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                _listener.Start();
                break; // Success
            }
            catch (HttpListenerException ex) when (retry < maxRetries - 1)
            {
                lastException = ex;
                // Port may have been taken - try a new one
                ReleasePort(Port);
                _listener.Close();
                _listener = new HttpListener();
                Port = FindAndReservePort();
                _listener.Prefixes.Add(BaseUrl);
                Thread.Sleep(10); // Small delay before retry
            }
        }

        if (!_listener.IsListening)
        {
            throw lastException ?? new InvalidOperationException("Failed to start listener");
        }

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

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            if (_routes.TryGetValue(path, out var handler))
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
