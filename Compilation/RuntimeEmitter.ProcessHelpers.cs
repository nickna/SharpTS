using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits process global helper methods (GetEnv, GetArgv, Hrtime, Uptime, MemoryUsage).
    /// </summary>
    private void EmitProcessMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitProcessGetEnv(typeBuilder, runtime);
        EmitProcessGetArgv(typeBuilder, runtime);
        EmitProcessHrtime(typeBuilder, runtime);
        EmitProcessUptime(typeBuilder, runtime);
        EmitProcessMemoryUsage(typeBuilder, runtime);
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

        // Create new Dictionary<string, object?>
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
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
    /// Creates a SharpTSArray containing command line arguments.
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

        // Get command line args: Environment.GetCommandLineArgs()
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetCommandLineArgs"));

        // Create array from string[]
        il.Emit(OpCodes.Call, runtime.CreateArray);

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

        // Get current process: Process.GetCurrentProcess()
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Process, "GetCurrentProcess"));
        var processLocal = il.DeclareLocal(_types.Process);
        il.Emit(OpCodes.Stloc, processLocal);

        // Get start time: process.StartTime
        il.Emit(OpCodes.Ldloc, processLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Process, "StartTime"));
        var startTimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Stloc, startTimeLocal);

        // Get current time: DateTime.UtcNow
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.DateTime, "UtcNow"));
        var nowLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Stloc, nowLocal);

        // Convert start time to UTC
        il.Emit(OpCodes.Ldloca, startTimeLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.DateTime, "ToUniversalTime"));
        il.Emit(OpCodes.Stloc, startTimeLocal);

        // Calculate difference: now - startTime
        il.Emit(OpCodes.Ldloc, nowLocal);
        il.Emit(OpCodes.Ldloc, startTimeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "op_Subtraction", _types.DateTime, _types.DateTime));
        var spanLocal = il.DeclareLocal(_types.TimeSpan);
        il.Emit(OpCodes.Stloc, spanLocal);

        // Get TotalSeconds
        il.Emit(OpCodes.Ldloca, spanLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.TimeSpan, "TotalSeconds"));

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
}
