using System.Collections.Concurrent;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Worker thread for parallel execution.
/// </summary>
/// <remarks>
/// Workers run TypeScript code in a separate thread with their own isolated interpreter.
/// Communication happens through message passing via postMessage/onmessage, using the
/// structured clone algorithm. SharedArrayBuffer is shared by reference, enabling
/// shared memory access with Atomics for synchronization.
///
/// Workers load script files from disk - inline functions are not supported.
/// This enforces a clean separation between threads and prevents closure issues.
/// </remarks>
public class SharpTSWorker : SharpTSEventEmitter, ITypeCategorized, IDisposable
{
    private static int _nextThreadId = 1;

    private readonly Thread _thread;
    private readonly BlockingCollection<SharpTSMessagePort.ClonedMessage> _parentToWorkerQueue = new();
    private readonly BlockingCollection<SharpTSMessagePort.ClonedMessage> _workerToParentQueue = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _scriptPath;
    private readonly object? _workerData;
    private readonly object? _stdin;
    private readonly object? _stdout;
    private readonly object? _stderr;
    private readonly SharpTSArray? _transferList;
    private readonly SharpTSArray? _resourceLimits;
    private volatile bool _isRunning;
    private volatile bool _isTerminated;
    private Exception? _workerError;
    private Interpreter? _parentInterpreter;

    // For compiled code support - enables Worker communication without interpreter
    private readonly SynchronizationContext? _syncContext;
    private readonly ConcurrentQueue<Action> _pendingCallbacks = new();

    /// <summary>
    /// Gets the unique thread ID for this worker.
    /// </summary>
    public double ThreadId { get; }

    /// <summary>
    /// Gets whether the worker thread is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Instance;

    /// <summary>
    /// Creates a new Worker that will execute the specified script file.
    /// </summary>
    /// <param name="filename">Path to the TypeScript file to execute.</param>
    /// <param name="options">Optional worker options (workerData, transferList, etc.).</param>
    /// <param name="parentInterpreter">The parent interpreter for message delivery.</param>
    public SharpTSWorker(string filename, SharpTSObject? options, Interpreter? parentInterpreter)
    {
        ThreadId = Interlocked.Increment(ref _nextThreadId);
        _scriptPath = filename;
        _parentInterpreter = parentInterpreter;

        // Capture sync context for compiled code to marshal callbacks to main thread
        _syncContext = SynchronizationContext.Current;

        // Extract options
        if (options != null)
        {
            _workerData = options.GetProperty("workerData");
            _transferList = options.GetProperty("transferList") as SharpTSArray;
            _stdin = options.GetProperty("stdin");
            _stdout = options.GetProperty("stdout");
            _stderr = options.GetProperty("stderr");
            _resourceLimits = options.GetProperty("resourceLimits") as SharpTSArray;
        }

        // Clone workerData for transfer to worker
        if (_workerData != null)
        {
            try
            {
                _workerData = StructuredClone.Clone(_workerData, _transferList);
            }
            catch (StructuredClone.DataCloneError e)
            {
                throw new Exception($"Failed to clone workerData: {e.Message}");
            }
        }

        // Create and start the worker thread
        _thread = new Thread(WorkerThreadMain)
        {
            Name = $"SharpTS-Worker-{ThreadId}",
            IsBackground = true
        };

        _isRunning = true;
        _thread.Start();
    }

    /// <summary>
    /// The main entry point for the worker thread.
    /// </summary>
    private void WorkerThreadMain()
    {
        try
        {
            RunWorkerScript();
        }
        catch (Exception ex)
        {
            _workerError = ex;
            // Emit error on parent thread
            EnqueueErrorToParent(ex);
        }
        finally
        {
            _isRunning = false;
            _parentToWorkerQueue.CompleteAdding();
            _workerToParentQueue.CompleteAdding();

            // Notify parent that worker has exited
            EnqueueExitToParent(0);
        }
    }

    /// <summary>
    /// Runs the worker script in an isolated interpreter.
    /// </summary>
    private void RunWorkerScript()
    {
        // Resolve the script path
        string absolutePath = Path.GetFullPath(_scriptPath);
        if (!File.Exists(absolutePath))
        {
            throw new Exception($"Worker script not found: {absolutePath}");
        }

        string source = File.ReadAllText(absolutePath);

        // Create isolated interpreter for this worker
        using var interpreter = new Interpreter();

        // Set up worker globals
        SetupWorkerGlobals(interpreter);

        // Parse and execute the script
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        if (!parseResult.IsSuccess)
        {
            throw new Exception($"Worker script parse error: {parseResult.Diagnostics.FirstOrDefault()?.Message ?? "Unknown error"}");
        }

        // Type check
        var typeChecker = new TypeChecker();
        var typeMap = typeChecker.Check(parseResult.Statements);

        // Set up message handling loop
        var messageHandler = new WorkerMessageHandler(this, interpreter);
        messageHandler.Start();

        try
        {
            // Execute the script
            interpreter.Interpret(parseResult.Statements, typeMap);
        }
        finally
        {
            messageHandler.Stop();
        }
    }

    /// <summary>
    /// Sets up worker-specific global variables.
    /// </summary>
    private void SetupWorkerGlobals(Interpreter interpreter)
    {
        var env = interpreter.Environment;

        // isMainThread - always false in worker
        env.Define("isMainThread", false);

        // threadId - this worker's ID
        env.Define("threadId", ThreadId);

        // workerData - data passed from parent
        env.Define("workerData", _workerData);

        // parentPort - MessagePort for communicating with parent
        var parentPort = new WorkerParentPort(this);
        env.Define("parentPort", parentPort);

        // postMessage - convenience function (same as parentPort.postMessage)
        env.Define("postMessage", new BuiltInMethod("postMessage", 1, 2, (interp, recv, args) =>
        {
            if (args.Count == 0)
                throw new Exception("postMessage requires at least one argument");
            var transfer = args.Count > 1 ? args[1] as SharpTSArray : null;
            PostMessageToParent(args[0], transfer);
            return null;
        }));
    }

    /// <summary>
    /// Posts a message from the worker to the parent.
    /// </summary>
    internal void PostMessageToParent(object? message, SharpTSArray? transfer = null)
    {
        if (_isTerminated)
            return;

        try
        {
            var cloned = StructuredClone.Clone(message, transfer);
            _workerToParentQueue.Add(new SharpTSMessagePort.ClonedMessage(cloned, transfer));

            // Schedule delivery on parent thread
            _parentInterpreter?.ScheduleTimer(0, 0, () =>
            {
                DeliverMessagesToParent();
            }, false);
        }
        catch (StructuredClone.DataCloneError e)
        {
            throw new Exception($"Failed to clone message: {e.Message}");
        }
        catch (InvalidOperationException)
        {
            // Queue was completed - worker is terminating
        }
    }

    /// <summary>
    /// Delivers pending messages from worker to parent event listeners.
    /// Called from the main thread to process messages queued by the worker.
    /// </summary>
    internal void DeliverMessagesToParent()
    {
        while (_workerToParentQueue.TryTake(out var message))
        {
            var eventData = new SharpTSObject(new Dictionary<string, object?>
            {
                ["data"] = message.Data
            });

            EmitEventOnMainThread("message", eventData);
        }
    }

    /// <summary>
    /// Enqueues an error event to be delivered to the parent.
    /// </summary>
    private void EnqueueErrorToParent(Exception ex)
    {
        var errorObj = new SharpTSError(ex.Message)
        {
            Stack = ex.StackTrace ?? ""
        };
        ScheduleOnMainThread(() => EmitEventOnMainThread("error", errorObj));
    }

    /// <summary>
    /// Enqueues an exit event to be delivered to the parent.
    /// </summary>
    private void EnqueueExitToParent(int exitCode)
    {
        ScheduleOnMainThread(() => EmitEventOnMainThread("exit", (double)exitCode));
    }

    /// <summary>
    /// Schedules an action to run on the main thread.
    /// Uses interpreter timers if available, SynchronizationContext if available,
    /// or queues for manual processing via ProcessPendingCallbacks().
    /// </summary>
    private void ScheduleOnMainThread(Action action)
    {
        if (_parentInterpreter != null)
        {
            // Interpreter path - use timer for callback delivery
            _parentInterpreter.ScheduleTimer(0, 0, action, false);
        }
        else if (_syncContext != null)
        {
            // Compiled path with sync context (WinForms, WPF, etc.)
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            // Compiled path without sync context (console app)
            // Queue for manual processing
            _pendingCallbacks.Enqueue(action);
        }
    }

    /// <summary>
    /// Emits an event using the appropriate mechanism.
    /// Works for both interpreted and compiled code.
    /// </summary>
    private void EmitEventOnMainThread(string eventName, object? data)
    {
        if (_parentInterpreter != null)
        {
            // Interpreter path - use BuiltInMethod
            var emitMethod = GetMember("emit") as BuiltInMethod;
            emitMethod?.Call(_parentInterpreter, [eventName, data]);
        }
        else
        {
            // Compiled path - use direct emit
            EmitDirect(eventName, data);
        }
    }

    /// <summary>
    /// Processes any pending callbacks queued for the main thread.
    /// Call this periodically from the main thread in console applications
    /// to receive Worker messages and events.
    /// </summary>
    /// <remarks>
    /// In GUI applications with a SynchronizationContext (WinForms, WPF),
    /// callbacks are automatically marshaled to the UI thread.
    /// In console applications, you must call this method to process events.
    /// </remarks>
    /// <example>
    /// <code>
    /// var worker = new Worker('./worker.ts');
    /// worker.on('message', (data) => console.log(data));
    ///
    /// // In console app main loop:
    /// while (worker.IsRunning) {
    ///     worker.ProcessPendingCallbacks();
    ///     Thread.Sleep(10); // Small delay to avoid busy-wait
    /// }
    /// </code>
    /// </example>
    public void ProcessPendingCallbacks()
    {
        // Process all queued messages first
        DeliverMessagesToParent();

        // Then process any pending callbacks
        while (_pendingCallbacks.TryDequeue(out var callback))
        {
            callback();
        }
    }

    /// <summary>
    /// Posts a message from the parent to the worker.
    /// </summary>
    public void PostMessage(object? message, SharpTSArray? transfer = null)
    {
        if (_isTerminated)
            return;

        try
        {
            var cloned = StructuredClone.Clone(message, transfer);
            _parentToWorkerQueue.Add(new SharpTSMessagePort.ClonedMessage(cloned, transfer));
        }
        catch (StructuredClone.DataCloneError e)
        {
            throw new Exception($"Failed to clone message: {e.Message}");
        }
        catch (InvalidOperationException)
        {
            // Queue was completed - worker is terminating
        }
    }

    /// <summary>
    /// Terminates the worker.
    /// </summary>
    public SharpTSPromise Terminate()
    {
        if (_isTerminated)
        {
            return SharpTSPromise.Resolve((double)0);
        }

        _isTerminated = true;
        _cts.Cancel();
        _parentToWorkerQueue.CompleteAdding();

        var task = Task.Run<object?>(() =>
        {
            _thread.Join(5000); // Wait up to 5 seconds
            return (double)0;
        });
        return new SharpTSPromise(task);
    }

    /// <summary>
    /// Gets a reference to the worker.
    /// </summary>
    public SharpTSWorker Ref()
    {
        return this;
    }

    /// <summary>
    /// Releases the worker reference.
    /// </summary>
    public void Unref()
    {
        // In our implementation, this is a no-op since we don't track refs
    }

    /// <summary>
    /// Gets a member (method or property) by name.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            "threadId" => ThreadId,

            "postMessage" => new BuiltInMethod("postMessage", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("postMessage requires at least one argument");
                var transfer = args.Count > 1 ? args[1] as SharpTSArray : null;
                PostMessage(args[0], transfer);
                return null;
            }),

            "terminate" => new BuiltInMethod("terminate", 0, (interp, recv, args) => Terminate()),

            "ref" => new BuiltInMethod("ref", 0, (interp, recv, args) => Ref()),

            "unref" => new BuiltInMethod("unref", 0, (interp, recv, args) =>
            {
                Unref();
                return this;
            }),

            // Inherit EventEmitter methods for on/once/emit
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Checks for pending messages from parent (called by worker thread).
    /// </summary>
    internal bool TryReceiveMessage(out SharpTSMessagePort.ClonedMessage? message, int timeoutMs = 0)
    {
        if (timeoutMs <= 0)
        {
            return _parentToWorkerQueue.TryTake(out message);
        }
        return _parentToWorkerQueue.TryTake(out message, timeoutMs);
    }

    /// <summary>
    /// Gets whether the worker is terminated.
    /// </summary>
    internal bool IsTerminated => _isTerminated;

    /// <summary>
    /// Gets the cancellation token for the worker.
    /// </summary>
    internal CancellationToken CancellationToken => _cts.Token;

    public void Dispose()
    {
        Terminate().Task.Wait(1000);
        _cts.Dispose();
        _parentToWorkerQueue.Dispose();
        _workerToParentQueue.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"Worker {{ threadId: {ThreadId} }}";
}

/// <summary>
/// MessagePort-like object for worker to communicate with parent.
/// </summary>
internal class WorkerParentPort : SharpTSEventEmitter, ITypeCategorized
{
    private readonly SharpTSWorker _worker;

    public TypeCategory RuntimeCategory => TypeCategory.EventEmitter;

    public WorkerParentPort(SharpTSWorker worker)
    {
        _worker = worker;
    }

    public void PostMessage(object? message, SharpTSArray? transfer = null)
    {
        _worker.PostMessageToParent(message, transfer);
    }

    public new object? GetMember(string name)
    {
        return name switch
        {
            "postMessage" => new BuiltInMethod("postMessage", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("postMessage requires at least one argument");
                var transfer = args.Count > 1 ? args[1] as SharpTSArray : null;
                PostMessage(args[0], transfer);
                return null;
            }),

            // Inherit EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "MessagePort { parentPort }";
}

/// <summary>
/// Handles message delivery on the worker thread.
/// </summary>
internal class WorkerMessageHandler
{
    private readonly SharpTSWorker _worker;
    private readonly Interpreter _interpreter;
    private Timer? _pollTimer;

    public WorkerMessageHandler(SharpTSWorker worker, Interpreter interpreter)
    {
        _worker = worker;
        _interpreter = interpreter;
    }

    public void Start()
    {
        // Poll for messages periodically
        _pollTimer = new Timer(PollMessages, null, 10, 10);
    }

    public void Stop()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void PollMessages(object? state)
    {
        if (_worker.IsTerminated)
            return;

        while (_worker.TryReceiveMessage(out var message))
        {
            if (message == null)
                continue;

            try
            {
                // Get the parentPort and emit message event
                if (_interpreter.Environment.TryGet("parentPort", out var portObj) &&
                    portObj is WorkerParentPort parentPort)
                {
                    var eventData = new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["data"] = message.Data
                    });

                    // Emit on parentPort
                    var emitMethod = parentPort.GetMember("emit") as BuiltInMethod;
                    emitMethod?.Call(_interpreter, ["message", eventData]);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Worker message handler error: {ex.Message}");
            }
        }
    }
}

/// <summary>
/// Static helper for worker_threads module functionality.
/// </summary>
public static class WorkerThreads
{
    /// <summary>
    /// Gets whether the current thread is the main thread.
    /// </summary>
    public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == 1 ||
                                       !Thread.CurrentThread.Name?.StartsWith("SharpTS-Worker-") == true;

    /// <summary>
    /// Gets the current thread's ID.
    /// </summary>
    public static double ThreadId => Thread.CurrentThread.ManagedThreadId;

    /// <summary>
    /// Synchronously receives a message from a MessagePort.
    /// </summary>
    public static object? ReceiveMessageOnPort(SharpTSMessagePort port)
    {
        return port.ReceiveMessageSync();
    }

    /// <summary>
    /// Creates a worker_threads module exports object.
    /// </summary>
    public static SharpTSObject CreateModuleExports()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["Worker"] = new WorkerConstructor(),
            ["isMainThread"] = IsMainThread,
            ["threadId"] = ThreadId,
            ["workerData"] = null, // Set in worker context
            ["parentPort"] = null, // Set in worker context
            ["MessageChannel"] = new MessageChannelConstructor(),
            ["MessagePort"] = null, // Can't construct directly
            ["receiveMessageOnPort"] = new BuiltInMethod("receiveMessageOnPort", 1, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not SharpTSMessagePort port)
                    throw new Exception("receiveMessageOnPort requires a MessagePort argument");
                return ReceiveMessageOnPort(port);
            }),
        });
    }
}

/// <summary>
/// Constructor for Worker class.
/// </summary>
internal class WorkerConstructor : ISharpTSCallable
{
    public int Arity() => 1;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0 || arguments[0] is not string filename)
            throw new Exception("Worker constructor requires a filename argument");

        var options = arguments.Count > 1 ? arguments[1] as SharpTSObject : null;
        return new SharpTSWorker(filename, options, interpreter);
    }
}

/// <summary>
/// Constructor for MessageChannel class.
/// </summary>
internal class MessageChannelConstructor : ISharpTSCallable
{
    public int Arity() => 0;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        return new SharpTSMessageChannel();
    }
}
