using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Stringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.Stringify = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is SharpTSUndefined) return "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // if (value is bool b) return b ? "true" : "false"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double d) return d.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is List<object?>) return array string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is BigInteger) return value.ToString() + "n"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // if (value is Dictionary<string, object?>) return "{ key: value, ... }"
        var dictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return value.ToString() ?? "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "null");
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Br, endLabel);

        // null case
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Br, endLabel);

        // undefined case
        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Br, endLabel);

        // bool case
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Br, endLabel);

        // double case - simple ToString
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var doubleLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, doubleLocal);
        il.Emit(OpCodes.Ldloca, doubleLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        // BigInteger case - format as value.ToString() + "n"
        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        var bigintLocal = il.DeclareLocal(_types.BigInteger);
        il.Emit(OpCodes.Stloc, bigintLocal);
        il.Emit(OpCodes.Ldloca, bigintLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.BigInteger, "ToString"));
        il.Emit(OpCodes.Ldstr, "n");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, endLabel);

        // List case - format as "[elem1, elem2, ...]"
        il.MarkLabel(listLabel);
        // Use StringBuilder to build the result
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        // Append "["
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Loop through list elements
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= list.Count) break
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (index > 0) append ", "
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // Append Stringify(list[index])
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Call, method); // Recursive call to Stringify
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Append "]" and return
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        // Dictionary case - use RuntimeTypes.Stringify for proper formatting
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod(
            nameof(RuntimeTypes.Stringify),
            [typeof(object)])!);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string GetConsoleIndent()
    /// Returns a string of spaces based on _consoleGroupLevel (2 spaces per level).
    /// </summary>
    private void EmitGetConsoleIndent(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetConsoleIndent",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            Type.EmptyTypes
        );
        runtime.GetConsoleIndent = method;

        var il = method.GetILGenerator();

        // if (_consoleGroupLevel <= 0) return ""
        var hasIndentLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.ConsoleGroupLevelField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasIndentLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasIndentLabel);
        // return new string(' ', _consoleGroupLevel * 2)
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)' ');
        il.Emit(OpCodes.Ldsfld, runtime.ConsoleGroupLevelField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.Char, _types.Int32]));
        il.Emit(OpCodes.Ret);
    }

    private void EmitConsoleLog(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleLog",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleLog = method;

        var il = method.GetILGenerator();
        var noFormatLabel = il.DefineLabel();

        // Check if arg is a string with format specifiers
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noFormatLabel);

        // Check HasFormatSpecifiers
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.HasFormatSpecifiers);
        il.Emit(OpCodes.Brfalse, noFormatLabel);

        // Has format specifiers - process with FormatSingleArg, then prepend indent
        // Console.WriteLine(GetConsoleIndent() + FormatSingleArg(value))
        il.Emit(OpCodes.Call, runtime.GetConsoleIndent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.FormatSingleArg);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);

        // No format specifiers - call Stringify then prepend indent
        // Console.WriteLine(GetConsoleIndent() + Stringify(value))
        il.MarkLabel(noFormatLabel);
        il.Emit(OpCodes.Call, runtime.GetConsoleIndent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitFormatSingleArg(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Process format specifiers in a single string (handles %% -> % and unsubstituted specifiers)
        var method = typeBuilder.DefineMethod(
            "FormatSingleArg",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        runtime.FormatSingleArg = method;

        var il = method.GetILGenerator();

        // StringBuilder result = new StringBuilder()
        var resultLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // int i = 0
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // while (i < format.Length)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Bge, loopEnd);

        // char c = format[i]
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);

        // if (c == '%' && i + 1 < format.Length && format[i+1] == '%')
        var notPercent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        il.Emit(OpCodes.Bne_Un, notPercent);

        // Check i + 1 < format.Length
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Bge, notPercent);

        // Check format[i+1] == '%'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        il.Emit(OpCodes.Bne_Un, notPercent);

        // Append '%' and skip 2
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        // Regular character - append as-is
        il.MarkLabel(notPercent);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Return result.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitConsoleLogMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleLogMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]
        );
        runtime.ConsoleLogMultiple = method;

        var il = method.GetILGenerator();

        // Check if first argument is a format string with specifiers
        // if (args.Length > 0 && args[0] is string fmt && HasFormatSpecifiers(fmt))
        var noFormatLabel = il.DefineLabel();

        // Check args.Length > 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noFormatLabel);

        // Check args[0] is string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noFormatLabel);

        // Check HasFormatSpecifiers(args[0] as string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.HasFormatSpecifiers);
        il.Emit(OpCodes.Brfalse, noFormatLabel);

        // Format string case: call FormatConsoleArgs, prepend indent
        // Console.WriteLine(GetConsoleIndent() + FormatConsoleArgs(args))
        il.Emit(OpCodes.Call, runtime.GetConsoleIndent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FormatConsoleArgs);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);

        // No format specifiers: join with spaces, prepend indent
        // Console.WriteLine(GetConsoleIndent() + string.Join(" ", args))
        il.MarkLabel(noFormatLabel);
        il.Emit(OpCodes.Call, runtime.GetConsoleIndent);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", _types.String, _types.ObjectArray));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitHasFormatSpecifiers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HasFormatSpecifiers",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String]
        );
        runtime.HasFormatSpecifiers = method;

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var returnFalse = il.DefineLabel();

        // int i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop: while (i < str.Length - 1)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, returnFalse);

        // if (str[i] == '%')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        var notPercentLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notPercentLabel);

        // Check next char is s, d, i, f, o, O, or j
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        var nextCharLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, nextCharLocal);

        // Check for each specifier (including %% escape)
        var checkNext = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        var returnTrue = il.DefineLabel();
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'s');
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'d');
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'i');
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'f');
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'o');
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'O');
        il.Emit(OpCodes.Beq, returnTrue);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'j');
        il.Emit(OpCodes.Beq, returnTrue);

        il.MarkLabel(notPercentLabel);
        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(returnTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitFormatConsoleArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FormatConsoleArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );
        runtime.FormatConsoleArgs = method;

        var il = method.GetILGenerator();

        // Get format string: string format = (string)args[0]
        var formatLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, formatLocal);

        // StringBuilder result = new StringBuilder()
        var resultLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // int currentArg = 1
        var argIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, argIndexLocal);

        // int i = 0
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var appendRemaining = il.DefineLabel();

        // while (i < format.Length)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Bge, appendRemaining);

        // char c = format[i]
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);

        // if (c == '%' && i + 1 < format.Length)
        var notPercent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        il.Emit(OpCodes.Bne_Un, notPercent);

        // Check i + 1 < format.Length
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Bge, notPercent);

        // char specifier = format[i + 1]
        var specifierLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, specifierLocal);

        // Handle %% -> %
        var notDoublePercent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        il.Emit(OpCodes.Bne_Un, notDoublePercent);

        // Append '%' and skip 2
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'%');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(notDoublePercent);

        // Check if we have args remaining: currentArg < args.Length
        var noArgsLeft = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, noArgsLeft);

        // Handle specifiers s, d, i, f, o, O, j
        var handleS = il.DefineLabel();
        var handleD = il.DefineLabel();
        var handleF = il.DefineLabel();
        var handleO = il.DefineLabel();
        var handleJ = il.DefineLabel();
        var unknownSpecifier = il.DefineLabel();

        // Check 's'
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'s');
        il.Emit(OpCodes.Beq, handleS);

        // Check 'd' or 'i'
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'d');
        il.Emit(OpCodes.Beq, handleD);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'i');
        il.Emit(OpCodes.Beq, handleD);

        // Check 'f'
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'f');
        il.Emit(OpCodes.Beq, handleF);

        // Check 'o' or 'O'
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'o');
        il.Emit(OpCodes.Beq, handleO);

        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'O');
        il.Emit(OpCodes.Beq, handleO);

        // Check 'j'
        il.Emit(OpCodes.Ldloc, specifierLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'j');
        il.Emit(OpCodes.Beq, handleJ);

        il.Emit(OpCodes.Br, unknownSpecifier);

        // Handle %s - string
        il.MarkLabel(handleS);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        var afterS = il.DefineLabel();
        il.Emit(OpCodes.Br, afterS);

        // Handle %d/%i - integer
        il.MarkLabel(handleD);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.FormatAsInteger);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterS);

        // Handle %f - float
        il.MarkLabel(handleF);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.FormatAsFloat);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterS);

        // Handle %o/%O - object (same as Stringify)
        il.MarkLabel(handleO);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterS);

        // Handle %j - JSON
        il.MarkLabel(handleJ);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.FormatAsJson);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterS);

        il.MarkLabel(afterS);
        // currentArg++, i += 2
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        // Unknown specifier or no args left - append char literally
        il.MarkLabel(noArgsLeft);
        il.MarkLabel(unknownSpecifier);
        il.MarkLabel(notPercent);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.Char));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        // Append remaining args
        il.MarkLabel(appendRemaining);
        var remainingLoop = il.DefineLabel();
        var remainingEnd = il.DefineLabel();

        il.MarkLabel(remainingLoop);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, remainingEnd);

        // Append " " + Stringify(args[currentArg])
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // currentArg++
        il.Emit(OpCodes.Ldloc, argIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argIndexLocal);
        il.Emit(OpCodes.Br, remainingLoop);

        il.MarkLabel(remainingEnd);

        // Return result.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitFormatAsInteger(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FormatAsInteger",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.FormatAsInteger = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var nanLabel = il.DefineLabel();

        // if (value == null) return "NaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nanLabel);

        // if (value is SharpTSUndefined) return "NaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nanLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // default: "NaN"
        il.Emit(OpCodes.Br, nanLabel);

        // double case
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // Check NaN
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, nanLabel);
        // Check Infinity
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, nanLabel);
        // Return ((long)d).ToString()
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I8);
        var longLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int64, "ToString"));
        il.Emit(OpCodes.Ret);

        // bool case
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var boolTrueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, boolTrueLabel);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(boolTrueLabel);
        il.Emit(OpCodes.Ldstr, "1");
        il.Emit(OpCodes.Ret);

        // string case - try parse
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        var parsedLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "TryParse", _types.String, _types.Double.MakeByRefType()));
        var parseFailedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, parseFailedLabel);
        // Parse succeeded - check NaN/Infinity
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, nanLabel);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, nanLabel);
        // Return ((long)parsed).ToString()
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, longLocal);
        il.Emit(OpCodes.Ldloca, longLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int64, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(parseFailedLabel);
        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);
    }

    private void EmitFormatAsFloat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FormatAsFloat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.FormatAsFloat = method;

        var il = method.GetILGenerator();
        var nanLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // if (value == null) return "NaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nanLabel);

        // if (value is SharpTSUndefined) return "NaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nanLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is bool)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // default: "NaN"
        il.Emit(OpCodes.Br, nanLabel);

        // double case
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // Check NaN
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        var notNanLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notNanLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNanLabel);
        // Check Infinity
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", _types.Double));
        var notPosInfLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", _types.Double));
        var notNegInfLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notNegInfLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNegInfLabel);
        // Return d.ToString()
        il.Emit(OpCodes.Ldloca, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "ToString"));
        il.Emit(OpCodes.Ret);

        // bool case
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var boolTrueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, boolTrueLabel);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(boolTrueLabel);
        il.Emit(OpCodes.Ldstr, "1");
        il.Emit(OpCodes.Ret);

        // string case - try parse
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        var parsedLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "TryParse", _types.String, _types.Double.MakeByRefType()));
        var parseFailedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, parseFailedLabel);
        // Parse succeeded
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        var parsedNotNan = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, parsedNotNan);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(parsedNotNan);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", _types.Double));
        var parsedNotPosInf = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, parsedNotPosInf);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(parsedNotPosInf);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", _types.Double));
        var parsedNotNegInf = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, parsedNotNegInf);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(parsedNotNegInf);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(parseFailedLabel);
        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);
    }

    private void EmitFormatAsJson(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FormatAsJson",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.FormatAsJson = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null"
        il.Emit(OpCodes.Ldarg_0);
        var notNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNull);

        // if (value is SharpTSUndefined) return "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefined = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefined);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notUndefined);

        // if (value is double d)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDouble = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDouble);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // NaN -> "null"
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        var dNotNan = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, dNotNan);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(dNotNan);
        // Infinity -> "null"
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        var dNotInf = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, dNotInf);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(dNotInf);
        il.Emit(OpCodes.Ldloca, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDouble);

        // if (value is bool b) return b ? "true" : "false"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        var notBool = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBool);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var bTrue = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, bTrue);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(bTrue);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBool);

        // if (value is string s) return "\"" + escaped + "\""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        var notString = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notString);
        // Simple escaping: just wrap in quotes (proper JSON escaping would need more work)
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notString);

        // Default: use RuntimeTypes.FormatAsJson for proper JSON formatting of objects/arrays
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod(
            nameof(RuntimeTypes.FormatAsJson),
            [typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.ToNumber = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);

        // Use Convert.ToDouble with try-catch fallback to NaN
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIsTruthy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsTruthy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.IsTruthy = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var checkBool = il.DefineLabel();
        var checkDouble = il.DefineLabel();
        var checkString = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null => false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // undefined => false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, falseLabel);

        // bool => return value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, checkBool);

        // double => check for 0 and NaN
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, checkDouble);

        // string => check for empty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, checkString);

        // everything else => true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Check bool value
        il.MarkLabel(checkBool);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // Check double: 0 and NaN are falsy
        il.MarkLabel(checkDouble);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // Check if d == 0
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, falseLabel);
        // Check if d is NaN (NaN != NaN)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel); // If d != d, it's NaN
        // Not 0 and not NaN => truthy
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Check string: empty is falsy
        il.MarkLabel(checkString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTypeOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.TypeOf = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var symbolLabel = il.DefineLabel();
        var functionLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // null => "object" (JS typeof null === "object")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // undefined => "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // Check for union types using $IUnionType marker interface
        // If value implements $IUnionType, unwrap via Value property and recurse
        var notUnionLabel = il.DefineLabel();
        var unionLocal = il.DeclareLocal(runtime.IUnionTypeInterface);

        // Check: if (value is $IUnionType union)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IUnionTypeInterface);
        il.Emit(OpCodes.Stloc, unionLocal);
        il.Emit(OpCodes.Ldloc, unionLocal);
        il.Emit(OpCodes.Brfalse, notUnionLabel);

        // Get underlying value via interface: union.Value
        il.Emit(OpCodes.Ldloc, unionLocal);
        il.Emit(OpCodes.Callvirt, runtime.IUnionTypeValueGetter);

        // return TypeOf(underlyingValue) - recursive call
        il.Emit(OpCodes.Call, method);  // Recursive call to self
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notUnionLabel);

        // bool => "boolean"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // double => "number"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, numberLabel);

        // string => "string"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // TSSymbol => "symbol"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, symbolLabel);

        // TSFunction => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // Delegate => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Delegate);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // $PromisifiedFunction => "function" (from util.promisify)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromisifiedFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // $DeprecatedFunction => "function" (from util.deprecate)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSDeprecatedFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // BigInteger => "bigint"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // Default => "object"
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldstr, "number");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "string");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(symbolLabel);
        il.Emit(OpCodes.Ldstr, "symbol");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(functionLabel);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldstr, "bigint");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitInstanceOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InstanceOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.InstanceOf = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();

        // if (instance == null || classType == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Get type of instance and check IsAssignableFrom
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Type);
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTypeLabel);

        // classType is Type, use it directly
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTypeLabel);
        // classType is not Type, get its type
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits HasIn(object key, object obj) -> bool
    /// Implements the JavaScript 'in' operator: checks if a property exists in an object.
    /// Handles both symbol keys and string keys.
    /// </summary>
    private void EmitHasIn(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HasIn",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.HasIn = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();

        // if (obj == null) return false
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Check if key is a symbol
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // String key path
        // Check if obj is $TSObject
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        // $TSObject - call HasProperty(string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasProperty);
        il.Emit(OpCodes.Ret);

        // Check if obj is Dictionary<string, object>
        il.MarkLabel(notTSObjectLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if obj is List<object> (array)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Other types - return false
        il.Emit(OpCodes.Br, falseLabel);

        // Dictionary - use ContainsKey
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Ret);

        // List (array) - check if index exists
        il.MarkLabel(listLabel);
        var indexLocal = il.DeclareLocal(_types.Int32);
        // Try to convert key to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Stloc, indexLocal);
        // index >= 0 && index < list.Count
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, falseLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, falseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Symbol key path
        il.MarkLabel(symbolKeyLabel);
        // Get symbol dict and check if key exists
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Ret);

        // Return false
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    // NOTE: EmitConvertArgsForUnionTypes was removed - it was dead code.
    // The actual conversion is done by the private ConvertArgsForUnionTypes method on $TSFunction.

    private void EmitAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Add",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.Add = method;

        var il = method.GetILGenerator();
        var stringConcatLabel = il.DefineLabel();

        // if (left is string || right is string) string concat
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringConcatLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringConcatLabel);

        // Numeric addition
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // String concat - use Stringify for JS-compatible conversion (null->"null", bool->"true"/"false")
        il.MarkLabel(stringConcatLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    private void EmitEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.Equals = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var checkRightNullish = il.DefineLabel();
        var notBothNullish = il.DefineLabel();
        var objectEqualsLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Local to track if left is nullish
        var leftNullish = il.DeclareLocal(_types.Boolean);
        var rightNullish = il.DeclareLocal(_types.Boolean);

        // Check if left is nullish (null or undefined)
        // leftNullish = (left == null || left is SharpTSUndefined)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, checkRightNullish); // left is null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un); // true if left is SharpTSUndefined
        il.Emit(OpCodes.Stloc, leftNullish);
        il.Emit(OpCodes.Br_S, notBothNullish);

        il.MarkLabel(checkRightNullish);
        // Left is null - mark as nullish
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, leftNullish);

        il.MarkLabel(notBothNullish);

        // Check if right is nullish (null or undefined)
        // rightNullish = (right == null || right is SharpTSUndefined)
        var rightNotNull = il.DefineLabel();
        var afterRightCheck = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue_S, rightNotNull);
        // Right is null - mark as nullish
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, rightNullish);
        il.Emit(OpCodes.Br_S, afterRightCheck);

        il.MarkLabel(rightNotNull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un); // true if right is SharpTSUndefined
        il.Emit(OpCodes.Stloc, rightNullish);

        il.MarkLabel(afterRightCheck);

        // If both are nullish, return true (null == undefined)
        il.Emit(OpCodes.Ldloc, leftNullish);
        il.Emit(OpCodes.Ldloc, rightNullish);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // If only one is nullish, return false
        il.Emit(OpCodes.Ldloc, leftNullish);
        il.Emit(OpCodes.Ldloc, rightNullish);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Brtrue, falseLabel);

        // Neither is nullish - use object.Equals
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}

