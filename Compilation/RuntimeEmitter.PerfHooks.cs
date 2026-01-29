using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// perf_hooks module support for standalone assemblies.
/// Provides high-resolution timing via performance.now().
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitPerfHooksMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Static fields for timing (lazy-initialized on first call)
        var startTicksField = typeBuilder.DefineField(
            "_perfHooksStartTicks",
            _types.Int64,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.PerfHooksStartTicks = startTicksField;

        var ticksPerMsField = typeBuilder.DefineField(
            "_perfHooksTicksPerMs",
            _types.Double,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.PerfHooksTicksPerMs = ticksPerMsField;

        var initializedField = typeBuilder.DefineField(
            "_perfHooksInitialized",
            _types.Boolean,
            FieldAttributes.Private | FieldAttributes.Static
        );

        EmitPerfHooksEnsureInitialized(typeBuilder, runtime, startTicksField, ticksPerMsField, initializedField);
        EmitPerfHooksPerformanceNow(typeBuilder, runtime, startTicksField, ticksPerMsField, initializedField);
        EmitPerfHooksGetPerformance(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits a helper method to lazily initialize perf_hooks fields.
    /// </summary>
    private MethodBuilder EmitPerfHooksEnsureInitialized(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder startTicksField,
        FieldBuilder ticksPerMsField,
        FieldBuilder initializedField)
    {
        var method = typeBuilder.DefineMethod(
            "PerfHooksEnsureInitialized",
            MethodAttributes.Private | MethodAttributes.Static,
            null,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        var alreadyInitializedLabel = il.DefineLabel();

        // if (_perfHooksInitialized) return;
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue_S, alreadyInitializedLabel);

        // _perfHooksStartTicks = Stopwatch.GetTimestamp();
        il.Emit(OpCodes.Call, typeof(Stopwatch).GetMethod("GetTimestamp")!);
        il.Emit(OpCodes.Stsfld, startTicksField);

        // _perfHooksTicksPerMs = Stopwatch.Frequency / 1000.0;
        il.Emit(OpCodes.Ldsfld, typeof(Stopwatch).GetField("Frequency")!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 1000.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stsfld, ticksPerMsField);

        // _perfHooksInitialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        il.MarkLabel(alreadyInitializedLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits PerformanceNow: returns high-resolution time in milliseconds.
    /// Signature: double PerformanceNow()
    /// </summary>
    private void EmitPerfHooksPerformanceNow(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder startTicksField,
        FieldBuilder ticksPerMsField,
        FieldBuilder initializedField)
    {
        var method = typeBuilder.DefineMethod(
            "PerformanceNow",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.PerfHooksPerformanceNow = method;

        var il = method.GetILGenerator();

        var alreadyInitializedLabel = il.DefineLabel();

        // Lazy init check - inline for performance
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue_S, alreadyInitializedLabel);

        // Initialize if needed
        il.Emit(OpCodes.Call, typeof(Stopwatch).GetMethod("GetTimestamp")!);
        il.Emit(OpCodes.Stsfld, startTicksField);
        il.Emit(OpCodes.Ldsfld, typeof(Stopwatch).GetField("Frequency")!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 1000.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stsfld, ticksPerMsField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        il.MarkLabel(alreadyInitializedLabel);

        // long elapsed = Stopwatch.GetTimestamp() - _perfHooksStartTicks;
        il.Emit(OpCodes.Call, typeof(Stopwatch).GetMethod("GetTimestamp")!);
        il.Emit(OpCodes.Ldsfld, startTicksField);
        il.Emit(OpCodes.Sub);

        // return elapsed / _perfHooksTicksPerMs;
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldsfld, ticksPerMsField);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits GetPerformance: returns the performance object.
    /// Signature: object GetPerformance()
    /// </summary>
    private void EmitPerfHooksGetPerformance(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First, create a wrapper method for PerformanceNow that takes object[] args
        var nowWrapper = EmitPerformanceNowWrapper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "PerfHooksGetPerformance",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.PerfHooksGetPerformance = method;

        var il = method.GetILGenerator();

        // Create a Dictionary<string, object?> for the performance object
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add timeOrigin property (Unix timestamp of process start)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "timeOrigin");

        // Calculate timeOrigin: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - PerformanceNow()
        il.Emit(OpCodes.Call, typeof(DateTimeOffset).GetProperty("UtcNow")!.GetGetMethod()!);
        var dtLocal = il.DeclareLocal(typeof(DateTimeOffset));
        il.Emit(OpCodes.Stloc, dtLocal);
        il.Emit(OpCodes.Ldloca, dtLocal);
        il.Emit(OpCodes.Call, typeof(DateTimeOffset).GetMethod("ToUnixTimeMilliseconds")!);
        il.Emit(OpCodes.Conv_R8);

        // Subtract current elapsed time to get start time
        il.Emit(OpCodes.Call, runtime.PerfHooksPerformanceNow);
        il.Emit(OpCodes.Sub);

        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        // Add "now" method as a TSFunction wrapping PerformanceNowWrapper
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "now");

        // Create TSFunction for now method: new $TSFunction(null, PerformanceNowWrapper)
        il.Emit(OpCodes.Ldnull); // target (null for static method)
        il.Emit(OpCodes.Ldtoken, nowWrapper);
        il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        il.Emit(OpCodes.Castclass, typeof(MethodInfo));
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);

        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        // Wrap in $Object and return
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a wrapper for PerformanceNow that takes object[] args.
    /// This is needed because TSFunction invokes methods with object[] signature.
    /// Signature: object PerformanceNowWrapper(object[] args)
    /// </summary>
    private MethodBuilder EmitPerformanceNowWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PerformanceNowWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Call PerformanceNow() and box the result
        il.Emit(OpCodes.Call, runtime.PerfHooksPerformanceNow);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
