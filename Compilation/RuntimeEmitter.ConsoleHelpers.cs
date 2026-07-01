using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits console extension methods (error, warn, info, debug, clear, time, timeEnd, timeLog, and Phase 2 methods).
    /// </summary>
    private void EmitConsoleExtensions(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit static field for timers dictionary: Dictionary<string, Stopwatch>
        var timersField = typeBuilder.DefineField(
            "_consoleTimers",
            _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = timersField;

        // Emit static field for counts dictionary: Dictionary<string, int>
        var countsField = typeBuilder.DefineField(
            "_consoleCounts",
            _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = countsField;

        // NOTE: _consoleGroupLevel field is defined early in EmitRuntimeType to allow ConsoleLog to use it
        var groupLevelField = runtime.ConsoleGroupLevelField;

        // Phase 1 methods
        EmitConsoleError(typeBuilder, runtime);
        EmitConsoleErrorMultiple(typeBuilder, runtime);
        EmitConsoleWarn(typeBuilder, runtime);
        EmitConsoleWarnMultiple(typeBuilder, runtime);
        EmitConsoleClear(typeBuilder, runtime);
        EmitConsoleTime(typeBuilder, runtime, timersField);
        EmitConsoleTimeEnd(typeBuilder, runtime, timersField);
        EmitConsoleTimeLog(typeBuilder, runtime, timersField);

        // Phase 2 methods
        EmitConsoleAssert(typeBuilder, runtime);
        EmitConsoleAssertMultiple(typeBuilder, runtime);
        EmitConsoleCount(typeBuilder, runtime, countsField);
        EmitConsoleCountReset(typeBuilder, runtime, countsField);
        EmitConsoleTable(typeBuilder, runtime, groupLevelField);
        EmitConsoleDir(typeBuilder, runtime, groupLevelField);
        EmitConsoleGroup(typeBuilder, runtime, groupLevelField);
        EmitConsoleGroupMultiple(typeBuilder, runtime, groupLevelField);
        EmitConsoleGroupEnd(typeBuilder, runtime, groupLevelField);
        EmitConsoleTrace(typeBuilder, runtime, groupLevelField);
        EmitConsoleTraceMultiple(typeBuilder, runtime, groupLevelField);
    }

    /// <summary>
    /// Emits: public static void ConsoleError(object value)
    /// Writes to stderr.
    /// </summary>
    private void EmitConsoleError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleError = method;

        var il = method.GetILGenerator();
        // Console.Error.WriteLine(Stringify(value))
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleErrorMultiple(object[] values)
    /// Writes multiple values to stderr.
    /// </summary>
    private void EmitConsoleErrorMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleErrorMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]
        );
        runtime.ConsoleErrorMultiple = method;

        var il = method.GetILGenerator();
        // Console.Error.WriteLine(string.Join(" ", values))
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", _types.String, _types.ObjectArray));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleWarn(object value)
    /// Writes to stderr (same as error in Node.js).
    /// </summary>
    private void EmitConsoleWarn(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleWarn",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleWarn = method;

        var il = method.GetILGenerator();
        // Console.Error.WriteLine(Stringify(value))
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleWarnMultiple(object[] values)
    /// Writes multiple values to stderr.
    /// </summary>
    private void EmitConsoleWarnMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleWarnMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]
        );
        runtime.ConsoleWarnMultiple = method;

        var il = method.GetILGenerator();
        // Console.Error.WriteLine(string.Join(" ", values))
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", _types.String, _types.ObjectArray));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleClear()
    /// Clears the console.
    /// </summary>
    private void EmitConsoleClear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleClear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.ConsoleClear = method;

        var il = method.GetILGenerator();

        // Try to clear console, ignore exceptions (e.g., when stdout is redirected)
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "Clear"));
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop); // Ignore exception
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleTime(object label)
    /// Starts a timer with the given label.
    /// </summary>
    private void EmitConsoleTime(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder timersField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleTime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleTime = method;

        var il = method.GetILGenerator();

        // Check if arg is null BEFORE calling Stringify
        var notNullLabel = il.DefineLabel();
        var labelLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue_S, notNullLabel);
        // arg is null - use "default"
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Stloc, labelLocal);
        var afterLabelInit = il.DefineLabel();
        il.Emit(OpCodes.Br_S, afterLabelInit);

        // arg is not null - stringify it
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, labelLocal);

        il.MarkLabel(afterLabelInit);

        // Initialize timers dictionary if null
        var dictInitialized = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Brtrue, dictInitialized);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stsfld, timersField);
        il.MarkLabel(dictInitialized);

        // _consoleTimers[labelStr] = Stopwatch.StartNew()
        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Stopwatch, "StartNew"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleTimeEnd(object label)
    /// Stops timer and prints elapsed time.
    /// </summary>
    private void EmitConsoleTimeEnd(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder timersField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleTimeEnd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleTimeEnd = method;

        var il = method.GetILGenerator();

        // Check if arg is null BEFORE calling Stringify
        var notNullLabel = il.DefineLabel();
        var labelLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue_S, notNullLabel);
        // arg is null - use "default"
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Stloc, labelLocal);
        var afterLabelInit = il.DefineLabel();
        il.Emit(OpCodes.Br_S, afterLabelInit);

        // arg is not null - stringify it
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, labelLocal);
        il.MarkLabel(afterLabelInit);

        // if (_consoleTimers == null || !_consoleTimers.TryGetValue(labelStr, out var sw)) return
        var doneLabel = il.DefineLabel();
        var hasTimerLabel = il.DefineLabel();
        var swLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Brfalse, doneLabel);

        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldloca, swLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, hasTimerLabel);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(hasTimerLabel);

        // Cast to Stopwatch and stop it
        il.Emit(OpCodes.Ldloc, swLocal);
        il.Emit(OpCodes.Castclass, _types.Stopwatch);
        var stopwatchLocal = il.DeclareLocal(_types.Stopwatch);
        il.Emit(OpCodes.Stloc, stopwatchLocal);

        il.Emit(OpCodes.Ldloc, stopwatchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Stopwatch, "Stop"));

        // Print: "{label}: {elapsed}ms"
        // Console.WriteLine($"{labelStr}: {sw.Elapsed.TotalMilliseconds}ms")
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, stopwatchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Stopwatch, "Elapsed").GetMethod!);
        var elapsedLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, elapsedLocal);
        il.Emit(OpCodes.Ldloca, elapsedLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.TimeSpan, "TotalMilliseconds").GetMethod!);
        var msLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, msLocal);
        il.Emit(OpCodes.Ldloca, msLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "ToString"));
        il.Emit(OpCodes.Ldstr, "ms");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // Remove from dictionary
        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleTimeLog(object label)
    /// Prints elapsed time without stopping the timer.
    /// </summary>
    private void EmitConsoleTimeLog(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder timersField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleTimeLog",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleTimeLog = method;

        var il = method.GetILGenerator();

        // Check if arg is null BEFORE calling Stringify
        var notNullLabel = il.DefineLabel();
        var labelLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue_S, notNullLabel);
        // arg is null - use "default"
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Stloc, labelLocal);
        var afterLabelInit = il.DefineLabel();
        il.Emit(OpCodes.Br_S, afterLabelInit);

        // arg is not null - stringify it
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, labelLocal);
        il.MarkLabel(afterLabelInit);

        // if (_consoleTimers == null || !_consoleTimers.TryGetValue(labelStr, out var sw)) return
        var doneLabel = il.DefineLabel();
        var hasTimerLabel = il.DefineLabel();
        var swLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Brfalse, doneLabel);

        il.Emit(OpCodes.Ldsfld, timersField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldloca, swLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, hasTimerLabel);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(hasTimerLabel);

        // Cast to Stopwatch (don't stop it)
        il.Emit(OpCodes.Ldloc, swLocal);
        il.Emit(OpCodes.Castclass, _types.Stopwatch);
        var stopwatchLocal = il.DeclareLocal(_types.Stopwatch);
        il.Emit(OpCodes.Stloc, stopwatchLocal);

        // Print: "{label}: {elapsed}ms"
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloc, stopwatchLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Stopwatch, "Elapsed").GetMethod!);
        var elapsedLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, elapsedLocal);
        il.Emit(OpCodes.Ldloca, elapsedLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.TimeSpan, "TotalMilliseconds").GetMethod!);
        var msLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, msLocal);
        il.Emit(OpCodes.Ldloca, msLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "ToString"));
        il.Emit(OpCodes.Ldstr, "ms");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    // ===================== Phase 2 Console Methods =====================

    /// <summary>
    /// Emits: public static void ConsoleAssert(object condition, object[] messageArgs)
    /// If condition is falsy, writes "Assertion failed" to stderr.
    /// </summary>
    private void EmitConsoleAssert(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleAssert",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.ObjectArray]
        );
        runtime.ConsoleAssert = method;

        var il = method.GetILGenerator();
        var isTruthyLabel = il.DefineLabel();

        // Check if condition is truthy using IsTruthy helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brtrue, isTruthyLabel);

        // Condition is falsy - print "Assertion failed"
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
        il.Emit(OpCodes.Ldstr, "Assertion failed");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));

        il.MarkLabel(isTruthyLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleAssertMultiple(object condition, object[] messageArgs)
    /// If condition is falsy, writes "Assertion failed: {message}" to stderr.
    /// </summary>
    private void EmitConsoleAssertMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleAssertMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.ObjectArray]
        );
        runtime.ConsoleAssertMultiple = method;

        var il = method.GetILGenerator();
        var isTruthyLabel = il.DefineLabel();

        // Check if condition is truthy
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brtrue, isTruthyLabel);

        // Condition is falsy - print "Assertion failed: {message}"
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Console, "Error").GetMethod!);
        il.Emit(OpCodes.Ldstr, "Assertion failed: ");
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", _types.String, _types.ObjectArray));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "WriteLine", _types.String));

        il.MarkLabel(isTruthyLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleCount(object label)
    /// Increments and prints counter for the label.
    /// </summary>
    private void EmitConsoleCount(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder countsField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleCount",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleCount = method;

        var il = method.GetILGenerator();

        // Get label (default to "default" if null)
        var notNullLabel = il.DefineLabel();
        var labelLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue_S, notNullLabel);
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Stloc, labelLocal);
        var afterLabelInit = il.DefineLabel();
        il.Emit(OpCodes.Br_S, afterLabelInit);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, labelLocal);

        il.MarkLabel(afterLabelInit);

        // Initialize counts dictionary if null
        var dictInitialized = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, countsField);
        il.Emit(OpCodes.Brtrue, dictInitialized);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stsfld, countsField);
        il.MarkLabel(dictInitialized);

        // Get current count (default 0), increment, store
        var countLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Object);
        var hasKey = il.DefineLabel();
        var afterGet = il.DefineLabel();

        // if (dict.TryGetValue(label, out var val)) count = (int)(double)val else count = 0
        il.Emit(OpCodes.Ldsfld, countsField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, hasKey);

        // No key - count = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, countLocal);
        il.Emit(OpCodes.Br, afterGet);

        // Has key - count = (int)(double)value
        il.MarkLabel(hasKey);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, countLocal);

        il.MarkLabel(afterGet);

        // count++
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, countLocal);

        // Store back as double (for Dictionary<string, object>)
        il.Emit(OpCodes.Ldsfld, countsField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Print "{label}: {count}"
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Ldloca, countLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleCountReset(object label)
    /// Resets counter for the label to 0.
    /// </summary>
    private void EmitConsoleCountReset(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder countsField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleCountReset",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleCountReset = method;

        var il = method.GetILGenerator();

        // Get label (default to "default" if null)
        var notNullLabel = il.DefineLabel();
        var labelLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue_S, notNullLabel);
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Stloc, labelLocal);
        var afterLabelInit = il.DefineLabel();
        il.Emit(OpCodes.Br_S, afterLabelInit);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, labelLocal);

        il.MarkLabel(afterLabelInit);

        // Initialize counts dictionary if null
        var dictInitialized = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, countsField);
        il.Emit(OpCodes.Brtrue, dictInitialized);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stsfld, countsField);
        il.MarkLabel(dictInitialized);

        // Set count to 0
        il.Emit(OpCodes.Ldsfld, countsField);
        il.Emit(OpCodes.Ldloc, labelLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleTable(object data, object columns)
    /// Prints data in a simplified table format for standalone DLLs.
    /// </summary>
    private void EmitConsoleTable(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleTable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.ConsoleTable = method;

        var il = method.GetILGenerator();

        // Simplified table output for standalone DLLs
        // Formats: arrays as indexed entries, dictionaries as key-value pairs

        var nullLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (data == null) goto nullLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (data is List<object>) goto listLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (data is Dictionary<string, object>) goto dictLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // default: just stringify and print
        il.Emit(OpCodes.Br, defaultLabel);

        // nullLabel: print "null"
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Br, endLabel);

        // listLabel: print array as table
        il.MarkLabel(listLabel);
        EmitPrintListAsTable(il, runtime);
        il.Emit(OpCodes.Br, endLabel);

        // dictLabel: print dictionary as table
        il.MarkLabel(dictLabel);
        EmitPrintDictAsTable(il, runtime);
        il.Emit(OpCodes.Br, endLabel);

        // defaultLabel: stringify and print
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to print a List as a simple table.
    /// Format: (index): value
    /// </summary>
    private void EmitPrintListAsTable(ILGenerator il, EmittedRuntime runtime)
    {
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notEmptyLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // number[] unboxing: materialize a numeric-mode $Array before reading its base list as a table.
        EmitDeoptArgIfNumericArray(il, runtime, 0);

        // var list = (List<object>)data
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // if (list.Count > 0) goto notEmpty
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // print "(empty array)" and return
        il.Emit(OpCodes.Ldstr, "(empty array)");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notEmptyLabel);

        // Print header with ASCII table borders
        il.Emit(OpCodes.Ldstr, "+---------+----------------------+");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ldstr, "| (index) | Value                |");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ldstr, "+---------+----------------------+");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // for (int i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Bge, loopEnd);

        // Build output: $"| {i,7} | {Stringify(list[i]).PadRight(20)} |"
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        // Append "| "
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "| ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append index (padded to 7 chars)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "PadLeft", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append " | "
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " | ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append Stringify(list[i]) padded to 20 chars
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "PadRight", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append " |"
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " |");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Console.WriteLine(sb.ToString())
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Print footer
        il.Emit(OpCodes.Ldstr, "+---------+----------------------+");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits IL to print a Dictionary as a simple table.
    /// Format: key | value
    /// </summary>
    private void EmitPrintDictAsTable(ILGenerator il, EmittedRuntime runtime)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var sbLocal = il.DeclareLocal(_types.StringBuilder);

        // Get enumerator types
        var enumeratorType = typeof(Dictionary<string, object?>.Enumerator);
        var kvpType = typeof(KeyValuePair<string, object?>);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notEmptyLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // var dict = (Dictionary<string, object>)data
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count > 0) goto notEmpty
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.DictionaryStringObject, "Count"));
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // print "(empty object)" and return
        il.Emit(OpCodes.Ldstr, "(empty object)");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notEmptyLabel);

        // Print header with ASCII table borders
        il.Emit(OpCodes.Ldstr, "+----------+----------------------+");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ldstr, "| (index)  | Values               |");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ldstr, "+----------+----------------------+");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        // while (enumerator.MoveNext())
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Build output: $"| {key.PadRight(8)} | {Stringify(value).PadRight(20)} |"
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        // Append "| "
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "| ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append key (padded to 8 chars)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        var kvpLocal = il.DeclareLocal(kvpType);
        il.Emit(OpCodes.Stloc, kvpLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "PadRight", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append " | "
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " | ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append Stringify(value) padded to 20 chars
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "PadRight", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append " |"
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, " |");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Console.WriteLine(sb.ToString())
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Print footer
        il.Emit(OpCodes.Ldstr, "+----------+----------------------+");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits: public static void ConsoleDir(object obj)
    /// Prints object in an inspected format using UtilInspectValue.
    /// </summary>
    private void EmitConsoleDir(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleDir",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleDir = method;

        var il = method.GetILGenerator();

        // Call Console.WriteLine(UtilInspectValue(obj, 2, 0))
        // UtilInspectValue(value, depth, currentDepth) returns formatted string
        il.Emit(OpCodes.Ldarg_0);       // obj
        il.Emit(OpCodes.Ldc_I4_2);      // depth = 2 (maxDepth)
        il.Emit(OpCodes.Ldc_I4_0);      // currentDepth = 0
        il.Emit(OpCodes.Call, runtime.UtilInspectValue);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleGroup(object label)
    /// Prints label and increases indent level.
    /// </summary>
    private void EmitConsoleGroup(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleGroup",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleGroup = method;

        var il = method.GetILGenerator();
        var skipLabel = il.DefineLabel();

        // If label is not null, print it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.MarkLabel(skipLabel);

        // _consoleGroupLevel++
        il.Emit(OpCodes.Ldsfld, groupLevelField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stsfld, groupLevelField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleGroupMultiple(object[] labels)
    /// Prints labels joined by space and increases indent level.
    /// </summary>
    private void EmitConsoleGroupMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleGroupMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]
        );
        runtime.ConsoleGroupMultiple = method;

        var il = method.GetILGenerator();

        // Print string.Join(" ", args)
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", _types.String, _types.ObjectArray));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // _consoleGroupLevel++
        il.Emit(OpCodes.Ldsfld, groupLevelField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stsfld, groupLevelField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleGroupEnd()
    /// Decreases indent level.
    /// </summary>
    private void EmitConsoleGroupEnd(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleGroupEnd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.ConsoleGroupEnd = method;

        var il = method.GetILGenerator();
        var skipLabel = il.DefineLabel();

        // if (_consoleGroupLevel > 0) _consoleGroupLevel--
        il.Emit(OpCodes.Ldsfld, groupLevelField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipLabel);

        il.Emit(OpCodes.Ldsfld, groupLevelField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stsfld, groupLevelField);

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleTrace(object message)
    /// Prints "Trace: {message}" and a stack trace.
    /// </summary>
    private void EmitConsoleTrace(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleTrace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ConsoleTrace = method;

        var il = method.GetILGenerator();
        var skipMessageLabel = il.DefineLabel();
        var afterMessageLabel = il.DefineLabel();

        // Print "Trace: " + message (if not null)
        il.Emit(OpCodes.Ldstr, "Trace: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, skipMessageLabel);

        // Has message
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, afterMessageLabel);

        // No message - just "Trace: "
        il.MarkLabel(skipMessageLabel);
        // Stack has "Trace: " already

        il.MarkLabel(afterMessageLabel);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // Print stack trace (simplified - just output a new StackTrace)
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StackTrace, Type.EmptyTypes));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ConsoleTraceMultiple(object[] args)
    /// Prints "Trace: {message}" and a stack trace with multiple args.
    /// </summary>
    private void EmitConsoleTraceMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder groupLevelField)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleTraceMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]
        );
        runtime.ConsoleTraceMultiple = method;

        var il = method.GetILGenerator();

        // Print "Trace: " + string.Join(" ", args)
        il.Emit(OpCodes.Ldstr, "Trace: ");
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", _types.String, _types.ObjectArray));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        // Print stack trace
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StackTrace, Type.EmptyTypes));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));

        il.Emit(OpCodes.Ret);
    }
}
