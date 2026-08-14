using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Object.hasOwn(obj, key) - checks if object has own property.
    /// Per ECMA-262 §20.1.2.13 step 1: ToObject(O) throws on null/undefined.
    /// Delegates to HasOwnPropertyHelper which handles all the receiver types
    /// (Dict, $TSObject, $TSFunction, List, String, System.Type, PDS extras),
    /// keeping the two helpers in sync.
    /// </summary>
    private void EmitObjectHasOwn(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectHasOwn",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.ObjectHasOwn = method;

        var il = method.GetILGenerator();

        // ToObject(O) throws on null/undefined per spec.
        var ohoOkLabel = il.DefineLabel();
        var ohoThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, ohoThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, ohoThrowLabel);
        il.Emit(OpCodes.Br, ohoOkLabel);
        il.MarkLabel(ohoThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(ohoOkLabel);

        // Delegate to HasOwnPropertyHelper(receiver, name).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.is(value1, value2) - determines whether two values are the same value.
    /// Unlike === operator:
    /// - Object.is(NaN, NaN) returns true
    /// - Object.is(-0, +0) returns false
    /// Signature: bool ObjectIs(object value1, object value2)
    /// </summary>
    private void EmitObjectIs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectIs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.ObjectIs = method;

        var il = method.GetILGenerator();

        var bothNullLabel = il.DefineLabel();
        var oneNullLabel = il.DefineLabel();
        var checkDoubleLabel = il.DefineLabel();
        var notBothDoubleLabel = il.DefineLabel();
        var checkNaNLabel = il.DefineLabel();
        var notNaNLabel = il.DefineLabel();
        var checkZeroLabel = il.DefineLabel();
        var notZeroLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var notStringLabel = il.DefineLabel();
        var checkBoolLabel = il.DefineLabel();
        var notBoolLabel = il.DefineLabel();
        var referenceEqualLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        var d1Local = il.DeclareLocal(_types.Double);
        var d2Local = il.DeclareLocal(_types.Double);

        // Check if both null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkDoubleLabel);
        // value1 is null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);  // both null
        il.Emit(OpCodes.Br, returnFalseLabel);       // only value1 is null

        // Check if both are double
        il.MarkLabel(checkDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);  // value2 is null but value1 isn't

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkStringLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);  // value1 is double but value2 isn't

        // Both are doubles - unbox them
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, d1Local);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, d2Local);

        // Check if both are NaN
        il.Emit(OpCodes.Ldloc, d1Local);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(double), "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, checkZeroLabel);

        il.Emit(OpCodes.Ldloc, d2Local);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(double), "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, returnTrueLabel);  // Both NaN -> true
        il.Emit(OpCodes.Br, returnFalseLabel);     // Only d1 is NaN -> false

        // Check if both are zero (need to distinguish +0 and -0)
        il.MarkLabel(checkZeroLabel);
        il.Emit(OpCodes.Ldloc, d1Local);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, notZeroLabel);

        il.Emit(OpCodes.Ldloc, d2Local);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, returnFalseLabel);  // d1 is 0 but d2 isn't

        // Both are zero - compare 1/d1 == 1/d2 to distinguish +0 and -0
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Ldloc, d1Local);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Ldloc, d2Local);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);

        // Not zero - normal double comparison
        il.MarkLabel(notZeroLabel);
        il.Emit(OpCodes.Ldloc, d1Local);
        il.Emit(OpCodes.Ldloc, d2Local);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);

        // Check if both are string
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkBoolLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Both strings - compare with string.Equals
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String));
        il.Emit(OpCodes.Br, endLabel);

        // Check if both are bool
        il.MarkLabel(checkBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, referenceEqualLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Both booleans - compare
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);

        // Reference equality for objects
        il.MarkLabel(referenceEqualLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Br, endLabel);

        // Return true
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Br, endLabel);

        // Return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.assign(target, sources) - copies properties from sources to target.
    /// Signature: object ObjectAssign(object target, List&lt;object&gt; sources)
    /// </summary>
    private void EmitObjectAssign(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectAssign",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ListOfObject]);
        runtime.ObjectAssign = method;

        var il = method.GetILGenerator();
        var listType = _types.ListOfObject;
        var target = il.DeclareLocal(_types.Object);
        var source = il.DeclareLocal(_types.Object);
        var keys = il.DeclareLocal(listType);
        var sourceIndex = il.DeclareLocal(_types.Int32);
        var keyIndex = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.Object);

        var throwTarget = il.DefineLabel();
        var targetReady = il.DefineLabel();
        var sourceLoop = il.DefineLabel();
        var sourcesDone = il.DefineLabel();
        var nextSource = il.DefineLabel();
        var sourcePresent = il.DefineLabel();
        var keyLoop = il.DefineLabel();
        var stringKeysDone = il.DefineLabel();
        var symbolLoop = il.DefineLabel();
        var symbolsDone = il.DefineLabel();
        var nextSymbol = il.DefineLabel();

        // 1. Let to be ? ToObject(target).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwTarget);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, targetReady);
        il.MarkLabel(throwTarget);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(targetReady);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToObjectMethod);
        il.Emit(OpCodes.Stloc, target);

        // 2. Process sources in argument order. GetKeys performs the source's
        // [[OwnPropertyKeys]]/[[GetOwnProperty]] work for enumerable string
        // keys and preserves the observable Proxy traps and key snapshot.
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, sourceIndex);
        il.MarkLabel(sourceLoop);
        il.Emit(OpCodes.Ldloc, sourceIndex);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, sourcesDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, sourceIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, source);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Brfalse, nextSource);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, sourcePresent);
        il.Emit(OpCodes.Br, nextSource);

        il.MarkLabel(sourcePresent);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Call, runtime.GetKeys);
        il.Emit(OpCodes.Stloc, keys);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, keyIndex);

        // Read and set one key at a time. GetIndex observes accessors; the
        // strict indexed setter implements Set(to, key, value, true) for both
        // string and Symbol keys, including Array exotic and integrity rules.
        il.MarkLabel(keyLoop);
        il.Emit(OpCodes.Ldloc, keyIndex);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, stringKeysDone);
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Ldloc, keyIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ldloc, keyIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, keyIndex);
        il.Emit(OpCodes.Br, keyLoop);

        // OrdinaryOwnPropertyKeys orders Symbols after strings. Symbol
        // descriptors are filtered here because GetOwnPropertySymbols returns
        // all own Symbol keys, including non-enumerable ones.
        il.MarkLabel(stringKeysDone);
        if (runtime.GetOwnPropertySymbols is not null)
        {
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Call, runtime.GetOwnPropertySymbols);
            il.Emit(OpCodes.Castclass, listType);
            il.Emit(OpCodes.Stloc, keys);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, keyIndex);
            il.MarkLabel(symbolLoop);
            il.Emit(OpCodes.Ldloc, keyIndex);
            il.Emit(OpCodes.Ldloc, keys);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Bge, symbolsDone);
            il.Emit(OpCodes.Ldloc, keys);
            il.Emit(OpCodes.Ldloc, keyIndex);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
            il.Emit(OpCodes.Stloc, key);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Call, runtime.PropertyIsEnumerableHelperMethod);
            il.Emit(OpCodes.Brfalse, nextSymbol);
            il.Emit(OpCodes.Ldloc, target);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Call, runtime.GetIndex);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, runtime.SetIndexStrict);
            il.MarkLabel(nextSymbol);
            il.Emit(OpCodes.Ldloc, keyIndex);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, keyIndex);
            il.Emit(OpCodes.Br, symbolLoop);
            il.MarkLabel(symbolsDone);
        }
        il.MarkLabel(nextSource);
        il.Emit(OpCodes.Ldloc, sourceIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, sourceIndex);
        il.Emit(OpCodes.Br, sourceLoop);

        il.MarkLabel(sourcesDone);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ret);
    }
}
