using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct GuestOwnQueryInputs(EmittedGuestOwnPropertyRuntime Runtime, MethodInfo ToPropertyKey);

    private void EmitGuestOwnNamedIndex(ILGenerator il, EmittedGuestOwnPropertyRuntime? own, MethodInfo? property, bool write)
    {
        if (own == null) return;
        var legacy = EmitGuestOwnReceiverGuard(il, own, () => il.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Brfalse, legacy);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, _types.String);
        if (write) il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, property!); il.Emit(OpCodes.Ret); il.MarkLabel(legacy);
    }

    private Label EmitGuestOwnReceiverGuard(ILGenerator il, EmittedGuestOwnPropertyRuntime own, Action load)
    {
        var legacy = il.DefineLabel(); var receiver = il.DeclareLocal(own.Contract);
        load(); il.Emit(OpCodes.Isinst, own.Contract); il.Emit(OpCodes.Stloc, receiver);
        il.Emit(OpCodes.Ldloc, receiver); il.Emit(OpCodes.Brfalse, legacy);
        il.Emit(OpCodes.Ldloc, receiver); il.Emit(OpCodes.Callvirt, own.IsReceiver); il.Emit(OpCodes.Brfalse, legacy);
        return legacy;
    }

    private void EmitGuestOwnMutation(ILGenerator il, EmittedGuestOwnPropertyRuntime? own, bool deleting,
        Action loadReceiver, Action loadKey)
    {
        if (own == null) return;
        var legacy = EmitGuestOwnReceiverGuard(il, own, loadReceiver);
        loadReceiver(); loadKey();
        if (!deleting) il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, deleting ? own.CommitDelete : own.CommitDefinition);
        il.MarkLabel(legacy);
    }

    private void EmitGuestOwnDescriptorQuery(ILGenerator il, GuestOwnQueryInputs? inputs,
        LocalBuilder descriptor, Label present, Label absent)
    {
        if (inputs is not { } query) return;
        var own = query.Runtime; var legacy = EmitGuestOwnReceiverGuard(il, own, () => il.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, own.Contract);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, query.ToPropertyKey);
        il.Emit(OpCodes.Callvirt, own.GetOwnDescriptor); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brtrue, present); il.Emit(OpCodes.Br, absent);
        il.MarkLabel(legacy);
    }

    private void EmitGuestOwnPresenceQuery(ILGenerator il, GuestOwnQueryInputs? inputs, MethodInfo? enumerable = null)
    {
        if (inputs is not { } query) return;
        var own = query.Runtime; var legacy = EmitGuestOwnReceiverGuard(il, own, () => il.Emit(OpCodes.Ldarg_0));
        var descriptor = il.DeclareLocal(own.DescriptorType); var absent = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, own.Contract);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, query.ToPropertyKey);
        il.Emit(OpCodes.Callvirt, own.GetOwnDescriptor); il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, absent);
        if (enumerable == null) il.Emit(OpCodes.Ldc_I4_1);
        else { il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, enumerable); }
        il.Emit(OpCodes.Ret); il.MarkLabel(absent); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        il.MarkLabel(legacy);
    }

    // A key snapshot has no value reads. Enumerable filtering rechecks each
    // descriptor, so earlier guest callbacks can still alter later properties.
    private void EmitGuestOwnKeyQuery(ILGenerator il, EmittedGuestOwnPropertyRuntime? own,
        MethodInfo? enumerable = null, bool symbolsOnly = false)
    {
        if (own == null) return;
        var legacy = EmitGuestOwnReceiverGuard(il, own, () => il.Emit(OpCodes.Ldarg_0));
        var snapshot = il.DeclareLocal(_types.ListOfObject); var result = il.DeclareLocal(_types.ListOfObject);
        var key = il.DeclareLocal(_types.Object); var index = il.DeclareLocal(_types.Int32);
        var descriptor = il.DeclareLocal(own.DescriptorType); var loop = il.DefineLabel(); var next = il.DefineLabel(); var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, own.Contract); il.Emit(OpCodes.Callvirt, own.OwnPropertyKeys); il.Emit(OpCodes.Stloc, snapshot);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject)); il.Emit(OpCodes.Stloc, result);
        il.MarkLabel(loop); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, snapshot);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")); il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, snapshot); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32)); il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Isinst, _types.String); il.Emit(symbolsOnly ? OpCodes.Brtrue : OpCodes.Brfalse, next);
        if (enumerable != null)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, own.Contract); il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Callvirt, own.GetOwnDescriptor); il.Emit(OpCodes.Stloc, descriptor);
            il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, descriptor); il.Emit(OpCodes.Callvirt, enumerable); il.Emit(OpCodes.Brfalse, next);
        }
        il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(next); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index); il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret); il.MarkLabel(legacy);
    }
}
