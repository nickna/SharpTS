using System.Collections.Concurrent;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a cluster worker spawned by cluster.fork().
/// Extends EventEmitter for lifecycle events (online, message, disconnect, exit, error).
/// Communication happens through structured-clone message passing via IPC queues.
/// </summary>
public class SharpTSClusterWorker : SharpTSEventEmitter, IDisposable
{
    private static int _nextWorkerId;

    private readonly Thread _thread;
    private readonly BlockingCollection<object?> _primaryToWorkerQueue = new();
    private readonly BlockingCollection<object?> _workerToParentQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _entryScript;
    private readonly Dictionary<string, object?>? _envOverrides;
    private readonly Interpreter? _parentInterpreter;

    private volatile bool _isRunning;
    private volatile bool _isDead;
    private volatile bool _isConnected = true;
    private volatile bool _exitedAfterDisconnect;

    /// <summary>
    /// Gets the unique worker ID.
    /// </summary>
    public double Id { get; }

    /// <summary>
    /// Gets whether the worker process is running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Creates a new cluster worker that re-executes the entry script.
    /// </summary>
    public SharpTSClusterWorker(string entryScript, Dictionary<string, object?>? envOverrides, Interpreter? parentInterpreter)
    {
        Id = Interlocked.Increment(ref _nextWorkerId);
        _entryScript = entryScript;
        _envOverrides = envOverrides;
        _parentInterpreter = parentInterpreter;

        _thread = new Thread(WorkerThreadMain)
        {
            Name = $"SharpTS-ClusterWorker-{Id}",
            IsBackground = true
        };

        _isRunning = true;

        // Keep parent event loop alive while worker is running
        _parentInterpreter?.Ref();

        _thread.Start();
    }

    /// <summary>
    /// Main entry point for the worker thread.
    /// </summary>
    private void WorkerThreadMain()
    {
        // Set up cluster context for this thread
        ClusterContext.IsWorker = true;
        ClusterContext.WorkerId = Id;
        ClusterContext.PrimaryToWorkerQueue = _primaryToWorkerQueue;
        ClusterContext.WorkerToPrimaryQueue = _workerToParentQueue;
        ClusterContext.CancellationToken = _cts.Token;
        ClusterContext.CurrentWorker = this;

        try
        {
            // Notify primary that worker is online
            ScheduleOnMainThread(() => EmitEventOnMainThread("online"));

            RunWorkerScript();
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation from kill/disconnect
        }
        catch (Exception ex)
        {
            ScheduleOnMainThread(() =>
            {
                var errorObj = new SharpTSError(ex.Message) { Stack = ex.StackTrace ?? "" };
                EmitEventOnMainThread("error", errorObj);
            });
        }
        finally
        {
            _isRunning = false;
            _isDead = true;
            _isConnected = false;

            try { _primaryToWorkerQueue.CompleteAdding(); } catch { }
            try { _workerToParentQueue.CompleteAdding(); } catch { }

            // Clean up shared listeners for this worker
            ClusterSingleton.Instance.SharedListeners.UnregisterAllForWorker(Id);

            // Clean up thread-local state
            ClusterContext.IsWorker = false;
            ClusterContext.PrimaryToWorkerQueue = null;
            ClusterContext.WorkerToPrimaryQueue = null;
            ClusterContext.CurrentWorker = null;

            // Emit exit event on primary and remove from workers registry.
            // Unref AFTER exit event delivery so the event loop stays alive.
            ScheduleOnMainThread(() =>
            {
                // Deliver any pending messages first
                DeliverMessagesToPrimary();

                EmitEventOnMainThread("exit", 0.0, _exitedAfterDisconnect ? "SIGTERM" : null);
                // Also emit on the cluster singleton
                ClusterSingleton.Instance.EmitWorkerEvent("exit", this, 0.0);
                ClusterSingleton.Instance.RemoveWorker(Id);

                // Now release the event loop handle
                _parentInterpreter?.Unref();
            });
        }
    }

    /// <summary>
    /// Runs the entry script in an isolated interpreter with full module resolution.
    /// </summary>
    private void RunWorkerScript()
    {
        string absolutePath = Path.GetFullPath(_entryScript);
        if (!File.Exists(absolutePath))
            throw new Exception($"Cluster worker script not found: {absolutePath}");

        using var interpreter = new Interpreter();

        // Use module resolver to handle imports (e.g., import * as cluster from 'cluster')
        var basePath = Path.GetDirectoryName(absolutePath) ?? ".";
        var resolver = new ModuleResolver(basePath);
        var entryModule = resolver.LoadModule(absolutePath);
        var modules = resolver.GetModulesInOrder(entryModule);

        // Type check all modules (CheckModules handles import resolution)
        var typeChecker = new TypeChecker();
        var typeMap = typeChecker.CheckModules(modules, resolver);

        _workerInterpreter = interpreter;

        // Start message polling
        var pollTimer = new Timer(PollMessages, interpreter, 10, 10);

        try
        {
            interpreter.InterpretModules(modules, resolver, typeMap);
        }
        finally
        {
            pollTimer.Dispose();
        }
    }

    // Reference to the worker's interpreter (for Unref on disconnect)
    private volatile Interpreter? _workerInterpreter;

    /// <summary>
    /// Polls for messages from the primary on the worker thread.
    /// </summary>
    private void PollMessages(object? state)
    {
        if (_isDead || !_isConnected) return;

        var interpreter = state as Interpreter;

        while (_primaryToWorkerQueue.TryTake(out var message))
        {
            try
            {
                // Emit 'message' event on the process object in the worker
                // Workers receive messages via process.on('message', handler)
                // Must use interpreter-based emit because listeners are ISharpTSCallable
                var cloned = StructuredClone.Clone(message);
                var workerInterp = interpreter;
                interpreter?.ScheduleTimer(0, 0, () =>
                {
                    var emitMethod = SharpTSProcess.Instance.GetMember("emit") as BuiltInMethod;
                    emitMethod?.Call(workerInterp!, ["message", cloned]);
                }, false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cluster worker message handler error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Sends a message from the primary to this worker.
    /// </summary>
    public void Send(object? message)
    {
        if (_isDead || !_isConnected) return;

        try
        {
            var cloned = StructuredClone.Clone(message);
            _primaryToWorkerQueue.Add(cloned);
        }
        catch (InvalidOperationException)
        {
            // Queue completed - worker is terminating
        }
    }

    /// <summary>
    /// Posts a message from the worker to the primary. Called from the worker thread.
    /// </summary>
    internal void PostMessageToPrimary(object? message)
    {
        if (_isDead || !_isConnected) return;

        try
        {
            var cloned = StructuredClone.Clone(message);
            _workerToParentQueue.Add(cloned);

            // Schedule delivery on parent thread
            _parentInterpreter?.ScheduleTimer(0, 0, () =>
            {
                DeliverMessagesToPrimary();
            }, false);
        }
        catch (InvalidOperationException)
        {
            // Queue completed
        }
    }

    /// <summary>
    /// Drains the worker-to-primary queue and emits message events.
    /// </summary>
    internal void DeliverMessagesToPrimary()
    {
        while (_workerToParentQueue.TryTake(out var message))
        {
            // Emit on the worker object
            EmitEventOnMainThread("message", message);

            // Also emit on the cluster singleton
            ClusterSingleton.Instance.EmitWorkerEvent("message", this, message);
        }
    }

    /// <summary>
    /// Gracefully disconnects the worker.
    /// </summary>
    public void Disconnect()
    {
        if (!_isConnected || _isDead) return;

        _isConnected = false;
        _exitedAfterDisconnect = true;

        try { _primaryToWorkerQueue.CompleteAdding(); } catch { }

        // Shut down the worker's event loop promptly via cooperative cancellation
        _workerInterpreter?.Shutdown();

        ScheduleOnMainThread(() =>
        {
            EmitEventOnMainThread("disconnect");
            ClusterSingleton.Instance.EmitWorkerEvent("disconnect", this);
        });

        _cts.Cancel();
    }

    /// <summary>
    /// Kills the worker (forced termination).
    /// </summary>
    public void Kill(string? signal = null)
    {
        if (_isDead) return;

        _isConnected = false;

        // Shut down the worker's event loop promptly via cooperative cancellation.
        // This interrupts TryTake in RunEventLoop, matching Node.js kill() semantics.
        _workerInterpreter?.Shutdown();

        _cts.Cancel();

        // Give it a moment to exit gracefully, then force
        if (!_thread.Join(1000))
        {
            // Thread didn't exit in time - it will exit when CancellationToken is checked
        }
    }

    /// <summary>
    /// Returns whether the worker has exited.
    /// </summary>
    public bool IsDead() => _isDead;

    /// <summary>
    /// Returns whether the worker IPC channel is connected.
    /// </summary>
    public bool IsConnectedCheck() => _isConnected;

    /// <summary>
    /// Schedules an action on the main thread.
    /// </summary>
    private void ScheduleOnMainThread(Action action)
    {
        if (_parentInterpreter != null)
        {
            _parentInterpreter.ScheduleTimer(0, 0, action, false);
        }
    }

    /// <summary>
    /// Emits an event on the main thread.
    /// </summary>
    private void EmitEventOnMainThread(string eventName, params object?[] args)
    {
        if (_parentInterpreter != null)
        {
            var emitMethod = GetMember("emit") as BuiltInMethod;
            var emitArgs = new List<object?> { eventName };
            emitArgs.AddRange(args);
            emitMethod?.Call(_parentInterpreter, emitArgs);
        }
        else
        {
            EmitDirect(eventName, args);
        }
    }

    /// <summary>
    /// Gets a member (method or property) by name.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "id" => Id,
            "exitedAfterDisconnect" => _exitedAfterDisconnect,

            "send" => BuiltInMethod.CreateV2("send", 1, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("worker.send() requires at least one argument");
                Send(args[0].ToObject());
                return RuntimeValue.True;
            }),

            "disconnect" => BuiltInMethod.CreateV2("disconnect", 0, (_, _, _) =>
            {
                Disconnect();
                return RuntimeValue.Null;
            }),

            "kill" => BuiltInMethod.CreateV2("kill", 0, 1, (_, _, args) =>
            {
                var signal = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                Kill(signal);
                return RuntimeValue.Null;
            }),

            "isDead" => BuiltInMethod.CreateV2("isDead", 0, (_, _, _) => RuntimeValue.FromBoxed(IsDead())),

            "isConnected" => BuiltInMethod.CreateV2("isConnected", 0, (_, _, _) => RuntimeValue.FromBoxed(IsConnectedCheck())),

            // Inherit EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    public void Dispose()
    {
        Kill();
        _cts.Dispose();
        _primaryToWorkerQueue.Dispose();
        _workerToParentQueue.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"Worker {{ id: {Id} }}";
}
