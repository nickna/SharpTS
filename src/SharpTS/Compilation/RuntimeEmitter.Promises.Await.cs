using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Implements Await's intrinsic PromiseResolve identity decision without
    /// changing the resolving-function policy of shared coercion consumers.
    /// </summary>
    private void EmitAwaitValueToTask(EmittedRuntime runtime)
    {
        var promise = runtime.RequirePromise();
        var il = promise.AwaitValueToTaskMethod.GetILGenerator();
        var nativePromiseLabel = il.DefineLabel();
        var intrinsicPromiseLabel = il.DefineLabel();
        var normalizedLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, promise.Type);
        il.Emit(OpCodes.Brtrue, nativePromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brtrue, nativePromiseLabel);

        // Ordinary values retain the existing coercion policy. Only native
        // promises need the observable constructor test introduced here.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, promise.CoerceAwaitableToTaskMethod);
        il.Emit(OpCodes.Br, normalizedLabel);

        il.MarkLabel(nativePromiseLabel);
        // Get constructor once, before choosing a fast path. A subclass may
        // override it with intrinsic Promise, and a base promise may replace it.
        // An abrupt constructor getter propagates from PromiseResolve itself.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Beq, intrinsicPromiseLabel);

        // ResolveValue captures the live then once and queues invocation with
        // the original receiver. Coerce would incorrectly unwrap this promise.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, promise.ResolveValueMethod);
        il.Emit(OpCodes.Br, normalizedLabel);

        il.MarkLabel(intrinsicPromiseLabel);
        // The intrinsic identity permits task reuse, including ignoring a
        // poisoned own then property. Coerce performs only the native unwrap.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, promise.CoerceAwaitableToTaskMethod);

        il.MarkLabel(normalizedLabel);
        // Every Await resumes in a Promise job, including primitives whose
        // coercion already completed. Use the same reaction facade for pending
        // tasks and immediately available values.
        // A synchronous-capable adoption bridge may settle inside then(resolve).
        // Give a pending await its own Promise job so guest code cannot resume
        // before the thenable finishes. The intrinsic no-handler path forwards
        // the raw value or exception without another guest then/constructor read.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, promise.Then);
        il.Emit(OpCodes.Ret);
    }
}
