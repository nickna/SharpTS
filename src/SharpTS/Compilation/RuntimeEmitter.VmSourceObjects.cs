using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void ValidateVmSourceObjectTracking(EmittedRuntime runtime)
    {
        if (runtime.Vm is not { } vm) return;
        var method = vm.RecordSourceObject;
        var owner = runtime.RuntimeClass.Type;
        if (!owner.IsCreated() || !ReferenceEquals(method.DeclaringType, owner)
            || !ReferenceEquals(method.Module, owner.Module) || !method.IsStatic || method.IsGenericMethod
            || method.ReturnType != _types.Object
            || !method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([_types.Object])
            || method.GetILGenerator().ILOffset == 0)
            throw new InvalidOperationException("VM source allocation tracking requires its completed actual operation.");
    }

    private MethodBuilder EmitVmSourceObjectTracking(TypeBuilder owner, EmittedRuntime runtime)
    {
        var tableType = _types.ConditionalWeakTableObjectObject;
        var table = owner.DefineField("_vmSourceObjectReceipts", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        var record = runtime.RequireVm().RecordSourceObject;
        if (!ReferenceEquals(record.DeclaringType, owner) || !record.IsStatic || record.ReturnType != _types.Object)
            throw new InvalidOperationException("VM source allocation receipt requires its actual declared operation.");
        var il = record.GetILGenerator();
        var ready = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, table);
        il.Emit(OpCodes.Brtrue, ready);
        il.Emit(OpCodes.Ldsflda, table);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(tableType));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod(nameof(Interlocked.CompareExchange),
            [typeof(object).MakeByRefType(), typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(ready);
        il.Emit(OpCodes.Ldsfld, table);
        il.Emit(OpCodes.Castclass, tableType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.ObjectStorage.Type);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tableType, "Add", _types.Object, _types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        var admits = owner.DefineMethod("_VmHasSourceObjectReceipt", MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean, [_types.Object]);
        il = admits.GetILGenerator();
        var missing = il.DefineLabel();
        var marker = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, table);
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldsfld, table);
        il.Emit(OpCodes.Castclass, tableType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, marker);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(tableType, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldloc, marker);
        il.Emit(OpCodes.Ldtoken, runtime.ObjectStorage.Type);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(missing);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return admits;
    }
}
