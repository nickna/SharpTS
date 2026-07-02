using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter implementation of the Node.js 'cluster' module.
/// Provides multi-process-like patterns using threads, following the worker_threads model.
/// </summary>
public static class ClusterModuleInterpreter
{
    /// <summary>
    /// Gets all exports for the cluster module.
    /// </summary>
    /// <remarks>
    /// cluster.workers and cluster.settings are stable objects mutated in place by
    /// <see cref="ClusterSingleton"/>, so import-time bindings stay live (#1167) — the
    /// same trick Node's own cluster module uses. isPrimary/isWorker/worker are
    /// thread-constant (a worker thread imports its own copy with ClusterContext already
    /// established), so import-time values are correct; the namespace object built by
    /// <see cref="CreateNamespaceObject"/> additionally resolves them through live accessors.
    /// </remarks>
    public static Dictionary<string, object?> GetExports()
    {
        var singleton = ClusterSingleton.Instance;

        return new Dictionary<string, object?>
        {
            ["isPrimary"] = ClusterContext.IsPrimary,
            ["isWorker"] = ClusterContext.IsWorker,
            ["isMaster"] = ClusterContext.IsPrimary,

            // Live views (stable object identity, mutated in place). Node exposes
            // cluster.workers only in the primary.
            ["workers"] = ClusterContext.IsWorker ? SharpTSUndefined.Instance : singleton.GetWorkersObject(),

            // Current worker reference (non-null in worker context)
            ["worker"] = ClusterContext.CurrentWorker,

            // Settings (live; normalized by setupPrimary/fork)
            ["settings"] = singleton.GetSettings(),

            // Scheduling policy (#1170)
            ["SCHED_NONE"] = (double)ClusterSingleton.SchedNone,
            ["SCHED_RR"] = (double)ClusterSingleton.SchedRR,
            ["schedulingPolicy"] = (double)singleton.SchedulingPolicy,

            // Methods
            ["fork"] = BuiltInMethod.CreateV2("fork", 0, 1, Fork),
            ["disconnect"] = BuiltInMethod.CreateV2("disconnect", 0, 1, Disconnect),
            ["setupPrimary"] = BuiltInMethod.CreateV2("setupPrimary", 0, 1, SetupPrimary),
            ["setupMaster"] = BuiltInMethod.CreateV2("setupMaster", 0, 1, SetupPrimary),

            // EventEmitter methods — delegate to singleton
            ["on"] = singleton.GetMember("on")!,
            ["once"] = singleton.GetMember("once")!,
            ["off"] = singleton.GetMember("off")!,
            ["addListener"] = singleton.GetMember("addListener")!,
            ["removeListener"] = singleton.GetMember("removeListener")!,
            ["emit"] = singleton.GetMember("emit")!,
            ["removeAllListeners"] = singleton.GetMember("removeAllListeners")!,
            ["listeners"] = singleton.GetMember("listeners")!,
            ["listenerCount"] = singleton.GetMember("listenerCount")!,
            ["eventNames"] = singleton.GetMember("eventNames")!,
        };
    }

    /// <summary>
    /// Builds the cluster module's namespace/default-export object: a SharpTSObject whose
    /// isPrimary/isWorker/isMaster/worker/workers/settings/schedulingPolicy members are
    /// live accessors over <see cref="ClusterSingleton"/>/<see cref="ClusterContext"/> —
    /// so `cluster.schedulingPolicy = cluster.SCHED_NONE` reaches the singleton and is
    /// honored by connection dispatch (#1170), and context resolves live (#1167).
    /// </summary>
    public static SharpTSObject CreateNamespaceObject(Dictionary<string, object?> exports)
    {
        var singleton = ClusterSingleton.Instance;

        var fields = new Dictionary<string, object?>(exports);
        foreach (var accessorBacked in (string[])["isPrimary", "isWorker", "isMaster", "worker", "workers", "settings", "schedulingPolicy"])
            fields.Remove(accessorBacked);

        var ns = new SharpTSObject(fields);
        ns.DefineGetter("isPrimary", Getter("isPrimary", () => ClusterContext.IsPrimary));
        ns.DefineGetter("isMaster", Getter("isMaster", () => ClusterContext.IsPrimary));
        ns.DefineGetter("isWorker", Getter("isWorker", () => ClusterContext.IsWorker));
        ns.DefineGetter("worker", Getter("worker", () => ClusterContext.CurrentWorker));
        ns.DefineGetter("workers", Getter("workers", () =>
            ClusterContext.IsWorker ? SharpTSUndefined.Instance : singleton.GetWorkersObject()));
        ns.DefineGetter("settings", Getter("settings", () => singleton.GetSettings()));
        ns.DefineGetter("schedulingPolicy", Getter("schedulingPolicy", () => (double)singleton.SchedulingPolicy));
        ns.DefineSetter("schedulingPolicy", BuiltInMethod.CreateV2("schedulingPolicy", 1, (_, _, args) =>
        {
            if (args.Length > 0 && args[0].ToObject() is double policy)
                singleton.SchedulingPolicy = (int)policy;
            return RuntimeValue.Null;
        }));
        return ns;
    }

    private static BuiltInMethod Getter(string name, Func<object?> read) =>
        BuiltInMethod.CreateV2(name, 0, (_, _, _) => RuntimeValue.FromBoxed(read()));

    /// <summary>
    /// cluster.fork(env?) — spawns a new worker that re-executes the entry script.
    /// </summary>
    private static RuntimeValue Fork(Execution.Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // Capture entry script from interpreter
        var entryPath = interpreter.EntryModulePath;
        if (entryPath == null)
            throw new Exception("Runtime Error: cluster.fork() cannot determine entry script path");

        ClusterSingleton.Instance.SetEntryScript(entryPath);

        Dictionary<string, object?>? env = null;
        if (args.Length > 0 && args[0].ToObject() is SharpTSObject envObj)
        {
            env = new Dictionary<string, object?>();
            foreach (var key in envObj.PropertyNames)
            {
                env[key] = envObj.GetProperty(key);
            }
        }

        return RuntimeValue.FromBoxed(ClusterSingleton.Instance.Fork(env, interpreter));
    }

    /// <summary>
    /// cluster.disconnect(callback?) — disconnects all workers.
    /// </summary>
    private static RuntimeValue Disconnect(Execution.Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        ClusterSingleton.Instance.DisconnectAll(args.Length > 0 ? args[0].ToObject() : null, interpreter);
        return RuntimeValue.Null;
    }

    /// <summary>
    /// cluster.setupPrimary(settings?) — merges settings and emits 'setup'.
    /// </summary>
    private static RuntimeValue SetupPrimary(Execution.Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        ClusterSingleton.Instance.SetupPrimary(args.Length > 0 ? args[0].ToObject() : null, interpreter);
        return RuntimeValue.Null;
    }
}
