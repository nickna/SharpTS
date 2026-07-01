using System.Collections.Concurrent;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Global cluster state manager. Acts as the cluster module's singleton that
/// tracks all workers and emits cluster-level events (fork, online, disconnect, exit, message).
/// Extends EventEmitter so that cluster.on('exit', ...) etc. work.
/// </summary>
public class ClusterSingleton : SharpTSEventEmitter
{
    public static readonly ClusterSingleton Instance = new();

    private readonly ConcurrentDictionary<double, SharpTSClusterWorker> _workers = new();
    private string? _entryScript;
    private SharpTSObject? _settings;

    /// <summary>
    /// Registry for shared TCP/HTTP listeners used by cluster port sharing.
    /// </summary>
    public SharedListenerRegistry SharedListeners { get; } = new();

    private ClusterSingleton() { }

    /// <summary>
    /// Resets the singleton state. Used in tests to prevent cross-test interference.
    /// </summary>
    public void Reset()
    {
        SharedListeners.CloseAll();
        foreach (var kvp in _workers)
        {
            try { kvp.Value.Dispose(); } catch { }
        }
        _workers.Clear();
        _entryScript = null;
        _settings = null;
    }

    /// <summary>
    /// Sets the entry script path. Called once when the cluster module is first loaded.
    /// </summary>
    public void SetEntryScript(string path)
    {
        _entryScript ??= path;
    }

    /// <summary>
    /// Forks a new worker that re-executes the entry script.
    /// </summary>
    public SharpTSClusterWorker Fork(Dictionary<string, object?>? env, Interpreter? interpreter)
    {
        if (_entryScript == null)
            throw new Exception("Runtime Error: cluster.fork() called but no entry script is set");

        var worker = new SharpTSClusterWorker(_entryScript, env, interpreter);
        _workers[worker.Id] = worker;

        // Emit 'fork' event on cluster
        EmitWorkerEvent("fork", worker);

        return worker;
    }

    /// <summary>
    /// Disconnects all workers.
    /// </summary>
    public void DisconnectAll(object? callback = null)
    {
        foreach (var kvp in _workers)
        {
            if (!kvp.Value.IsDead())
            {
                kvp.Value.Disconnect();
            }
        }

        // If callback provided, invoke it after disconnect
        if (callback is ISharpTSCallable callable)
        {
            callable.Call(null!, []);
        }
    }

    /// <summary>
    /// Stores settings from setupPrimary/setupMaster.
    /// Accepts SharpTSObject (interpreter) or Dictionary (compiled).
    /// </summary>
    public void SetupPrimary(object? settings)
    {
        if (settings is SharpTSObject obj)
            _settings = obj;
        else if (settings is Dictionary<string, object?> dict)
            _settings = new SharpTSObject(dict);
        else
            _settings = new SharpTSObject(new Dictionary<string, object?>());
    }

    /// <summary>
    /// Gets the workers dictionary as a SharpTSObject for TS access.
    /// </summary>
    public SharpTSObject GetWorkersObject()
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kvp in _workers)
        {
            dict[kvp.Key.ToString("0")] = kvp.Value;
        }
        return new SharpTSObject(dict);
    }

    /// <summary>
    /// Gets the current settings object.
    /// </summary>
    public SharpTSObject? GetSettings() => _settings;

    /// <summary>
    /// Removes a worker from the registry (called when a worker exits).
    /// </summary>
    public void RemoveWorker(double id)
    {
        _workers.TryRemove(id, out _);
    }

    /// <summary>
    /// Emits an event on the cluster singleton from a specific worker.
    /// Used internally by workers to bubble events up to the cluster.
    /// </summary>
    internal void EmitWorkerEvent(string eventName, SharpTSClusterWorker worker, params object?[] extraArgs)
    {
        var args = new List<object?> { eventName, worker };
        args.AddRange(extraArgs);

        // Try interpreter-based emit first, fallback to direct
        var emitMethod = GetMember("emit") as BuiltInMethod;
        if (emitMethod != null)
        {
            try
            {
                emitMethod.Call(null!, args);
            }
            catch
            {
                // Fallback to direct emit if no interpreter available
                var directArgs = new object?[extraArgs.Length + 1];
                directArgs[0] = worker;
                Array.Copy(extraArgs, 0, directArgs, 1, extraArgs.Length);
                EmitDirect(eventName, directArgs);
            }
        }
        else
        {
            var directArgs = new object?[extraArgs.Length + 1];
            directArgs[0] = worker;
            Array.Copy(extraArgs, 0, directArgs, 1, extraArgs.Length);
            EmitDirect(eventName, directArgs);
        }
    }

    /// <summary>
    /// Gets a member for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "isPrimary" => ClusterContext.IsPrimary,
            "isWorker" => ClusterContext.IsWorker,
            "isMaster" => ClusterContext.IsPrimary,

            "fork" => BuiltInMethod.CreateV2("fork", 0, 1, (interp, _, args) =>
            {
                Dictionary<string, object?>? env = null;
                if (args.Length > 0 && args[0].ToObject() is SharpTSObject envObj)
                {
                    env = new Dictionary<string, object?>();
                    foreach (var key in envObj.PropertyNames)
                    {
                        env[key] = envObj.GetProperty(key);
                    }
                }
                return RuntimeValue.FromBoxed(Fork(env, interp));
            }),

            "disconnect" => BuiltInMethod.CreateV2("disconnect", 0, 1, (_, _, args) =>
            {
                DisconnectAll(args.Length > 0 ? args[0].ToObject() : null);
                return RuntimeValue.Null;
            }),

            "setupPrimary" => BuiltInMethod.CreateV2("setupPrimary", 0, 1, (_, _, args) =>
            {
                SetupPrimary(args.Length > 0 ? args[0].ToObject() as SharpTSObject : null);
                return RuntimeValue.Null;
            }),

            "setupMaster" => BuiltInMethod.CreateV2("setupMaster", 0, 1, (_, _, args) =>
            {
                SetupPrimary(args.Length > 0 ? args[0].ToObject() as SharpTSObject : null);
                return RuntimeValue.Null;
            }),

            "workers" => GetWorkersObject(),
            "worker" => null, // Only valid in worker context (handled by module interpreter)
            "settings" => _settings,

            // Inherit EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object cluster]";
}
