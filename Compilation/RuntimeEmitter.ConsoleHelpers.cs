using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits console extension methods (error, warn, info, debug, clear, time, timeEnd, timeLog).
    /// </summary>
    private void EmitConsoleExtensions(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit static field for timers dictionary: Dictionary<string, Stopwatch>
        var timersField = typeBuilder.DefineField(
            "_consoleTimers",
            _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.ConsoleTimersField = timersField;

        // Initialize timers dictionary in static constructor (already exists, but we need to add to it)
        // Actually, we'll initialize lazily in the time methods

        EmitConsoleError(typeBuilder, runtime);
        EmitConsoleErrorMultiple(typeBuilder, runtime);
        EmitConsoleWarn(typeBuilder, runtime);
        EmitConsoleWarnMultiple(typeBuilder, runtime);
        EmitConsoleClear(typeBuilder, runtime);
        EmitConsoleTime(typeBuilder, runtime, timersField);
        EmitConsoleTimeEnd(typeBuilder, runtime, timersField);
        EmitConsoleTimeLog(typeBuilder, runtime, timersField);
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
}
