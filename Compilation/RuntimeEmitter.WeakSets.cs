using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWeakSetMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit validation helper first
        EmitValidateWeakSetValue(typeBuilder, runtime);

        EmitCreateWeakSet(typeBuilder, runtime);
        EmitWeakSetAdd(typeBuilder, runtime);
        EmitWeakSetHas(typeBuilder, runtime);
        EmitWeakSetDelete(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the ValidateWeakSetValue helper that throws if value is a primitive type.
    /// </summary>
    private void EmitValidateWeakSetValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateWeakSetValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ValidateWeakSetValue = method;

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

        // Value is valid (not a primitive)
        il.Emit(OpCodes.Br, validLabel);

        // Throw for string
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid value used in weak set. WeakSet values must be objects, not 'string'.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Throw for number
        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid value used in weak set. WeakSet values must be objects, not 'number'.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Throw for boolean
        il.MarkLabel(booleanLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Invalid value used in weak set. WeakSet values must be objects, not 'boolean'.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Valid - just return
        il.MarkLabel(validLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateWeakSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateWeakSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateWeakSet = method;

        var il = method.GetILGenerator();

        // new ConditionalWeakTable<object, object>()
        var cwtType = _types.ConditionalWeakTableObjectObject;
        il.Emit(OpCodes.Newobj, cwtType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetAdd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.WeakSetAdd = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakSetValue);

        // table.AddOrUpdate(value, value); - Use value itself as sentinel (non-null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_1); // Use value as sentinel (any non-null object works)
        il.Emit(OpCodes.Callvirt, cwtType.GetMethod("AddOrUpdate")!);

        // return weakSet;
        il.MarkLabel(returnSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWeakSetHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakSetHas = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakSetValue);

        // return table.TryGetValue(value, out _);
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

    private void EmitWeakSetDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WeakSetDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.WeakSetDelete = method;

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
        il.Emit(OpCodes.Call, runtime.ValidateWeakSetValue);

        // return table.Remove(value);
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
