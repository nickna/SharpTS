using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefineVmValueBridge(TypeBuilder owner, EmittedVmRuntime vm)
    {
        vm.AfterCheckpointQueue = owner.DefineField("_vmAfterCheckpoint",
            _types.MakeGenericType(_types.QueueOpen, typeof(Action)), FieldAttributes.Private | FieldAttributes.Static);
        vm.EnterExecutionScope = owner.DefineMethod("EnterVmExecutionScope", MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(IDisposable), Type.EmptyTypes);
        vm.ConvertException = owner.DefineMethod("ConvertVmException", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Exception, [_types.Exception]);
        vm.IsRelatedValue = owner.DefineMethod("IsRelatedVmValue", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Boolean, [_types.Object]);
        vm.IsMappedValue = owner.DefineMethod("IsMappedVmValue", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Boolean, [_types.Object]);
        vm.ReadMappedProperty = owner.DefineMethod("ReadMappedVmProperty", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String]);
        vm.MappedPrototype = owner.DefineMethod("GetMappedVmPrototype", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        vm.NormalizeValue = owner.DefineMethod("NormalizeVmValue", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        vm.RestoreValue = owner.DefineMethod("RestoreVmValue", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        vm.IsErrorValue = owner.DefineMethod("IsVmErrorValue", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Boolean, [_types.Object]);
        vm.ReadErrorProperty = owner.DefineMethod("ReadVmErrorProperty", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String]);
        vm.GetExecutionHost = owner.DefineMethod("GetVmExecutionHost", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Object, Type.EmptyTypes);
        vm.DisposeExecutionHost = owner.DefineMethod("DisposeVmExecutionHost", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Void, Type.EmptyTypes);
        vm.QueueAfterCheckpoint = owner.DefineMethod("QueueVmAfterCheckpoint", MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Void, [typeof(Action)]);
    }
    private void EmitVmValueBridge(TypeBuilder owner, EmittedRuntime runtime)
    {
        var vm = runtime.RequireVm();
        var promise = runtime.RequirePromise();
        var cacheType = _types.ConditionalWeakTableObjectObject;
        var forward = owner.DefineField("_vmValueWrappers", cacheType, FieldAttributes.Private | FieldAttributes.Static);
        var reverse = owner.DefineField("_vmValueSources", cacheType, FieldAttributes.Private | FieldAttributes.Static);
        var nativeCallbacks = owner.DefineField("_vmNativePromiseCallbacks", cacheType, FieldAttributes.Private | FieldAttributes.Static);
        var nativeCallbackSources = owner.DefineField("_vmNativePromiseCallbackSources", cacheType, FieldAttributes.Private | FieldAttributes.Static);
        var callbackRole = EmitVmNativePromiseCallbackRole(owner, runtime, nativeCallbackSources);
        var host = EmitVmExecutionHost(owner, runtime, callbackRole);
        var transfer = EmitVmPromiseTransfer(owner, runtime, host);
        var callable = EmitVmCallableTransport(owner, runtime);
        EmitVmExceptionBridge(runtime);
        EmitVmErrorBridge(runtime);
        var tryGet = _types.GetMethod(cacheType, "TryGetValue", _types.Object, _types.Object.MakeByRefType());
        var add = _types.GetMethod(cacheType, "Add", _types.Object, _types.Object);
        var cacheCtor = _types.GetDefaultConstructor(cacheType);
        EmitVmMappedValueReaders(runtime, reverse);

        var il = vm.RestoreValue.GetILGenerator();
        var restored = il.DeclareLocal(_types.Object);
        var unchanged = il.DefineLabel();
        var nativeCallback = il.DefineLabel();
        var notUndefined = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Bne_Un, notUndefined);
        EmitVmCoreInvoke(il, "GetVmUndefinedValue");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notUndefined);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, unchanged);
        il.Emit(OpCodes.Ldsfld, reverse);
        il.Emit(OpCodes.Brfalse, nativeCallback);
        il.Emit(OpCodes.Ldsfld, reverse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, restored);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, nativeCallback);
        il.Emit(OpCodes.Ldloc, restored);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nativeCallback);
        EmitVmNativePromiseCallbackWrapping(il, runtime, nativeCallbacks, nativeCallbackSources);
        il.MarkLabel(unchanged);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il = vm.NormalizeValue.GetILGenerator();
        var same = il.DefineLabel();
        var ordinaryValue = il.DefineLabel();
        var admittedValue = il.DefineLabel();
        var createCallable = il.DefineLabel();
        var cacheValue = il.DefineLabel();
        var isPromise = il.DeclareLocal(_types.Boolean);
        var cachesReady = il.DefineLabel();
        var create = il.DefineLabel();
        var pending = il.DefineLabel();
        var done = il.DefineLabel();
        var wrapper = il.DeclareLocal(_types.Object);
        var source = il.DeclareLocal(_types.TaskOfObject);
        var result = il.DeclareLocal(_types.TaskCompletionSourceOfObject);
        var observer = il.DeclareLocal(transfer.Type);
        var originalCallback = il.DeclareLocal(_types.Object);
        var unchangedCallback = il.DefineLabel();
        EmitVmCoreInvoke(il, "UnwrapVmCallback", arg => arg.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Stloc, originalCallback);
        // Only wrappers produced from the two actual resolving-function
        // endpoints gain this inverse identity mapping.
        var notNativeCallback = il.DefineLabel();
        var nativeCallbackSource = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, originalCallback);
        il.Emit(OpCodes.Brfalse, notNativeCallback);
        il.Emit(OpCodes.Ldsfld, nativeCallbackSources);
        il.Emit(OpCodes.Brfalse, notNativeCallback);
        il.Emit(OpCodes.Ldsfld, nativeCallbackSources);
        il.Emit(OpCodes.Ldloc, originalCallback);
        il.Emit(OpCodes.Ldloca, nativeCallbackSource);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, notNativeCallback);
        il.Emit(OpCodes.Ldloc, nativeCallbackSource);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNativeCallback);
        il.Emit(OpCodes.Ldloc, originalCallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Beq, unchangedCallback);
        il.Emit(OpCodes.Ldloc, originalCallback);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(unchangedCallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, same);
        EmitVmCoreInvoke(il, "IsVmUndefinedValue", arg => arg.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, ordinaryValue);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(ordinaryValue);
        EmitVmCoreInvoke(il, "IsVmPromiseValue", arg => arg.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Stloc, isPromise);
        il.Emit(OpCodes.Ldloc, isPromise);
        il.Emit(OpCodes.Brtrue, admittedValue);
        EmitVmCoreInvoke(il, "IsVmCallableValue", arg => arg.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, same);
        il.MarkLabel(admittedValue);
        il.Emit(OpCodes.Ldsfld, forward);
        il.Emit(OpCodes.Brtrue, cachesReady);
        il.Emit(OpCodes.Newobj, cacheCtor);
        il.Emit(OpCodes.Stsfld, forward);
        il.Emit(OpCodes.Newobj, cacheCtor);
        il.Emit(OpCodes.Stsfld, reverse);
        il.MarkLabel(cachesReady);
        il.Emit(OpCodes.Ldsfld, forward);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, wrapper);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, create);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(create);
        il.Emit(OpCodes.Ldloc, isPromise);
        il.Emit(OpCodes.Brfalse, createCallable);
        EmitVmCoreInvoke(il, "GetVmPromiseTask", arg => arg.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, source);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject));
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, promise.Ctor);
        il.Emit(OpCodes.Stloc, wrapper);
        il.Emit(OpCodes.Br, cacheValue);
        il.MarkLabel(createCallable);
        var target = il.DeclareLocal(callable.Type);
        var name = il.DeclareLocal(_types.String);
        var length = il.DeclareLocal(_types.Int32);
        var metadata = il.DeclareLocal(_types.ObjectArray);
        var sourceCallable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, callable.Constructor);
        il.Emit(OpCodes.Stloc, target);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, name);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, length);
        EmitVmCoreInvoke(il, "IsVmValue", body => body.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, sourceCallable);
        // Ownerless native facades have an immutable producer snapshot. Source
        // callables retain live owner property reads, with no eager user getter.
        EmitVmCoreInvoke(il, "GetVmFacadeCallableMetadata", body => body.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, metadata);
        il.Emit(OpCodes.Ldloc, metadata);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, name);
        il.Emit(OpCodes.Ldloc, metadata);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, length);
        il.MarkLabel(sourceCallable);
        il.Emit(OpCodes.Ldloc, target);
        EmitReflectedMethodLiteral(il, callable.Invoke);
        il.Emit(OpCodes.Ldloc, name);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Newobj, runtime.FunctionConstruction.CachedConstructor);
        il.Emit(OpCodes.Stloc, wrapper);
        il.MarkLabel(cacheValue);
        // Publish both directions before observing an already-settled result.
        // Only this exact produced wrapper gains reverse-transport authority.
        il.Emit(OpCodes.Ldsfld, forward);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Callvirt, add);
        il.Emit(OpCodes.Ldsfld, reverse);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, add);
        il.Emit(OpCodes.Ldloc, isPromise);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldsfld, host.Current);
        il.Emit(OpCodes.Newobj, transfer.Constructor);
        il.Emit(OpCodes.Stloc, observer);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCompleted").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, pending);
        il.Emit(OpCodes.Ldloc, observer);
        il.Emit(OpCodes.Callvirt, transfer.Complete);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(pending);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, observer);
        il.Emit(OpCodes.Ldftn, transfer.Observe);
        il.Emit(OpCodes.Newobj, typeof(Action<Task<object>>).GetConstructor([_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Callvirt, typeof(Task<object>).GetMethod("ContinueWith",
            [typeof(Action<Task<object>>), typeof(TaskContinuationOptions)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(same);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }
}
