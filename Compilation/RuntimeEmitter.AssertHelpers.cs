using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits assert module methods with full IL for standalone execution.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits assert module helper methods.
    /// </summary>
    private void EmitAssertMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit helper methods first (used by main assert methods)
        EmitAssertIsTruthy(typeBuilder, runtime);
        EmitAssertStrictEquals(typeBuilder, runtime);
        EmitAssertLooseEquals(typeBuilder, runtime);
        EmitAssertDeepEquals(typeBuilder, runtime);

        // Emit main assert methods
        EmitAssertOk(typeBuilder, runtime);
        EmitAssertStrictEqual(typeBuilder, runtime);
        EmitAssertNotStrictEqual(typeBuilder, runtime);
        EmitAssertDeepStrictEqual(typeBuilder, runtime);
        EmitAssertNotDeepStrictEqual(typeBuilder, runtime);
        EmitAssertThrows(typeBuilder, runtime);
        EmitAssertDoesNotThrow(typeBuilder, runtime);
        EmitAssertFail(typeBuilder, runtime);
        EmitAssertEqual(typeBuilder, runtime);
        EmitAssertNotEqual(typeBuilder, runtime);
        EmitAssertMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: private static bool IsTruthy(object? value)
    /// </summary>
    private void EmitAssertIsTruthy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertIsTruthy",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.AssertIsTruthy = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var checkBoolLabel = il.DefineLabel();
        var checkDoubleLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var defaultTrueLabel = il.DefineLabel();

        // if (value == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (value is bool b) return b
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, checkDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // if (value is double d) return d != 0 && !double.IsNaN(d)
        il.MarkLabel(checkDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkStringLabel);

        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dLocal);

        // d != 0
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, returnFalseLabel);

        // !double.IsNaN(d)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Br, returnTrueLabel);

        // if (value is string s) return s.Length > 0
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, defaultTrueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ret);

        // Default: return true
        il.MarkLabel(defaultTrueLabel);
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool StrictEquals(object? a, object? b)
    /// </summary>
    private void EmitAssertStrictEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertStrictEquals",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.AssertStrictEquals = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var checkBothNullLabel = il.DefineLabel();
        var checkTypesLabel = il.DefineLabel();
        var checkDoubleLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var checkBoolLabel = il.DefineLabel();
        var referenceEqualsLabel = il.DefineLabel();

        // if (a == null && b == null) return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkBothNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);
        il.Emit(OpCodes.Br, returnFalseLabel);

        il.MarkLabel(checkBothNullLabel);
        // if (a != null && b == null) return false
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (a.GetType() != b.GetType()) return false
        il.MarkLabel(checkTypesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (a is double) return ((double)a).Equals((double)b)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkStringLabel);
        var daLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, daLocal);
        il.Emit(OpCodes.Ldloca, daLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("Equals", [_types.Double])!);
        il.Emit(OpCodes.Ret);

        // if (a is string) return (string)a == (string)b
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        // if (a is bool) return (bool)a == (bool)b
        il.MarkLabel(checkBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, referenceEqualsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        // Default: ReferenceEquals(a, b)
        il.MarkLabel(referenceEqualsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool LooseEquals(object? a, object? b)
    /// Simplified version that checks types and falls back to string comparison
    /// </summary>
    private void EmitAssertLooseEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertLooseEquals",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.AssertLooseEquals = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var checkBothNullLabel = il.DefineLabel();
        var sameTypeLabel = il.DefineLabel();
        var compareStringsLabel = il.DefineLabel();

        // if (a == null && b == null) return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkBothNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);
        il.Emit(OpCodes.Br, returnFalseLabel);

        il.MarkLabel(checkBothNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (a.GetType() == b.GetType()) return StrictEquals(a, b)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, compareStringsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertStrictEquals);
        il.Emit(OpCodes.Ret);

        // return a.ToString() == b.ToString()
        il.MarkLabel(compareStringsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool DeepEquals(object? a, object? b)
    /// Checks arrays and dicts first (recursive), then falls back to StrictEquals.
    /// </summary>
    private void EmitAssertDeepEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertDeepEquals",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.AssertDeepEquals = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var checkBothNullLabel = il.DefineLabel();
        var checkArraysLabel = il.DefineLabel();
        var checkDictsLabel = il.DefineLabel();
        var useStrictEqualsLabel = il.DefineLabel();

        // if (a == null && b == null) return true
        // if (a == null || b == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkBothNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);
        il.Emit(OpCodes.Br, returnFalseLabel);

        il.MarkLabel(checkBothNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // --- Check arrays first ---
        il.MarkLabel(checkArraysLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, checkDictsLabel);

        // a is a list - b must also be a list
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Compare arrays recursively
        var listALocal = il.DeclareLocal(_types.ListOfObject);
        var listBLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var arrayLoopStart = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listALocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listBLocal);

        // if (listA.Count != listB.Count) return false
        il.Emit(OpCodes.Ldloc, listALocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, listBLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, returnFalseLabel);

        // for (int i = 0; i < listA.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(arrayLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, listALocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnTrueLabel);

        // if (!DeepEquals(listA[i], listB[i])) return false
        il.Emit(OpCodes.Ldloc, listALocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Ldloc, listBLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, arrayLoopStart);

        // --- Check dictionaries ---
        il.MarkLabel(checkDictsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, useStrictEqualsLabel);

        // a is a dict - b must also be a dict
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Compare dictionaries recursively
        var dictALocal = il.DeclareLocal(_types.DictionaryStringObject);
        var dictBLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictALocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictBLocal);

        // if (dictA.Count != dictB.Count) return false
        il.Emit(OpCodes.Ldloc, dictALocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dictBLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, returnFalseLabel);

        // foreach key in dictA.Keys, check dictB has it and values are DeepEquals
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object?>.KeyCollection.Enumerator));
        var currentKeyLocal = il.DeclareLocal(_types.String);
        var bValueLocal = il.DeclareLocal(_types.Object);
        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        // var enumerator = dictA.Keys.GetEnumerator()
        il.Emit(OpCodes.Ldloc, dictALocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.KeyCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(dictLoopStart);
        // while (enumerator.MoveNext())
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.KeyCollection.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // var key = enumerator.Current
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object?>.KeyCollection.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        // if (!dictB.TryGetValue(key, out bValue)) return false
        il.Emit(OpCodes.Ldloc, dictBLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Ldloca, bValueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (!DeepEquals(dictA[key], bValue)) return false
        il.Emit(OpCodes.Ldloc, dictALocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [_types.String])!);
        il.Emit(OpCodes.Ldloc, bValueLocal);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Br, returnTrueLabel);

        // --- For anything else (primitives, etc.), use StrictEquals ---
        il.MarkLabel(useStrictEqualsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertStrictEquals);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertOk(object? value, object? message)
    /// </summary>
    private void EmitAssertOk(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertOk",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.AssertOk = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();
        var msgLocal = il.DeclareLocal(_types.String);

        // if (IsTruthy(value)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.AssertIsTruthy);
        il.Emit(OpCodes.Brtrue, passLabel);

        // var msg = message?.ToString() ?? "The expression evaluated to a falsy value"
        var hasMessageLabel = il.DefineLabel();
        var afterMsgLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, hasMessageLabel);
        il.Emit(OpCodes.Ldstr, "The expression evaluated to a falsy value");
        il.Emit(OpCodes.Br, afterMsgLabel);
        il.MarkLabel(hasMessageLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.MarkLabel(afterMsgLabel);
        il.Emit(OpCodes.Stloc, msgLocal);

        // throw new $AssertionError(msg, value, true, "ok")
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ldstr, "ok");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertStrictEqual = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();

        // if (StrictEquals(actual, expected)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertStrictEquals);
        il.Emit(OpCodes.Brtrue, passLabel);

        // throw new $AssertionError(msg, actual, expected, "strictEqual")
        EmitGetMessageOrDefault(il, 2, "Expected values to be strictly equal");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "strictEqual");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertNotStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertNotStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertNotStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertNotStrictEqual = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();

        // if (!StrictEquals(actual, expected)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertStrictEquals);
        il.Emit(OpCodes.Brfalse, passLabel);

        // throw new $AssertionError(msg, actual, expected, "notStrictEqual")
        EmitGetMessageOrDefault(il, 2, "Expected values not to be strictly equal");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "notStrictEqual");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertDeepStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertDeepStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertDeepStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertDeepStrictEqual = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();

        // if (DeepEquals(actual, expected)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertDeepEquals);
        il.Emit(OpCodes.Brtrue, passLabel);

        // throw new $AssertionError(msg, actual, expected, "deepStrictEqual")
        EmitGetMessageOrDefault(il, 2, "Expected values to be deeply equal");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "deepStrictEqual");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertNotDeepStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertNotDeepStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertNotDeepStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertNotDeepStrictEqual = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();

        // if (!DeepEquals(actual, expected)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertDeepEquals);
        il.Emit(OpCodes.Brfalse, passLabel);

        // throw new $AssertionError(msg, actual, expected, "notDeepStrictEqual")
        EmitGetMessageOrDefault(il, 2, "Expected values not to be deeply equal");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "notDeepStrictEqual");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertThrows(object? fn, object? message)
    /// </summary>
    private void EmitAssertThrows(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertThrows",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.AssertThrows = method;

        var il = method.GetILGenerator();
        var fnNotNullLabel = il.DefineLabel();
        var afterTryLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var threwLocal = il.DeclareLocal(_types.Boolean);

        // if (fn == null) throw error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, fnNotNullLabel);
        EmitGetMessageOrDefault(il, 1, "Missing function to test");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldstr, "throws");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(fnNotNullLabel);

        // bool threw = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, threwLocal);

        // try { invoke fn }
        il.BeginExceptionBlock();

        // If fn is Delegate, call DynamicInvoke
        var invokeReflectionLabel = il.DefineLabel();
        var doInvokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Delegate));
        il.Emit(OpCodes.Brfalse, invokeReflectionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Delegate));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, _types.DelegateDynamicInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterTryLabel);

        // For other types, try to get Invoke method via reflection
        il.MarkLabel(invokeReflectionLabel);
        var invokeLocal = il.DeclareLocal(typeof(MethodInfo));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [_types.String])!);
        il.Emit(OpCodes.Stloc, invokeLocal);

        il.Emit(OpCodes.Ldloc, invokeLocal);
        il.Emit(OpCodes.Brtrue, doInvokeLabel);
        // No Invoke method found, just leave without invoking
        il.Emit(OpCodes.Leave, afterTryLabel);

        il.MarkLabel(doInvokeLabel);
        il.Emit(OpCodes.Ldloc, invokeLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.MethodInfoInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterTryLabel);

        // catch (Exception) { threw = true }
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop); // discard exception
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, threwLocal);
        il.Emit(OpCodes.Leave, afterTryLabel);

        il.EndExceptionBlock();

        il.MarkLabel(afterTryLabel);

        // if (!threw) throw error
        il.Emit(OpCodes.Ldloc, threwLocal);
        il.Emit(OpCodes.Brtrue, endLabel);

        EmitGetMessageOrDefault(il, 1, "Missing expected exception");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldstr, "throws");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertDoesNotThrow(object? fn, object? message)
    /// </summary>
    private void EmitAssertDoesNotThrow(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertDoesNotThrow",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.AssertDoesNotThrow = method;

        var il = method.GetILGenerator();
        var fnNotNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        var exLocal = il.DeclareLocal(_types.Exception);

        // if (fn == null) throw error
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, fnNotNullLabel);
        EmitGetMessageOrDefault(il, 1, "Missing function to test");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldstr, "doesNotThrow");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(fnNotNullLabel);

        // try { invoke fn }
        il.BeginExceptionBlock();

        var invokeReflectionLabel = il.DefineLabel();
        var doInvokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Delegate));
        il.Emit(OpCodes.Brfalse, invokeReflectionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(Delegate));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, _types.DelegateDynamicInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.MarkLabel(invokeReflectionLabel);
        var invokeLocal = il.DeclareLocal(typeof(MethodInfo));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [_types.String])!);
        il.Emit(OpCodes.Stloc, invokeLocal);

        il.Emit(OpCodes.Ldloc, invokeLocal);
        il.Emit(OpCodes.Brtrue, doInvokeLabel);
        // No Invoke method found, just leave
        il.Emit(OpCodes.Leave, endLabel);

        il.MarkLabel(doInvokeLabel);
        il.Emit(OpCodes.Ldloc, invokeLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.MethodInfoInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        // catch (Exception ex) { throw AssertionError }
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);

        // Get message: message?.ToString() ?? "Got unwanted exception: " + ex.Message
        var hasCustomMsgLabel = il.DefineLabel();
        var afterMsgBuildLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, hasCustomMsgLabel);

        il.Emit(OpCodes.Ldstr, "Got unwanted exception: ");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.Exception.GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Br, afterMsgBuildLabel);

        il.MarkLabel(hasCustomMsgLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);

        il.MarkLabel(afterMsgBuildLabel);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldstr, "doesNotThrow");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertFail(object? message)
    /// </summary>
    private void EmitAssertFail(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertFail",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.AssertFail = method;

        var il = method.GetILGenerator();

        // throw new $AssertionError(message?.ToString() ?? "Failed", null, null, "fail")
        EmitGetMessageOrDefault(il, 0, "Failed");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldstr, "fail");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static void AssertEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertEqual = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();

        // if (LooseEquals(actual, expected)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertLooseEquals);
        il.Emit(OpCodes.Brtrue, passLabel);

        // throw new $AssertionError(msg, actual, expected, "equal")
        EmitGetMessageOrDefault(il, 2, "Expected values to be loosely equal");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "equal");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertNotEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertNotEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertNotEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertNotEqual = method;

        var il = method.GetILGenerator();
        var passLabel = il.DefineLabel();

        // if (!LooseEquals(actual, expected)) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AssertLooseEquals);
        il.Emit(OpCodes.Brfalse, passLabel);

        // throw new $AssertionError(msg, actual, expected, "notEqual")
        EmitGetMessageOrDefault(il, 2, "Expected values not to be loosely equal");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "notEqual");
        il.Emit(OpCodes.Newobj, runtime.TSAssertionErrorCtor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(passLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper to emit: message?.ToString() ?? defaultMessage
    /// Leaves result on stack
    /// </summary>
    private void EmitGetMessageOrDefault(ILGenerator il, int argIndex, string defaultMessage)
    {
        var hasMessageLabel = il.DefineLabel();
        var afterMessageLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Brtrue, hasMessageLabel);
        il.Emit(OpCodes.Ldstr, defaultMessage);
        il.Emit(OpCodes.Br, afterMessageLabel);

        il.MarkLabel(hasMessageLabel);
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);

        il.MarkLabel(afterMessageLabel);
    }

    /// <summary>
    /// Emits wrapper methods for assert functions that can be used as first-class values.
    /// </summary>
    private void EmitAssertMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ok(value, message?) - 2 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "ok", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.AssertOk);
            il.Emit(OpCodes.Ldnull);
        });

        // strictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "strictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // notStrictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "notStrictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertNotStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // deepStrictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "deepStrictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertDeepStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // notDeepStrictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "notDeepStrictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertNotDeepStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // throws(fn, message?) - 2 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "throws", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.AssertThrows);
            il.Emit(OpCodes.Ldnull);
        });

        // doesNotThrow(fn, message?) - 2 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "doesNotThrow", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.AssertDoesNotThrow);
            il.Emit(OpCodes.Ldnull);
        });

        // fail(message?) - 1 param
        EmitAssertWrapperSimple(typeBuilder, runtime, "fail", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AssertFail);
            il.Emit(OpCodes.Ldnull);
        });

        // equal(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "equal", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // notEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "notEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertNotEqual);
            il.Emit(OpCodes.Ldnull);
        });
    }

    private void EmitAssertWrapperSimple(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator> emitCall)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            $"Assert_{methodName}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();
        emitCall(il);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("assert", methodName, method);
    }
}
