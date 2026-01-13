using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWeakMapMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit validation helper first (shared with WeakSets)
        EmitValidateWeakMapKey(typeBuilder, runtime);

        EmitCreateWeakMap(typeBuilder, runtime);
        EmitWeakMapGet(typeBuilder, runtime);
        EmitWeakMapSet(typeBuilder, runtime);
        EmitWeakMapHas(typeBuilder, runtime);
        EmitWeakMapDelete(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the ValidateWeakMapKey helper that throws if key is a primitive type.
    /// </summary>
    private void EmitValidateWeakMapKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateWeakMapKey",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ValidateWeakMapKey = method;

        var il = method.GetILGenerator();

        var stringLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var booleanLabel = il.DefineLabel();
        var validLabel = il.DefineLabel();

        // Check string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Check double (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // Check int (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // Check long (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int64);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // Check float (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Single);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // Check decimal (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Decimal);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // Check bool (boxed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, booleanLabel);

        // Key is valid (not a primitive)
        il.Emit(OpCodes.Br, validLabel);

        // Throw for string
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects, not 'string'.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Throw for number
        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects, not 'number'.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Throw for boolean
        il.MarkLabel(booleanLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects, not 'boolean'.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Valid - just return
        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateWeakMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateWeakMap = method;

        var il = method.GetILGenerator();

        // new ConditionalWeakTable<object, object>()
        var cwtType = _types.ConditionalWeakTableObjectObject;
        il.Emit(OpCodes.Newobj, cwtType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.WeakMapGet = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakMapKey);

        // if (table.TryGetValue(key, out var value)) return value; else return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, cwtType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.WeakMapSet = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakMapKey);

        // table.AddOrUpdate(key, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, cwtType.GetMethod("AddOrUpdate")!);

        // return weakMap;
        il.MarkLabel(returnMapLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakMapHas = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakMapKey);

        // return table.TryGetValue(key, out _);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, dummyLocal);
        il.Emit(OpCodes.Callvirt, cwtType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakMapDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakMapDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakMapDelete = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakMapKey);

        // return table.Remove(key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, cwtType.GetMethod("Remove", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
