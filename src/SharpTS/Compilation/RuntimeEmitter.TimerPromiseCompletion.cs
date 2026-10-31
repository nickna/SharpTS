using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTimerPromiseCompletion(TypeBuilder runtimeType, ModuleBuilder module, EmittedRuntime runtime)
    {
        // A worker completes only the native wait. The actual promise settles
        // on its caller loop, so dropping the native timer ref cannot race the
        // enqueue of the promise's reactions.
        var callbackType = _types.MakeGenericType(typeof(Func<,>), _types.Task, _types.Object);
        var completionType = _types.TaskCompletionSourceOfObject;
        var closure = EmitTypeDefinitions.DefineType(module, "$TimerPromiseCompletion",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
        var source = closure.DefineField("Source", _types.Task, FieldAttributes.Private | FieldAttributes.InitOnly);
        var callback = closure.DefineField("Callback", callbackType, FieldAttributes.Private | FieldAttributes.InitOnly);
        var completion = closure.DefineField("Completion", completionType, FieldAttributes.Public | FieldAttributes.InitOnly);
        var ctor = closure.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Task, callbackType]);
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, source);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, callback);
            il.Emit(OpCodes.Ldarg_0);
            // Inline task continuations hand off to the existing promise-job
            // queue before this loop callback returns. No worker runs guest code.
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(completionType));
            il.Emit(OpCodes.Stfld, completion);
            il.Emit(OpCodes.Ret);
        }
        var run = closure.DefineMethod("Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        {
            var il = run.GetILGenerator();
            var exception = il.DeclareLocal(_types.Exception);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, completion);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, callback);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, source);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(callbackType, "Invoke", _types.Task));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(completionType, "SetResult", _types.Object));
            il.BeginCatchBlock(_types.Exception);
            il.Emit(OpCodes.Stloc, exception);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, completion);
            il.Emit(OpCodes.Ldloc, exception);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(completionType, "SetException", _types.Exception));
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ret);
        }
        var onCompleted = closure.DefineMethod("OnCompleted", MethodAttributes.Public, _types.Void, [_types.Task]);
        {
            var il = onCompleted.GetILGenerator();
            il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, run);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(Action), [_types.Object, _types.IntPtr])!);
            il.Emit(OpCodes.Callvirt, runtime.EventLoop.Schedule);
            il.Emit(OpCodes.Ret);
        }
        closure.CreateType();
        var method = runtimeType.DefineMethod("CompleteTimerPromiseOnEventLoop", MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject, [_types.Task, callbackType]);
        runtime.RequireTimerPromises().CompleteOnEventLoop = method;
        {
            var il = method.GetILGenerator();
            var local = il.DeclareLocal(closure);
            var actionType = _types.MakeGenericType(typeof(Action<>), _types.Task);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Ldftn, onCompleted);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(actionType, [_types.Object, _types.IntPtr])!);
            il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Task, "ContinueWith", actionType, typeof(TaskContinuationOptions)));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Ldfld, completion);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(completionType, "Task").GetGetMethod()!);
            il.Emit(OpCodes.Ret);
        }
    }
}
