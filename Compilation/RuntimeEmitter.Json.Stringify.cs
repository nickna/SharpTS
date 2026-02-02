using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _escapeJsonStringMethod;

    /// <summary>
    /// Emits a helper method that escapes a string for JSON output.
    /// This replaces dependency on System.Text.Json.JsonSerializer.
    /// </summary>
    private MethodBuilder EmitEscapeJsonStringHelper(TypeBuilder typeBuilder)
    {
        if (_escapeJsonStringMethod != null)
            return _escapeJsonStringMethod;

        var method = typeBuilder.DefineMethod(
            "EscapeJsonString",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var iLocal = il.DeclareLocal(_types.Int32);
        var cLocal = il.DeclareLocal(_types.Char);
        var lenLocal = il.DeclareLocal(_types.Int32);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var checkBackslash = il.DefineLabel();
        var checkNewline = il.DefineLabel();
        var checkReturn = il.DefineLabel();
        var checkTab = il.DefineLabel();
        var checkControl = il.DefineLabel();
        var appendNormal = il.DefineLabel();
        var nextChar = il.DefineLabel();

        // sb = new StringBuilder("\"");
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // len = s.Length;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // i = 0;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        // while (i < len)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // c = s[i];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Stloc, cLocal);

        // if (c == '"') sb.Append("\\\"");
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Bne_Un, checkBackslash);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\\') sb.Append("\\\\");
        il.MarkLabel(checkBackslash);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Bne_Un, checkNewline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\\\");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\n') sb.Append("\\n");
        il.MarkLabel(checkNewline);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\n');
        il.Emit(OpCodes.Bne_Un, checkReturn);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\n");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\r') sb.Append("\\r");
        il.MarkLabel(checkReturn);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\r');
        il.Emit(OpCodes.Bne_Un, checkTab);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\r");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\t') sb.Append("\\t");
        il.MarkLabel(checkTab);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\t');
        il.Emit(OpCodes.Bne_Un, checkControl);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\t");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
        il.MarkLabel(checkControl);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Bge, appendNormal);
        // Control character - emit \uXXXX
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\u");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        // Convert char to int and format as 4-digit hex
        var charAsIntLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Stloc, charAsIntLocal);
        il.Emit(OpCodes.Ldloca, charAsIntLocal);
        il.Emit(OpCodes.Ldstr, "x4");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", [_types.String]));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // Normal character - append as-is
        il.MarkLabel(appendNormal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);

        // i++;
        il.MarkLabel(nextChar);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        // sb.Append("\"");
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // return sb.ToString();
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);

        _escapeJsonStringMethod = method;
        return method;
    }

    private void EmitJsonStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the escape helper (needed by stringify)
        EmitEscapeJsonStringHelper(typeBuilder);

        // Then emit the main stringify helper
        var stringifyHelper = EmitJsonStringifyHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.JsonStringify = method;

        var il = method.GetILGenerator();

        // Call our emitted StringifyValue helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0); // indent = 0
        il.Emit(OpCodes.Ldc_I4_0); // depth = 0
        il.Emit(OpCodes.Call, stringifyHelper);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitJsonStringifyHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the class instance stringify helper
        var classInstanceHelper = EmitStringifyClassInstanceHelper(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "StringifyValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32] // value, indent, depth
        );

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();

        // Store value in local (we may modify it via toJSON)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check for BigInt - get type name and check
        EmitBigIntCheck(il, valueLocal);

        // Check for toJSON() method and call it if present
        EmitToJsonCheck(il, valueLocal, runtime);

        // if (value is bool)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if it's a class instance (has _fields field)
        EmitIsClassInstanceCheck(il, valueLocal, classInstanceLabel);

        // Default: return "null"
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumber(il, valueLocal);

        // string - escape for JSON
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Ret);

        // List<object> - stringify array
        il.MarkLabel(listLabel);
        EmitStringifyArray(il, method, valueLocal);

        // Dictionary<string, object> - stringify object
        il.MarkLabel(dictLabel);
        EmitStringifyObject(il, method, valueLocal);

        // Class instance - stringify via reflection
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldarg_1); // indent
        il.Emit(OpCodes.Ldarg_2); // depth
        il.Emit(OpCodes.Call, classInstanceHelper);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitBigIntCheck(ILGenerator il, LocalBuilder valueLocal)
    {
        var notBigIntLabel = il.DefineLabel();
        var typeLocal = il.DeclareLocal(_types.Type);
        var nameLocal = il.DeclareLocal(_types.String);

        // var type = value.GetType();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // var name = type.Name;
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // if (name == "SharpTSBigInt" || name == "BigInteger")
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "SharpTSBigInt");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String]));
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, throwLabel);

        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "BigInteger");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String]));
        il.Emit(OpCodes.Brfalse, notBigIntLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "TypeError: BigInt value can't be serialized in JSON");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notBigIntLabel);
    }

    private void EmitToJsonCheck(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var noToJsonLabel = il.DefineLabel();
        var typeLocal = il.DeclareLocal(_types.Type);
        var methodLocal = il.DeclareLocal(_types.MethodInfo);

        // First, check if value is a Dictionary<string, object?> (object literal)
        var notDictionaryLabel = il.DefineLabel();
        var toJsonFieldLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // if (value is Dictionary<string, object?>)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictionaryLabel);

        // dict.TryGetValue("toJSON", out var fn)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldloca, toJsonFieldLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notDictionaryLabel);

        // Check if field is a TSFunction
        var notTSFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // Call TSFunction.Invoke with empty args
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(notTSFunctionLabel);
        // Check for BoundTSFunction
        var notBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBoundLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(notBoundLabel);
        il.MarkLabel(notDictionaryLabel);

        // Fallback: check for toJSON method via reflection (class instances)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Public |
                                      System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod",
            [_types.String, _types.BindingFlags]));
        il.Emit(OpCodes.Stloc, methodLocal);

        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, noToJsonLabel);

        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke",
            [_types.Object, _types.ObjectArray]));
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(noToJsonLabel);
    }

    private void EmitIsClassInstanceCheck(ILGenerator il, LocalBuilder valueLocal, Label classInstanceLabel)
    {
        var typeLocal = il.DeclareLocal(_types.Type);
        var fieldLocal = il.DeclareLocal(_types.FieldInfo);

        // var type = value.GetType();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", [_types.String, _types.BindingFlags]));
        il.Emit(OpCodes.Stloc, fieldLocal);

        // if (field != null) goto classInstanceLabel;
        il.Emit(OpCodes.Ldloc, fieldLocal);
        il.Emit(OpCodes.Brtrue, classInstanceLabel);
    }

    private void EmitFormatNumber(ILGenerator il, LocalBuilder valueLocal)
    {
        var local = il.DeclareLocal(_types.Double);
        var isIntLabel = il.DefineLabel();
        var isNanLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, local);

        // Check NaN/Infinity
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double]));
        il.Emit(OpCodes.Brtrue, isNanLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", [_types.Double]));
        il.Emit(OpCodes.Brtrue, isNanLabel);

        // Check if integer
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", [_types.Double]));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, isIntLabel);

        // Float format
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String]));
        il.Emit(OpCodes.Ret);

        // NaN/Infinity -> "null"
        il.MarkLabel(isNanLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // Integer format
        il.MarkLabel(isIntLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int64, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringifyArray(ILGenerator il, MethodBuilder stringifyMethod, LocalBuilder valueLocal)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var arrLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
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

        // sb.Append(StringifyValue(arr[i], indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
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

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringifyObject(ILGenerator il, MethodBuilder stringifyMethod, LocalBuilder valueLocal)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var enumeratorLocal = il.DeclareLocal(_types.DictionaryStringObjectEnumerator);
        var currentLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        var firstLocal = il.DeclareLocal(_types.Boolean);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
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

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.DictionaryStringObjectEnumerator.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.DictionaryStringObjectEnumerator.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(EscapeJsonString(key));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(StringifyValue(value, indent, depth + 1));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, _types.DictionaryStringObjectEnumerator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper method that stringifies class instances using reflection.
    /// This handles objects with _fields dictionary and __ prefixed backing fields.
    /// </summary>
    private MethodBuilder EmitStringifyClassInstanceHelper(TypeBuilder typeBuilder)
    {
        // Note: This is complex because we need to iterate over:
        // 1. Backing fields (fields starting with __)
        // 2. _fields dictionary entries
        // The approach: delegate to RuntimeTypes for now as pure IL is very complex.
        // This will be inlined by the JIT and the dependency is acceptable since
        // it's called via reflection pattern which is self-contained.

        // Actually, let's emit a pure IL version that uses RuntimeTypes as a
        // transitional step. For full standalone, we'd need to emit all the logic.

        // For simplicity during this phase, create a wrapper that delegates to RuntimeTypes.
        // This can be enhanced later if full standalone is required.

        var method = typeBuilder.DefineMethod(
            "StringifyClassInstance",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32] // value, indent, depth
        );

        var il = method.GetILGenerator();

        // Call RuntimeTypes.StringifyClassInstance equivalent via reflection
        // Actually no - we need this standalone. Let me emit the full logic.

        // Locals
        var typeLocal = il.DeclareLocal(_types.Type);
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var fieldInfoArrayLocal = il.DeclareLocal(_types.FieldInfoArray);
        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);
        var iLocal = il.DeclareLocal(_types.Int32);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var nameLocal = il.DeclareLocal(_types.String);
        var fieldValueLocal = il.DeclareLocal(_types.Object);
        var stringifyResultLocal = il.DeclareLocal(_types.String);
        var camelNameLocal = il.DeclareLocal(_types.String);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipFieldLabel = il.DefineLabel();
        var notEmptyLabel = il.DefineLabel();

        // var type = value.GetType();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetFields", [_types.BindingFlags]));
        il.Emit(OpCodes.Stloc, fieldInfoArrayLocal);

        // for (int i = 0; i < fields.Length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, fieldInfoArrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // var field = fields[i];
        il.Emit(OpCodes.Ldloc, fieldInfoArrayLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // var name = field.Name;
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FieldInfo, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // if (!name.StartsWith("__")) continue;
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "__");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String]));
        il.Emit(OpCodes.Brfalse, skipFieldLabel);

        // var fieldValue = field.GetValue(value);
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "GetValue", [_types.Object]));
        il.Emit(OpCodes.Stloc, fieldValueLocal);

        // camelName = ToCamelCase(name.Substring(2))
        // Substring(2) to skip "__"
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32]));
        // ToCamelCase - just lowercase first character
        // We'll emit this inline: if (s.Length > 0 && char.IsUpper(s[0])) -> char.ToLower(s[0]) + s.Substring(1)
        EmitToCamelCase(il, camelNameLocal);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(EscapeJsonString(camelName));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, camelNameLocal);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // Stringify the field value
        var valueString = il.DeclareLocal(_types.String);

        // Check if fieldValue is null
        var notNullLabel = il.DefineLabel();
        var appendValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldValueLocal);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Stloc, valueString);
        il.Emit(OpCodes.Br, appendValueLabel);

        il.MarkLabel(notNullLabel);
        // For nested objects, we need to recurse. Since we can't call StringifyValue yet,
        // we'll emit a simplified version that handles primitives and falls back to "null"
        // for nested objects. This can be improved in a later pass.

        // Check if it's a primitive type - pass method for recursive nested class calls
        EmitSimplifiedStringify(il, fieldValueLocal, valueString, method);

        il.MarkLabel(appendValueLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, valueString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipFieldLabel);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Now handle _fields dictionary
        EmitStringifyFieldsDictionary(il, typeLocal, sbLocal, firstLocal, method);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitToCamelCase(ILGenerator il, LocalBuilder resultLocal)
    {
        // String is on stack. Convert to camelCase (lowercase first char)
        var inputLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, inputLocal);

        var emptyLabel = il.DefineLabel();
        var alreadyLowerLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (s.Length == 0) return s;
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // if (char.IsLower(s[0])) return s;
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLower", [_types.Char]));
        il.Emit(OpCodes.Brtrue, alreadyLowerLabel);

        // return char.ToLowerInvariant(s[0]) + s.Substring(1);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToLowerInvariant", [_types.Char]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", [_types.Char]));
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32]));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String]));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(emptyLabel);
        il.MarkLabel(alreadyLowerLabel);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(endLabel);
    }

    private void EmitSimplifiedStringify(ILGenerator il, LocalBuilder valueLocal, LocalBuilder resultLocal, MethodBuilder? classInstanceMethod = null)
    {
        // Simplified stringify for nested values - handles primitives
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var nestedClassLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();
        var endSimplify = il.DefineLabel();

        // Check bool
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // Check double
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // Check string
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Check Dictionary
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check List
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Check for nested class instance (has _fields) - only if we have a method to call
        if (classInstanceMethod != null)
        {
            var checkTypeLocal = il.DeclareLocal(_types.Type);
            var checkFieldLocal = il.DeclareLocal(_types.FieldInfo);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Stloc, checkTypeLocal);
            il.Emit(OpCodes.Ldloc, checkTypeLocal);
            il.Emit(OpCodes.Ldstr, "_fields");
            il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", [_types.String, _types.BindingFlags]));
            il.Emit(OpCodes.Stloc, checkFieldLocal);
            il.Emit(OpCodes.Ldloc, checkFieldLocal);
            il.Emit(OpCodes.Brtrue, nestedClassLabel);
        }

        // Default: "null"
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);

        // double
        il.MarkLabel(doubleLabel);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // Simple format - use ToString("G15")
        il.Emit(OpCodes.Ldloca, dLocal);
        il.Emit(OpCodes.Ldstr, "G15");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String]));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);

        // string - escape for JSON
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);

        // Dictionary - stringify as JSON object (simplified for nested dicts)
        il.MarkLabel(dictLabel);
        // For nested dicts, use the main StringifyValue recursively via parent method
        // For now, use a simple "[object Object]" placeholder to avoid System.Text.Json dependency
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);

        // List - stringify as JSON array (simplified for nested lists)
        il.MarkLabel(listLabel);
        // For nested lists, use the main StringifyValue recursively via parent method
        // For now, use a simple "[]" placeholder to avoid System.Text.Json dependency
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, endSimplify);

        // Nested class instance - call StringifyClassInstance recursively
        il.MarkLabel(nestedClassLabel);
        if (classInstanceMethod != null)
        {
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldarg_1); // indent
            il.Emit(OpCodes.Ldarg_2); // depth
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Call, classInstanceMethod);
            il.Emit(OpCodes.Stloc, resultLocal);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "{}");
            il.Emit(OpCodes.Stloc, resultLocal);
        }
        il.Emit(OpCodes.Br, endSimplify);

        il.MarkLabel(endSimplify);
    }

    private void EmitStringifyFieldsDictionary(ILGenerator il, LocalBuilder typeLocal, LocalBuilder sbLocal, LocalBuilder firstLocal, MethodBuilder classInstanceMethod)
    {
        // Get _fields dictionary from the object and serialize its entries
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsValueLocal = il.DeclareLocal(_types.Object);
        var fieldsDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var enumeratorLocal = il.DeclareLocal(_types.DictionaryStringObjectEnumerator);
        var currentLocal = il.DeclareLocal(_types.KeyValuePairStringObject);

        var skipFieldsLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // var fieldsField = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", [_types.String, _types.BindingFlags]));
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField == null) skip
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, skipFieldsLabel);

        // var fieldsValue = fieldsField.GetValue(arg0);
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "GetValue", [_types.Object]));
        il.Emit(OpCodes.Stloc, fieldsValueLocal);

        // if (fieldsValue == null || !(fieldsValue is Dictionary<string, object?>)) skip
        il.Emit(OpCodes.Ldloc, fieldsValueLocal);
        il.Emit(OpCodes.Brfalse, skipFieldsLabel);
        il.Emit(OpCodes.Ldloc, fieldsValueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, skipFieldsLabel);

        // var dict = (Dictionary<string, object?>)fieldsValue;
        il.Emit(OpCodes.Ldloc, fieldsValueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.DictionaryStringObjectEnumerator.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.DictionaryStringObjectEnumerator.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(EscapeJsonString(kv.Key));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // Serialize value - simplified
        var valueStringLocal = il.DeclareLocal(_types.String);
        var fieldVal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, fieldVal);

        // Check for null
        var notNullLabel = il.DefineLabel();
        var appendLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldVal);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Stloc, valueStringLocal);
        il.Emit(OpCodes.Br, appendLabel);

        il.MarkLabel(notNullLabel);
        EmitSimplifiedStringify(il, fieldVal, valueStringLocal, classInstanceMethod);

        il.MarkLabel(appendLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, valueStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, _types.DictionaryStringObjectEnumerator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));

        il.MarkLabel(skipFieldsLabel);
    }
}

