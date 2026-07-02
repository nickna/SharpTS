using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Manages shared TCP/HTTP listeners for cluster port sharing.
/// When multiple cluster workers call server.listen() on the same port,
/// a single listener is created and connections are distributed round-robin.
/// </summary>
public class SharedListenerRegistry
{
    private readonly ConcurrentDictionary<int, SharedTcpListener> _tcpListeners = new();
    private readonly ConcurrentDictionary<int, SharedHttpListener> _httpListeners = new();

    /// <summary>
    /// Registers a worker to receive TCP connections on a shared port.
    /// Creates the listener if this is the first worker on that port.
    /// </summary>
    public void RegisterTcpWorker(int port, string host, double workerId, Action<TcpClient> onConnection, Interp workerInterpreter)
    {
        var shared = _tcpListeners.GetOrAdd(port, p =>
        {
            var ipAddress = host is "0.0.0.0" or "::"
                ? IPAddress.Any
                : IPAddress.TryParse(host, out var parsed) ? parsed : IPAddress.Loopback;
            return new SharedTcpListener(p, ipAddress);
        });

        shared.AddWorker(workerId, onConnection, workerInterpreter);
    }

    /// <summary>
    /// Unregisters a TCP worker from a shared port.
    /// Stops the listener if no workers remain.
    /// </summary>
    public void UnregisterTcpWorker(int port, double workerId)
    {
        if (_tcpListeners.TryGetValue(port, out var shared))
        {
            shared.RemoveWorker(workerId);
            if (shared.WorkerCount == 0)
            {
                if (_tcpListeners.TryRemove(port, out var removed))
                    removed.Stop();
            }
        }
    }

    /// <summary>
    /// Registers a worker to receive HTTP requests on a shared port.
    /// Creates the listener if this is the first worker on that port.
    /// </summary>
    public void RegisterHttpWorker(int port, double workerId, Action<HttpListenerContext> onRequest, Interp workerInterpreter)
    {
        var shared = _httpListeners.GetOrAdd(port, p => new SharedHttpListener(p));
        shared.AddWorker(workerId, onRequest, workerInterpreter);
    }

    /// <summary>
    /// Unregisters an HTTP worker from a shared port.
    /// </summary>
    public void UnregisterHttpWorker(int port, double workerId)
    {
        if (_httpListeners.TryGetValue(port, out var shared))
        {
            shared.RemoveWorker(workerId);
            if (shared.WorkerCount == 0)
            {
                if (_httpListeners.TryRemove(port, out var removed))
                    removed.Stop();
            }
        }
    }

    /// <summary>
    /// Unregisters all listeners for a given worker (called on worker exit).
    /// </summary>
    public void UnregisterAllForWorker(double workerId)
    {
        foreach (var kvp in _tcpListeners)
        {
            kvp.Value.RemoveWorker(workerId);
            if (kvp.Value.WorkerCount == 0)
            {
                if (_tcpListeners.TryRemove(kvp.Key, out var removed))
                    removed.Stop();
            }
        }
        foreach (var kvp in _httpListeners)
        {
            kvp.Value.RemoveWorker(workerId);
            if (kvp.Value.WorkerCount == 0)
            {
                if (_httpListeners.TryRemove(kvp.Key, out var removed))
                    removed.Stop();
            }
        }
    }

    /// <summary>
    /// Closes all shared listeners and clears the registry.
    /// </summary>
    public void CloseAll()
    {
        foreach (var kvp in _tcpListeners)
        {
            try { kvp.Value.Stop(); } catch { }
        }
        _tcpListeners.Clear();

        foreach (var kvp in _httpListeners)
        {
            try { kvp.Value.Stop(); } catch { }
        }
        _httpListeners.Clear();
    }
}

/// <summary>
/// A shared TCP listener that distributes incoming connections round-robin across workers.
/// </summary>
internal class SharedTcpListener
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<double, WorkerRegistration> _workers = new();
    private int _roundRobinIndex;
    private CancellationTokenSource? _cts;
    private bool _started;

    public int WorkerCount => _workers.Count;

    public SharedTcpListener(int port, IPAddress address)
    {
        _listener = new TcpListener(address, port);
    }

    public void AddWorker(double workerId, Action<TcpClient> onConnection, Interp workerInterpreter)
    {
        _workers[workerId] = new WorkerRegistration(workerId, onConnection, workerInterpreter);
        if (!_started)
        {
            _started = true;
            _cts = new CancellationTokenSource();
            _listener.Start();
            StartAccepting();
        }
    }

    public void RemoveWorker(double workerId)
    {
        _workers.TryRemove(workerId, out _);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        _started = false;
    }

    private void StartAccepting()
    {
        var token = _cts!.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(token);
                    DispatchConnection(tcpClient);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
            }
        }, token);
    }

    private void DispatchConnection(TcpClient tcpClient)
    {
        var workerArray = _workers.Values.ToArray();
        if (workerArray.Length == 0)
        {
            tcpClient.Close();
            return;
        }

        var worker = workerArray[SharedListenerDispatch.NextIndex(ref _roundRobinIndex, workerArray.Length)];

        // Schedule on the worker's interpreter thread
        worker.WorkerInterpreter.ScheduleTimer(0, 0, () =>
        {
            worker.OnConnection(tcpClient);
        }, isInterval: false);
    }
}

/// <summary>
/// A shared HTTP listener that distributes incoming requests round-robin across workers.
/// </summary>
internal class SharedHttpListener
{
    private HttpListener? _listener;
    private readonly ConcurrentDictionary<double, HttpWorkerRegistration> _workers = new();
    private int _roundRobinIndex;
    private CancellationTokenSource? _cts;
    private bool _started;
    private readonly int _port;

    public int WorkerCount => _workers.Count;

    public SharedHttpListener(int port)
    {
        _port = port;
    }

    public void AddWorker(double workerId, Action<HttpListenerContext> onRequest, Interp workerInterpreter)
    {
        _workers[workerId] = new HttpWorkerRegistration(workerId, onRequest, workerInterpreter);
        if (!_started)
        {
            _started = true;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            try
            {
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Start();
            }
            StartAccepting();
        }
    }

    public void RemoveWorker(double workerId)
    {
        _workers.TryRemove(workerId, out _);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _started = false;
    }

    private void StartAccepting()
    {
        var token = _cts!.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    DispatchRequest(context);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private void DispatchRequest(HttpListenerContext context)
    {
        var workerArray = _workers.Values.ToArray();
        if (workerArray.Length == 0)
        {
            try
            {
                context.Response.StatusCode = 503;
                context.Response.Close();
            }
            catch { }
            return;
        }

        var worker = workerArray[SharedListenerDispatch.NextIndex(ref _roundRobinIndex, workerArray.Length)];

        worker.WorkerInterpreter.ScheduleTimer(0, 0, () =>
        {
            worker.OnRequest(context);
        }, isInterval: false);
    }
}

internal record WorkerRegistration(double WorkerId, Action<TcpClient> OnConnection, Interp WorkerInterpreter);
internal record HttpWorkerRegistration(double WorkerId, Action<HttpListenerContext> OnRequest, Interp WorkerInterpreter);

/// <summary>
/// Worker-pick strategy for the shared listeners, honoring cluster.schedulingPolicy
/// (#1170): SCHED_RR distributes round-robin; SCHED_NONE means "leave it to the OS",
/// which the thread model approximates with an arbitrary (random) pick — the closest
/// analog of Node's accept-race behavior.
/// </summary>
internal static class SharedListenerDispatch
{
    internal static int NextIndex(ref int roundRobinIndex, int workerCount)
    {
        if (ClusterSingleton.Instance.SchedulingPolicy == ClusterSingleton.SchedNone)
            return Random.Shared.Next(workerCount);
        return (int)((uint)Interlocked.Increment(ref roundRobinIndex) % (uint)workerCount);
    }
}
