using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits util.inspect helper methods (InspectValue, InspectArray, InspectObject, Inspect).
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits InspectValue: dispatches to appropriate handler based on type.
    /// </summary>
    private void EmitUtilInspectValueBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilInspectValue.GetILGenerator();

        var returnNullLabel = il.DefineLabel();
        var checkDepthLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var delegateLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (currentDepth > depth) return "[Object]"
        il.Emit(OpCodes.Ldarg_2); // currentDepth
        il.Emit(OpCodes.Ldarg_1); // depth
        il.Emit(OpCodes.Bgt, checkDepthLabel);

        // Check if string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Check if double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // Check if bool
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // Check if IList<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, arrayLabel);

        // Check if IDictionary<string, object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if Delegate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Delegate));
        il.Emit(OpCodes.Brtrue, delegateLabel);

        // Default: return value.ToString() ?? "undefined"
        il.Emit(OpCodes.Br, defaultLabel);

        // return "null"
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // return "[Object]" (depth exceeded)
        il.MarkLabel(checkDepthLabel);
        il.Emit(OpCodes.Ldstr, "[Object]");
        il.Emit(OpCodes.Ret);

        // String case: return "'" + s + "'"
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ret);

        // Double case: return d.ToString(InvariantCulture)
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        var localDouble = il.DeclareLocal(typeof(double));
        il.Emit(OpCodes.Stloc, localDouble);
        il.Emit(OpCodes.Ldloca, localDouble);
        il.Emit(OpCodes.Call, typeof(System.Globalization.CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.DoubleToStringWithFormat);
        il.Emit(OpCodes.Ret);

        // Bool case: return b ? "true" : "false"
        il.MarkLabel(boolLabel);
        var boolTrueLabel = il.DefineLabel();
        var boolEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolTrueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Br, boolEndLabel);
        il.MarkLabel(boolTrueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.MarkLabel(boolEndLabel);
        il.Emit(OpCodes.Ret);

        // Array case: call InspectArray
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.UtilInspectArray);
        il.Emit(OpCodes.Ret);

        // Dict case: call InspectObject
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.UtilInspectObject);
        il.Emit(OpCodes.Ret);

        // Delegate case: return "[Function]"
        il.MarkLabel(delegateLabel);
        il.Emit(OpCodes.Ldstr, "[Function]");
        il.Emit(OpCodes.Ret);

        // Default case: value.ToString() ?? "undefined"
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits InspectArray: formats array as "[ elem1, elem2, ... ]"
    /// </summary>
    private void EmitUtilInspectArrayBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilInspectArray.GetILGenerator();

        var depthExceededLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopConditionLabel = il.DefineLabel();

        // if (currentDepth >= depth) return "[Array]"
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Bge, depthExceededLabel);

        // var sb = new StringBuilder("[ ")
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Ldstr, "[ ");
        il.Emit(OpCodes.Newobj, _types.StringBuilderStringCtor);
        il.Emit(OpCodes.Stloc, sbLocal);

        // var list = (IList<object?>)arg0
        var listLocal = il.DeclareLocal(_types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, listLocal);

        // var count = list.Count
        var countLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // int i = 0
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopConditionLabel);

        // Loop body
        il.MarkLabel(loopStartLabel);

        // if (i > 0) sb.Append(", ")
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipCommaLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipCommaLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipCommaLabel);

        // sb.Append(InspectValue(list[i], depth, currentDepth + 1))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldarg_1); // depth
        il.Emit(OpCodes.Ldarg_2); // currentDepth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, runtime.UtilInspectValue);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Loop condition: i < count
        il.MarkLabel(loopConditionLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, loopStartLabel);

        // sb.Append(" ]") and return
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " ]");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);

        // Depth exceeded
        il.MarkLabel(depthExceededLabel);
        il.Emit(OpCodes.Ldstr, "[Array]");
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits InspectObject: formats object as "{ key1: val1, key2: val2, ... }"
    /// Uses Keys collection to avoid complex enumerator handling.
    /// </summary>
    private void EmitUtilInspectObjectBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilInspectObject.GetILGenerator();

        var depthExceededLabel = il.DefineLabel();

        // if (currentDepth >= depth) return "[Object]"
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Bge, depthExceededLabel);

        // var sb = new StringBuilder("{ ")
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Ldstr, "{ ");
        il.Emit(OpCodes.Newobj, _types.StringBuilderStringCtor);
        il.Emit(OpCodes.Stloc, sbLocal);

        // var dict = (Dictionary<string, object?>)arg0
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Get keys as a List for indexed access
        // var keys = new List<string>(dict.Keys)
        var keysLocal = il.DeclareLocal(typeof(List<string>));
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.ListStringFromEnumerableCtor);
        il.Emit(OpCodes.Stloc, keysLocal);

        // var count = keys.Count
        var countLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // int i = 0
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStartLabel = il.DefineLabel();
        var loopConditionLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, loopConditionLabel);

        // Loop body
        il.MarkLabel(loopStartLabel);

        // if (i > 0) sb.Append(", ")
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipCommaLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipCommaLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipCommaLabel);

        // var key = keys[i]
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListStringGetItem);
        il.Emit(OpCodes.Stloc, keyLocal);

        // sb.Append(key)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);

        // sb.Append(": ")
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);

        // sb.Append(InspectValue(dict[key], depth, currentDepth + 1))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Ldarg_1); // depth
        il.Emit(OpCodes.Ldarg_2); // currentDepth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, runtime.UtilInspectValue);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Loop condition: i < count
        il.MarkLabel(loopConditionLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, loopStartLabel);

        // sb.Append(" }") and return
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " }");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);

        // Depth exceeded
        il.MarkLabel(depthExceededLabel);
        il.Emit(OpCodes.Ldstr, "[Object]");
        il.Emit(OpCodes.Ret);
    }

}
