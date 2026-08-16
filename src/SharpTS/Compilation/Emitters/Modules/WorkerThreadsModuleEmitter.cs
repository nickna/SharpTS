using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'worker_threads' module.
/// Handles Worker, MessageChannel, isMainThread, threadId, etc.
/// </summary>
public sealed class WorkerThreadsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "worker_threads";

    private static readonly string[] _exportedMembers =
    [
        "Worker", "MessageChannel", "MessagePort", "BroadcastChannel",
        "isMainThread", "threadId", "workerData", "parentPort",
        "receiveMessageOnPort", "getEnvironmentData", "setEnvironmentData",
        "markAsUntransferable"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return methodName switch
        {
            "receiveMessageOnPort" => EmitReceiveMessageOnPort(emitter, arguments),
            "getEnvironmentData" => EmitGetEnvironmentData(emitter, arguments),
            "setEnvironmentData" => EmitSetEnvironmentData(emitter, arguments),
            "markAsUntransferable" => EmitMarkAsUntransferable(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "isMainThread" => EmitIsMainThread(emitter),
            "threadId" => EmitThreadId(emitter),
            "workerData" => EmitWorkerData(emitter),
            "parentPort" => EmitParentPort(emitter),
            "Worker" => EmitWorkerConstructor(emitter),
            "MessageChannel" => EmitMessageChannelConstructor(emitter),
            "MessagePort" => EmitMessagePortConstructor(emitter),
            "BroadcastChannel" => EmitBroadcastChannelConstructorRef(emitter),
            _ => false
        };
    }

    private static bool EmitBroadcastChannelConstructorRef(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Load the $BroadcastChannel Type so callers can recognize the constructor reference.
        // Actual `new BroadcastChannel(...)` is wired through TryEmitBuiltInConstructor.
        il.Emit(OpCodes.Ldtoken, ctx.Runtime!.BroadcastChannelType);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle", ctx.Types.RuntimeTypeHandle));
        return true;
    }

    private static bool EmitIsMainThread(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Call, ctx.Runtime!.WorkerThreadsIsMainThread);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitThreadId(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Call, ctx.Runtime!.WorkerThreadsThreadId);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitWorkerData(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // workerData is only defined in worker context, and worker child scripts
        // always run under the interpreter (SharpTSWorker.RunWorkerScript news up an
        // Interpreter), which binds the cloned workerData via env.Define. This
        // compiled path is therefore only ever reached on the MAIN thread, where
        // Node's workerData is null — so null is the correct value here (#380).
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitParentPort(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // null is the CORRECT value here, not a stub (#1109). parentPort is the worker→parent port and
        // is null on the main thread in Node. A worker's body never runs as compiled code: even when the
        // main program is compiled, SharpTSWorker.RunWorkerScript spins up a fresh INTERPRETER for the
        // worker file, and that interpreter binds the real WorkerParentPort via SetupWorkerGlobals. So
        // this compiled path is only ever reached on the MAIN thread — exactly the EmitWorkerData case —
        // where parentPort is null. Wiring a "$MessagePort" would be wrong (none exists in compiled mode),
        // and throwing would break the canonical `if (parentPort) { ... }` main-thread guard.
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitWorkerConstructor(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Load the Worker constructor as a callable
        // This is used for: const Worker = require('worker_threads').Worker
        // Then: new Worker(...)
        // We return a special marker that the new expression handler recognizes
        il.Emit(OpCodes.Ldtoken, ctx.Runtime!.TSWorkerType);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle", ctx.Types.RuntimeTypeHandle));
        return true;
    }

    private static bool EmitMessageChannelConstructor(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Load the MessageChannel constructor type
        il.Emit(OpCodes.Ldtoken, ctx.Runtime!.TSMessageChannelType);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle", ctx.Types.RuntimeTypeHandle));
        return true;
    }

    private static bool EmitMessagePortConstructor(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // MessagePort cannot be constructed directly, return null
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitReceiveMessageOnPort(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 1) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Call, ctx.Runtime!.WorkerThreadsReceiveMessageOnPort);
        return true;
    }

    private static bool EmitGetEnvironmentData(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Reaching the C# WorkerEnvironmentData store needs SharpTS.dll co-located (#1000).
        ctx.Runtime!.RequireSharpTSRuntime("worker_threads.getEnvironmentData");

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.WorkerThreadsGetEnvironmentData);

        // Map absent (CLR null) to JS undefined — stored values are never null (a null/undefined
        // set deletes the key), so null here means "not present".
        var result = il.DeclareLocal(ctx.Types.Object);
        var notNull = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(notNull);
        il.Emit(OpCodes.Ldloc, result);
        il.MarkLabel(done);
        return true;
    }

    private static bool EmitSetEnvironmentData(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        ctx.Runtime!.RequireSharpTSRuntime("worker_threads.setEnvironmentData");

        // key
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        // value (absent → null, which deletes the key)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.WorkerThreadsSetEnvironmentData);

        // setEnvironmentData returns undefined.
        il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
        return true;
    }

    private static bool EmitMarkAsUntransferable(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Reaching the C# StructuredClone registry needs SharpTS.dll co-located (#1002).
        ctx.Runtime!.RequireSharpTSRuntime("worker_threads.markAsUntransferable");

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.WorkerThreadsMarkAsUntransferable);

        // markAsUntransferable returns undefined.
        il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
        return true;
    }
}
