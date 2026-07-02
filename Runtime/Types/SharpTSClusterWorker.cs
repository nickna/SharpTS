using System.Collections.Concurrent;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a cluster worker spawned by cluster.fork().
/// Extends EventEmitter for lifecycle events (online, message, disconnect, exit, error, listening).
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
    private readonly List<object?>? _argvOverrides;
    private readonly bool _silent;
    private readonly Interpreter? _parentInterpreter;

    // Compiled-mode parent loop bridge (the worker_threads CreateForCompiledLoop
    // pattern, #354): when the primary is a compiled program there is no parent
    // Interpreter, so the emitted $EventLoop's Ref/Unref/Schedule are injected as
    // delegates and used for keep-alive and event marshaling instead.
    private readonly Action? _loopRef;
    private readonly Action? _loopUnref;
    private readonly Action<Action>? _loopSchedule;

    private readonly ClusterWorkerProcess _process;

    private volatile bool _isRunning;
    private volatile bool _isDead;
    private volatile bool _isConnected = true;
    private volatile bool _exitedAfterDisconnect;
    private volatile string? _killSignal;

    /// <summary>
    /// Gets the unique worker ID.
    /// </summary>
    public double Id { get; }

    /// <summary>
    /// The primary-side interpreter this worker marshals events onto (null when the
    /// primary is a compiled program using the $EventLoop delegates).
    /// </summary>
    internal Interpreter? ParentInterpreter => _parentInterpreter;

    /// <summary>
    /// Gets whether the worker process is running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Creates a new cluster worker that re-executes the entry script.
    /// </summary>
    public SharpTSClusterWorker(string entryScript, Dictionary<string, object?>? envOverrides, Interpreter? parentInterpreter)
        : this(entryScript, envOverrides, parentInterpreter, argvOverrides: null, silent: false,
               loopRef: null, loopUnref: null, loopSchedule: null)
    {
    }

    /// <summary>
    /// Creates a new cluster worker honoring cluster.settings (args, silent) and, for
    /// compiled primaries, the emitted $EventLoop delegates.
    /// </summary>
    public SharpTSClusterWorker(
        string entryScript, Dictionary<string, object?>? envOverrides, Interpreter? parentInterpreter,
        List<object?>? argvOverrides, bool silent,
        Action? loopRef, Action? loopUnref, Action<Action>? loopSchedule)
    {
        Id = Interlocked.Increment(ref _nextWorkerId);
        _entryScript = entryScript;
        _envOverrides = envOverrides;
        _argvOverrides = argvOverrides;
        _silent = silent;
        _parentInterpreter = parentInterpreter;
        _loopRef = loopRef;
        _loopUnref = loopUnref;
        _loopSchedule = loopSchedule;
        _process = new ClusterWorkerProcess(this, silent);

        _thread = new Thread(WorkerThreadMain)
        {
            Name = $"SharpTS-ClusterWorker-{Id}",
            IsBackground = true
        };

        _isRunning = true;

        // Keep parent event loop alive while worker is running
        RefParentLoop();

        _thread.Start();
    }

    private void RefParentLoop()
    {
        if (_parentInterpreter != null)
            _parentInterpreter.Ref();
        else
            _loopRef?.Invoke();
    }

    private void UnrefParentLoop()
    {
        if (_parentInterpreter != null)
            _parentInterpreter.Unref();
        else
            _loopUnref?.Invoke();
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

        // Honor cluster.settings.args / fork(env): the worker's process.argv and
        // process.env resolve through these thread-local overrides. The worker's
        // interpreter event loop is confined to this thread, so [ThreadStatic] is safe.
        ProcessBuiltIns.ThreadArgv = BuildWorkerArgv();
        ProcessBuiltIns.ThreadEnv = BuildWorkerEnv();

        double exitCode = 0;
        try
        {
            // Notify primary that worker is online
            ScheduleOnMainThread(() => EmitWorkerEvent("online"));

            RunWorkerScript();
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation from kill/disconnect
        }
        catch (Exception ex)
        {
            exitCode = 1;
            ScheduleOnMainThread(() =>
            {
                var errorObj = new SharpTSError(ex.Message) { Stack = ex.StackTrace ?? "" };
                EmitWorkerEvent("error", errorObj);
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
            ProcessBuiltIns.ThreadArgv = null;
            ProcessBuiltIns.ThreadEnv = null;

            // Node exit semantics: kill → (null, signal); otherwise → (code, null).
            object? finalCode = _killSignal != null ? null : exitCode;
            string? finalSignal = _killSignal;

            // Emit exit event on primary and remove from workers registry.
            // Unref AFTER exit event delivery so the event loop stays alive.
            ScheduleOnMainThread(() =>
            {
                // Deliver any pending messages first
                DeliverMessagesToPrimary();

                _process.EndStdio(_parentInterpreter);
                // Node removes the worker from cluster.workers BEFORE emitting 'exit' —
                // inside an 'exit' handler the worker is no longer in cluster.workers.
                ClusterSingleton.Instance.RemoveWorker(Id);
                EmitWorkerEvent("exit", finalCode, finalSignal);
                ClusterSingleton.Instance.EmitWorkerEvent("exit", this, finalCode, finalSignal);

                // Now release the event loop handle
                UnrefParentLoop();
            });
        }
    }

    /// <summary>
    /// Builds the worker's process.argv: [runtime, script, ...cluster.settings.args].
    /// </summary>
    private SharpTSArray BuildWorkerArgv()
    {
        var elements = new List<object?>
        {
            Environment.ProcessPath ?? "sharpts",
            Path.GetFullPath(_entryScript)
        };
        if (_argvOverrides != null)
            elements.AddRange(_argvOverrides);
        return new SharpTSArray(elements);
    }

    /// <summary>
    /// Builds the worker's process.env: parent environment + fork(env) overrides +
    /// NODE_UNIQUE_ID (Node's worker marker).
    /// </summary>
    private SharpTSObject BuildWorkerEnv()
    {
        var fields = new Dictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            fields[entry.Key?.ToString() ?? ""] = entry.Value?.ToString();
        }
        if (_envOverrides != null)
        {
            foreach (var (key, value) in _envOverrides)
            {
                fields[key] = value switch
                {
                    null or SharpTSUndefined => null,
                    bool b => b ? "true" : "false",
                    _ => value.ToString(),
                };
            }
        }
        fields["NODE_UNIQUE_ID"] = Id.ToString("0");
        return new SharpTSObject(fields);
    }

    /// <summary>
    /// Runs the entry script in an isolated interpreter with full module resolution.
    /// </summary>
    private void RunWorkerScript()
    {
        string absolutePath = Path.GetFullPath(_entryScript);
        if (!File.Exists(absolutePath))
            throw new Exception($"Cluster worker script not found: {absolutePath}");

        // Worker stdio: inherit the primary's writers (Node default) unless silent,
        // in which case output is diverted to worker.process.stdout/stderr (#1169).
        TextWriter stdout, stderr;
        if (_silent)
        {
            stdout = new WorkerStreamWriter(this, _process.StdoutStream!);
            stderr = new WorkerStreamWriter(this, _process.StderrStream!);
        }
        else if (_parentInterpreter != null)
        {
            stdout = TextWriter.Synchronized(_parentInterpreter.Out);
            stderr = TextWriter.Synchronized(_parentInterpreter.Error);
        }
        else
        {
            stdout = Console.Out;
            stderr = Console.Error;
        }

        using var interpreter = new Interpreter(stdout, stderr);

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
    /// Sends a message to this worker (primary side), or — when invoked from inside the
    /// worker's own context (cluster.worker.send(...)) — to the primary, matching Node.
    /// </summary>
    public void Send(object? message)
    {
        if (ClusterContext.IsWorker && ReferenceEquals(ClusterContext.CurrentWorker, this))
        {
            PostMessageToPrimary(message);
            return;
        }

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
            ScheduleOnMainThread(DeliverMessagesToPrimary);
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
            // Emit on the worker object (forwarded to worker.process too)
            EmitWorkerEvent("message", message);

            // Also emit on the cluster singleton
            ClusterSingleton.Instance.EmitWorkerEvent("message", this, message);
        }
    }

    /// <summary>
    /// Called from the worker thread when one of the worker's servers begins listening.
    /// Emits 'listening' on the worker object and the cluster singleton (primary side)
    /// with Node's { address, port, addressType } payload (#1168).
    /// </summary>
    internal void NotifyListening(string address, int port, double addressType)
    {
        ScheduleOnMainThread(() =>
        {
            var addressObj = new SharpTSObject(new Dictionary<string, object?>
            {
                ["address"] = address,
                ["port"] = (double)port,
                ["addressType"] = addressType,
            });
            EmitWorkerEvent("listening", addressObj);
            ClusterSingleton.Instance.EmitWorkerEvent("listening", this, addressObj);
        });
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
            EmitWorkerEvent("disconnect");
            ClusterSingleton.Instance.EmitWorkerEvent("disconnect", this);
        });

        _cts.Cancel();
    }

    /// <summary>
    /// Kills the worker. Node's worker.kill() attempts a graceful disconnect first, so
    /// exitedAfterDisconnect becomes true and the exit event carries (null, signal).
    /// </summary>
    public void Kill(string? signal = null)
    {
        if (_isDead) return;

        _killSignal = signal ?? "SIGTERM";
        _exitedAfterDisconnect = true;
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
    /// Schedules an action on the main thread (parent interpreter timer, or the
    /// compiled $EventLoop's Schedule delegate).
    /// </summary>
    private void ScheduleOnMainThread(Action action)
    {
        if (_parentInterpreter != null)
        {
            _parentInterpreter.ScheduleTimer(0, 0, action, false);
        }
        else if (_loopSchedule != null)
        {
            _loopSchedule(action);
        }
        // No parent loop to marshal onto — the event cannot be delivered.
    }

    /// <summary>
    /// Emits an event on this worker and forwards it to the worker.process handle
    /// (Node's Worker events originate from its underlying ChildProcess).
    /// Must run on the main thread.
    /// </summary>
    private void EmitWorkerEvent(string eventName, params object?[] args)
    {
        EmitEventOnMainThread(this, eventName, args);
        // worker.process mirrors the ChildProcess events (#1169). 'online'/'listening'
        // are cluster-protocol events that exist only on the Worker, not the process.
        if (eventName is "message" or "disconnect" or "exit" or "error")
            EmitEventOnMainThread(_process, eventName, args);
    }

    /// <summary>
    /// Emits an event on an emitter using the appropriate mechanism for the parent
    /// (interpreter-aware emit, or direct emit for compiled listeners).
    /// </summary>
    private void EmitEventOnMainThread(SharpTSEventEmitter target, string eventName, params object?[] args)
    {
        if (_parentInterpreter != null)
        {
            target.EmitWith(_parentInterpreter, eventName, args);
        }
        else
        {
            target.EmitDirect(eventName, args);
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
            "process" => _process,

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

            // worker.destroy([signal]) — alias of kill (#1169)
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, (_, _, args) =>
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

    /// <summary>
    /// A TextWriter that redirects a silent worker's console output into the
    /// worker.process.stdout/stderr Readable, pushed on the parent's thread.
    /// </summary>
    private sealed class WorkerStreamWriter(SharpTSClusterWorker worker, SharpTSReadable target) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(string? value) => Push(value);
        public override void WriteLine(string? value) => Push((value ?? string.Empty) + Environment.NewLine);
        public override void Write(char value) => Push(value.ToString());
        private void Push(string? text)
        {
            if (!string.IsNullOrEmpty(text))
                worker.ScheduleOnMainThread(() => target.PushFromHost(worker._parentInterpreter, text));
        }
    }
}

/// <summary>
/// The ChildProcess-like handle exposed as worker.process (#1169). In the thread model
/// the "process" is a thread, so pid is the host process id and kill/send/disconnect
/// delegate to the worker — a documented approximation of Node's process-per-worker.
/// Forwards the worker's message/disconnect/exit/error events.
/// </summary>
public class ClusterWorkerProcess : SharpTSEventEmitter
{
    private readonly SharpTSClusterWorker _worker;

    /// <summary>stdout capture stream when the worker runs silent; null otherwise.</summary>
    internal SharpTSReadable? StdoutStream { get; }

    /// <summary>stderr capture stream when the worker runs silent; null otherwise.</summary>
    internal SharpTSReadable? StderrStream { get; }

    internal ClusterWorkerProcess(SharpTSClusterWorker worker, bool silent)
    {
        _worker = worker;
        if (silent)
        {
            StdoutStream = new SharpTSReadable();
            StderrStream = new SharpTSReadable();
        }
    }

    /// <summary>Signals EOF on the capture streams when the worker exits.</summary>
    internal void EndStdio(Interpreter? interpreter)
    {
        StdoutStream?.PushFromHost(interpreter, null);
        StderrStream?.PushFromHost(interpreter, null);
    }

    public override object? GetMember(string name)
    {
        return name switch
        {
            // Thread model: all workers share the host process (documented ceiling).
            "pid" => (double)Environment.ProcessId,
            "connected" => _worker.IsConnectedCheck(),
            "stdout" => StdoutStream,
            "stderr" => StderrStream,

            "kill" => BuiltInMethod.CreateV2("kill", 0, 1, (_, _, args) =>
            {
                _worker.Kill(args.Length > 0 ? args[0].ToObject()?.ToString() : null);
                return RuntimeValue.True;
            }),

            "send" => BuiltInMethod.CreateV2("send", 1, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("worker.process.send() requires at least one argument");
                _worker.Send(args[0].ToObject());
                return RuntimeValue.True;
            }),

            "disconnect" => BuiltInMethod.CreateV2("disconnect", 0, (_, _, _) =>
            {
                _worker.Disconnect();
                return RuntimeValue.Null;
            }),

            _ => base.GetMember(name)
        };
    }

    public override string ToString() => $"ChildProcess {{ pid: {Environment.ProcessId} }}";
}
