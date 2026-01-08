using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitJsonStringifyFull(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsonStringifyFull",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.JsonStringifyFull = method;

        var il = method.GetILGenerator();

        // Delegate to RuntimeTypes.JsonStringifyFull
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("JsonStringifyFull", [typeof(object), typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    private static MethodBuilder EmitJsonStringifyWithKeysHelper(TypeBuilder typeBuilder)
    {
        // StringifyWithKeys(object value, HashSet<string> allowedKeys, int indent, int depth) -> string
        var method = typeBuilder.DefineMethod(
            "StringifyValueWithKeys",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.HashSetOfString, _types.Int32, _types.Int32]
        );

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumberForFullStringify(il);

        // string
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldnull);
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Ret);

        // List<object> - stringify array with indent
        il.MarkLabel(listLabel);
        EmitStringifyArrayWithIndent(il, method);

        // Dictionary<string, object> - stringify object with indent and allowedKeys
        il.MarkLabel(dictLabel);
        EmitStringifyObjectWithKeysAndIndent(il, method);

        return method;
    }

    private static void EmitFormatNumberForFullStringify(ILGenerator il)
    {
        var local = il.DeclareLocal(_types.Double);
        var isIntLabel = il.DefineLabel();
        var isNanLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, local);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double]));
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", [_types.Double]));
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", [_types.Double]));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, isIntLabel);

        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String]));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isNanLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isIntLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int64, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyArrayWithIndent(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var arrLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var newlineLocal = il.DeclareLocal(_types.String);
        var closeLocal = il.DeclareLocal(_types.String);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var noIndent = il.DefineLabel();
        var hasIndent = il.DefineLabel();
        var doneFormatting = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, arrLocal);

        // if (arr.Count == 0) return "[]";
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // if (indent > 0) compute newline/close strings
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Brfalse, noIndent);

        // newline = "\n" + new string(' ', indent * (depth + 1))
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.Char, _types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String]));
        il.Emit(OpCodes.Stloc, newlineLocal);

        // close = "\n" + new string(' ', indent * depth)
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.Char, _types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String]));
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, hasIndent);

        il.MarkLabel(noIndent);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);

        il.MarkLabel(hasIndent);

        // StringBuilder sb = new StringBuilder("[");
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < arr.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(",");
        il.Emit(OpCodes.Ldloc, iLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValueWithKeys(arr[i], allowedKeys, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringifyObjectWithKeysAndIndent(ILGenerator il, MethodBuilder stringifyMethod)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        // Keep nested enumerator types as typeof() to avoid BadImageFormatException
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object>.Enumerator));
        var currentLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var newlineLocal = il.DeclareLocal(_types.String);
        var closeLocal = il.DeclareLocal(_types.String);
        var keyStrLocal = il.DeclareLocal(_types.String);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var noIndent = il.DefineLabel();
        var hasIndent = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // Compute newline/close if indent > 0
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Brfalse, noIndent);

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.Char, _types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String]));
        il.Emit(OpCodes.Stloc, newlineLocal);

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.Char, _types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String]));
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, hasIndent);

        il.MarkLabel(noIndent);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);

        il.MarkLabel(hasIndent);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // first = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // foreach (var kv in dict) - keep typeof() for nested enumerator types
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, typeof(Dictionary<string, object>.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (allowedKeys != null && !allowedKeys.Contains(kv.Key)) continue;
        var skipKeyCheck = il.DefineLabel();
        var keyAllowed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Brfalse, skipKeyCheck);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfString, "Contains", [_types.String]));
        il.Emit(OpCodes.Brtrue, keyAllowed);
        il.Emit(OpCodes.Br, loopStart); // skip this key

        il.MarkLabel(skipKeyCheck);
        il.MarkLabel(keyAllowed);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // first = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // keyStr = JsonSerializer.Serialize(kv.Key)
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ldnull);
        var serializeMethod = typeof(System.Text.Json.JsonSerializer)
            .GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType.IsGenericParameter &&
                        m.GetParameters()[1].ParameterType == typeof(System.Text.Json.JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));
        il.Emit(OpCodes.Call, serializeMethod);
        il.Emit(OpCodes.Stloc, keyStrLocal);

        // sb.Append(keyStr);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(indent > 0 ? ": " : ":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_2);
        var colonNoSpace = il.DefineLabel();
        var colonDone = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, colonNoSpace);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Br, colonDone);
        il.MarkLabel(colonNoSpace);
        il.Emit(OpCodes.Ldstr, ":");
        il.MarkLabel(colonDone);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValueWithKeys(kv.Value, allowedKeys, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1); // allowedKeys
        il.Emit(OpCodes.Ldarg_2); // indent
        il.Emit(OpCodes.Ldarg_3); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }
}
