using System.Net;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an HTTP Server.
/// </summary>
/// <remarks>
/// Wraps HttpListener to provide the Node.js http.Server interface.
/// Extends SharpTSEventEmitter for full event handling support (on, once, off, emit, etc.).
/// Implements IAsyncHandle to keep the event loop alive while listening.
/// Methods: listen(port, callback?), close(callback?)
/// Events: 'listening', 'request', 'error', 'close'
/// </remarks>
public class SharpTSHttpServer : SharpTSEventEmitter, ITypeCategorized, IDisposable
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    private HttpListener? _listener;
    private readonly ISharpTSCallable _requestHandler;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isListening;
    private Interpreter? _interpreter;

    /// <summary>
    /// Creates a new HTTP server with the given request handler.
    /// </summary>
    /// <param name="requestHandler">The callback function (req, res) => void.</param>
    public SharpTSHttpServer(ISharpTSCallable requestHandler)
    {
        _requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
    }

    /// <summary>
    /// Whether the server is currently listening.
    /// </summary>
    public bool Listening => _isListening;

    /// <summary>
    /// Gets a member (property or method) by name.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            "listening" => Listening,
            "listen" => new BuiltInMethod("listen", 1, 3, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpServer server)
                    return server.Listen(interp, args);
                return receiver;
            }).Bind(this),
            "close" => new BuiltInMethod("close", 0, 1, (interp, receiver, args) =>
            {
                if (receiver is SharpTSHttpServer server)
                    return server.Close(args);
                return receiver;
            }).Bind(this),
            "address" => GetAddress(),
            // Inherit EventEmitter methods for on, once, off, emit, removeAllListeners, etc.
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Starts listening on the specified port.
    /// </summary>
    private object? Listen(Interpreter interpreter, List<object?> args)
    {
        if (_isListening)
            throw new Exception("Runtime Error: Server is already listening");

        if (args.Count == 0 || args[0] is not double portNum)
            throw new Exception("Runtime Error: listen requires a port number");

        var port = (int)portNum;
        ISharpTSCallable? callback = null;

        // Second argument can be hostname (ignored for now) or callback
        if (args.Count > 1)
        {
            if (args[1] is ISharpTSCallable cb)
            {
                callback = cb;
            }
            else if (args.Count > 2 && args[2] is ISharpTSCallable cb2)
            {
                callback = cb2;
            }
        }

        _interpreter = interpreter;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            // Try localhost only if wildcard fails (requires admin on Windows)
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
        }

        _isListening = true;
        _cts = new CancellationTokenSource();

        // Register with interpreter's event loop to keep process alive
        interpreter.Ref();

        // Start accepting requests
        _listenTask = AcceptRequestsAsync(_cts.Token);

        // Call the listening callback
        if (callback != null)
        {
            callback.Call(interpreter, new List<object?>());
        }

        // Emit 'listening' event
        EmitEvent("listening", new List<object?>());

        return this;
    }

    /// <summary>
    /// Closes the server.
    /// </summary>
    private object? Close(List<object?> args)
    {
        if (!_isListening || _listener == null)
            return this;

        _cts?.Cancel();
        _listener.Stop();
        _listener.Close();
        _listener = null;
        _isListening = false;

        // Unregister from interpreter's event loop
        _interpreter?.Unref();

        // Call the callback if provided
        if (args.Count > 0 && args[0] is ISharpTSCallable callback && _interpreter != null)
        {
            callback.Call(_interpreter, new List<object?>());
        }

        // Emit 'close' event
        EmitEvent("close", new List<object?>());

        return this;
    }

    /// <summary>
    /// Emits an event using the inherited EventEmitter mechanism.
    /// </summary>
    private void EmitEvent(string eventName, List<object?> eventArgs)
    {
        if (_interpreter == null) return;

        // Use the inherited EventEmitter emit method
        var emitMethod = base.GetMember("emit") as BuiltInMethod;
        var args = new List<object?> { eventName };
        args.AddRange(eventArgs);
        emitMethod?.Call(_interpreter, args);
    }

    /// <summary>
    /// Gets the server address information.
    /// </summary>
    private object? GetAddress()
    {
        if (!_isListening || _listener == null)
            return null;

        var prefix = _listener.Prefixes.FirstOrDefault();
        if (prefix == null) return null;

        // Parse the prefix to extract port
        var uri = new Uri(prefix);
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["address"] = "0.0.0.0",
            ["family"] = "IPv4",
            ["port"] = (double)uri.Port
        });
    }

    /// <summary>
    /// Accepts incoming HTTP requests asynchronously.
    /// </summary>
    private async Task AcceptRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null && _isListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
            catch (HttpListenerException)
            {
                // Listener was closed
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Handles an individual HTTP request.
    /// </summary>
    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        if (_interpreter == null) return;

        var req = new SharpTSHttpRequest(context.Request);
        var res = new SharpTSHttpResponse(context.Response);

        try
        {
            // Schedule the request handler to run on the virtual timer system
            // This ensures it runs on the main thread during the event loop
            var tcs = new TaskCompletionSource();

            _interpreter.ScheduleTimer(0, 0, async () =>
            {
                try
                {
                    // Emit 'request' event
                    EmitEvent("request", new List<object?> { req, res });

                    // Call the request handler
                    _requestHandler.Call(_interpreter!, new List<object?> { req, res });

                    // Read and emit body events
                    await req.EmitDataEventsAsync(_interpreter!);

                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    // Emit error event
                    EmitEvent("error", new List<object?> { new SharpTSError(ex.Message) });
                    tcs.TrySetException(ex);
                }
            }, isInterval: false);

            await tcs.Task;
        }
        catch (Exception ex)
        {
            // Send error response if not already sent
            if (!res.HeadersSent)
            {
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.StatusDescription = "Internal Server Error";
                    var errorBytes = System.Text.Encoding.UTF8.GetBytes($"Error: {ex.Message}");
                    context.Response.ContentLength64 = errorBytes.Length;
                    await context.Response.OutputStream.WriteAsync(errorBytes);
                }
                catch
                {
                    // Ignore errors when writing error response
                }
            }
        }
        finally
        {
            if (!res.Finished)
            {
                try
                {
                    context.Response.Close();
                }
                catch
                {
                    // Ignore close errors
                }
            }
        }
    }

    /// <summary>
    /// Disposes the server and releases resources.
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        _cts?.Dispose();
        _listener = null;
        _isListening = false;
    }

    public override string ToString() => $"Server {{ listening: {Listening} }}";
}
