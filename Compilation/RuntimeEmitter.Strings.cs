using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitStringCharAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringCharAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringCharAt = method;

        var il = method.GetILGenerator();

        // index = (int)(double)args[0]
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        var returnEmpty = il.DefineLabel();
        var validIndex = il.DefineLabel();

        // if (index < 0) return ""
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnEmpty);

        // if (index >= str.Length) return ""
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnEmpty);

        // Return str[index].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmpty);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSubstring(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSubstring",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringSubstring = method;

        var il = method.GetILGenerator();

        // start = Math.Max(0, (int)(double)args[0])
        var startLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);

        // end = args.Length > 1 ? (int)(double)args[1] : str.Length
        var endLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        var hasEnd = il.DefineLabel();
        var afterEnd = il.DefineLabel();
        il.Emit(OpCodes.Bgt, hasEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Br, afterEnd);
        il.MarkLabel(hasEnd);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.MarkLabel(afterEnd);
        il.Emit(OpCodes.Stloc, endLocal);

        // Clamp end to str.Length
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);

        // if (start >= str.Length || end <= start) return ""
        var validRange = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Blt, validRange);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validRange);
        var validRange2 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, validRange2);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validRange2);
        // return str.Substring(start, end - start)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.String]
        );
        runtime.StringIndexOf = method;

        var il = method.GetILGenerator();

        // return (double)str.IndexOf(search)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringReplace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.String, _types.String]
        );
        runtime.StringReplace = method;

        var il = method.GetILGenerator();

        // JavaScript replace only replaces first occurrence
        // var index = str.IndexOf(search)
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", _types.String));
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0) return str
        var found = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, found);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(found);
        // return str.Substring(0, index) + replacement + str.Substring(index + search.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));

        il.Emit(OpCodes.Ldarg_2); // replacement

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));

        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSplit(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSplit",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.String, _types.String]
        );
        runtime.StringSplit = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (separator == "") split into chars
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notEmpty);

        // Split into characters
        var charIndex = il.DeclareLocal(_types.Int32);
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, charIndex);

        var charLoopStart = il.DefineLabel();
        var charLoopEnd = il.DefineLabel();

        il.MarkLabel(charLoopStart);
        il.Emit(OpCodes.Ldloc, charIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, charLoopEnd);

        // Get char at index and convert to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, charIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.EmptyTypes));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, charIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, charIndex);
        il.Emit(OpCodes.Br, charLoopStart);

        il.MarkLabel(charLoopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);
        // Regular split: str.Split(separator)
        var partsLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, (int)StringSplitOptions.None);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Split", _types.String, _types.StringSplitOptions));
        il.Emit(OpCodes.Stloc, partsLocal);

        // Add each part to result
        var partIndex = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, partIndex);

        var partLoopStart = il.DefineLabel();
        var partLoopEnd = il.DefineLabel();

        il.MarkLabel(partLoopStart);
        il.Emit(OpCodes.Ldloc, partIndex);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, partLoopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, partIndex);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, partIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, partIndex);
        il.Emit(OpCodes.Br, partLoopStart);

        il.MarkLabel(partLoopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringIncludes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.String]
        );
        runtime.StringIncludes = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringStartsWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringStartsWith",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.String]
        );
        runtime.StringStartsWith = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringEndsWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringEndsWith",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.String]
        );
        runtime.StringEndsWith = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "EndsWith", _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringSlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringSlice(string str, int argCount, object[] args) -> string
        // Handles negative indices and optional end parameter
        var method = typeBuilder.DefineMethod(
            "StringSlice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32, _types.ObjectArray]
        );
        runtime.StringSlice = method;

        var il = method.GetILGenerator();
        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // lengthLocal = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // start = (int)(double)args[0]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, startLocal);

        // end = argCount > 1 ? (int)(double)args[1] : length
        var noEndArg = il.DefineLabel();
        var endArgDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noEndArg);
        // has end arg
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endArgDone);
        il.MarkLabel(noEndArg);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endArgDone);

        // Handle negative start: if (start < 0) start = max(0, length + start)
        var startNotNegative = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);
        // start is negative
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotNegative);

        // Handle negative end: if (end < 0) end = max(0, length + end)
        var endNotNegative = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNegative);
        // end is negative
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotNegative);

        // Clamp start to length: start = min(start, length)
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);

        // Clamp end to length: end = min(end, length)
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);

        // if (end <= start) return ""
        var returnSubstring = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, returnSubstring);
        // return ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        // return str.Substring(start, end - start)
        il.MarkLabel(returnSubstring);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringRepeat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringRepeat(string str, double count) -> string
        var method = typeBuilder.DefineMethod(
            "StringRepeat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Double]
        );
        runtime.StringRepeat = method;

        var il = method.GetILGenerator();
        var countLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var emptyLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // count = (int)countArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, countLocal);

        // if (count <= 0 || str.Length == 0) return ""
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, emptyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // result = ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 0; i < count; i++) result += str
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringPadStart(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringPadStart(string str, int argCount, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringPadStart",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32, _types.ObjectArray]
        );
        runtime.StringPadStart = method;

        var il = method.GetILGenerator();
        var targetLengthLocal = il.DeclareLocal(_types.Int32);
        var padStringLocal = il.DeclareLocal(_types.String);
        var padLengthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var returnOriginal = il.DefineLabel();
        var hasPadArg = il.DefineLabel();
        var buildPadding = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // targetLength = (int)(double)args[0]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLengthLocal);

        // if (targetLength <= str.Length) return str
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnOriginal);

        // padString = argCount > 1 ? (string)args[1] : " "
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasPadArg);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, buildPadding);
        il.MarkLabel(hasPadArg);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.MarkLabel(buildPadding);

        // if (padString.Length == 0) return str
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // padLength = targetLength - str.Length
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, padLengthLocal);

        // Build padding by repeating padString
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Trim to exact length and prepend
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringPadEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringPadEnd(string str, int argCount, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringPadEnd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32, _types.ObjectArray]
        );
        runtime.StringPadEnd = method;

        var il = method.GetILGenerator();
        var targetLengthLocal = il.DeclareLocal(_types.Int32);
        var padStringLocal = il.DeclareLocal(_types.String);
        var padLengthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.String);
        var returnOriginal = il.DefineLabel();
        var hasPadArg = il.DefineLabel();
        var buildPadding = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // targetLength = (int)(double)args[0]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLengthLocal);

        // if (targetLength <= str.Length) return str
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnOriginal);

        // padString = argCount > 1 ? (string)args[1] : " "
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasPadArg);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.Emit(OpCodes.Br, buildPadding);
        il.MarkLabel(hasPadArg);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, padStringLocal);
        il.MarkLabel(buildPadding);

        // if (padString.Length == 0) return str
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // padLength = targetLength - str.Length
        il.Emit(OpCodes.Ldloc, targetLengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, padLengthLocal);

        // Build padding by repeating padString
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padStringLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Trim to exact length and append
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, padLengthLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringCharCodeAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringCharCodeAt(string str, double index) -> double
        var method = typeBuilder.DefineMethod(
            "StringCharCodeAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Double]
        );
        runtime.StringCharCodeAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var nanLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // index = (int)indexArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0 || index >= str.Length) return NaN
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nanLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, nanLabel);

        // return (double)str[index]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringConcat(string str, object[] args) -> string
        var method = typeBuilder.DefineMethod(
            "StringConcat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.StringConcat = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // result = str
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 0; i < args.Length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // result += Stringify(args[i])  (handles null->"null", bool->"true"/"false")
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringLastIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringLastIndexOf(string str, string search) -> double
        var method = typeBuilder.DefineMethod(
            "StringLastIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.String]
        );
        runtime.StringLastIndexOf = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "LastIndexOf", _types.String));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringReplaceAll(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringReplaceAll(string str, string search, string replacement) -> string
        var method = typeBuilder.DefineMethod(
            "StringReplaceAll",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.String, _types.String]
        );
        runtime.StringReplaceAll = method;

        var il = method.GetILGenerator();
        var returnOriginal = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (search.Length == 0) return str
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnOriginal);

        // return str.Replace(search, replacement)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.String, _types.String));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(returnOriginal);
        il.Emit(OpCodes.Ldarg_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringAt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StringAt(string str, double index) -> object (string or null)
        var method = typeBuilder.DefineMethod(
            "StringAt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Double]
        );
        runtime.StringAt = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // length = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = (int)indexArg
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, indexLocal);

        // if (index < 0) index = length + index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegative = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegative);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(notNegative);

        // if (index < 0 || index >= length) return null
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, nullLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, nullLabel);

        // return str[index].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        // Box the char and call ToString on it
        il.Emit(OpCodes.Box, _types.Char);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", _types.EmptyTypes));
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }
}

