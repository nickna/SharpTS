using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Late-bound entry points for the compiled 'cluster' module. The emitted
/// $Runtime.ClusterFork/ClusterInvoke helpers resolve this type via
/// <c>Type.GetType("SharpTS.Runtime.Types.ClusterCompiledBridge, SharpTS")</c> —
/// keeping the standalone DLL free of a hard SharpTS.dll reference — and route the
/// whole module surface through <see cref="ClusterSingleton"/>, so a compiled primary
/// and its interpreted workers share one coherent cluster state (workers map, settings,
/// scheduling policy, events). Workers run the original entry script interpreted on a
/// thread, exactly like the worker_threads compiled bridge (#354).
/// </summary>
public static class ClusterCompiledBridge
{
    /// <summary>
    /// cluster.fork(env?) for a compiled primary. The entry-script path is baked in at
    /// compile time; the worker interprets that source, so it must exist at runtime
    /// (the same contract as compiled `new Worker('./x.ts')` / child_process.fork).
    /// The emitted $EventLoop's Ref/Unref/Schedule delegates keep the compiled loop
    /// alive and marshal worker events onto it.
    /// </summary>
    public static object Fork(string? entryPath, object? env,
        Action loopRef, Action loopUnref, Action<Action> loopSchedule)
    {
        var resolved = ResolveEntryScript(entryPath);
        ClusterSingleton.Instance.SetEntryScript(resolved);

        Dictionary<string, object?>? envDict = env as Dictionary<string, object?>;
        return ClusterSingleton.Instance.Fork(envDict, interpreter: null, loopRef, loopUnref, loopSchedule);
    }

    private static string ResolveEntryScript(string? entryPath)
    {
        if (string.IsNullOrEmpty(entryPath))
            throw new InvalidOperationException(
                "cluster.fork() cannot determine the entry script for this compiled program.");

        if (File.Exists(entryPath))
            return Path.GetFullPath(entryPath);

        // The program may have been deployed elsewhere — look for the entry source
        // next to the compiled output.
        var coLocated = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(entryPath));
        if (File.Exists(coLocated))
            return coLocated;

        throw new InvalidOperationException(
            $"cluster.fork() in a compiled program runs workers by interpreting the original entry script, " +
            $"but it was not found at '{entryPath}' or next to the output. " +
            "Deploy the TypeScript source alongside the compiled program.");
    }

    /// <summary>
    /// Dispatch for the rest of the compiled cluster module surface. One late-bound
    /// entry point keeps the emitted reflection plumbing small; members map directly
    /// onto <see cref="ClusterSingleton"/>. Returns compiled-friendly shapes
    /// (Dictionary / List / double) — never interpreter-only types.
    /// </summary>
    public static object? Invoke(string member, object?[] args)
    {
        var cluster = ClusterSingleton.Instance;
        switch (member)
        {
            case "setupPrimary":
                cluster.SetupPrimary(args.Length > 0 ? args[0] : null);
                return null;

            case "disconnect":
                cluster.DisconnectAll(args.Length > 0 ? args[0] : null);
                return null;

            case "workers":
                return cluster.GetWorkersDictionary();

            case "settings":
                // Normalize on first read so compiled code sees Node's defaults even
                // before any setupPrimary/fork (matches the interpreter's live object).
                return cluster.GetSettingsDictionary();

            case "worker":
                // The compiled primary's main thread is never a cluster worker
                // (workers run interpreted); live for symmetry with the interp module.
                return ClusterContext.CurrentWorker;

            case "getSchedulingPolicy":
                return (double)cluster.SchedulingPolicy;

            case "setSchedulingPolicy":
                if (args.Length > 0 && args[0] is double policy)
                    cluster.SchedulingPolicy = (int)policy;
                return null;

            case "on" or "addListener":
                cluster.AddListenerDirect(RequireEventName(args), args[1]!);
                return null;

            case "once":
                cluster.AddListenerDirect(RequireEventName(args), args[1]!, once: true);
                return null;

            case "off" or "removeListener":
                cluster.RemoveListenerDirect(RequireEventName(args), args[1]!);
                return null;

            case "emit":
                return cluster.EmitDirect(RequireEventName(args), args[1..]);

            case "removeAllListeners":
                CallEmitterBuiltin(cluster, "removeAllListeners", args.Length > 0 ? [args[0]] : []);
                return null;

            case "listeners":
                return ToList(CallEmitterBuiltin(cluster, "listeners", [RequireEventName(args)]));

            case "listenerCount":
                return CallEmitterBuiltin(cluster, "listenerCount", [RequireEventName(args)]);

            case "eventNames":
                return ToList(CallEmitterBuiltin(cluster, "eventNames", []));

            default:
                throw new InvalidOperationException($"Unknown cluster member: {member}");
        }
    }

    private static string RequireEventName(object?[] args)
    {
        if (args.Length == 0 || args[0] is not string name)
            throw new InvalidOperationException("cluster event methods require an event name");
        return name;
    }

    private static object? CallEmitterBuiltin(ClusterSingleton cluster, string name, List<object?> args)
    {
        var method = (BuiltInMethod)cluster.GetMember(name)!;
        return method.Call(null!, args);
    }

    /// <summary>Converts an interpreter SharpTSArray result to a compiled List shape.</summary>
    private static List<object?>? ToList(object? value) =>
        value is SharpTSArray arr ? new List<object?>(arr) : value as List<object?>;
}
