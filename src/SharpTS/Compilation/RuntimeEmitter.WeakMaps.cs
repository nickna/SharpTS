using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWeakMapMethods(TypeBuilder typeBuilder, EmittedWeakMapRuntime weakMap)
    {
        // Emit validation helper first (shared primitive probe: RuntimeEmitter.WeakValidation.cs)
        weakMap.ValidateKey = EmitWeakTargetValidator(typeBuilder, "ValidateWeakMapKey",
            "Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects");

        EmitCreateWeakMap(typeBuilder, weakMap);
        EmitWeakMapGet(typeBuilder, weakMap);
        EmitWeakMapSet(typeBuilder, weakMap);
        EmitWeakMapHas(typeBuilder, weakMap);
        EmitWeakMapDelete(typeBuilder, weakMap);
    }

    private void EmitCreateWeakMap(TypeBuilder typeBuilder, EmittedWeakMapRuntime weakMap)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        weakMap.Create = method;

        var il = method.GetILGenerator();

        // new ConditionalWeakTable<object, object>()
        var cwtType = _types.ConditionalWeakTableObjectObject;
        il.Emit(OpCodes.Newobj, _types.GetConstructor(cwtType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapGet(TypeBuilder typeBuilder, EmittedWeakMapRuntime weakMap)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        weakMap.Get = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;
        var valueLocal = il.DeclareLocal(_types.Object);

        var returnNullLabel = il.DefineLabel();

        // if (weakMap is not ConditionalWeakTable<object, object> table) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (key == null) return null;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // ValidateWeakMapKey(key);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakMap.ValidateKey);

        // if (table.TryGetValue(key, out var value)) return value; else return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cwtType, "TryGetValue")!);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapSet(TypeBuilder typeBuilder, EmittedWeakMapRuntime weakMap)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        weakMap.Set = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;

        var returnMapLabel = il.DefineLabel();

        // if (weakMap is not ConditionalWeakTable<object, object> table) return weakMap;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnMapLabel);

        // if (key == null) return weakMap;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnMapLabel);

        // ValidateWeakMapKey(key);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakMap.ValidateKey);

        // table.AddOrUpdate(key, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cwtType, "AddOrUpdate")!);

        // return weakMap;
        il.MarkLabel(returnMapLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapHas(TypeBuilder typeBuilder, EmittedWeakMapRuntime weakMap)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        weakMap.Has = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;
        var dummyLocal = il.DeclareLocal(_types.Object);

        var returnFalseLabel = il.DefineLabel();

        // if (weakMap is not ConditionalWeakTable<object, object> table) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (key == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // ValidateWeakMapKey(key);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakMap.ValidateKey);

        // return table.TryGetValue(key, out _);
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

    private void EmitWeakMapDelete(TypeBuilder typeBuilder, EmittedWeakMapRuntime weakMap)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        weakMap.Delete = method;

        var il = method.GetILGenerator();
        var cwtType = _types.ConditionalWeakTableObjectObject;

        var returnFalseLabel = il.DefineLabel();

        // if (weakMap is not ConditionalWeakTable<object, object> table) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, cwtType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (key == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // ValidateWeakMapKey(key);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, weakMap.ValidateKey);

        // return table.Remove(key);
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
