using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct VmHostEmission(TypeBuilder Type, FieldBuilder Current,
        FieldBuilder Loop, FieldBuilder Thread, FieldBuilder Enabled);

    private VmHostEmission EmitVmExecutionHost(TypeBuilder owner, EmittedRuntime runtime, MethodInfo nativeCallbackRole)
    {
        var vm = runtime.RequireVm();
        var inputProperties = EmitVmInputProperties(owner, runtime);
        var inputTag = EmitVmInputTag(owner, runtime, inputProperties.Admits);
        var inputWrite = EmitVmInputWrite(owner, runtime, inputProperties.Admits);
        var contextStorage = EmitVmContextStorageCallbacks(owner, runtime);
        var module = (ModuleBuilder)owner.Module;
        var callbacks = EmitTypeDefinitions.DefineType(module, "$VmHostCallbacks",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
        var loop = callbacks.DefineField("Loop", runtime.EventLoop.Type, FieldAttributes.Public | FieldAttributes.InitOnly);
        var enabled = callbacks.DefineField("Enabled", _types.Boolean, FieldAttributes.Public);
        var ownerThread = callbacks.DefineField("OwnerThread", _types.Int32, FieldAttributes.Public | FieldAttributes.InitOnly);
        var ctor = callbacks.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [runtime.EventLoop.Type]);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, enabled);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Environment).GetProperty(nameof(Environment.CurrentManagedThreadId))!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, ownerThread);
        il.Emit(OpCodes.Ret);

        var post = EmitTypeDefinitions.DefineType(module, "$VmPostedJob",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, _types.Object);
        var callbackOwner = post.DefineField("Owner", callbacks, FieldAttributes.Public);
        var enqueue = post.DefineField("Enqueue", typeof(Action<Action>), FieldAttributes.Public);
        var callback = post.DefineField("Callback", typeof(Action), FieldAttributes.Public);
        var postCtor = post.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        il = postCtor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ret);
        var postRun = post.DefineMethod("Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        il = postRun.GetILGenerator();
        var active = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbackOwner);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, enabled);
        il.Emit(OpCodes.Brtrue, active);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(active);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, enqueue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callback);
        il.Emit(OpCodes.Callvirt, typeof(Action<Action>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        post.CreateType();

        MethodBuilder DelegateMethod(string name, MethodInfo target, bool takesAction, bool postJob)
        {
            var method = callbacks.DefineMethod(name, MethodAttributes.Public, _types.Void,
                takesAction ? [typeof(Action)] : Type.EmptyTypes);
            var body = method.GetILGenerator();
            var live = body.DefineLabel();
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Volatile);
            body.Emit(OpCodes.Ldfld, enabled);
            body.Emit(OpCodes.Brtrue, live);
            body.Emit(OpCodes.Ret);
            body.MarkLabel(live);
            if (postJob)
            {
                var postFromWorker = body.DefineLabel();
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, ownerThread);
                body.Emit(OpCodes.Call, typeof(Environment).GetProperty(nameof(Environment.CurrentManagedThreadId))!.GetGetMethod()!);
                body.Emit(OpCodes.Bne_Un, postFromWorker);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Call, target);
                body.Emit(OpCodes.Ret);
                body.MarkLabel(postFromWorker);
            }
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, loop);
            if (postJob)
            {
                var job = body.DeclareLocal(post);
                body.Emit(OpCodes.Newobj, postCtor);
                body.Emit(OpCodes.Stloc, job);
                body.Emit(OpCodes.Ldloc, job);
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Stfld, callbackOwner);
                body.Emit(OpCodes.Ldloc, job);
                body.Emit(OpCodes.Ldnull);
                body.Emit(OpCodes.Ldftn, target);
                body.Emit(OpCodes.Newobj, typeof(Action<Action>).GetConstructor([_types.Object, _types.IntPtr])!);
                body.Emit(OpCodes.Stfld, enqueue);
                body.Emit(OpCodes.Ldloc, job);
                body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Stfld, callback);
                body.Emit(OpCodes.Ldloc, job);
                body.Emit(OpCodes.Ldftn, postRun);
                body.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, _types.IntPtr])!);
                body.Emit(OpCodes.Callvirt, runtime.EventLoop.Schedule);
            }
            else
            {
                if (takesAction) body.Emit(OpCodes.Ldarg_1);
                body.Emit(OpCodes.Callvirt, target);
            }
            body.Emit(OpCodes.Ret);
            return method;
        }
        var promiseJob = DelegateMethod("PromiseJob", runtime.Microtasks.QueuePromiseJob, true, true);
        var afterCheckpoint = DelegateMethod("AfterCheckpoint", vm.QueueAfterCheckpoint, true, true);
        var schedule = DelegateMethod("Schedule", runtime.EventLoop.Schedule, true, false);
        var refHandle = DelegateMethod("Ref", runtime.EventLoop.Ref, false, false);
        var unrefHandle = DelegateMethod("Unref", runtime.EventLoop.Unref, false, false);
        callbacks.CreateType();

        var hostField = owner.DefineField("_vmExecutionHost", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        var callbacksField = owner.DefineField("_vmHostCallbacks", callbacks, FieldAttributes.Private | FieldAttributes.Static);
        il = vm.GetExecutionHost.GetILGenerator();
        var ready = il.DefineLabel();
        var created = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, hostField);
        il.Emit(OpCodes.Brtrue, ready);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Stsfld, callbacksField);
        Action<ILGenerator> InstanceDelegate(MethodInfo method, ConstructorInfo constructor) => body =>
        {
            body.Emit(OpCodes.Ldsfld, callbacksField);
            body.Emit(OpCodes.Ldftn, method);
            body.Emit(OpCodes.Newobj, constructor);
        };
        var actionConstructor = typeof(Action).GetConstructor([_types.Object, _types.IntPtr])!;
        var queueConstructor = typeof(Action<Action>).GetConstructor([_types.Object, _types.IntPtr])!;
        var adapterConstructor = typeof(Func<object?, object?>).GetConstructor([_types.Object, _types.IntPtr])!;
        var isThrown = owner.DefineMethod("_VmIsGuestThrownValue", MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean, [_types.Exception]);
        var predicate = isThrown.GetILGenerator();
        predicate.Emit(OpCodes.Ldarg_0);
        predicate.Emit(OpCodes.Isinst, runtime.Errors.ThrownValueType);
        predicate.Emit(OpCodes.Ldnull);
        predicate.Emit(OpCodes.Cgt_Un);
        predicate.Emit(OpCodes.Ret);
        var getThrown = owner.DefineMethod("_VmGetGuestThrownValue", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.Exception]);
        var getter = getThrown.GetILGenerator();
        getter.Emit(OpCodes.Ldarg_0);
        getter.Emit(OpCodes.Castclass, runtime.Errors.ThrownValueType);
        getter.Emit(OpCodes.Callvirt, runtime.Errors.ThrownValueValueGetter);
        getter.Emit(OpCodes.Ret);
        var thrownPredicateConstructor = typeof(Func<Exception, bool>).GetConstructor([_types.Object, _types.IntPtr])!;
        var thrownGetterConstructor = typeof(Func<Exception, object?>).GetConstructor([_types.Object, _types.IntPtr])!;
        EmitVmCoreInvoke(il, "CreateExecutionHost",
            InstanceDelegate(promiseJob, queueConstructor),
            InstanceDelegate(schedule, queueConstructor),
            InstanceDelegate(afterCheckpoint, queueConstructor),
            InstanceDelegate(refHandle, actionConstructor),
            InstanceDelegate(unrefHandle, actionConstructor),
            body => EmitVmStaticDelegate(body, runtime.Cancellation.Check, actionConstructor));
        il.Emit(OpCodes.Stloc, created);
        EmitVmCoreInvoke(il, "ConfigureExecutionHostValueAdapters",
            body => body.Emit(OpCodes.Ldloc, created),
            body => EmitVmStaticDelegate(body, vm.NormalizeValue, adapterConstructor),
            body => EmitVmStaticDelegate(body, vm.RestoreValue, adapterConstructor));
        il.Emit(OpCodes.Pop);
        // Only the actual emitted thrown-value carrier gains inverse transport.
        // Configure the freshly created host before entering a scope or lease.
        EmitVmCoreInvoke(il, "ConfigureExecutionHostThrownValueAdapter",
            body => body.Emit(OpCodes.Ldloc, created),
            body => EmitVmStaticDelegate(body, isThrown, thrownPredicateConstructor),
            body => EmitVmStaticDelegate(body, getThrown, thrownGetterConstructor));
        il.Emit(OpCodes.Pop);
        EmitVmCoreInvoke(il, "ConfigureExecutionHostNativeCallbackRoles",
            body => body.Emit(OpCodes.Ldloc, created),
            body => EmitVmStaticDelegate(body, nativeCallbackRole,
                typeof(Func<object, int>).GetConstructor([_types.Object, _types.IntPtr])!));
        il.Emit(OpCodes.Pop);
        EmitVmInputPropertyConfiguration(il, runtime, inputProperties, created);
        EmitVmInputTagConfiguration(il, created, inputTag);
        EmitVmInputWriteConfiguration(il, created, inputWrite);
        EmitVmContextStorageConfiguration(il, runtime, contextStorage, created);
        il.Emit(OpCodes.Ldloc, created);
        il.Emit(OpCodes.Stsfld, hostField);
        il.MarkLabel(ready);
        il.Emit(OpCodes.Ldsfld, hostField);
        il.Emit(OpCodes.Ret);

        il = vm.EnterExecutionScope.GetILGenerator();
        EmitVmCoreInvoke(il, "EnterExecutionHost", body => body.Emit(OpCodes.Call, vm.GetExecutionHost));
        il.Emit(OpCodes.Castclass, typeof(IDisposable));
        il.Emit(OpCodes.Ret);

        il = vm.DisposeExecutionHost.GetILGenerator();
        var done = il.DefineLabel();
        var host = il.DeclareLocal(_types.Object);
        var oldCallbacks = il.DeclareLocal(callbacks);
        il.Emit(OpCodes.Ldsfld, hostField);
        il.Emit(OpCodes.Stloc, host);
        il.Emit(OpCodes.Ldloc, host);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldsfld, callbacksField);
        il.Emit(OpCodes.Stloc, oldCallbacks);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stsfld, callbacksField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stsfld, hostField);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, host);
        il.Emit(OpCodes.Castclass, typeof(IDisposable));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!);
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, oldCallbacks);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Stfld, enabled);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stsfld, vm.AfterCheckpointQueue);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
        return new VmHostEmission(callbacks, callbacksField, loop, ownerThread, enabled);
    }
    private void EmitVmStaticDelegate(ILGenerator il, MethodInfo method, ConstructorInfo constructor)
    {
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, method);
        il.Emit(OpCodes.Newobj, constructor);
    }
}
