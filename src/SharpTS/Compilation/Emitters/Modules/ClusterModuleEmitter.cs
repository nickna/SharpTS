using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'cluster' module.
///
/// The compiled module late-binds into SharpTS.dll via $Runtime.ClusterFork /
/// $Runtime.ClusterInvoke (→ ClusterCompiledBridge): fork() runs the original entry
/// script interpreted on a worker thread (the worker_threads pattern), and the rest of
/// the surface routes to the same ClusterSingleton those workers use — one coherent
/// workers map / settings / scheduling policy / event stream across the boundary.
/// Every bridge-routed emit site records RequireSharpTSRuntime("cluster") so the CLI
/// co-locates SharpTS.dll; under --standalone the helpers throw a clear error (#1171).
///
/// isPrimary/isWorker/isMaster and the SCHED_* constants stay pure IL: a compiled
/// program's threads are never cluster workers (workers run interpreted), so
/// isPrimary is constantly true in compiled code.
/// </summary>
public sealed class ClusterModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "cluster";

    private static readonly string[] _exportedMembers =
    [
        "isPrimary", "isWorker", "isMaster",
        "fork", "disconnect", "setupPrimary", "setupMaster",
        "workers", "worker", "settings",
        "schedulingPolicy", "SCHED_NONE", "SCHED_RR",
        "on", "once", "off", "emit", "removeAllListeners",
        "addListener", "removeListener",
        "listeners", "listenerCount", "eventNames"
    ];

    private static readonly HashSet<string> _properties =
    [
        "isPrimary", "isWorker", "isMaster",
        "workers", "worker", "settings",
        "schedulingPolicy", "SCHED_NONE", "SCHED_RR"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool IsExportedProperty(string memberName) => _properties.Contains(memberName);

    // schedulingPolicy is mutable runtime state — reads must hit the singleton at each
    // access site, not the import-time namespace-dict snapshot.
    public bool HasLivePropertyGet(string memberName) => memberName == "schedulingPolicy";

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "fork" => EmitFork(emitter, arguments),
            "disconnect" => EmitInvoke(emitter, "disconnect", arguments),
            "setupPrimary" or "setupMaster" => EmitInvoke(emitter, "setupPrimary", arguments),
            "on" or "addListener" => EmitInvoke(emitter, "on", arguments),
            "once" => EmitInvoke(emitter, "once", arguments),
            "off" or "removeListener" => EmitInvoke(emitter, "off", arguments),
            "emit" => EmitInvoke(emitter, "emit", arguments),
            "removeAllListeners" => EmitInvoke(emitter, "removeAllListeners", arguments),
            "listeners" => EmitInvoke(emitter, "listeners", arguments),
            "listenerCount" => EmitInvoke(emitter, "listenerCount", arguments),
            "eventNames" => EmitInvoke(emitter, "eventNames", arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return propertyName switch
        {
            "isPrimary" or "isMaster" => EmitBoolConstant(emitter, true),
            "isWorker" => EmitBoolConstant(emitter, false),
            "SCHED_NONE" => EmitNumberConstant(emitter, 1),
            "SCHED_RR" => EmitNumberConstant(emitter, 2),
            "workers" => EmitInvoke(emitter, "workers", []),
            "worker" => EmitInvoke(emitter, "worker", []),
            "settings" => EmitInvoke(emitter, "settings", []),
            "schedulingPolicy" => EmitInvoke(emitter, "getSchedulingPolicy", []),
            // Methods emitted as null for the namespace dict — actual calls go through TryEmitMethodCall
            "fork" or "disconnect" or "setupPrimary" or "setupMaster"
                or "on" or "once" or "off" or "emit" or "removeAllListeners"
                or "addListener" or "removeListener"
                or "listeners" or "listenerCount" or "eventNames" => EmitNull(emitter),
            _ => false
        };
    }

    /// <summary>
    /// cluster.schedulingPolicy = value — routes the write to the singleton so the
    /// shared-listener dispatch honors it (#1170). Leaves the assigned value on the
    /// stack (assignment-expression semantics).
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, string propertyName, Expr value)
    {
        if (propertyName != "schedulingPolicy")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;
        ctx.Runtime!.RequireSharpTSRuntime("cluster");

        emitter.EmitExpression(value);
        emitter.EmitBoxIfNeeded(value);
        var valueLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Ldstr, "setSchedulingPolicy");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, ctx.Runtime!.ClusterInvoke);
        il.Emit(OpCodes.Pop); // discard the null result

        il.Emit(OpCodes.Ldloc, valueLocal); // assignment expression value
        return true;
    }

    private static bool EmitNull(IEmitterContext emitter)
    {
        emitter.Context.IL.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitBoolConstant(IEmitterContext emitter, bool value)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitNumberConstant(IEmitterContext emitter, double value)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Ldc_R8, value);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    /// <summary>
    /// cluster.fork(env?) → $Runtime.ClusterFork(env) — spawns an interpreted worker
    /// bound to the compiled $EventLoop.
    /// </summary>
    private static bool EmitFork(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        ctx.Runtime!.RequireSharpTSRuntime("cluster");

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.ClusterFork);
        return true;
    }

    /// <summary>
    /// Everything else → $Runtime.ClusterInvoke(member, [boxed args]).
    /// </summary>
    private static bool EmitInvoke(IEmitterContext emitter, string member, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        ctx.Runtime!.RequireSharpTSRuntime("cluster");

        il.Emit(OpCodes.Ldstr, member);
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.ClusterInvoke);
        return true;
    }
}
