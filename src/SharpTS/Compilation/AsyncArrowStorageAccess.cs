using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

// A resolved storage location, independent of the caller's name-resolution precedence.
// This is compiler metadata only; no reference to it is emitted into the guest assembly.
internal sealed class AsyncArrowStorageAccess
{
    private readonly record struct ReceiverStep(FieldInfo Field, Type? UnboxType = null);

    private readonly LocalBuilder? _local;
    private readonly FieldInfo? _field;
    private readonly ReceiverStep[] _receiverPath;

    private AsyncArrowStorageAccess(LocalBuilder? local, FieldInfo? field, params ReceiverStep[] receiverPath)
    {
        _local = local;
        _field = field;
        _receiverPath = receiverPath;
    }

    public static AsyncArrowStorageAccess Local(LocalBuilder local) => new(local, null);

    public static AsyncArrowStorageAccess Cell(LocalBuilder cell, FieldInfo valueField) => new(cell, valueField);

    public static AsyncArrowStorageAccess StateMachineField(FieldInfo field) => new(null, field);

    public static AsyncArrowStorageAccess OwnDisplayClassField(FieldInfo displayClassField, FieldInfo field)
        => new(null, field, new ReceiverStep(displayClassField));

    public static AsyncArrowStorageAccess CapturedField(
        AsyncArrowStateMachineBuilder builder, string name, FieldInfo field)
        => new(null, field, OuterReceiverPath(builder, builder.TransitiveCaptures.Contains(name)));

    public static AsyncArrowStorageAccess OuterDisplayClassField(
        AsyncArrowStateMachineBuilder builder, FieldInfo displayClassField, FieldInfo field)
        => new(null, field, [.. OuterReceiverPath(builder, transitive: false), new(displayClassField)]);

    private static ReceiverStep[] OuterReceiverPath(AsyncArrowStateMachineBuilder builder, bool transitive)
    {
        var outer = new ReceiverStep(builder.OuterStateMachineField!, builder.OuterStateMachineType!);
        if (transitive && builder.ParentOuterStateMachineField != null && builder.GrandparentStateMachineType != null)
            return [outer, new(builder.ParentOuterStateMachineField, builder.GrandparentStateMachineType)];
        return [outer];
    }

    public void EmitLoad(ILGenerator il)
    {
        EmitReceiver(il);
        if (_field != null)
            il.Emit(OpCodes.Ldfld, _field);
    }

    // Consumes exactly one boxed value, preserving any assignment-result copy below it.
    public void EmitStore(ILGenerator il)
    {
        if (_field == null)
        {
            il.Emit(OpCodes.Stloc, _local!);
            return;
        }

        var value = il.DeclareLocal(_field.FieldType);
        il.Emit(OpCodes.Stloc, value);
        EmitReceiver(il);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Stfld, _field);
    }

    private void EmitReceiver(ILGenerator il)
    {
        if (_local != null)
            il.Emit(OpCodes.Ldloc, _local);
        else
            il.Emit(OpCodes.Ldarg_0);

        foreach (var step in _receiverPath)
        {
            il.Emit(OpCodes.Ldfld, step.Field);
            // Preserve the pointer to the existing box. Unbox_Any would copy the state machine
            // and lose shared storage. Display-class stores then follow the reference field,
            // keeping mutation off the readonly pointer returned by Unbox for IL verification.
            if (step.UnboxType != null)
                il.Emit(OpCodes.Unbox, step.UnboxType);
        }
    }
}
