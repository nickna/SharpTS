using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWeakSetMethods(TypeBuilder typeBuilder, EmittedWeakSetRuntime weakSet)
    {
        // Emit validation helper first (shared primitive probe: RuntimeEmitter.WeakValidation.cs)
        weakSet.ValidateValue = EmitWeakTargetValidator(typeBuilder, "ValidateWeakSetValue",
            "Runtime Error: Invalid value used in weak set. WeakSet values must be objects");

        EmitCreateWeakSet(typeBuilder, weakSet);
        EmitWeakSetAdd(typeBuilder, weakSet);
        EmitWeakSetHas(typeBuilder, weakSet);
        EmitWeakSetDelete(typeBuilder, weakSet);
    }

    private void EmitCreateWeakSet(TypeBuilder typeBuilder, EmittedWeakSetRuntime weakSet)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        weakSet.Create = method;

        var il = method.GetILGenerator();

        // new ConditionalWeakTable<object, object>()
        var cwtType = _types.ConditionalWeakTableObjectObject;
        il.Emit(OpCodes.Newobj, _types.GetConstructor(cwtType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetAdd(TypeBuilder typeBuilder, EmittedWeakSetRuntime weakSet)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetAdd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        weakSet.Add = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;

        // We need a sentinel value to represent "in the set"
        // We'll use a static field for this
        var sentinelLocal = il.DeclareLocal(_types.Object);

        var returnSetLabel = il.DefineLabel();

        // if (weakSet is not ConditionalWeakTable<object, object> table) return weakSet;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnSetLabel);

        // if (value == null) return weakSet;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnSetLabel);

        // ValidateWeakSetValue(value);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakSet.ValidateValue);

        // table.AddOrUpdate(value, value); - Use value itself as sentinel (non-null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_1); // Use value as sentinel (any non-null object works)
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cwtType, "AddOrUpdate")!);

        // return weakSet;
        il.MarkLabel(returnSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetHas(TypeBuilder typeBuilder, EmittedWeakSetRuntime weakSet)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        weakSet.Has = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;
        var dummyLocal = il.DeclareLocal(_types.Object);

        var returnFalseLabel = il.DefineLabel();

        // if (weakSet is not ConditionalWeakTable<object, object> table) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (value == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // ValidateWeakSetValue(value);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakSet.ValidateValue);

        // return table.TryGetValue(value, out _);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, dummyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cwtType, "TryGetValue")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetDelete(TypeBuilder typeBuilder, EmittedWeakSetRuntime weakSet)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        weakSet.Delete = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;

        var returnFalseLabel = il.DefineLabel();

        // if (weakSet is not ConditionalWeakTable<object, object> table) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (value == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // ValidateWeakSetValue(value);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakSet.ValidateValue);

        // return table.Remove(value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cwtType, "Remove", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
