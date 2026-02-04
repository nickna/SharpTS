using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits self-contained util module helper methods into $Runtime.
/// These methods do not require SharpTS.dll at runtime.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all self-contained util helper methods.
    /// Must define all method signatures first, then emit bodies (for recursive calls).
    /// </summary>
    private void EmitUtilStandaloneMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Phase 1: Define all method signatures
        DefineUtilHelperSignatures(typeBuilder, runtime);
        DefineUtilDeepEqualSignature(typeBuilder, runtime);
        DefineUtilParseArgsSignatures(typeBuilder, runtime);

        // Phase 2: Emit all method bodies (can now reference each other)
        EmitUtilInspectValueBody(runtime);
        EmitUtilInspectArrayBody(runtime);
        EmitUtilInspectObjectBody(runtime);
        EmitUtilInspectBody(runtime);
        EmitUtilFormatBody(runtime);
        EmitUtilIsDeepStrictEqualBody(runtime);
        EmitUtilDeepEqualImplBody(runtime);

        // Phase 3: Emit parseArgs helper methods
        EmitUtilParseArgsGetBoolOptionBody(runtime);
        EmitUtilParseArgsGetArgsArrayBody(runtime);
        EmitUtilParseArgsGetOptionsDefBody(runtime);
        EmitUtilParseLongOptionBody(runtime);
        EmitUtilParseShortOptionsBody(runtime);
        EmitUtilParseArgsBody(runtime);
    }

    private void DefineUtilHelperSignatures(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // InspectValue(object value, int depth, int currentDepth) -> string
        runtime.UtilInspectValue = typeBuilder.DefineMethod(
            "UtilInspectValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // InspectArray(object arr, int depth, int currentDepth) -> string
        runtime.UtilInspectArray = typeBuilder.DefineMethod(
            "UtilInspectArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // InspectObject(object obj, int depth, int currentDepth) -> string
        runtime.UtilInspectObject = typeBuilder.DefineMethod(
            "UtilInspectObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // UtilInspect signature already defined in EmitUtilMethods
        // UtilFormat signature already defined in EmitUtilMethods
        // UtilIsDeepStrictEqual signature already defined in EmitUtilMethods
        // UtilParseArgs signature already defined in EmitUtilMethods
    }

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

    /// <summary>
    /// Emits UtilInspect: entry point that extracts depth option and calls InspectValue.
    /// </summary>
    private void EmitUtilInspectBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilInspect.GetILGenerator();

        var depthLocal = il.DeclareLocal(_types.Int32);
        var checkOptionsLabel = il.DefineLabel();
        var callInspectLabel = il.DefineLabel();

        // int depth = 2 (default)
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stloc, depthLocal);

        // if (options != null && options is IDictionary<string, object?>)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, callInspectLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, callInspectLabel);

        // Try to get "depth" from options
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Check if ContainsKey("depth")
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "depth");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("ContainsKey", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, callInspectLabel);

        // Get depth value
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "depth");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, callInspectLabel);

        // Convert to int
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "depth");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, depthLocal);

        // Call InspectValue(obj, depth, 0)
        il.MarkLabel(callInspectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, depthLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.UtilInspectValue);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits UtilFormat: handles format specifiers %s, %d, %i, %f, %j, %o, %O, %%.
    /// </summary>
    private void EmitUtilFormatBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilFormat.GetILGenerator();

        // Locals
        var resultLocal = il.DeclareLocal(typeof(StringBuilder));
        var formatLocal = il.DeclareLocal(_types.String);
        var argIndexLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var argsLengthLocal = il.DeclareLocal(_types.Int32);

        var emptyLabel = il.DefineLabel();
        var singleArgLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopConditionLabel = il.DefineLabel();
        var appendRemaining = il.DefineLabel();
        var appendRemainingLoop = il.DefineLabel();
        var appendRemainingCondition = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (args.Length == 0) return ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // format = args[0]?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Dup);
        var notNullFormatLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullFormatLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, singleArgLabel);
        il.MarkLabel(notNullFormatLabel);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var formatNotNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, formatNotNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(formatNotNullLabel);

        il.MarkLabel(singleArgLabel);
        il.Emit(OpCodes.Stloc, formatLocal);

        // argsLength = args.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLengthLocal);

        // Note: We can't early-return here even with 1 arg because we need to process %% escapes

        // result = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.StringBuilderDefaultCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // argIndex = 1
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, argIndexLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        // length = format.Length
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        il.Emit(OpCodes.Br, loopConditionLabel);

        // Main loop
        il.MarkLabel(loopStartLabel);

        // Check for '%'
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Ldc_I4, '%');
        var notPercentLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notPercentLabel);

        // Check if i + 1 < length
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, notPercentLabel);

        // Get specifier
        var specifierLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, specifierLocal);

        // Handle specifiers with simple fallthrough pattern
        EmitFormatSpecifierHandling(il, runtime, resultLocal, argIndexLocal, iLocal, argsLengthLocal, specifierLocal, loopConditionLabel);

        // Not a format specifier - append character normally
        il.MarkLabel(notPercentLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Loop condition
        il.MarkLabel(loopConditionLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Blt, loopStartLabel);

        // Append remaining arguments
        il.MarkLabel(appendRemaining);
        il.Emit(OpCodes.Br, appendRemainingCondition);

        il.MarkLabel(appendRemainingLoop);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, ' ');
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        // Append args[argIndex]?.ToString() ?? "undefined"
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Dup);
        var argNotNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, argNotNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "undefined");
        var argAppendLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, argAppendLabel);
        il.MarkLabel(argNotNullLabel);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var argStrNotNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, argStrNotNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.MarkLabel(argStrNotNullLabel);
        il.MarkLabel(argAppendLabel);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);

        // argIndex++
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);

        il.MarkLabel(appendRemainingCondition);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Blt, appendRemainingLoop);

        // Return result.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);

        // Empty case
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitFormatSpecifierHandling(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder resultLocal,
        LocalBuilder argIndexLocal,
        LocalBuilder iLocal,
        LocalBuilder argsLengthLocal,
        LocalBuilder specifierLocal,
        Label loopConditionLabel)
    {
        var specifierSLabel = il.DefineLabel();
        var specifierDLabel = il.DefineLabel();
        var specifierFLabel = il.DefineLabel();
        var specifierJLabel = il.DefineLabel();
        var specifierOLabel = il.DefineLabel();
        var specifierPercentLabel = il.DefineLabel();
        var unknownSpecifierLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();

        // Switch on specifier
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 's');
        il.Emit(OpCodes.Beq, specifierSLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 'd');
        il.Emit(OpCodes.Beq, specifierDLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 'i');
        il.Emit(OpCodes.Beq, specifierDLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 'f');
        il.Emit(OpCodes.Beq, specifierFLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 'j');
        il.Emit(OpCodes.Beq, specifierJLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 'o');
        il.Emit(OpCodes.Beq, specifierOLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, 'O');
        il.Emit(OpCodes.Beq, specifierOLabel);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Beq, specifierPercentLabel);

        il.Emit(OpCodes.Br, unknownSpecifierLabel);

        // %s - string
        il.MarkLabel(specifierSLabel);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        var noArgSLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, noArgSLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        EmitToStringOrUndefined(il);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Br, continueLabel);
        il.MarkLabel(noArgSLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "%s");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // %d/%i - integer
        il.MarkLabel(specifierDLabel);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        var noArgDLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, noArgDLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, typeof(double));
        var notDoubleDLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleDLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendInt);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Br, continueLabel);
        il.MarkLabel(notDoubleDLabel);
        il.MarkLabel(noArgDLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // %f - float
        il.MarkLabel(specifierFLabel);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        var noArgFLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, noArgFLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, typeof(double));
        var notDoubleFLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleFLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        var fLocal = il.DeclareLocal(typeof(double));
        il.Emit(OpCodes.Stloc, fLocal);
        il.Emit(OpCodes.Ldloca, fLocal);
        il.Emit(OpCodes.Call, typeof(System.Globalization.CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.DoubleToStringWithFormat);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Br, continueLabel);
        il.MarkLabel(notDoubleFLabel);
        il.MarkLabel(noArgFLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "%f");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // %j - JSON
        il.MarkLabel(specifierJLabel);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        var noArgJLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, noArgJLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        // Call JsonStringify which returns object? (always a string for valid input)
        il.Emit(OpCodes.Call, runtime.JsonStringify);
        // Convert result to string (it's already a string, but cast to be safe)
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Br, continueLabel);
        il.MarkLabel(noArgJLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "%j");
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // %o/%O - object (inspect)
        il.MarkLabel(specifierOLabel);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        var noArgOLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, noArgOLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_2); // depth
        il.Emit(OpCodes.Ldc_I4_0); // currentDepth
        il.Emit(OpCodes.Call, runtime.UtilInspectValue);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendString);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Br, continueLabel);
        il.MarkLabel(noArgOLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // %% - literal percent
        il.MarkLabel(specifierPercentLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // Unknown specifier - just append the character
        il.MarkLabel(unknownSpecifierLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);
        // Don't skip the specifier character, let normal processing handle it
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopConditionLabel);

        // Continue: i += 2 and loop
        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopConditionLabel);
    }

    private void EmitToStringOrUndefined(ILGenerator il)
    {
        // Stack: value
        // Returns: string
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var strNotNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, strNotNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.MarkLabel(strNotNullLabel);
        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Defines the signature for UtilDeepEqualImpl helper.
    /// </summary>
    private void DefineUtilDeepEqualSignature(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // DeepEqualImpl(object a, object b, Dictionary<object, object> seen) -> bool
        runtime.UtilDeepEqualImpl = typeBuilder.DefineMethod(
            "UtilDeepEqualImpl",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object, typeof(Dictionary<object, object>)]);
    }

    /// <summary>
    /// Defines signatures for all parseArgs helper methods.
    /// </summary>
    private void DefineUtilParseArgsSignatures(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetArgsArray(IDictionary<string, object?> config) -> List<object?>
        runtime.UtilParseArgsGetArgsArray = typeBuilder.DefineMethod(
            "UtilParseArgsGetArgsArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObjectNullable,
            [_types.DictionaryStringObject]);

        // GetOptionsDef(IDictionary<string, object?> config) -> Dictionary<string, Dictionary<string, object?>>
        runtime.UtilParseArgsGetOptionsDef = typeBuilder.DefineMethod(
            "UtilParseArgsGetOptionsDef",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Dictionary<string, Dictionary<string, object?>>),
            [_types.DictionaryStringObject]);

        // GetBoolOption(IDictionary<string, object?> config, string name, bool defaultValue) -> bool
        runtime.UtilParseArgsGetBoolOption = typeBuilder.DefineMethod(
            "UtilParseArgsGetBoolOption",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.DictionaryStringObject, _types.String, _types.Boolean]);

        // ParseLongOption(string arg, int index, List<object?> argsArray,
        //                 Dictionary<string, Dictionary<string, object?>> optionsDef,
        //                 Dictionary<string, object?> values, List<object?> tokens,
        //                 bool strict, bool allowNegative, bool returnTokens) -> int
        runtime.UtilParseLongOption = typeBuilder.DefineMethod(
            "UtilParseLongOption",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [
                _types.String,                                          // arg
                _types.Int32,                                           // index
                _types.ListOfObjectNullable,                            // argsArray
                typeof(Dictionary<string, Dictionary<string, object?>>), // optionsDef
                _types.DictionaryStringObject,                          // values
                _types.ListOfObjectNullable,                            // tokens
                _types.Boolean,                                         // strict
                _types.Boolean,                                         // allowNegative
                _types.Boolean                                          // returnTokens
            ]);

        // ParseShortOptions(string arg, int index, List<object?> argsArray,
        //                   Dictionary<string, Dictionary<string, object?>> optionsDef,
        //                   Dictionary<string, object?> values, List<object?> tokens,
        //                   bool strict, bool returnTokens) -> int
        runtime.UtilParseShortOptions = typeBuilder.DefineMethod(
            "UtilParseShortOptions",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [
                _types.String,                                          // arg
                _types.Int32,                                           // index
                _types.ListOfObjectNullable,                            // argsArray
                typeof(Dictionary<string, Dictionary<string, object?>>), // optionsDef
                _types.DictionaryStringObject,                          // values
                _types.ListOfObjectNullable,                            // tokens
                _types.Boolean,                                         // strict
                _types.Boolean                                          // returnTokens
            ]);
    }

    /// <summary>
    /// Emits IsDeepStrictEqual entry point - creates seen dictionary and calls impl.
    /// </summary>
    private void EmitUtilIsDeepStrictEqualBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilIsDeepStrictEqual.GetILGenerator();

        // var seen = new Dictionary<object, object>($ReferenceEqualityComparer.Instance)
        var seenLocal = il.DeclareLocal(typeof(Dictionary<object, object>));
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<object, object>)
            .GetConstructor([typeof(IEqualityComparer<object>)])!);
        il.Emit(OpCodes.Stloc, seenLocal);

        // return DeepEqualImpl(a, b, seen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, seenLocal);
        il.Emit(OpCodes.Call, runtime.UtilDeepEqualImpl);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the recursive DeepEqualImpl body with full comparison logic.
    /// </summary>
    private void EmitUtilDeepEqualImplBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilDeepEqualImpl.GetILGenerator();

        var returnTrue = il.DefineLabel();
        var returnFalse = il.DefineLabel();
        var checkNulls = il.DefineLabel();
        var checkTypes = il.DefineLabel();

        // if (ReferenceEquals(a, b)) return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Beq, returnTrue);

        // if (a == null || b == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnFalse);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalse);

        // Check string
        var checkDouble = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkDouble);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, returnFalse);
        // Both are strings - compare
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Ret);

        // Check double
        il.MarkLabel(checkDouble);
        var checkBool = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, checkBool);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, returnFalse);
        // Both are doubles - compare (with NaN handling)
        EmitDoubleComparison(il, returnTrue, returnFalse);

        // Check bool
        il.MarkLabel(checkBool);
        var checkArray = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, checkArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, returnFalse);
        // Both are bools - compare
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        // Check array (IList<object?>)
        il.MarkLabel(checkArray);
        var checkDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brfalse, checkDict);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brfalse, returnFalse);
        // Both are arrays - check cycle then compare elements
        EmitCycleCheckAndArrayComparison(il, runtime, returnTrue, returnFalse);

        // Check dictionary (Dictionary<string, object?>)
        il.MarkLabel(checkDict);
        var defaultCompare = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, defaultCompare);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, returnFalse);
        // Both are dicts - check cycle then compare entries
        EmitCycleCheckAndDictComparison(il, runtime, returnTrue, returnFalse);

        // Default: use Object.Equals
        il.MarkLabel(defaultCompare);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.ObjectStaticEquals);
        il.Emit(OpCodes.Ret);

        // Return labels
        il.MarkLabel(returnTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits double comparison with NaN handling (NaN === NaN is true for deep equality).
    /// </summary>
    private void EmitDoubleComparison(ILGenerator il, Label returnTrue, Label returnFalse)
    {
        var d1 = il.DeclareLocal(typeof(double));
        var d2 = il.DeclareLocal(typeof(double));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Stloc, d1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Stloc, d2);

        // Check if both are NaN
        var notBothNan = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, d1);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brfalse, notBothNan);
        il.Emit(OpCodes.Ldloc, d2);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brtrue, returnTrue);

        il.MarkLabel(notBothNan);
        // Normal comparison
        il.Emit(OpCodes.Ldloc, d1);
        il.Emit(OpCodes.Ldloc, d2);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits cycle check and array element-by-element comparison.
    /// </summary>
    private void EmitCycleCheckAndArrayComparison(ILGenerator il, EmittedRuntime runtime, Label returnTrue, Label returnFalse)
    {
        var listA = il.DeclareLocal(_types.ListOfObjectNullable);
        var listB = il.DeclareLocal(_types.ListOfObjectNullable);
        var count = il.DeclareLocal(_types.Int32);
        var i = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, listA);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, listB);

        // Cycle check: if seen.TryGetValue(a, out var prev) return ReferenceEquals(b, prev)
        var notInSeen = il.DefineLabel();
        var prevLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2); // seen
        il.Emit(OpCodes.Ldarg_0); // a
        il.Emit(OpCodes.Ldloca, prevLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryObjectObjectTryGetValue);
        il.Emit(OpCodes.Brfalse, notInSeen);
        // Found in seen - return ReferenceEquals(b, prev)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, prevLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notInSeen);
        // Add to seen: seen[a] = b
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<object, object>).GetMethod("set_Item")!);

        // Check counts match
        il.Emit(OpCodes.Ldloc, listA);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, listB);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, returnFalse);

        il.Emit(OpCodes.Ldloc, listA);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, count);

        // Loop: for (i = 0; i < count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, i);
        var loopStart = il.DefineLabel();
        var loopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopStart);
        // if (!DeepEqualImpl(listA[i], listB[i], seen)) return false
        il.Emit(OpCodes.Ldloc, listA);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldloc, listB);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ldarg_2); // seen
        il.Emit(OpCodes.Call, runtime.UtilDeepEqualImpl);
        il.Emit(OpCodes.Brfalse, returnFalse);

        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Blt, loopStart);

        // All elements match
        il.Emit(OpCodes.Br, returnTrue);
    }

    /// <summary>
    /// Emits cycle check and dictionary key-value comparison.
    /// </summary>
    private void EmitCycleCheckAndDictComparison(ILGenerator il, EmittedRuntime runtime, Label returnTrue, Label returnFalse)
    {
        var dictA = il.DeclareLocal(_types.DictionaryStringObject);
        var dictB = il.DeclareLocal(_types.DictionaryStringObject);
        var keys = il.DeclareLocal(typeof(List<string>));
        var count = il.DeclareLocal(_types.Int32);
        var i = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictA);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictB);

        // Cycle check
        var notInSeen = il.DefineLabel();
        var prevLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, prevLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryObjectObjectTryGetValue);
        il.Emit(OpCodes.Brfalse, notInSeen);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, prevLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notInSeen);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<object, object>).GetMethod("set_Item")!);

        // Check counts match
        il.Emit(OpCodes.Ldloc, dictA);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dictB);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, returnFalse);

        // Get keys as list
        il.Emit(OpCodes.Ldloc, dictA);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.ListStringFromEnumerableCtor);
        il.Emit(OpCodes.Stloc, keys);

        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, count);

        // Loop through keys
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, i);
        var loopStart = il.DefineLabel();
        var loopCond = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopStart);
        // key = keys[i]
        il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Callvirt, _types.ListStringGetItem);
        il.Emit(OpCodes.Stloc, key);

        // if (!dictB.ContainsKey(key)) return false
        il.Emit(OpCodes.Ldloc, dictB);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("ContainsKey", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, returnFalse);

        // if (!DeepEqualImpl(dictA[key], dictB[key], seen)) return false
        il.Emit(OpCodes.Ldloc, dictA);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Ldloc, dictB);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.UtilDeepEqualImpl);
        il.Emit(OpCodes.Brfalse, returnFalse);

        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Blt, loopStart);

        // All entries match
        il.Emit(OpCodes.Br, returnTrue);
    }

    /// <summary>
    /// Emits GetBoolOption body - extracts a boolean option from config with default.
    /// GetBoolOption(IDictionary<string, object?> config, string name, bool defaultValue) -> bool
    /// </summary>
    private void EmitUtilParseArgsGetBoolOptionBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilParseArgsGetBoolOption.GetILGenerator();

        var returnDefault = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // if (config == null) return defaultValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnDefault);

        // if (!config.TryGetValue(name, out var val)) return defaultValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, returnDefault);

        // if (val is bool b) return b
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, returnDefault);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Ret);

        // return defaultValue
        il.MarkLabel(returnDefault);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits GetArgsArray body - extracts args array from config.
    /// GetArgsArray(IDictionary<string, object?> config) -> List<object?>
    /// </summary>
    private void EmitUtilParseArgsGetArgsArrayBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilParseArgsGetArgsArray.GetILGenerator();

        var returnEmpty = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // if (config == null) return new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // if (!config.TryGetValue("args", out var argsVal)) return empty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "args");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // if (argsVal is IList<object?> arr) return new List<object?>(arr)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brfalse, returnEmpty);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Newobj, _types.ListObjectFromEnumerableCtor);
        il.Emit(OpCodes.Ret);

        // return new List<object?>()
        il.MarkLabel(returnEmpty);
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableDefaultCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits GetOptionsDef body - extracts options definitions from config.
    /// GetOptionsDef(IDictionary<string, object?> config) -> Dictionary<string, Dictionary<string, object?>>
    /// </summary>
    private void EmitUtilParseArgsGetOptionsDefBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilParseArgsGetOptionsDef.GetILGenerator();
        var resultType = typeof(Dictionary<string, Dictionary<string, object?>>);

        var returnEmpty = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopCondition = il.DefineLabel();

        var resultLocal = il.DeclareLocal(resultType);
        var valueLocal = il.DeclareLocal(_types.Object);
        var optionsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var keysLocal = il.DeclareLocal(typeof(List<string>));
        var countLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.String);
        var optDefLocal = il.DeclareLocal(_types.Object);

        // result = new Dictionary<string, Dictionary<string, object?>>()
        il.Emit(OpCodes.Newobj, resultType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (config == null) return result
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // if (!config.TryGetValue("options", out var optionsVal)) return result
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "options");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // if (optionsVal is not IDictionary<string, object?> options) return result
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, returnEmpty);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, optionsLocal);

        // keys = new List<string>(options.Keys)
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.ListStringFromEnumerableCtor);
        il.Emit(OpCodes.Stloc, keysLocal);

        // count = keys.Count
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCondition);

        // Loop body
        il.MarkLabel(loopStart);

        // key = keys[i]
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListStringGetItem);
        il.Emit(OpCodes.Stloc, keyLocal);

        // optDef = options[key]
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, optDefLocal);

        // if (optDef is IDictionary<string, object?> optDefDict) result[key] = new Dictionary<string, object?>(optDefDict)
        var skipAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, skipAdd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([typeof(IDictionary<string, object?>)])!);
        il.Emit(OpCodes.Callvirt, resultType.GetMethod("set_Item", [typeof(string), typeof(Dictionary<string, object?>)])!);

        il.MarkLabel(skipAdd);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Loop condition: i < count
        il.MarkLabel(loopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, loopStart);

        // return result
        il.MarkLabel(returnEmpty);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ParseLongOption body - parses --option and --option=value arguments.
    /// </summary>
    private void EmitUtilParseLongOptionBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilParseLongOption.GetILGenerator();
        var optionsDefType = typeof(Dictionary<string, Dictionary<string, object?>>);

        // Locals
        var nameLocal = il.DeclareLocal(_types.String);           // loc.0
        var inlineValueLocal = il.DeclareLocal(_types.String);    // loc.1
        var hasInlineValueLocal = il.DeclareLocal(_types.Boolean); // loc.2
        var eqIndexLocal = il.DeclareLocal(_types.Int32);         // loc.3
        var isNegatedLocal = il.DeclareLocal(_types.Boolean);     // loc.4
        var originalNameLocal = il.DeclareLocal(_types.String);   // loc.5
        var optDefLocal = il.DeclareLocal(typeof(Dictionary<string, object?>)); // loc.6
        var optTypeLocal = il.DeclareLocal(_types.String);        // loc.7
        var multipleLocal = il.DeclareLocal(_types.Boolean);      // loc.8
        var valueLocal = il.DeclareLocal(_types.Object);          // loc.9
        var indexLocal = il.DeclareLocal(_types.Int32);           // loc.10 - working copy of index
        var typeValLocal = il.DeclareLocal(_types.Object);        // loc.11
        var mValLocal = il.DeclareLocal(_types.Object);           // loc.12
        var existingLocal = il.DeclareLocal(_types.Object);       // loc.13
        var listLocal = il.DeclareLocal(_types.ListOfObjectNullable); // loc.14

        // Labels
        var noInlineValue = il.DefineLabel();
        var checkNegation = il.DefineLabel();
        var afterNegation = il.DefineLabel();
        var unknownOption = il.DefineLabel();
        var isBooleanType = il.DefineLabel();
        var isStringType = il.DefineLabel();
        var afterValueExtraction = il.DefineLabel();
        var storeValue = il.DefineLabel();
        var checkMultiple = il.DefineLabel();
        var notMultiple = il.DefineLabel();
        var addTokens = il.DefineLabel();
        var returnIndex = il.DefineLabel();

        // index = arg1 (working copy)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        // hasInlineValue = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasInlineValueLocal);

        // inlineValue = null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, inlineValueLocal);

        // eqIndex = arg.IndexOf('=')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, '=');
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("IndexOf", [typeof(char)])!);
        il.Emit(OpCodes.Stloc, eqIndexLocal);

        // if (eqIndex > 0) { name = arg[2..eqIndex]; inlineValue = arg[(eqIndex+1)..]; hasInlineValue = true }
        il.Emit(OpCodes.Ldloc, eqIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noInlineValue);

        // Has inline value
        // name = arg.Substring(2, eqIndex - 2)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, eqIndexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // inlineValue = arg.Substring(eqIndex + 1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, eqIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, inlineValueLocal);

        // hasInlineValue = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasInlineValueLocal);
        il.Emit(OpCodes.Br, checkNegation);

        // No inline value
        il.MarkLabel(noInlineValue);
        // name = arg.Substring(2)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // Check for negation (--no-xxx)
        il.MarkLabel(checkNegation);
        // isNegated = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isNegatedLocal);

        // originalName = name
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Stloc, originalNameLocal);

        // if (allowNegative && name.StartsWith("no-"))
        il.Emit(OpCodes.Ldarg, 7); // allowNegative
        il.Emit(OpCodes.Brfalse, afterNegation);

        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "no-");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, afterNegation);

        // positiveName = name.Substring(3)
        var positiveNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, positiveNameLocal);

        // if (optionsDef.TryGetValue(positiveName, out var posDef) && posDef["type"] == "boolean")
        var posDefLocal = il.DeclareLocal(typeof(Dictionary<string, object?>));
        var skipNegation = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3); // optionsDef
        il.Emit(OpCodes.Ldloc, positiveNameLocal);
        il.Emit(OpCodes.Ldloca, posDefLocal);
        il.Emit(OpCodes.Callvirt, optionsDefType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, skipNegation);

        // Check if type is "boolean"
        il.Emit(OpCodes.Ldloc, posDefLocal);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldloca, typeValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, skipNegation);

        il.Emit(OpCodes.Ldloc, typeValLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, skipNegation);

        // name = positiveName; isNegated = true
        il.Emit(OpCodes.Ldloc, positiveNameLocal);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isNegatedLocal);

        il.MarkLabel(skipNegation);
        il.MarkLabel(afterNegation);

        // if (!optionsDef.TryGetValue(name, out optDef)) goto unknownOption
        var foundOption = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3); // optionsDef
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, optDefLocal);
        il.Emit(OpCodes.Callvirt, optionsDefType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brtrue, foundOption); // Jump to foundOption if TryGetValue returns true
        il.Emit(OpCodes.Br, unknownOption);   // Otherwise go to unknownOption

        il.MarkLabel(foundOption);

        // Option found - extract type and multiple flag
        // optType = optDef.TryGetValue("type", out t) ? t?.ToString() : "boolean"
        var defaultType = il.DefineLabel();
        var afterType = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldloca, typeValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, defaultType);

        il.Emit(OpCodes.Ldloc, typeValLocal);
        il.Emit(OpCodes.Brfalse, defaultType);

        il.Emit(OpCodes.Ldloc, typeValLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, optTypeLocal);
        il.Emit(OpCodes.Br, afterType);

        il.MarkLabel(defaultType);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Stloc, optTypeLocal);

        il.MarkLabel(afterType);

        // multiple = optDef.TryGetValue("multiple", out m) && m is true
        var notMultipleLabel = il.DefineLabel();
        var afterMultiple = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, multipleLocal);

        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Ldstr, "multiple");
        il.Emit(OpCodes.Ldloca, mValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, afterMultiple);

        il.Emit(OpCodes.Ldloc, mValLocal);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, afterMultiple);

        il.Emit(OpCodes.Ldloc, mValLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Stloc, multipleLocal);

        il.MarkLabel(afterMultiple);

        // Now handle value extraction based on type
        // if (optType == "boolean")
        il.Emit(OpCodes.Ldloc, optTypeLocal);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, isBooleanType);
        il.Emit(OpCodes.Br, isStringType);

        // Boolean type handling
        il.MarkLabel(isBooleanType);
        // if (hasInlineValue && strict) throw
        var boolNoError = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hasInlineValueLocal);
        il.Emit(OpCodes.Brfalse, boolNoError);
        il.Emit(OpCodes.Ldarg, 6); // strict
        il.Emit(OpCodes.Brfalse, boolNoError);

        // throw new Exception("Option '--{name}' does not take an argument")
        il.Emit(OpCodes.Ldstr, "Option '--");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' does not take an argument");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(boolNoError);
        // value = !isNegated (box bool)
        il.Emit(OpCodes.Ldloc, isNegatedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Stloc, valueLocal);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // String type handling
        il.MarkLabel(isStringType);

        // if (isNegated && strict) throw
        var stringNoNegateError = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isNegatedLocal);
        il.Emit(OpCodes.Brfalse, stringNoNegateError);
        il.Emit(OpCodes.Ldarg, 6); // strict
        il.Emit(OpCodes.Brfalse, stringNoNegateError);

        il.Emit(OpCodes.Ldstr, "Option '--");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' cannot be negated");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(stringNoNegateError);

        // if (hasInlineValue) { value = inlineValue; index++ }
        var noInlineForString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hasInlineValueLocal);
        il.Emit(OpCodes.Brfalse, noInlineForString);

        il.Emit(OpCodes.Ldloc, inlineValueLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // else if (index + 1 < argsArray.Count)
        il.MarkLabel(noInlineForString);
        var noNextArg = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_2); // argsArray
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, noNextArg);

        // value = argsArray[index + 1]?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Dup);
        var notNullArg = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullArg);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var afterNullCheck = il.DefineLabel();
        il.Emit(OpCodes.Br, afterNullCheck);
        il.MarkLabel(notNullArg);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var notNullStr = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullStr);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(notNullStr);
        il.MarkLabel(afterNullCheck);
        il.Emit(OpCodes.Stloc, valueLocal);

        // index += 2
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // No next argument available
        il.MarkLabel(noNextArg);
        // if (strict) throw
        var noStrictError = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 6); // strict
        il.Emit(OpCodes.Brfalse, noStrictError);

        il.Emit(OpCodes.Ldstr, "Option '--");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' requires an argument");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(noStrictError);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // Unknown option handling
        il.MarkLabel(unknownOption);
        // if (strict) throw
        var noUnknownError = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 6); // strict
        il.Emit(OpCodes.Brfalse, noUnknownError);

        il.Emit(OpCodes.Ldstr, "Unknown option '--");
        il.Emit(OpCodes.Ldloc, originalNameLocal);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(noUnknownError);
        // values[name] = !isNegated
        il.Emit(OpCodes.Ldarg, 4); // values
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloc, isNegatedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // return index + 1
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);

        // Check if multiple values
        il.MarkLabel(checkMultiple);
        il.Emit(OpCodes.Ldloc, multipleLocal);
        il.Emit(OpCodes.Brfalse, notMultiple);

        // Multiple: add to list
        // if (!values.TryGetValue(name, out existing) || existing is not IList<object?>)
        var hasExistingList = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 4); // values
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, existingLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse_S, (byte)0);
        var createNewList = il.DefineLabel();
        il.Emit(OpCodes.Br, createNewList);

        // Check if existing is list
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, hasExistingList);

        // Create new list
        il.MarkLabel(createNewList);
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableDefaultCtor);
        il.Emit(OpCodes.Stloc, listLocal);

        // values[name] = list
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        var afterListSetup = il.DefineLabel();
        il.Emit(OpCodes.Br, afterListSetup);

        il.MarkLabel(hasExistingList);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, listLocal);

        il.MarkLabel(afterListSetup);
        // list.Add(value)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Br, addTokens);

        // Not multiple: just set value
        il.MarkLabel(notMultiple);
        il.Emit(OpCodes.Ldarg, 4); // values
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // Add tokens if returnTokens is true
        il.MarkLabel(addTokens);
        il.Emit(OpCodes.Ldarg, 8); // returnTokens
        il.Emit(OpCodes.Brfalse, returnIndex);

        // tokens.Add(token dict) - simplified, just add basic token
        var tokenDict = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, tokenDict);

        // token["kind"] = "option"
        il.Emit(OpCodes.Ldloc, tokenDict);
        il.Emit(OpCodes.Ldstr, "kind");
        il.Emit(OpCodes.Ldstr, "option");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // token["name"] = name
        il.Emit(OpCodes.Ldloc, tokenDict);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // tokens.Add(token)
        il.Emit(OpCodes.Ldarg, 5); // tokens
        il.Emit(OpCodes.Ldloc, tokenDict);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        // Return index
        il.MarkLabel(returnIndex);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ParseShortOptions body - parses -v and -abc style short options.
    /// </summary>
    private void EmitUtilParseShortOptionsBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilParseShortOptions.GetILGenerator();
        var optionsDefType = typeof(Dictionary<string, Dictionary<string, object?>>);

        // Locals
        var shortOptsLocal = il.DeclareLocal(_types.String);      // loc.0 - arg[1..]
        var jLocal = il.DeclareLocal(_types.Int32);               // loc.1 - inner loop index
        var shortCharLocal = il.DeclareLocal(_types.String);      // loc.2 - current short char as string
        var optNameLocal = il.DeclareLocal(_types.String);        // loc.3 - found option name
        var optDefLocal = il.DeclareLocal(typeof(Dictionary<string, object?>)); // loc.4
        var optTypeLocal = il.DeclareLocal(_types.String);        // loc.5
        var multipleLocal = il.DeclareLocal(_types.Boolean);      // loc.6
        var valueLocal = il.DeclareLocal(_types.Object);          // loc.7
        var indexLocal = il.DeclareLocal(_types.Int32);           // loc.8 - working copy
        var keysLocal = il.DeclareLocal(typeof(List<string>));    // loc.9
        var kLocal = il.DeclareLocal(_types.Int32);               // loc.10 - keys loop index
        var keyLocal = il.DeclareLocal(_types.String);            // loc.11
        var defLocal = il.DeclareLocal(typeof(Dictionary<string, object?>)); // loc.12
        var shortValLocal = il.DeclareLocal(_types.Object);       // loc.13
        var typeValLocal = il.DeclareLocal(_types.Object);        // loc.14
        var mValLocal = il.DeclareLocal(_types.Object);           // loc.15
        var existingLocal = il.DeclareLocal(_types.Object);       // loc.16
        var listLocal = il.DeclareLocal(_types.ListOfObjectNullable); // loc.17

        // Labels
        var outerLoopStart = il.DefineLabel();
        var outerLoopCondition = il.DefineLabel();
        var innerLoopStart = il.DefineLabel();
        var innerLoopCondition = il.DefineLabel();
        var foundOption = il.DefineLabel();
        var afterInnerLoop = il.DefineLabel();
        var unknownShort = il.DefineLabel();
        var isBoolType = il.DefineLabel();
        var isStrType = il.DefineLabel();
        var checkMultiple = il.DefineLabel();
        var notMultiple = il.DefineLabel();
        var addTokens = il.DefineLabel();
        var continueOuter = il.DefineLabel();
        var returnIndex = il.DefineLabel();

        // index = arg1
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        // shortOpts = arg.Substring(1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, shortOptsLocal);

        // j = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, outerLoopCondition);

        // Outer loop: for each character in shortOpts
        il.MarkLabel(outerLoopStart);

        // shortChar = shortOpts[j].ToString()
        il.Emit(OpCodes.Ldloc, shortOptsLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
        var charLocal = il.DeclareLocal(typeof(char));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, shortCharLocal);

        // optName = null; optDef = null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, optNameLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, optDefLocal);

        // Inner loop: find matching option in optionsDef
        // keys = new List<string>(optionsDef.Keys)
        il.Emit(OpCodes.Ldarg_3); // optionsDef
        il.Emit(OpCodes.Callvirt, optionsDefType.GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.ListStringFromEnumerableCtor);
        il.Emit(OpCodes.Stloc, keysLocal);

        // k = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, innerLoopCondition);

        // Inner loop body
        il.MarkLabel(innerLoopStart);

        // key = keys[k]
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Callvirt, _types.ListStringGetItem);
        il.Emit(OpCodes.Stloc, keyLocal);

        // def = optionsDef[key]
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, optionsDefType.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, defLocal);

        // if (def.TryGetValue("short", out shortVal) && shortVal?.ToString() == shortChar)
        var nextKey = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, defLocal);
        il.Emit(OpCodes.Ldstr, "short");
        il.Emit(OpCodes.Ldloca, shortValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, nextKey);

        il.Emit(OpCodes.Ldloc, shortValLocal);
        il.Emit(OpCodes.Brfalse, nextKey);

        il.Emit(OpCodes.Ldloc, shortValLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldloc, shortCharLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, nextKey);

        // Found match
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Stloc, optNameLocal);
        il.Emit(OpCodes.Ldloc, defLocal);
        il.Emit(OpCodes.Stloc, optDefLocal);
        il.Emit(OpCodes.Br, foundOption);

        il.MarkLabel(nextKey);
        // k++
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);

        // Inner loop condition: k < keys.Count
        il.MarkLabel(innerLoopCondition);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, innerLoopStart);

        // After inner loop - no match found
        il.MarkLabel(afterInnerLoop);
        // if (optName == null)
        il.Emit(OpCodes.Ldloc, optNameLocal);
        il.Emit(OpCodes.Brfalse, unknownShort);
        il.Emit(OpCodes.Br, foundOption);

        // Unknown short option
        il.MarkLabel(unknownShort);
        // if (strict) throw
        var noUnknownError = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 6); // strict
        il.Emit(OpCodes.Brfalse, noUnknownError);

        il.Emit(OpCodes.Ldstr, "Unknown option '-");
        il.Emit(OpCodes.Ldloc, shortCharLocal);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(noUnknownError);
        // continue to next char
        il.Emit(OpCodes.Br, continueOuter);

        // Found option - process it
        il.MarkLabel(foundOption);

        // optType = optDef.TryGetValue("type", ...) ? ... : "boolean"
        var defaultType = il.DefineLabel();
        var afterType = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldloca, typeValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, defaultType);

        il.Emit(OpCodes.Ldloc, typeValLocal);
        il.Emit(OpCodes.Brfalse, defaultType);

        il.Emit(OpCodes.Ldloc, typeValLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, optTypeLocal);
        il.Emit(OpCodes.Br, afterType);

        il.MarkLabel(defaultType);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Stloc, optTypeLocal);

        il.MarkLabel(afterType);

        // multiple = optDef.TryGetValue("multiple", ...) && ...
        var afterMultiple = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, multipleLocal);

        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Ldstr, "multiple");
        il.Emit(OpCodes.Ldloca, mValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, afterMultiple);

        il.Emit(OpCodes.Ldloc, mValLocal);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, afterMultiple);

        il.Emit(OpCodes.Ldloc, mValLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Stloc, multipleLocal);

        il.MarkLabel(afterMultiple);

        // if (optType == "boolean") value = true
        il.Emit(OpCodes.Ldloc, optTypeLocal);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, isBoolType);
        il.Emit(OpCodes.Br, isStrType);

        il.MarkLabel(isBoolType);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // String type - get value from rest of shortOpts or next arg
        il.MarkLabel(isStrType);

        // if (j + 1 < shortOpts.Length) { value = shortOpts[(j+1)..]; j = shortOpts.Length }
        var noInlineShort = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, shortOptsLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, noInlineShort);

        // value = shortOpts.Substring(j + 1)
        il.Emit(OpCodes.Ldloc, shortOptsLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // j = shortOpts.Length (exit outer loop after this)
        il.Emit(OpCodes.Ldloc, shortOptsLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // else if (index + 1 < argsArray.Count)
        il.MarkLabel(noInlineShort);
        var noNextArg = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_2); // argsArray
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, noNextArg);

        // value = argsArray[index + 1]?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Dup);
        var notNullArg = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullArg);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var afterNull = il.DefineLabel();
        il.Emit(OpCodes.Br, afterNull);
        il.MarkLabel(notNullArg);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var notNullStr = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullStr);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(notNullStr);
        il.MarkLabel(afterNull);
        il.Emit(OpCodes.Stloc, valueLocal);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, checkMultiple);

        // No argument available
        il.MarkLabel(noNextArg);
        // if (strict) throw
        var noStrictErr = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 6); // strict
        il.Emit(OpCodes.Brfalse, noStrictErr);

        il.Emit(OpCodes.Ldstr, "Option '-");
        il.Emit(OpCodes.Ldloc, shortCharLocal);
        il.Emit(OpCodes.Ldstr, "' requires an argument");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(noStrictErr);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, valueLocal);

        // Check multiple
        il.MarkLabel(checkMultiple);
        il.Emit(OpCodes.Ldloc, multipleLocal);
        il.Emit(OpCodes.Brfalse, notMultiple);

        // Multiple: add to list
        var hasExistingList = il.DefineLabel();
        var createNewList = il.DefineLabel();
        var afterListSetup = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, 4); // values
        il.Emit(OpCodes.Ldloc, optNameLocal);
        il.Emit(OpCodes.Ldloca, existingLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, createNewList);

        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, hasExistingList);

        il.MarkLabel(createNewList);
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableDefaultCtor);
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, optNameLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Br, afterListSetup);

        il.MarkLabel(hasExistingList);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Stloc, listLocal);

        il.MarkLabel(afterListSetup);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Br, addTokens);

        // Not multiple
        il.MarkLabel(notMultiple);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, optNameLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // Add tokens if needed
        il.MarkLabel(addTokens);
        il.Emit(OpCodes.Ldarg, 7); // returnTokens
        il.Emit(OpCodes.Brfalse, continueOuter);

        // Simplified token
        var tokenDict = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, tokenDict);

        il.Emit(OpCodes.Ldloc, tokenDict);
        il.Emit(OpCodes.Ldstr, "kind");
        il.Emit(OpCodes.Ldstr, "option");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        il.Emit(OpCodes.Ldloc, tokenDict);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, optNameLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        il.Emit(OpCodes.Ldarg, 5); // tokens
        il.Emit(OpCodes.Ldloc, tokenDict);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        // Continue outer loop
        il.MarkLabel(continueOuter);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);

        // Outer loop condition: j < shortOpts.Length
        il.MarkLabel(outerLoopCondition);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, shortOptsLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, outerLoopStart);

        // Return index + 1
        il.MarkLabel(returnIndex);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ParseArgs body - the main argument parsing entry point.
    /// </summary>
    private void EmitUtilParseArgsBody(EmittedRuntime runtime)
    {
        var il = runtime.UtilParseArgs.GetILGenerator();
        var optionsDefType = typeof(Dictionary<string, Dictionary<string, object?>>);

        // Locals
        var configDictLocal = il.DeclareLocal(_types.DictionaryStringObject);     // loc.0
        var argsArrayLocal = il.DeclareLocal(_types.ListOfObjectNullable);        // loc.1
        var optionsDefLocal = il.DeclareLocal(optionsDefType);                    // loc.2
        var strictLocal = il.DeclareLocal(_types.Boolean);                        // loc.3
        var allowPositionalsLocal = il.DeclareLocal(_types.Boolean);              // loc.4
        var allowNegativeLocal = il.DeclareLocal(_types.Boolean);                 // loc.5
        var returnTokensLocal = il.DeclareLocal(_types.Boolean);                  // loc.6
        var valuesLocal = il.DeclareLocal(_types.DictionaryStringObject);         // loc.7
        var positionalsLocal = il.DeclareLocal(_types.ListOfObjectNullable);      // loc.8
        var tokensLocal = il.DeclareLocal(_types.ListOfObjectNullable);           // loc.9
        var iLocal = il.DeclareLocal(_types.Int32);                               // loc.10
        var argLocal = il.DeclareLocal(_types.String);                            // loc.11
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);         // loc.12
        var keysLocal = il.DeclareLocal(typeof(List<string>));                    // loc.13
        var kLocal = il.DeclareLocal(_types.Int32);                               // loc.14
        var keyLocal = il.DeclareLocal(_types.String);                            // loc.15
        var optDefLocal = il.DeclareLocal(typeof(Dictionary<string, object?>));   // loc.16
        var defaultValLocal = il.DeclareLocal(_types.Object);                     // loc.17

        // Labels
        var mainLoopStart = il.DefineLabel();
        var mainLoopCondition = il.DefineLabel();
        var checkTerminator = il.DefineLabel();
        var checkLongOption = il.DefineLabel();
        var checkShortOption = il.DefineLabel();
        var handlePositional = il.DefineLabel();
        var afterTerminator = il.DefineLabel();
        var terminatorLoop = il.DefineLabel();
        var terminatorLoopCond = il.DefineLabel();
        var buildResult = il.DefineLabel();
        var defaultsLoopStart = il.DefineLabel();
        var defaultsLoopCond = il.DefineLabel();

        // Cast config to dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, configDictLocal);

        // argsArray = GetArgsArray(configDict)
        il.Emit(OpCodes.Ldloc, configDictLocal);
        il.Emit(OpCodes.Call, runtime.UtilParseArgsGetArgsArray);
        il.Emit(OpCodes.Stloc, argsArrayLocal);

        // optionsDef = GetOptionsDef(configDict)
        il.Emit(OpCodes.Ldloc, configDictLocal);
        il.Emit(OpCodes.Call, runtime.UtilParseArgsGetOptionsDef);
        il.Emit(OpCodes.Stloc, optionsDefLocal);

        // strict = GetBoolOption(configDict, "strict", true)
        il.Emit(OpCodes.Ldloc, configDictLocal);
        il.Emit(OpCodes.Ldstr, "strict");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.UtilParseArgsGetBoolOption);
        il.Emit(OpCodes.Stloc, strictLocal);

        // allowPositionals = GetBoolOption(configDict, "allowPositionals", !strict)
        il.Emit(OpCodes.Ldloc, configDictLocal);
        il.Emit(OpCodes.Ldstr, "allowPositionals");
        il.Emit(OpCodes.Ldloc, strictLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq); // !strict
        il.Emit(OpCodes.Call, runtime.UtilParseArgsGetBoolOption);
        il.Emit(OpCodes.Stloc, allowPositionalsLocal);

        // allowNegative = GetBoolOption(configDict, "allowNegative", false)
        il.Emit(OpCodes.Ldloc, configDictLocal);
        il.Emit(OpCodes.Ldstr, "allowNegative");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.UtilParseArgsGetBoolOption);
        il.Emit(OpCodes.Stloc, allowNegativeLocal);

        // returnTokens = GetBoolOption(configDict, "tokens", false)
        il.Emit(OpCodes.Ldloc, configDictLocal);
        il.Emit(OpCodes.Ldstr, "tokens");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.UtilParseArgsGetBoolOption);
        il.Emit(OpCodes.Stloc, returnTokensLocal);

        // values = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, valuesLocal);

        // positionals = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableDefaultCtor);
        il.Emit(OpCodes.Stloc, positionalsLocal);

        // tokens = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableDefaultCtor);
        il.Emit(OpCodes.Stloc, tokensLocal);

        // Apply defaults from optionsDef
        // keys = new List<string>(optionsDef.Keys)
        il.Emit(OpCodes.Ldloc, optionsDefLocal);
        il.Emit(OpCodes.Callvirt, optionsDefType.GetProperty("Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.ListStringFromEnumerableCtor);
        il.Emit(OpCodes.Stloc, keysLocal);

        // k = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, defaultsLoopCond);

        // Defaults loop
        il.MarkLabel(defaultsLoopStart);
        // key = keys[k]
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Callvirt, _types.ListStringGetItem);
        il.Emit(OpCodes.Stloc, keyLocal);

        // optDef = optionsDef[key]
        il.Emit(OpCodes.Ldloc, optionsDefLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, optionsDefType.GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, optDefLocal);

        // if (optDef.TryGetValue("default", out defaultVal) && defaultVal != null)
        var skipDefault = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optDefLocal);
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Ldloca, defaultValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableTryGetValue);
        il.Emit(OpCodes.Brfalse, skipDefault);

        il.Emit(OpCodes.Ldloc, defaultValLocal);
        il.Emit(OpCodes.Brfalse, skipDefault);

        // values[key] = defaultVal
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, defaultValLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        il.MarkLabel(skipDefault);
        // k++
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);

        il.MarkLabel(defaultsLoopCond);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, defaultsLoopStart);

        // Main parsing loop: i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, mainLoopCondition);

        // Main loop body
        il.MarkLabel(mainLoopStart);

        // arg = argsArray[i]?.ToString() ?? ""
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Dup);
        var argNotNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, argNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var argReady = il.DefineLabel();
        il.Emit(OpCodes.Br, argReady);
        il.MarkLabel(argNotNull);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var strNotNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, strNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(strNotNull);
        il.MarkLabel(argReady);
        il.Emit(OpCodes.Stloc, argLocal);

        // if (arg == "--")
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldstr, "--");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, checkTerminator);

        // else if (arg.StartsWith("--"))
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldstr, "--");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, checkLongOption);

        // else if (arg.StartsWith("-") && arg.Length > 1)
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldstr, "-");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, handlePositional);

        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, handlePositional);
        il.Emit(OpCodes.Br, checkShortOption);

        // Handle option terminator "--"
        il.MarkLabel(checkTerminator);
        // Add terminator token if needed
        il.Emit(OpCodes.Ldloc, returnTokensLocal);
        var skipTermToken = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipTermToken);

        var termTokenDict = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, termTokenDict);
        il.Emit(OpCodes.Ldloc, termTokenDict);
        il.Emit(OpCodes.Ldstr, "kind");
        il.Emit(OpCodes.Ldstr, "option-terminator");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, termTokenDict);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, tokensLocal);
        il.Emit(OpCodes.Ldloc, termTokenDict);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(skipTermToken);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Rest are positionals
        il.Emit(OpCodes.Br, terminatorLoopCond);

        il.MarkLabel(terminatorLoop);
        // arg = argsArray[i]?.ToString() ?? ""
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Dup);
        var termArgNotNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, termArgNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var termArgReady = il.DefineLabel();
        il.Emit(OpCodes.Br, termArgReady);
        il.MarkLabel(termArgNotNull);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var termStrNotNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, termStrNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(termStrNotNull);
        il.MarkLabel(termArgReady);
        il.Emit(OpCodes.Stloc, argLocal);

        // if (!allowPositionals && strict) throw
        var allowTermPositional = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, allowPositionalsLocal);
        il.Emit(OpCodes.Brtrue, allowTermPositional);
        il.Emit(OpCodes.Ldloc, strictLocal);
        il.Emit(OpCodes.Brfalse, allowTermPositional);

        il.Emit(OpCodes.Ldstr, "Unexpected argument: ");
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(allowTermPositional);
        // positionals.Add(arg)
        il.Emit(OpCodes.Ldloc, positionalsLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        // Add positional token if needed
        il.Emit(OpCodes.Ldloc, returnTokensLocal);
        var skipPosToken2 = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipPosToken2);

        var posTokenDict2 = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, posTokenDict2);
        il.Emit(OpCodes.Ldloc, posTokenDict2);
        il.Emit(OpCodes.Ldstr, "kind");
        il.Emit(OpCodes.Ldstr, "positional");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, posTokenDict2);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, posTokenDict2);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, tokensLocal);
        il.Emit(OpCodes.Ldloc, posTokenDict2);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(skipPosToken2);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(terminatorLoopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, terminatorLoop);

        // After terminator handling, go to build result
        il.Emit(OpCodes.Br, buildResult);

        // Handle long option (--xxx)
        il.MarkLabel(checkLongOption);
        // i = ParseLongOption(arg, i, argsArray, optionsDef, values, tokens, strict, allowNegative, returnTokens)
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, optionsDefLocal);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldloc, tokensLocal);
        il.Emit(OpCodes.Ldloc, strictLocal);
        il.Emit(OpCodes.Ldloc, allowNegativeLocal);
        il.Emit(OpCodes.Ldloc, returnTokensLocal);
        il.Emit(OpCodes.Call, runtime.UtilParseLongOption);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, mainLoopCondition);

        // Handle short option (-x)
        il.MarkLabel(checkShortOption);
        // i = ParseShortOptions(arg, i, argsArray, optionsDef, values, tokens, strict, returnTokens)
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Ldloc, optionsDefLocal);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldloc, tokensLocal);
        il.Emit(OpCodes.Ldloc, strictLocal);
        il.Emit(OpCodes.Ldloc, returnTokensLocal);
        il.Emit(OpCodes.Call, runtime.UtilParseShortOptions);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, mainLoopCondition);

        // Handle positional argument
        il.MarkLabel(handlePositional);
        // if (!allowPositionals && strict) throw
        var allowPos = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, allowPositionalsLocal);
        il.Emit(OpCodes.Brtrue, allowPos);
        il.Emit(OpCodes.Ldloc, strictLocal);
        il.Emit(OpCodes.Brfalse, allowPos);

        il.Emit(OpCodes.Ldstr, "Unexpected argument: ");
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(allowPos);
        // positionals.Add(arg)
        il.Emit(OpCodes.Ldloc, positionalsLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        // Add positional token if needed
        il.Emit(OpCodes.Ldloc, returnTokensLocal);
        var skipPosToken = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipPosToken);

        var posTokenDict = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, posTokenDict);
        il.Emit(OpCodes.Ldloc, posTokenDict);
        il.Emit(OpCodes.Ldstr, "kind");
        il.Emit(OpCodes.Ldstr, "positional");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, posTokenDict);
        il.Emit(OpCodes.Ldstr, "index");
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, posTokenDict);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ldloc, tokensLocal);
        il.Emit(OpCodes.Ldloc, posTokenDict);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(skipPosToken);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Main loop condition: i < argsArray.Count
        il.MarkLabel(mainLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, mainLoopStart);

        // Build result object
        il.MarkLabel(buildResult);
        // result = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // result["values"] = values
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "values");
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // result["positionals"] = positionals
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "positionals");
        il.Emit(OpCodes.Ldloc, positionalsLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        // if (returnTokens) result["tokens"] = tokens
        var skipTokensResult = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, returnTokensLocal);
        il.Emit(OpCodes.Brfalse, skipTokensResult);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "tokens");
        il.Emit(OpCodes.Ldloc, tokensLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [typeof(string), typeof(object)])!);

        il.MarkLabel(skipTokensResult);
        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
