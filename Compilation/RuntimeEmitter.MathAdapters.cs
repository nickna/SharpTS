using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits <c>$Runtime</c> adapter methods that expose JS <c>Math</c> static
/// methods as values (for <c>var f = Math.floor; f(x)</c>-style patterns used
/// by lodash and similar libraries). Adapter signatures are
/// <c>object(object)</c> / <c>object(object, object)</c> / <c>object(object[])</c>
/// so they're directly wrappable in <c>$TSFunction</c>, and each adapter routes
/// its arg(s) through <c>ToNumber</c> first to preserve ECMAScript coercion
/// (e.g. <c>Math.floor("2.5") === 2</c>, <c>Math.floor(null) === 0</c>).
///
/// See issue #60 for the motivating lodash breakage and the wider set of
/// built-in static methods that need analogous treatment.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitMathAdapters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.MathFloorAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathFloorAdapter", "Floor");
        runtime.MathCeilAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathCeilAdapter", "Ceiling");
        runtime.MathAbsAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAbsAdapter", "Abs");
        runtime.MathSqrtAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathSqrtAdapter", "Sqrt");
        runtime.MathTruncAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathTruncAdapter", "Truncate");
        runtime.MathSinAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathSinAdapter", "Sin");
        runtime.MathCosAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathCosAdapter", "Cos");
        runtime.MathTanAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathTanAdapter", "Tan");
        runtime.MathLogAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathLogAdapter", "Log");
        runtime.MathExpAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathExpAdapter", "Exp");

        runtime.MathRoundAdapter = EmitMathRoundAdapter(typeBuilder, runtime);
        runtime.MathSignAdapter = EmitMathSignAdapter(typeBuilder, runtime);
        runtime.MathPowAdapter = EmitMathPowAdapter(typeBuilder, runtime);
        runtime.MathMaxAdapter = EmitMathMinMaxAdapter(typeBuilder, runtime, "MathMaxAdapter", isMax: true);
        runtime.MathMinAdapter = EmitMathMinMaxAdapter(typeBuilder, runtime, "MathMinAdapter", isMax: false);

        // Stage 4y: ES2015+ Math.* exposed as values. Each adapter routes
        // arg(s) through ToNumber for spec coercion, then dispatches to the
        // matching System.Math method.
        runtime.MathAsinAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAsinAdapter", "Asin");
        runtime.MathAcosAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAcosAdapter", "Acos");
        runtime.MathAtanAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAtanAdapter", "Atan");
        runtime.MathAtan2Adapter = EmitBinaryMathAdapter(typeBuilder, runtime, "MathAtan2Adapter", "Atan2");
        runtime.MathSinhAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathSinhAdapter", "Sinh");
        runtime.MathCoshAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathCoshAdapter", "Cosh");
        runtime.MathTanhAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathTanhAdapter", "Tanh");
        runtime.MathAsinhAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAsinhAdapter", "Asinh");
        runtime.MathAcoshAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAcoshAdapter", "Acosh");
        runtime.MathAtanhAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathAtanhAdapter", "Atanh");
        runtime.MathCbrtAdapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathCbrtAdapter", "Cbrt");
        runtime.MathLog10Adapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathLog10Adapter", "Log10");
        runtime.MathLog2Adapter = EmitUnaryMathAdapter(typeBuilder, runtime, "MathLog2Adapter", "Log2");
        runtime.MathLog1pAdapter = EmitMathLog1pAdapter(typeBuilder, runtime);
        runtime.MathExpm1Adapter = EmitMathExpm1Adapter(typeBuilder, runtime);
        runtime.MathFroundAdapter = EmitMathFroundAdapter(typeBuilder, runtime);
        runtime.MathF16RoundAdapter = EmitMathF16RoundAdapter(typeBuilder, runtime);
        runtime.MathClz32Adapter = EmitMathClz32Adapter(typeBuilder, runtime);
        runtime.MathImulAdapter = EmitMathImulAdapter(typeBuilder, runtime);
        runtime.MathHypotAdapter = EmitMathHypotAdapter(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits <c>public static object {name}(object a, object b) =&gt; Math.{systemMethod}(ToNumber(a), ToNumber(b))</c>.
    /// </summary>
    private MethodBuilder EmitBinaryMathAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, string systemMathMethod)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, systemMathMethod, _types.Double, _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Math.log1p(x) → Math.Log(x + 1) after ToNumber coercion.</summary>
    private MethodBuilder EmitMathLog1pAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathLog1pAdapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Log", _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Math.expm1(x) → Math.Exp(x) - 1 after ToNumber coercion.</summary>
    private MethodBuilder EmitMathExpm1Adapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathExpm1Adapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Exp", _types.Double));
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Math.fround(x) — round to float32 then back to double.</summary>
    private MethodBuilder EmitMathFroundAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathFroundAdapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_R4);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Math.f16round(x) — round to binary16 then back to double.</summary>
    private MethodBuilder EmitMathF16RoundAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathF16RoundAdapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        var halfFromDouble = typeof(Half).GetMethods()
            .First(m => m.Name == "op_Explicit" && m.ReturnType == typeof(Half)
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(double));
        var doubleFromHalf = typeof(Half).GetMethods()
            .First(m => m.Name == "op_Explicit" && m.ReturnType == typeof(double)
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(Half));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Call, halfFromDouble);
        il.Emit(OpCodes.Call, doubleFromHalf);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Math.clz32(x) — leading zero count of ToUint32(x); 32 when x is 0.</summary>
    private MethodBuilder EmitMathClz32Adapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathClz32Adapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        // ECMA-262 21.3.2.7: clz32(x) = number of leading zero bits in ToUint32(x).
        // Conv_U4 from double has undefined behavior for values outside [0, 2^32);
        // JsToInt32 handles NaN/Infinity → 0 + modular reduction.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("LeadingZeroCount", [typeof(uint)])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>Math.imul(a, b) — int32 multiplication after ToNumber.</summary>
    private MethodBuilder EmitMathImulAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathImulAdapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var aLocal = il.DeclareLocal(_types.Int32);
        var bLocal = il.DeclareLocal(_types.Int32);
        // ECMA-262 21.3.2.18: a = ToUint32(x), b = ToUint32(y); product = (a*b) mod 2^32
        // returned as int32. Use JsToInt32 helper: properly handles NaN, +/-Infinity,
        // and values outside int32 range by modular reduction (Conv_I4 from double
        // is undefined behavior outside [-2^31, 2^31)).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Stloc, aLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.JsToInt32);
        il.Emit(OpCodes.Stloc, bLocal);
        // (a * b) wraps via Mul. Result fits in int32 by construction (mod 2^32).
        il.Emit(OpCodes.Ldloc, aLocal);
        il.Emit(OpCodes.Ldloc, bLocal);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Math.hypot(...args) — sqrt(sum(arg_i^2)) after ToNumber on each. Variadic
    /// via object[]; matches the inline emitter's local-stash strategy.
    /// </summary>
    private MethodBuilder EmitMathHypotAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MathHypotAdapter",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.ObjectArray]);
        var il = method.GetILGenerator();
        var sumLocal = il.DeclareLocal(_types.Double);
        var iLocal = il.DeclareLocal(_types.Int32);
        var argLocal = il.DeclareLocal(_types.Double);

        // ECMA-262 21.3.2.16: the Infinity check fires BEFORE NaN, so
        // Math.hypot(NaN, Infinity) === Infinity (not NaN). First pass: if any
        // ToNumber(arg) is ±Infinity, return +Infinity. Mirrors the inline
        // MathStaticEmitter hypot path so value-form / spread hypot agree (#951).
        var infLoopStart = il.DefineLabel();
        var infLoopEnd = il.DefineLabel();
        var notInfArg = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(infLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, infLoopEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, notInfArg);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notInfArg);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, infLoopStart);
        il.MarkLabel(infLoopEnd);

        // sum = 0; i = 0
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, sumLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // arg = ToNumber(args[i]); sum += arg * arg
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, argLocal);
        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, sumLocal);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Sqrt", _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits <c>public static object {name}(object arg) =&gt; Math.{systemMethod}(ToNumber(arg))</c>.
    /// </summary>
    private MethodBuilder EmitUnaryMathAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, string systemMathMethod)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, systemMathMethod, _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits Math.round adapter. JS rounds half-values toward +∞
    /// (<c>Math.round(-0.5) === 0</c>, <c>Math.round(0.5) === 1</c>), which
    /// matches <c>Math.Floor(x + 0.5)</c>.
    /// </summary>
    private MethodBuilder EmitMathRoundAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MathRoundAdapter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldc_R8, 0.5);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits Math.sign adapter. System.Math.Sign returns int and throws on NaN;
    /// JS returns NaN for NaN input. Handle NaN explicitly, convert int result
    /// to double for boxing consistency.
    /// </summary>
    private MethodBuilder EmitMathSignAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MathSignAdapter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var vLocal = il.DeclareLocal(_types.Double);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, vLocal);

        // if (double.IsNaN(v)) return NaN (boxed)
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNaN);
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Sign", _types.Double));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits <c>MathPowAdapter(object base, object exponent)</c>.
    /// </summary>
    private MethodBuilder EmitMathPowAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MathPowAdapter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Pow", _types.Double, _types.Double));
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits variadic Math.max / Math.min adapter. Signature
    /// <c>object(object[])</c> so that <c>$TSFunction.AdjustArgs</c> packs all
    /// JS args into the <c>object[]</c> slot (its existing rest-parameter
    /// handling). Spec behavior: empty args returns ±∞; any NaN in the input
    /// short-circuits to NaN.
    /// </summary>
    private MethodBuilder EmitMathMinMaxAdapter(TypeBuilder typeBuilder, EmittedRuntime runtime, string name, bool isMax)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);
        var iLocal = il.DeclareLocal(_types.Int32);
        var nextLocal = il.DeclareLocal(_types.Double);

        // if (args == null || args.Length == 0) return isMax ? -Infinity : +Infinity;
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notEmpty);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldc_R8, isMax ? double.NegativeInfinity : double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // result = ToNumber(args[0])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (i = 1; i < args.Length; i++)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // next = ToNumber(args[i])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, nextLocal);

        // if (double.IsNaN(next) || double.IsNaN(result)) return NaN;
        var nanReturn = il.DefineLabel();
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nextLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, nanReturn);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, notNaN);

        il.MarkLabel(nanReturn);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNaN);

        // if (isMax ? next > result : next < result) result = next;
        var skipUpdate = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nextLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(isMax ? OpCodes.Ble : OpCodes.Bge, skipUpdate);
        il.Emit(OpCodes.Ldloc, nextLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(skipUpdate);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
