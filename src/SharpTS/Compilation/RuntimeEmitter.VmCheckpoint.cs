using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitVmAfterCheckpointQueue(TypeBuilder owner, EmittedVmRuntime vm,
        EmittedMicrotaskRuntime microtasks, Type queueType)
    {
        var marker = owner.DefineMethod("VmCheckpointMarker", MethodAttributes.Private | MethodAttributes.Static,
            _types.Void, Type.EmptyTypes);
        marker.GetILGenerator().Emit(OpCodes.Ret);
        var il = vm.QueueAfterCheckpoint.GetILGenerator();
        var ready = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, vm.AfterCheckpointQueue);
        il.Emit(OpCodes.Brtrue, ready);
        il.Emit(OpCodes.Newobj, EmitterTypeHelpers.ResolveConstructor(queueType,
            _types.GetDefaultConstructor(_types.QueueOpen)));
        il.Emit(OpCodes.Stsfld, vm.AfterCheckpointQueue);
        il.MarkLabel(ready);
        il.Emit(OpCodes.Ldsfld, vm.AfterCheckpointQueue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.ResolveMethod(queueType,
            _types.GetMethod(_types.QueueOpen, "Enqueue")!));
        // The ordinary queue owns checkpoint scheduling, including hosted dispatch.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, marker);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Call, microtasks.QueuePromiseJob);
        il.Emit(OpCodes.Ret);
    }

    private void EmitVmAfterCheckpointBatch(ILGenerator il, EmittedVmRuntime vm,
        Type queueType, Label promiseLoop)
    {
        var count = il.DeclareLocal(_types.Int32);
        var next = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, vm.AfterCheckpointQueue);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldsfld, vm.AfterCheckpointQueue);
        il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.ResolveMethod(queueType,
            _types.GetProperty(_types.QueueOpen, "Count").GetGetMethod()!));
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Brfalse, done);
        // Preserve Core's selected callback batch before returning to Promise jobs.
        il.MarkLabel(next);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldsfld, vm.AfterCheckpointQueue);
        il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.ResolveMethod(queueType,
            _types.GetMethod(_types.QueueOpen, "Dequeue")!));
        il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke")!);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Brtrue, next);
        il.Emit(OpCodes.Br, promiseLoop);
        il.MarkLabel(done);
    }
}
