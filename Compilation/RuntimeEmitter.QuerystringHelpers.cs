using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits querystring module helper methods.
    /// </summary>
    private void EmitQuerystringMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitQuerystringParse(typeBuilder, runtime);
        EmitQuerystringStringify(typeBuilder, runtime);
        EmitQuerystringMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits wrapper methods for querystring functions that can be used as first-class values.
    /// Uses individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitQuerystringMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // escape(str) - 1 param
        EmitQuerystringWrapperSimple(typeBuilder, runtime, "escape", 1, il =>
        {
            // arg0?.ToString() ?? ""
            var notNull = il.DefineLabel();
            var afterToString = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, notNull);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Br, afterToString);
            il.MarkLabel(notNull);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.MarkLabel(afterToString);
            il.Emit(OpCodes.Call, typeof(Uri).GetMethod("EscapeDataString", [typeof(string)])!);
        });

        // unescape(str) - 1 param
        EmitQuerystringWrapperSimple(typeBuilder, runtime, "unescape", 1, il =>
        {
            // arg0?.ToString() ?? ""
            var notNull = il.DefineLabel();
            var afterToString = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, notNull);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Br, afterToString);
            il.MarkLabel(notNull);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.MarkLabel(afterToString);
            // Replace + with space, then unescape
            il.Emit(OpCodes.Ldc_I4, '+');
            il.Emit(OpCodes.Ldc_I4, ' ');
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.Char, _types.Char));
            il.Emit(OpCodes.Call, typeof(Uri).GetMethod("UnescapeDataString", [typeof(string)])!);
        });

        // parse(str, sep?, eq?) - 3 params
        EmitQuerystringParseWrapper(typeBuilder, runtime);

        // stringify(obj, sep?, eq?) - 3 params
        EmitQuerystringStringifyWrapper(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits a simple wrapper method for a querystring function.
    /// </summary>
    private void EmitQuerystringWrapperSimple(
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
            $"Querystring_{methodName}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();
        emitCall(il);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("querystring", methodName, method);
    }

    private void EmitQuerystringParseWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // parse(str, sep?, eq?) -> takes 3 object params
        var method = typeBuilder.DefineMethod(
            "Querystring_parse_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // str = arg0?.ToString() ?? ""
        var strNotNull = il.DefineLabel();
        var afterStr = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, strNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, afterStr);
        il.MarkLabel(strNotNull);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterStr);

        // sep = arg1?.ToString() ?? "&"
        var sepNotNull = il.DefineLabel();
        var afterSep = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, sepNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "&");
        il.Emit(OpCodes.Br, afterSep);
        il.MarkLabel(sepNotNull);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterSep);

        // eq = arg2?.ToString() ?? "="
        var eqNotNull = il.DefineLabel();
        var afterEq = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, eqNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "=");
        il.Emit(OpCodes.Br, afterEq);
        il.MarkLabel(eqNotNull);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterEq);

        il.Emit(OpCodes.Call, runtime.QuerystringParse);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("querystring", "parse", method);
        runtime.RegisterBuiltInModuleMethod("querystring", "decode", method); // alias
    }

    private void EmitQuerystringStringifyWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // stringify(obj, sep?, eq?) -> takes 3 object params
        var method = typeBuilder.DefineMethod(
            "Querystring_stringify_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // obj = arg0 (pass as-is)
        il.Emit(OpCodes.Ldarg_0);

        // sep = arg1?.ToString() ?? "&"
        var sepNotNull = il.DefineLabel();
        var afterSep = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, sepNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "&");
        il.Emit(OpCodes.Br, afterSep);
        il.MarkLabel(sepNotNull);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterSep);

        // eq = arg2?.ToString() ?? "="
        var eqNotNull = il.DefineLabel();
        var afterEq = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, eqNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "=");
        il.Emit(OpCodes.Br, afterEq);
        il.MarkLabel(eqNotNull);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterEq);

        il.Emit(OpCodes.Call, runtime.QuerystringStringify);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("querystring", "stringify", method);
        runtime.RegisterBuiltInModuleMethod("querystring", "encode", method); // alias
    }

    /// <summary>
    /// Emits: public static object QuerystringParse(string str, string sep, string eq)
    /// Parses a query string into a Dictionary.
    /// </summary>
    private void EmitQuerystringParse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "QuerystringParse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.String, _types.String]
        );
        runtime.QuerystringParse = method;

        var il = method.GetILGenerator();

        // Create new Dictionary<string, object?>
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, resultLocal);

        // If str is null or empty, return empty dict
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); // str
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty", _types.String));
        il.Emit(OpCodes.Brfalse, notEmpty);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // Split string by separator
        // str.Split(sep, StringSplitOptions.RemoveEmptyEntries)
        il.Emit(OpCodes.Ldarg_0); // str
        il.Emit(OpCodes.Ldarg_1); // sep
        il.Emit(OpCodes.Ldc_I4_1); // StringSplitOptions.RemoveEmptyEntries
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Split", _types.String, _types.StringSplitOptions));
        var pairsLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Stloc, pairsLocal);

        // Loop through pairs
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (i >= pairs.Length) goto loopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, pairsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // pair = pairs[i]
        il.Emit(OpCodes.Ldloc, pairsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        var pairLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pairLocal);

        // eqIndex = pair.IndexOf(eq)
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldarg_2); // eq
        il.Emit(OpCodes.Ldc_I4_4); // StringComparison.Ordinal
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String, _types.Resolve("System.StringComparison")));
        var eqIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, eqIndexLocal);

        // Declare key and value locals
        var keyLocal = il.DeclareLocal(_types.String);
        var valueLocal = il.DeclareLocal(_types.String);

        // if (eqIndex >= 0)
        var noEq = il.DefineLabel();
        var afterKV = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, eqIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, noEq);

        // key = pair.Substring(0, eqIndex)
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, eqIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        // Replace + with space
        il.Emit(OpCodes.Ldc_I4, '+');
        il.Emit(OpCodes.Ldc_I4, ' ');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.Char, _types.Char));
        // Uri.UnescapeDataString
        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("UnescapeDataString", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // value = pair.Substring(eqIndex + eq.Length)
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloc, eqIndexLocal);
        il.Emit(OpCodes.Ldarg_2); // eq
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.String, "Length"));
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        // Replace + with space
        il.Emit(OpCodes.Ldc_I4, '+');
        il.Emit(OpCodes.Ldc_I4, ' ');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.Char, _types.Char));
        // Uri.UnescapeDataString
        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("UnescapeDataString", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, afterKV);

        // else: key = pair, value = ""
        il.MarkLabel(noEq);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldc_I4, '+');
        il.Emit(OpCodes.Ldc_I4, ' ');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.Char, _types.Char));
        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("UnescapeDataString", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(afterKV);

        // result[key] = value (simplified - doesn't handle arrays)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string QuerystringStringify(object? obj, string sep, string eq)
    /// Serializes an object into a query string.
    /// </summary>
    private void EmitQuerystringStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "QuerystringStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.String, _types.String]
        );
        runtime.QuerystringStringify = method;

        var il = method.GetILGenerator();

        // If obj is null, return ""
        var notNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNull);

        // Create StringBuilder
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.StringBuilder));
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Stloc, sbLocal);

        // Check if obj is Dictionary<string, object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDict);

        // Cast to Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.DictionaryStringObject, "GetEnumerator"));
        var enumeratorLocal = il.DeclareLocal(_types.DictionaryStringObjectEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var firstLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // Loop through dictionary entries
        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.DictionaryStringObjectEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get current key-value pair
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.DictionaryStringObjectEnumerator, "Current"));
        var kvpLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // If not first, append sep
        var skipSep = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Brtrue, skipSep);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_1); // sep
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipSep);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // Append escaped key
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.KeyValuePairStringObject, "Key"));
        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("EscapeDataString", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append eq
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_2); // eq
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append escaped value
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.KeyValuePairStringObject, "Value"));
        // value?.ToString() ?? ""
        var valueNotNullLabel = il.DefineLabel();
        var afterValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, valueNotNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, afterValueLabel);
        il.MarkLabel(valueNotNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterValueLabel);
        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("EscapeDataString", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        il.MarkLabel(notDict);

        // Return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }
}
