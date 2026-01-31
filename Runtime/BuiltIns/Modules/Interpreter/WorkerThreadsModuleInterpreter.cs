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

            // Synchronous message receive
            ["receiveMessageOnPort"] = new BuiltInMethod("receiveMessageOnPort", 1, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not SharpTSMessagePort port)
                    throw new Exception("receiveMessageOnPort requires a MessagePort argument");
                return port.ReceiveMessageSync();
            }),

            // SHARE_ENV constant (placeholder - we don't support env sharing)
            ["SHARE_ENV"] = new SharpTSSymbol("SHARE_ENV"),

            // resourceLimits (not fully implemented)
            ["resourceLimits"] = new SharpTSObject(new Dictionary<string, object?>()),

            // markAsUntransferable (no-op in our implementation)
            ["markAsUntransferable"] = new BuiltInMethod("markAsUntransferable", 1, (interp, recv, args) =>
            {
                // No-op - we don't track transferability at runtime
                return null;
            }),

            // moveMessagePortToContext (not fully implemented - requires VM)
            ["moveMessagePortToContext"] = new BuiltInMethod("moveMessagePortToContext", 2, (interp, recv, args) =>
            {
                throw new Exception("moveMessagePortToContext is not supported in SharpTS");
            }),

            // getEnvironmentData / setEnvironmentData (environment data sharing)
            ["getEnvironmentData"] = new BuiltInMethod("getEnvironmentData", 1, (interp, recv, args) =>
            {
                // Simple implementation - return from process.env
                if (args.Count > 0 && args[0] is string key)
                {
                    return Environment.GetEnvironmentVariable(key);
                }
                return null;
            }),

            ["setEnvironmentData"] = new BuiltInMethod("setEnvironmentData", 2, (interp, recv, args) =>
            {
                if (args.Count >= 2 && args[0] is string key)
                {
                    string? value = args[1]?.ToString();
                    if (value != null)
                    {
                        Environment.SetEnvironmentVariable(key, value);
                    }
                    else
                    {
                        Environment.SetEnvironmentVariable(key, null);
                    }
                }
                return null;
            }),
        };
    }
}
