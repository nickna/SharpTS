using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits process global helper methods (GetEnv, GetArgv, Hrtime, Uptime, MemoryUsage, stdin/stdout/stderr).
    /// </summary>
    private void EmitProcessMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitProcessGetEnv(typeBuilder, runtime);
        EmitProcessGetArgv(typeBuilder, runtime);
        EmitProcessHrtime(typeBuilder, runtime);
        EmitProcessUptime(typeBuilder, runtime);
        EmitProcessMemoryUsage(typeBuilder, runtime);
        EmitProcessGetNextTick(typeBuilder, runtime);
        EmitProcessEventEmitterCallMethod(typeBuilder, runtime);
        EmitProcessEmitExitMethod(typeBuilder, runtime);
        EmitGetProcessEventEmitter(typeBuilder, runtime);
        EmitStdinMethods(typeBuilder, runtime);
        EmitStdoutMethods(typeBuilder, runtime);
        EmitStderrMethods(typeBuilder, runtime);
        // process.stdout / stderr / stdin singletons are $Writable / $Readable
        // instances. Without UsesNodeStreams the stream types don't exist.
        if (_features.UsesNodeStreams)
            EmitProcessStreamSingletons(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object ProcessGetEnv()
    /// Creates a Dictionary containing environment variables and wraps it as an object.
    /// </summary>
    private void EmitProcessGetEnv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessGetEnv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ProcessGetEnv = method;

        var il = method.GetILGenerator();

        // Create new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        // This ensures case-insensitive lookup for environment variables (Windows uses "Path" not "PATH")
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.StringComparer, "OrdinalIgnoreCase"));
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, _types.IEqualityComparerOfString));
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Get environment variables: Environment.GetEnvironmentVariables()
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetEnvironmentVariables"));
        var envVarsLocal = il.DeclareLocal(_types.IDictionary);
        il.Emit(OpCodes.Stloc, envVarsLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, envVarsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDictionary, "GetEnumerator"));
        var enumeratorLocal = il.DeclareLocal(_types.IDictionaryEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop: while (enumerator.MoveNext())
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current entry key and value
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.IDictionaryEnumerator, "Key").GetMethod!);
        var keyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.IDictionaryEnumerator, "Value").GetMethod!);
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, valueLocal);

        // dict[key.ToString()] = value?.ToString()
        il.Emit(OpCodes.Ldloc, dictLocal);

        // key.ToString()
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));

        // value?.ToString() - check if value is null
        var valueNotNull = il.DefineLabel();
        var afterValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brtrue, valueNotNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, afterValue);
        il.MarkLabel(valueNotNull);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterValue);

        // Set the dictionary entry
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Wrap in SharpTSObject and return
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ProcessGetArgv()
    /// Creates a List containing command line arguments in Node.js format.
    /// Node.js argv: [runtime_path, script_path, ...args]
    /// We prepend the executable path to maintain compatibility with code
    /// that does process.argv.slice(2) to get actual arguments.
    /// </summary>
    private void EmitProcessGetArgv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessGetArgv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ProcessGetArgv = method;

        var il = method.GetILGenerator();

        // Get command line args first (we need args[0] as fallback)
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetCommandLineArgs"));
        var argsLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Create new List<object?>
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Add Environment.ProcessPath ?? args[0] as argv[0] (executable path)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Environment, "ProcessPath"));
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        // ProcessPath was null, use args[0] instead
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // Loop through args and add to list
        // int i = 0
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // while (i < args.Length)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // list.Add(args[i])
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Return the list
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ProcessHrtime(object? prev)
    /// Returns a [seconds, nanoseconds] tuple as a SharpTSArray.
    /// </summary>
    private void EmitProcessHrtime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessHrtime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ProcessHrtime = method;

        var il = method.GetILGenerator();

        // Get static field references for start timestamp and frequency
        var stopwatchType = _types.Stopwatch;
        var getTimestampMethod = _types.GetMethodNoParams(stopwatchType, "GetTimestamp");
        var frequencyField = _types.GetField(stopwatchType, "Frequency");

        // Store initial values
        // We need to store current ticks first
        il.Emit(OpCodes.Call, getTimestampMethod);
        var currentTicksLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, currentTicksLocal);

        // Get frequency
        il.Emit(OpCodes.Ldsfld, frequencyField);
        var frequencyLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, frequencyLocal);

        // Calculate total nanoseconds: (currentTicks * 1_000_000_000.0) / frequency
        il.Emit(OpCodes.Ldloc, currentTicksLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 1_000_000_000.0);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, frequencyLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Div);
        var totalNanosLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, totalNanosLocal);

        // Check if prev argument is not null and is a List<object?>
        var noPrevTime = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0); // prev
        il.Emit(OpCodes.Brfalse, noPrevTime);

        // Try to check if prev is a List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, noPrevTime);

        // prev is a List<object?>, use it directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        var elementsLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, elementsLocal);

        // Check if we have at least 2 elements
        il.Emit(OpCodes.Ldloc, elementsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, noPrevTime);

        // Get prevSeconds = elements[0]
        il.Emit(OpCodes.Ldloc, elementsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var prevSecondsLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, prevSecondsLocal);

        // Get prevNanos = elements[1]
        il.Emit(OpCodes.Ldloc, elementsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var prevNanosLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, prevNanosLocal);

        // Calculate prevTotalNanos = prevSeconds * 1_000_000_000.0 + prevNanos
        il.Emit(OpCodes.Ldloc, prevSecondsLocal);
        il.Emit(OpCodes.Ldc_R8, 1_000_000_000.0);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, prevNanosLocal);
        il.Emit(OpCodes.Add);

        // Subtract from totalNanos
        il.Emit(OpCodes.Ldloc, totalNanosLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Neg); // We computed (prev - current), need (current - prev)
        il.Emit(OpCodes.Stloc, totalNanosLocal);

        il.MarkLabel(noPrevTime);

        // Calculate seconds = floor(totalNanos / 1_000_000_000.0)
        il.Emit(OpCodes.Ldloc, totalNanosLocal);
        il.Emit(OpCodes.Ldc_R8, 1_000_000_000.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", _types.Double));
        var secondsLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, secondsLocal);

        // Calculate nanos = totalNanos % 1_000_000_000.0
        il.Emit(OpCodes.Ldloc, totalNanosLocal);
        il.Emit(OpCodes.Ldc_R8, 1_000_000_000.0);
        il.Emit(OpCodes.Rem);
        var nanosLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, nanosLocal);

        // Create new List<object?>
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Add seconds (boxed)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, secondsLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // Add nanos (boxed)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, nanosLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // Return the list directly (compiled arrays are List<object?>)
        il.Emit(OpCodes.Ldloc, resultLocal);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static double ProcessUptime()
    /// Returns the number of seconds the process has been running.
    /// </summary>
    private void EmitProcessUptime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessUptime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.ProcessUptime = method;

        var il = method.GetILGenerator();

        // Monotonic uptime: (now - baseline) / Stopwatch.Frequency, where baseline is a
        // Stopwatch timestamp captured in the $Runtime .cctor. Stopwatch is monotonic,
        // so two successive reads never decrease — unlike the former DateTime.UtcNow -
        // Process.StartTime, which an NTP slew could reverse (intermittently failing
        // Process_Uptime_IncreasesOverTime).
        //
        // CRITICAL ordering: read the baseline field BEFORE sampling 'now'. $Runtime is
        // beforefieldinit, so its .cctor (which stamps the baseline) runs lazily at the
        // first static-field access. Sampling 'now' first and then touching the field
        // would let the .cctor stamp the baseline AFTER 'now', yielding a tiny negative
        // uptime. Loading the field first forces the .cctor, so baseline <= now always.
        var stopwatchType = _types.Stopwatch;

        var baselineLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldsfld, runtime.ProcessUptimeBaselineField);
        il.Emit(OpCodes.Stloc, baselineLocal);

        // (now - baseline) as ticks, widened to double
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(stopwatchType, "GetTimestamp"));
        il.Emit(OpCodes.Ldloc, baselineLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);

        // / (double)Stopwatch.Frequency
        il.Emit(OpCodes.Ldsfld, _types.GetField(stopwatchType, "Frequency"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Div);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ProcessMemoryUsage()
    /// Returns an object with memory usage information.
    /// </summary>
    private void EmitProcessMemoryUsage(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessMemoryUsage",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ProcessMemoryUsage = method;

        var il = method.GetILGenerator();

        // Get current process for WorkingSet64
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Process, "GetCurrentProcess"));
        var processLocal = il.DeclareLocal(_types.Process);
        il.Emit(OpCodes.Stloc, processLocal);

        // Get rss (WorkingSet64)
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "WorkingSet64"));
        il.Emit(OpCodes.Conv_R8);
        var rssLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, rssLocal);

        // Get heapUsed (GC.GetTotalMemory(false))
        il.Emit(OpCodes.Ldc_I4_0); // false
        il.Emit(OpCodes.Call, _types.GetMethod(_types.GC, "GetTotalMemory", _types.Boolean));
        il.Emit(OpCodes.Conv_R8);
        var heapUsedLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, heapUsedLocal);

        // Create Dictionary<string, object?>
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add rss
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "rss");
        il.Emit(OpCodes.Ldloc, rssLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Add heapTotal (same as heapUsed for now)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "heapTotal");
        il.Emit(OpCodes.Ldloc, heapUsedLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Add heapUsed
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "heapUsed");
        il.Emit(OpCodes.Ldloc, heapUsedLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Add external (0.0)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "external");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Add arrayBuffers (0.0)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "arrayBuffers");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits stdin methods (Read, IsTTY).
    /// </summary>
    private void EmitStdinMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StdinRead: public static object StdinRead()
        var readMethod = typeBuilder.DefineMethod(
            "StdinRead",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.StdinRead = readMethod;

        var readIl = readMethod.GetILGenerator();
        // Call Console.ReadLine() - returns string or null
        readIl.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "ReadLine"));
        readIl.Emit(OpCodes.Ret);

        // StdinIsTTY: public static object StdinIsTTY()
        var isTtyMethod = typeBuilder.DefineMethod(
            "StdinIsTTY",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.StdinIsTTY = isTtyMethod;

        var isTtyIl = isTtyMethod.GetILGenerator();
        // Return !Console.IsInputRedirected
        isTtyIl.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "IsInputRedirected"));
        isTtyIl.Emit(OpCodes.Ldc_I4_0);
        isTtyIl.Emit(OpCodes.Ceq); // Negate: true becomes false, false becomes true
        isTtyIl.Emit(OpCodes.Box, _types.Boolean);
        isTtyIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits stdout methods (Write, IsTTY).
    /// </summary>
    private void EmitStdoutMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StdoutWrite: public static object StdoutWrite(object data)
        var writeMethod = typeBuilder.DefineMethod(
            "StdoutWrite",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.StdoutWrite = writeMethod;

        var writeIl = writeMethod.GetILGenerator();
        // Convert to string if needed and write
        writeIl.Emit(OpCodes.Ldarg_0);
        writeIl.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        writeIl.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));
        // Return true
        writeIl.Emit(OpCodes.Ldc_I4_1);
        writeIl.Emit(OpCodes.Box, _types.Boolean);
        writeIl.Emit(OpCodes.Ret);

        // StdoutIsTTY: public static object StdoutIsTTY()
        var isTtyMethod = typeBuilder.DefineMethod(
            "StdoutIsTTY",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.StdoutIsTTY = isTtyMethod;

        var isTtyIl = isTtyMethod.GetILGenerator();
        // Return !Console.IsOutputRedirected
        isTtyIl.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "IsOutputRedirected"));
        isTtyIl.Emit(OpCodes.Ldc_I4_0);
        isTtyIl.Emit(OpCodes.Ceq); // Negate
        isTtyIl.Emit(OpCodes.Box, _types.Boolean);
        isTtyIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits stderr methods (Write, IsTTY).
    /// </summary>
    private void EmitStderrMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // StderrWrite: public static object StderrWrite(object data)
        var writeMethod = typeBuilder.DefineMethod(
            "StderrWrite",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.StderrWrite = writeMethod;

        var writeIl = writeMethod.GetILGenerator();
        // Get Console.Error (TextWriter) and write to it
        writeIl.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "Error"));
        writeIl.Emit(OpCodes.Ldarg_0);
        writeIl.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        writeIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "Write", _types.String));
        // Return true
        writeIl.Emit(OpCodes.Ldc_I4_1);
        writeIl.Emit(OpCodes.Box, _types.Boolean);
        writeIl.Emit(OpCodes.Ret);

        // StderrIsTTY: public static object StderrIsTTY()
        var isTtyMethod = typeBuilder.DefineMethod(
            "StderrIsTTY",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.StderrIsTTY = isTtyMethod;

        var isTtyIl = isTtyMethod.GetILGenerator();
        // Return !Console.IsErrorRedirected
        isTtyIl.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "IsErrorRedirected"));
        isTtyIl.Emit(OpCodes.Ldc_I4_0);
        isTtyIl.Emit(OpCodes.Ceq); // Negate
        isTtyIl.Emit(OpCodes.Box, _types.Boolean);
        isTtyIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits singleton cached $Writable/$Readable instances for process.stdout/stderr/stdin.
    /// Each getter creates the instance on first call and caches it in a static field.
    /// stdout/stderr get a write callback that writes to Console.Out/Console.Error.
    /// </summary>
    private void EmitProcessStreamSingletons(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // --- stdout write callback: static method that writes to Console.Out ---
        var stdoutWriteImpl = typeBuilder.DefineMethod(
            "StdoutWriteImpl",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object] // chunk, encoding, doneCallback
        );
        {
            var il = stdoutWriteImpl.GetILGenerator();
            // Console.Write(chunk?.ToString() ?? "")
            il.Emit(OpCodes.Ldarg_0); // chunk
            var chunkNullLabel = il.DefineLabel();
            var afterChunkLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, chunkNullLabel);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Br, afterChunkLabel);
            il.MarkLabel(chunkNullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.MarkLabel(afterChunkLabel);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));
            // Call doneCallback if not null (it's a $WriteCallbackWrapper)
            var noDoneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2); // doneCallback
            il.Emit(OpCodes.Brfalse, noDoneLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, noDoneLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldnull); // this
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(noDoneLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // --- stderr write callback: static method that writes to Console.Error ---
        var stderrWriteImpl = typeBuilder.DefineMethod(
            "StderrWriteImpl",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        {
            var il = stderrWriteImpl.GetILGenerator();
            il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.Console, "Error"));
            il.Emit(OpCodes.Ldarg_0);
            var chunkNullLabel = il.DefineLabel();
            var afterChunkLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, chunkNullLabel);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Br, afterChunkLabel);
            il.MarkLabel(chunkNullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.MarkLabel(afterChunkLabel);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TextWriter, "Write", _types.String));
            // Call doneCallback if not null
            var noDoneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brfalse, noDoneLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, noDoneLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(noDoneLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // --- Static cache fields ---
        runtime.StdoutInstance = typeBuilder.DefineField("_stdoutInstance", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        runtime.StderrInstance = typeBuilder.DefineField("_stderrInstance", _types.Object, FieldAttributes.Private | FieldAttributes.Static);
        runtime.StdinInstance = typeBuilder.DefineField("_stdinInstance", _types.Object, FieldAttributes.Private | FieldAttributes.Static);

        // --- GetStdout: create $Writable with Console.Write callback, cache in static field ---
        runtime.GetStdout = EmitStreamSingletonGetter(typeBuilder, runtime, "GetStdout",
            runtime.StdoutInstance, runtime.TSWritableCtor, stdoutWriteImpl);

        // --- GetStderr: create $Writable with Console.Error.Write callback ---
        runtime.GetStderr = EmitStreamSingletonGetter(typeBuilder, runtime, "GetStderr",
            runtime.StderrInstance, runtime.TSWritableCtor, stderrWriteImpl);

        // --- GetStdin: create $Readable (no write callback needed) ---
        runtime.GetStdin = EmitStreamSingletonGetter(typeBuilder, runtime, "GetStdin",
            runtime.StdinInstance, runtime.TSReadableCtor, null);
    }

    private MethodBuilder EmitStreamSingletonGetter(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        string methodName, FieldBuilder cacheField,
        ConstructorBuilder streamCtor,
        MethodBuilder? writeImpl)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // if (_instance != null) return _instance;
        var createLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cacheField);
        il.Emit(OpCodes.Brfalse, createLabel);
        il.Emit(OpCodes.Ldsfld, cacheField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(createLabel);

        // var stream = new $Writable() or new $Readable()
        il.Emit(OpCodes.Newobj, streamCtor);

        // If write callback provided, set it
        if (writeImpl != null)
        {
            il.Emit(OpCodes.Dup); // keep stream on stack

            // Create $TSFunction wrapping the write impl method
            il.Emit(OpCodes.Ldnull); // target (static method, no instance)
            il.Emit(OpCodes.Ldtoken, writeImpl);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);

            // Call stream.SetWriteCallback(tsFunction)
            il.Emit(OpCodes.Callvirt, runtime.TSWritableType.GetMethod("SetWriteCallback")!);
        }

        // Cache: _instance = stream
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, cacheField);

        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits: public static object ProcessGetNextTick()
    /// Returns a TSFunction wrapper for process.nextTick.
    /// The returned function, when called, schedules its callback via SetTimeout with delay 0.
    /// </summary>
    private void EmitProcessGetNextTick(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First, emit the implementation method that will be wrapped
        // Use List<object> as the parameter type because TSFunction.AdjustArgs
        // recognizes List<object> as a rest parameter and packs all args into it
        var implMethod = typeBuilder.DefineMethod(
            "ProcessNextTickImpl",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );

        var implIl = implMethod.GetILGenerator();

        // ProcessNextTickImpl(List<object> args):
        // - args[0] is the callback
        // - args[1..] are the callback arguments
        // We call SetTimeout(callback, 0, callbackArgs)

        // Store callback in local first (needs casting to TSFunctionType)
        var callbackLocal = implIl.DeclareLocal(runtime.TSFunctionType);
        implIl.Emit(OpCodes.Ldarg_0);
        implIl.Emit(OpCodes.Ldc_I4_0);
        implIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        implIl.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        implIl.Emit(OpCodes.Stloc, callbackLocal);

        // Create callback args array (args[1..])
        // int extraArgCount = args.Count - 1
        var argsLengthLocal = implIl.DeclareLocal(_types.Int32);
        implIl.Emit(OpCodes.Ldarg_0);
        implIl.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        implIl.Emit(OpCodes.Ldc_I4_1);
        implIl.Emit(OpCodes.Sub);
        implIl.Emit(OpCodes.Stloc, argsLengthLocal);

        // Create new object[extraArgCount]
        var extraArgsLocal = implIl.DeclareLocal(_types.ObjectArray);
        var skipCopy = implIl.DefineLabel();
        var afterCopy = implIl.DefineLabel();

        // if (extraArgCount <= 0) create empty array
        implIl.Emit(OpCodes.Ldloc, argsLengthLocal);
        implIl.Emit(OpCodes.Ldc_I4_0);
        implIl.Emit(OpCodes.Ble, skipCopy);

        // Create and copy extra args
        implIl.Emit(OpCodes.Ldloc, argsLengthLocal);
        implIl.Emit(OpCodes.Newarr, _types.Object);
        implIl.Emit(OpCodes.Stloc, extraArgsLocal);

        // Copy loop: for (int i = 0; i < extraArgCount; i++) extraArgs[i] = args[i+1]
        var indexLocal = implIl.DeclareLocal(_types.Int32);
        implIl.Emit(OpCodes.Ldc_I4_0);
        implIl.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = implIl.DefineLabel();
        var loopEnd = implIl.DefineLabel();

        implIl.MarkLabel(loopStart);
        implIl.Emit(OpCodes.Ldloc, indexLocal);
        implIl.Emit(OpCodes.Ldloc, argsLengthLocal);
        implIl.Emit(OpCodes.Bge, loopEnd);

        // extraArgs[i] = args[i + 1]
        implIl.Emit(OpCodes.Ldloc, extraArgsLocal);
        implIl.Emit(OpCodes.Ldloc, indexLocal);
        implIl.Emit(OpCodes.Ldarg_0);
        implIl.Emit(OpCodes.Ldloc, indexLocal);
        implIl.Emit(OpCodes.Ldc_I4_1);
        implIl.Emit(OpCodes.Add);
        implIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        implIl.Emit(OpCodes.Stelem_Ref);

        // i++
        implIl.Emit(OpCodes.Ldloc, indexLocal);
        implIl.Emit(OpCodes.Ldc_I4_1);
        implIl.Emit(OpCodes.Add);
        implIl.Emit(OpCodes.Stloc, indexLocal);
        implIl.Emit(OpCodes.Br, loopStart);

        implIl.MarkLabel(loopEnd);
        implIl.Emit(OpCodes.Br, afterCopy);

        // Skip copy: create empty array
        implIl.MarkLabel(skipCopy);
        implIl.Emit(OpCodes.Ldc_I4_0);
        implIl.Emit(OpCodes.Newarr, _types.Object);
        implIl.Emit(OpCodes.Stloc, extraArgsLocal);

        implIl.MarkLabel(afterCopy);

        // Now set up the call: SetTimeout(callback, delay, args)
        implIl.Emit(OpCodes.Ldloc, callbackLocal);  // callback (TSFunctionType)
        implIl.Emit(OpCodes.Ldc_R8, 0.0);           // delay = 0
        implIl.Emit(OpCodes.Ldloc, extraArgsLocal); // args

        // Call SetTimeout(callback, 0, extraArgs)
        implIl.Emit(OpCodes.Call, runtime.SetTimeout);

        // nextTick returns undefined
        implIl.Emit(OpCodes.Pop);
        implIl.Emit(OpCodes.Ldnull);
        implIl.Emit(OpCodes.Ret);

        // Now emit the getter method that returns a TSFunction wrapping the impl
        var getterMethod = typeBuilder.DefineMethod(
            "ProcessGetNextTick",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ProcessGetNextTick = getterMethod;

        var il = getterMethod.GetILGenerator();

        // Create new TSFunction(null, implMethod)
        il.Emit(OpCodes.Ldnull); // target (static method)
        il.Emit(OpCodes.Ldtoken, implMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ProcessEventEmitterCall(string methodName, object[] args)
    /// Delegates to ProcessBuiltIns.EventEmitterCall via reflection.
    /// </summary>
    private void EmitProcessEventEmitterCallMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessEventEmitterCall",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.ObjectArray]
        );
        runtime.ProcessEventEmitterCall = method;

        var il = method.GetILGenerator();

        // Type t = Type.GetType("SharpTS.Runtime.BuiltIns.ProcessBuiltIns, SharpTS");
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.BuiltIns.ProcessBuiltIns, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        var typeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, typeLocal);

        var typeOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brtrue, typeOk);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(typeOk);

        // MethodInfo m = t.GetMethod("EventEmitterCall");
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "EventEmitterCall");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));

        // m.Invoke(null, new object[] { methodName, args })
        il.Emit(OpCodes.Ldnull); // static method
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0); // methodName (string)
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_1); // args (object[])
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ProcessEmitExit(int exitCode)
    /// Calls ProcessBuiltIns.EmitExitEvent to fire the 'exit' event.
    /// </summary>
    private void EmitProcessEmitExitMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessEmitExit",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [_types.Int32]
        );
        runtime.ProcessEmitExit = method;

        var il = method.GetILGenerator();

        // Type.GetType("SharpTS.Runtime.BuiltIns.ProcessBuiltIns, SharpTS")
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.BuiltIns.ProcessBuiltIns, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        var typeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, typeLocal);

        // If type not found, just return
        var typeOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Brtrue, typeOk);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(typeOk);

        // Call EmitExitEvent(null, exitCode) via reflection
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "EmitExitEvent");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull); // static method
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull); // interpreter = null
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0); // exitCode
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop); // discard return value
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a static field and getter for a process-level EventEmitter singleton.
    /// The field is a $EventEmitter instance created lazily.
    /// </summary>
    private void EmitGetProcessEventEmitter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Static field to store the singleton
        var field = typeBuilder.DefineField(
            "_processEventEmitter",
            runtime.TSEventEmitterType,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = field;

        // Getter method that lazily creates the instance
        var getter = typeBuilder.DefineMethod(
            "GetProcessEventEmitter",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSEventEmitterType,
            Type.EmptyTypes
        );
        runtime.GetProcessEventEmitter = getter;

        var il = getter.GetILGenerator();

        // if (_processEventEmitter != null) return it
        il.Emit(OpCodes.Ldsfld, field);
        var createNew = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, createNew);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ret);

        // Create new instance
        il.MarkLabel(createNew);
        il.Emit(OpCodes.Newobj, runtime.TSEventEmitterCtor);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Ret);
    }
}
