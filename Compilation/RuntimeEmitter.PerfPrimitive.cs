using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// <c>primitive:perf</c> runtime support for standalone assemblies.
/// Emits a single <c>PerfPrimitiveNow()</c> method returning high-resolution
/// milliseconds since first call. The rest of the perf_hooks surface
/// (mark, measure, getEntries*, clear*, PerformanceObserver) is implemented
/// in pure TypeScript in <c>stdlib/node/perf_hooks.ts</c>.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitPerfPrimitiveMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var startTicksField = typeBuilder.DefineField(
            "_perfPrimitiveStartTicks",
            _types.Int64,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = startTicksField;

        var ticksPerMsField = typeBuilder.DefineField(
            "_perfPrimitiveTicksPerMs",
            _types.Double,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = ticksPerMsField;

        var initializedField = typeBuilder.DefineField(
            "_perfPrimitiveInitialized",
            _types.Boolean,
            FieldAttributes.Private | FieldAttributes.Static
        );

        EmitPerfPrimitiveNow(typeBuilder, runtime, startTicksField, ticksPerMsField, initializedField);
    }

    /// <summary>
    /// Emits <c>PerfPrimitiveNow()</c>: returns high-resolution time in milliseconds
    /// since the first invocation. Signature: <c>double PerfPrimitiveNow()</c>.
    /// </summary>
    private void EmitPerfPrimitiveNow(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder startTicksField,
        FieldBuilder ticksPerMsField,
        FieldBuilder initializedField)
    {
        var method = typeBuilder.DefineMethod(
            "PerfPrimitiveNow",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.PerfPrimitiveNow = method;

        var il = method.GetILGenerator();

        var alreadyInitializedLabel = il.DefineLabel();

        // if (_perfPrimitiveInitialized) skip init
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue_S, alreadyInitializedLabel);

        // _perfPrimitiveStartTicks = Stopwatch.GetTimestamp();
        il.Emit(OpCodes.Call, _types.StopwatchGetTimestamp);
        il.Emit(OpCodes.Stsfld, startTicksField);

        // _perfPrimitiveTicksPerMs = Stopwatch.Frequency / 1000.0;
        il.Emit(OpCodes.Ldsfld, typeof(Stopwatch).GetField("Frequency")!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 1000.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stsfld, ticksPerMsField);

        // _perfPrimitiveInitialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        il.MarkLabel(alreadyInitializedLabel);

        // long elapsed = Stopwatch.GetTimestamp() - _perfPrimitiveStartTicks;
        il.Emit(OpCodes.Call, _types.StopwatchGetTimestamp);
        il.Emit(OpCodes.Ldsfld, startTicksField);
        il.Emit(OpCodes.Sub);

        // return elapsed / _perfPrimitiveTicksPerMs;
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldsfld, ticksPerMsField);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ret);
    }
}
