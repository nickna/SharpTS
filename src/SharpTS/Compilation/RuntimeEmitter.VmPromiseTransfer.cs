using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Complete, MethodBuilder Observe, MethodBuilder NotifyHandled)
        EmitVmPromiseTransfer(TypeBuilder owner, EmittedRuntime runtime,
            VmHostEmission host)
    {
        var vm = runtime.RequireVm();
        var transfer = EmitTypeDefinitions.DefineType((ModuleBuilder)owner.Module, "$VmPromiseTransfer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
        var source = transfer.DefineField("Source", _types.TaskOfObject, FieldAttributes.Private | FieldAttributes.InitOnly);
        var result = transfer.DefineField("Result", _types.TaskCompletionSourceOfObject, FieldAttributes.Private | FieldAttributes.InitOnly);
        var callbacks = transfer.DefineField("Callbacks", host.Type, FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = transfer.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [_types.TaskOfObject, _types.TaskCompletionSourceOfObject, host.Type]);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        foreach (var (field, argument) in new[] { (source, 1), (result, 2), (callbacks, 3) })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg, argument);
            il.Emit(OpCodes.Stfld, field);
        }
        il.Emit(OpCodes.Ret);

        var complete = transfer.DefineMethod("Complete", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        il = complete.GetILGenerator();
        var active = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, host.Enabled);
        il.Emit(OpCodes.Brtrue, active);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(active);
        var awaiter = il.DeclareLocal(_types.TaskAwaiterOfObject);
        var value = il.DeclareLocal(_types.Object);
        var exception = il.DeclareLocal(_types.Exception);
        var done = il.DefineLabel();
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, source);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        il.Emit(OpCodes.Stloc, awaiter);
        il.Emit(OpCodes.Ldloca, awaiter);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult"));
        il.Emit(OpCodes.Call, vm.NormalizeValue);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, result);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetResult", _types.Object));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, done);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Call, vm.ConvertException);
        il.Emit(OpCodes.Stloc, exception);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, result);
        il.Emit(OpCodes.Ldloc, exception);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetException", _types.Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, done);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);

        var observe = transfer.DefineMethod("Observe", MethodAttributes.Public, _types.Void, [_types.TaskOfObject]);
        il = observe.GetILGenerator();
        var live = il.DefineLabel();
        var worker = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, host.Enabled);
        il.Emit(OpCodes.Brtrue, live);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(live);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Ldfld, host.Thread);
        il.Emit(OpCodes.Call, typeof(Environment).GetProperty(nameof(Environment.CurrentManagedThreadId))!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, worker);
        // Adoption adds no extra user reaction job when already on the owner.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, complete);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(worker);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Ldfld, host.Loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, complete);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoop.Schedule);
        il.Emit(OpCodes.Ret);
        var applyHandled = transfer.DefineMethod("ApplyHandled", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        il = applyHandled.GetILGenerator();
        var apply = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, host.Enabled);
        il.Emit(OpCodes.Brtrue, apply);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(apply);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, result);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.RequirePromise().NotifyPromiseRejectionHandler);
        il.Emit(OpCodes.Ret);

        var notifyHandled = transfer.DefineMethod("NotifyHandled", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        il = notifyHandled.GetILGenerator();
        var enabled = il.DefineLabel();
        var postHandled = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, host.Enabled);
        il.Emit(OpCodes.Brtrue, enabled);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(enabled);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Ldfld, host.Thread);
        il.Emit(OpCodes.Call, typeof(Environment).GetProperty(nameof(Environment.CurrentManagedThreadId))!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, postHandled);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, applyHandled);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(postHandled);
        // Same captured owner queue as settlement transfer; no new keepalive.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbacks);
        il.Emit(OpCodes.Ldfld, host.Loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, applyHandled);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoop.Schedule);
        il.Emit(OpCodes.Ret);
        transfer.CreateType();
        return (transfer, ctor, complete, observe, notifyHandled);
    }
}
