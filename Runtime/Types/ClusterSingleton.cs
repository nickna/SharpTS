using System.Collections.Concurrent;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Global cluster state manager. Acts as the cluster module's singleton that
/// tracks all workers and emits cluster-level events (fork, online, disconnect, exit,
/// message, listening, setup).
/// Extends EventEmitter so that cluster.on('exit', ...) etc. work.
/// </summary>
public class ClusterSingleton : SharpTSEventEmitter
{
    public static readonly ClusterSingleton Instance = new();

    /// <summary>cluster.SCHED_NONE — leave scheduling to the OS (approximated as arbitrary pick).</summary>
    public const int SchedNone = 1;

    /// <summary>cluster.SCHED_RR — round-robin connection distribution.</summary>
    public const int SchedRR = 2;

    private readonly ConcurrentDictionary<double, SharpTSClusterWorker> _workers = new();
    private string? _entryScript;

    // Live views (#1167): cluster.workers and cluster.settings are single stable objects
    // mutated in place — exactly how Node's cluster module keeps them live across
    // import-time snapshot bindings. The SharpTSObject wraps the dictionary by
    // reference, so the same backing store serves interpreter property reads and the
    // compiled bridge (which hands the raw dictionary to compiled code as its $Object
    // shape). All runtime mutations must go through SharpTSObject so its descriptor and
    // property-order metadata remain synchronized with that backing store.
    private readonly Dictionary<string, object?> _workersDict = new();
    private readonly SharpTSObject _workersObject;
    private readonly Dictionary<string, object?> _settingsDict = new();
    private readonly SharpTSObject _settingsObject;
    private bool _settingsNormalized;

    private int _schedulingPolicy = DefaultSchedulingPolicy();

    /// <summary>
    /// Registry for shared TCP/HTTP listeners used by cluster port sharing.
    /// </summary>
    public SharedListenerRegistry SharedListeners { get; } = new();

    private ClusterSingleton()
    {
        _workersObject = new SharpTSObject(_workersDict);
        _settingsObject = new SharpTSObject(_settingsDict);
    }

    /// <summary>
    /// cluster.schedulingPolicy — SCHED_RR or SCHED_NONE. Defaults from
    /// NODE_CLUSTER_SCHED_POLICY ('rr'/'none'), else the Node platform default
    /// (SCHED_NONE on Windows, SCHED_RR elsewhere). Read by the shared listeners
    /// at dispatch time.
    /// </summary>
    public int SchedulingPolicy
    {
        get => _schedulingPolicy;
        set => _schedulingPolicy = value == SchedNone ? SchedNone : SchedRR;
    }

    private static int DefaultSchedulingPolicy()
    {
        var env = Environment.GetEnvironmentVariable("NODE_CLUSTER_SCHED_POLICY");
        if (string.Equals(env, "rr", StringComparison.OrdinalIgnoreCase))
            return SchedRR;
        if (string.Equals(env, "none", StringComparison.OrdinalIgnoreCase))
            return SchedNone;
        return OperatingSystem.IsWindows() ? SchedNone : SchedRR;
    }

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
        ClearProperties(_workersObject);
        ClearProperties(_settingsObject);
        _settingsNormalized = false;
        _entryScript = null;
        _schedulingPolicy = DefaultSchedulingPolicy();
        ClearAllListenersInternal();
    }

    /// <summary>
    /// Sets the entry script path. Called once when the cluster module is first loaded.
    /// </summary>
    public void SetEntryScript(string path)
    {
        _entryScript ??= path;
    }

    /// <summary>
    /// Forks a new worker that re-executes the entry script (or cluster.settings.exec).
    /// </summary>
    public SharpTSClusterWorker Fork(Dictionary<string, object?>? env, Interpreter? interpreter)
    {
        return Fork(env, interpreter, loopRef: null, loopUnref: null, loopSchedule: null);
    }

    /// <summary>
    /// Forks a new worker. In compiled mode the parent has no interpreter, so the emitted
    /// $EventLoop's Ref/Unref/Schedule are passed as delegates instead (the worker_threads
    /// CreateForCompiledLoop pattern, #354).
    /// </summary>
    public SharpTSClusterWorker Fork(
        Dictionary<string, object?>? env, Interpreter? interpreter,
        Action? loopRef, Action? loopUnref, Action<Action>? loopSchedule)
    {
        // Node normalizes settings on the first fork if setupPrimary was never called.
        NormalizeSettings(null);

        var script = GetSettingValue("exec") as string;
        if (string.IsNullOrEmpty(script))
            script = _entryScript;
        if (script == null)
            throw new Exception("Runtime Error: cluster.fork() called but no entry script is set");

        var argv = GetSettingValue("args") switch
        {
            SharpTSArray arr => new List<object?>(arr),
            List<object?> list => list,
            _ => null,
        };
        bool silent = GetSettingValue("silent") is bool b && b;

        var worker = new SharpTSClusterWorker(script, env, interpreter, argv, silent,
            loopRef, loopUnref, loopSchedule);
        _workers[worker.Id] = worker;
        _workersObject.SetProperty(worker.Id.ToString("0"), worker);

        // Emit 'fork' event on cluster
        EmitWorkerEvent("fork", worker);

        return worker;
    }

    private object? GetSettingValue(string name) =>
        _settingsDict.TryGetValue(name, out var v) ? v : null;

    /// <summary>
    /// Disconnects all workers.
    /// </summary>
    public void DisconnectAll(object? callback = null, Interpreter? interpreter = null)
    {
        foreach (var kvp in _workers)
        {
            if (!kvp.Value.IsDead())
            {
                kvp.Value.Disconnect();
            }
        }

        // If callback provided, invoke it after disconnect
        if (callback != null)
        {
            RuntimeCallableDispatcher.Invoke(interpreter, callback, []);
        }
    }

    /// <summary>
    /// Stores settings from setupPrimary/setupMaster and emits the 'setup' event.
    /// Accepts SharpTSObject (interpreter) or Dictionary (compiled).
    /// </summary>
    public void SetupPrimary(object? settings, Interpreter? interpreter = null)
    {
        NormalizeSettings(settings);
        EmitClusterEvent(interpreter, "setup", _settingsObject);
    }

    /// <summary>
    /// Merges <paramref name="settings"/> over the current cluster.settings, filling
    /// Node's defaults on first normalization. Mutates the stable settings object in
    /// place so import-time bindings observe the change (#1167/#1170).
    /// </summary>
    private void NormalizeSettings(object? settings)
    {
        if (!_settingsNormalized)
        {
            _settingsNormalized = true;
            // Node defaults: exec = process.argv[1], args = process.argv.slice(2),
            // execArgv = process.execArgv, silent = false. In the thread model the
            // entry script plays the role of argv[1] and there are no exec args.
            SetSettingValue("exec", _entryScript ?? "");
            SetSettingValue("args", new List<object?>());
            SetSettingValue("execArgv", new List<object?>());
            SetSettingValue("silent", false);
            SetSettingValue("serialization", "json");
        }
        else if (_entryScript != null && _settingsDict.TryGetValue("exec", out var exec) && exec as string == "")
        {
            // The entry script became known after an early setupPrimary().
            SetSettingValue("exec", _entryScript);
        }

        switch (settings)
        {
            case SharpTSObject obj:
                foreach (var key in obj.PropertyNames)
                    SetSettingValue(key, obj.GetProperty(key));
                break;
            case Dictionary<string, object?> dict:
                foreach (var (key, value) in dict)
                    SetSettingValue(key, value);
                break;
        }
    }

    private void SetSettingValue(string name, object? value)
        => _settingsObject.SetProperty(name, value);

    private static void ClearProperties(SharpTSObject obj)
    {
        foreach (var key in obj.OwnStringKeys().ToArray())
            obj.DeleteProperty(key);
    }

    /// <summary>
    /// Gets the live workers object (stable identity, mutated on fork/exit) for TS access.
    /// </summary>
    public SharpTSObject GetWorkersObject() => _workersObject;

    /// <summary>
    /// Gets the live workers dictionary — the compiled bridge hands this to compiled
    /// code directly (a compiled $Object IS a Dictionary&lt;string, object?&gt;).
    /// </summary>
    public Dictionary<string, object?> GetWorkersDictionary() => _workersDict;

    /// <summary>
    /// Gets the live settings object (stable identity, repopulated by setupPrimary).
    /// </summary>
    public SharpTSObject GetSettings() => _settingsObject;

    /// <summary>
    /// Gets the live settings dictionary for the compiled bridge.
    /// </summary>
    public Dictionary<string, object?> GetSettingsDictionary() => _settingsDict;

    /// <summary>
    /// Removes a worker from the registry (called when a worker exits).
    /// </summary>
    public void RemoveWorker(double id)
    {
        _workers.TryRemove(id, out _);
        _workersObject.DeleteProperty(id.ToString("0"));
    }

    /// <summary>
    /// Emits an event on the cluster singleton from a specific worker.
    /// Used internally by workers to bubble events up to the cluster. The worker's
    /// parent interpreter (null for a compiled primary) drives listener invocation.
    /// </summary>
    internal void EmitWorkerEvent(string eventName, SharpTSClusterWorker worker, params object?[] extraArgs)
    {
        var args = new object?[extraArgs.Length + 1];
        args[0] = worker;
        Array.Copy(extraArgs, 0, args, 1, extraArgs.Length);
        EmitClusterEvent(worker.ParentInterpreter, eventName, args);
    }

    /// <summary>
    /// Emits a cluster-level event ('setup' has no worker argument, so this is the
    /// general form). With an interpreter, uses the interpreter-aware emit so guest
    /// listeners that need one (console.log) work; without one (compiled primary),
    /// emits directly — RuntimeCallableDispatcher invokes compiled listeners.
    /// </summary>
    internal void EmitClusterEvent(Interpreter? interpreter, string eventName, params object?[] args)
    {
        if (interpreter != null)
            EmitWith(interpreter, eventName, args);
        else
            EmitDirect(eventName, args);
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

            "SCHED_NONE" => (double)SchedNone,
            "SCHED_RR" => (double)SchedRR,
            "schedulingPolicy" => (double)SchedulingPolicy,

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

            "disconnect" => BuiltInMethod.CreateV2("disconnect", 0, 1, (interp, _, args) =>
            {
                DisconnectAll(args.Length > 0 ? args[0].ToObject() : null, interp);
                return RuntimeValue.Null;
            }),

            "setupPrimary" => BuiltInMethod.CreateV2("setupPrimary", 0, 1, (interp, _, args) =>
            {
                SetupPrimary(args.Length > 0 ? args[0].ToObject() : null, interp);
                return RuntimeValue.Null;
            }),

            "setupMaster" => BuiltInMethod.CreateV2("setupMaster", 0, 1, (interp, _, args) =>
            {
                SetupPrimary(args.Length > 0 ? args[0].ToObject() : null, interp);
                return RuntimeValue.Null;
            }),

            "workers" => ClusterContext.IsWorker ? SharpTSUndefined.Instance : GetWorkersObject(),
            "worker" => ClusterContext.CurrentWorker,
            "settings" => _settingsObject,

            // Inherit EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object cluster]";
}
