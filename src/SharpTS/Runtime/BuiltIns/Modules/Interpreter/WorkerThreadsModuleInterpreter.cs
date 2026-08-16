using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter implementation of the worker_threads module.
/// </summary>
/// <remarks>
/// Provides Worker Threads API for parallel execution:
/// - Worker: Execute scripts in separate threads
/// - isMainThread: Check if running on main thread
/// - parentPort: MessagePort for worker-to-parent communication
/// - workerData: Data passed from parent to worker
/// - MessageChannel: Create connected message ports
/// - receiveMessageOnPort: Synchronously receive messages
/// </remarks>
public static class WorkerThreadsModuleInterpreter
{
    /// <summary>
    /// Gets all exports for the worker_threads module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Worker constructor
            ["Worker"] = new WorkerConstructor(),

            // Check if current context is main thread
            ["isMainThread"] = WorkerThreads.IsMainThread,

            // Thread ID of current context
            ["threadId"] = WorkerThreads.ThreadId,

            // In main thread, these are null; in worker context, they're set up by the worker
            ["parentPort"] = null,
            ["workerData"] = null,

            // MessageChannel constructor
            ["MessageChannel"] = new MessageChannelConstructor(),

            // BroadcastChannel constructor
            ["BroadcastChannel"] = new BroadcastChannelConstructor(),

            // Synchronous message receive. Accepts a native MessagePort or a
            // transferred compiled port adopted by this worker (#465).
            ["receiveMessageOnPort"] = BuiltInMethod.CreateV2("receiveMessageOnPort", 1, (interp, recv, args) =>
            {
                object? result = (args.Length == 0 ? null : args[0].ToObject()) switch
                {
                    SharpTSMessagePort port => port.ReceiveMessageSync(),
                    CompiledMessagePortBridge bridge => bridge.ReceiveMessageSync(),
                    _ => throw new Exception("receiveMessageOnPort requires a MessagePort argument"),
                };
                // Node returns `undefined` (not null) when the port has no queued message;
                // a present message is wrapped as `{ message }` by ReceiveMessageSync.
                return result is null ? RuntimeValue.Undefined : RuntimeValue.FromBoxed(result);
            }),

            // SHARE_ENV constant (placeholder - we don't support env sharing)
            ["SHARE_ENV"] = new SharpTSSymbol("SHARE_ENV"),

            // resourceLimits (not fully implemented)
            ["resourceLimits"] = new SharpTSObject(new Dictionary<string, object?>()),

            // markAsUntransferable: mark the object so it is cloned (never transferred) if it
            // appears in a postMessage transfer list (#1002).
            ["markAsUntransferable"] = BuiltInMethod.CreateV2("markAsUntransferable", 1, (interp, recv, args) =>
            {
                if (args.Length > 0)
                    StructuredClone.MarkUntransferable(args[0].ToObject());
                return RuntimeValue.Undefined;
            }),

            // moveMessagePortToContext: not supported. It re-homes a MessagePort into a vm
            // context's V8 isolate — SharpTS has no per-context isolate model (one process,
            // shared interpreters), so there is nothing to move a port into (#1004).
            ["moveMessagePortToContext"] = BuiltInMethod.CreateV2("moveMessagePortToContext", 2, (interp, recv, args) =>
            {
                throw new Exception(
                    "moveMessagePortToContext() is not supported in SharpTS: it requires V8 vm contexts/isolates, which SharpTS's single-process threading model does not provide.");
            }),

            // getEnvironmentData / setEnvironmentData — a real per-process data store keyed by
            // arbitrary values, independent of process.env (#1000).
            ["getEnvironmentData"] = BuiltInMethod.CreateV2("getEnvironmentData", 1, (interp, recv, args) =>
            {
                var key = args.Length > 0 ? args[0].ToObject() : null;
                return WorkerEnvironmentData.TryGet(key, out var value)
                    ? RuntimeValue.FromBoxed(value)
                    : RuntimeValue.Undefined;
            }),

            ["setEnvironmentData"] = BuiltInMethod.CreateV2("setEnvironmentData", 1, 2, (interp, recv, args) =>
            {
                if (args.Length >= 1)
                {
                    var key = args[0].ToObject();
                    // setEnvironmentData(key) with no value (or undefined/null) deletes the key.
                    var value = args.Length >= 2 ? args[1].ToObject() : null;
                    WorkerEnvironmentData.Set(key, value);
                }
                return RuntimeValue.Undefined;
            }),
        };
    }
}
