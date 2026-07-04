using System.Reflection;
using System.Reflection.Emit;
using System.Text;

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

        // number[] unboxing: materialize a numeric-mode $Array before any array-formatting reads its base list.
        EmitDeoptArgIfNumericArray(il, runtime, 0);

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

        // No format specifiers: join with spaces using Stringify for JS-compatible output
        // Console.WriteLine(GetConsoleIndent() + JoinWithStringify(" ", args))
        il.MarkLabel(noFormatLabel);
        il.Emit(OpCodes.Call, runtime.GetConsoleIndent);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JoinWithStringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "WriteLine", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits JoinWithStringify: joins array elements with separator using Stringify for JS-compatible output.
    /// Signature: string JoinWithStringify(string separator, object[] args)
    /// </summary>
    private void EmitJoinWithStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JoinWithStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.ObjectArray]
        );
        runtime.JoinWithStringify = method;

        var il = method.GetILGenerator();

        // StringBuilder result = new StringBuilder()
        var resultLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // int i = 0
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (i >= args.Length) goto loopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) result.Append(separator)
        var skipSeparator = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipSeparator);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0); // separator
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipSeparator);

        // result.Append(Stringify(args[i]))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return result.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
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
        // Escape backslash and quote: s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldstr, "\\");
        il.Emit(OpCodes.Ldstr, "\\\\");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.String, _types.String));
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldstr, "\\\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", _types.String, _types.String));
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notString);

        // if (value is List<object?> list)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        var notList = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notList);

        // Build "[" + elements.join(",") + "]"
        var listSbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, listSbLocal);
        il.Emit(OpCodes.Ldloc, listSbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Iterate list
        var listIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, listIdxLocal);
        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();
        il.Emit(OpCodes.Br, listLoopEnd);

        il.MarkLabel(listLoopStart);
        // if (i > 0) sb.Append(",")
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        var skipCommaLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipCommaLabel);
        il.Emit(OpCodes.Ldloc, listSbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipCommaLabel);

        // sb.Append(FormatAsJson(list[i]))
        il.Emit(OpCodes.Ldloc, listSbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Call, method); // Recursive call to FormatAsJson
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // i++
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, listIdxLocal);

        il.MarkLabel(listLoopEnd);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Blt, listLoopStart);

        // sb.Append("]")
        il.Emit(OpCodes.Ldloc, listSbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, listSbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notList);

        // if (value is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDict);

        // Build "{" + pairs.join(",") + "}"
        var dictSbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, dictSbLocal);
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Get enumerator for key-value pairs
        var dictEnumLocal = il.DeclareLocal(_types.DictionaryStringObjectEnumerator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.DictionaryStringObject, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, dictEnumLocal);

        var dictFirstLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, dictFirstLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, dictEnumLocal);
        il.Emit(OpCodes.Call, _types.DictionaryStringObjectEnumerator.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // if (!first) sb.Append(",")
        il.Emit(OpCodes.Ldloc, dictFirstLocal);
        var skipDictComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipDictComma);
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipDictComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, dictFirstLocal);

        // Get current key-value pair
        var kvpLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Ldloca, dictEnumLocal);
        il.Emit(OpCodes.Call, _types.DictionaryStringObjectEnumerator.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // sb.Append("\"").Append(key).Append("\":").Append(FormatAsJson(value))
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.KeyValuePairStringObject.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Ldstr, "\":");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        // Append FormatAsJson(value)
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.KeyValuePairStringObject.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, method); // Recursive call
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDict);

        // Fallback: use Stringify (already emitted in $Runtime)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ret);
    }
}
