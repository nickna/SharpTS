using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Math static method calls and property access.
/// Handles Math.random(), Math.min(), Math.max(), Math.round(), etc. and Math.PI, Math.E.
/// </summary>
public sealed class MathStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a Math static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Reject non-Math members before emitting any arguments. The generic
        // call emitter must handle inherited/user properties such as
        // Math.hasOwnProperty("x"). Previously this method converted and left
        // every argument on the IL stack before eventually returning false,
        // so the fallback emitted the call a second time and produced invalid
        // IL at runtime.
        if (methodName is not (
            "random" or "min" or "max" or "sumPrecise" or "hypot" or
            "round" or "sign" or "abs" or "floor" or "ceil" or "sqrt" or
            "sin" or "cos" or "tan" or "log" or "exp" or "trunc" or "pow" or
            "asin" or "acos" or "atan" or "atan2" or "sinh" or "cosh" or
            "tanh" or "asinh" or "acosh" or "atanh" or "cbrt" or "log10" or
            "log2" or "log1p" or "expm1" or "fround" or "f16round" or
            "clz32" or "imul"))
        {
            return false;
        }

        if (methodName == "random")
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.Random);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Handle variadic min/max (JavaScript allows any number of arguments)
        if (methodName is "min" or "max")
        {
            // A replaced Math binding/property must be obtained and invoked as
            // an ordinary live property. Returning false leaves that work to
            // the generic call emitter.
            if (ctx.RuntimeFeatures?.UsesMathMutation != false)
                return false;

            if (EmitsUnboxedFixedArityMinMax(emitter, methodName, arguments))
            {
                EmitUnboxedFixedArityMinMax(emitter, methodName, arguments);
                return true;
            }

            // Spread args (\`Math.max(...arr)\`) have a runtime-unknown count, so the
            // inline per-arg loop below can't expand them. Route to the variadic
            // \`object[]\`-taking adapter, feeding a spread-expanded args array — the
            // adapter applies ToNumber per element with the same empty→∓∞ / NaN
            // short-circuit semantics as the inline path (#951).
            if (arguments.Any(a => a is Expr.Spread))
            {
                emitter.EmitArgsArrayWithSpread(arguments);
                il.Emit(OpCodes.Call, methodName == "min"
                    ? ctx.Runtime!.MathMinAdapter
                    : ctx.Runtime!.MathMaxAdapter);
                return true;
            }

            var minMaxMethod = methodName == "min"
                ? ctx.Types.GetMethod(ctx.Types.Math, "Min", ctx.Types.Double, ctx.Types.Double)
                : ctx.Types.GetMethod(ctx.Types.Math, "Max", ctx.Types.Double, ctx.Types.Double);

            if (arguments.Count == 0)
            {
                // No args: min() returns Infinity, max() returns -Infinity
                il.Emit(OpCodes.Ldc_R8, methodName == "min" ? double.PositiveInfinity : double.NegativeInfinity);
            }
            else
            {
                // Same ToNumber routing as the unary-Math loop below — handles
                // \`Math.max(undefined, 1)\` returning NaN (spec) instead of crashing.
                emitter.EmitExpression(arguments[0]);
                emitter.EnsureBoxed();
                il.Emit(OpCodes.Call, ctx.Runtime!.ToNumber);
                for (int i = 1; i < arguments.Count; i++)
                {
                    emitter.EmitExpression(arguments[i]);
                    emitter.EnsureBoxed();
                    il.Emit(OpCodes.Call, ctx.Runtime!.ToNumber);
                    il.Emit(OpCodes.Call, minMaxMethod);
                }
            }
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Math.sumPrecise(iterable) — ECMA-262 21.3.2.31. Iterates the input,
        // throws TypeError on non-Number elements (incl. BigInt), and returns
        // the precise sum. Unlike other Math.* methods, the arg is an iterable
        // not a number, so it bypasses the ToNumber-coercion loop below.
        if (methodName == "sumPrecise")
        {
            var input = il.DeclareLocal(ctx.Types.Object);
            if (arguments.Count == 0)
            {
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
            }
            else
            {
                emitter.EmitExpression(arguments[0]);
                emitter.EnsureBoxed();
            }
            il.Emit(OpCodes.Stloc, input);
            for (int i = 1; i < arguments.Count; i++)
            {
                emitter.EmitExpression(arguments[i]);
                il.Emit(OpCodes.Pop);
            }
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Call, ctx.Runtime!.MathSumPrecise);
            return true;
        }

        // Variadic hypot with spread args (\`Math.hypot(...arr)\`) has a runtime-
        // unknown count, so the inline local-stash path below can't expand it.
        // Route to the \`object[]\`-taking adapter with a spread-expanded args
        // array (it applies ToNumber + the Infinity-first / NaN-propagating
        // sqrt(Σx²)). Must run BEFORE the general ToNumber loop, which would
        // otherwise emit ToNumber(array) → NaN for the spread element (#951).
        if (methodName == "hypot" && arguments.Any(a => a is Expr.Spread))
        {
            emitter.EmitArgsArrayWithSpread(arguments);
            il.Emit(OpCodes.Call, ctx.Runtime!.MathHypotAdapter);
            return true;
        }

        // Emit all arguments as doubles. Per ECMA-262, Math.* methods coerce
        // each arg via ToNumber — undefined → NaN, null → +0, "abc" → NaN, etc.
        // Pre-fix EmitExpressionAsDouble used Convert.ToDouble(object) which
        // threw InvalidCastException on $Undefined.Instance. Routing through
        // $Runtime.ToNumber gives spec semantics for all primitives.
        foreach (var arg in arguments)
        {
            emitter.EmitExpression(arg);
            emitter.EnsureBoxed();
            il.Emit(OpCodes.Call, ctx.Runtime!.ToNumber);
        }

        if (methodName == "round")
        {
            il.Emit(OpCodes.Box, ctx.Types.Double);
            il.Emit(OpCodes.Call, ctx.Runtime!.MathRoundAdapter);
            return true;
        }

        if (methodName == "sign")
        {
            // System.Math.Sign throws ArithmeticException on NaN; spec says
            // Math.sign(NaN) === NaN. Math.sign(±0) must preserve sign per
            // ECMA-262 21.3.2.30: Math.sign(-0) = -0, Math.sign(+0) = +0.
            // Math.Sign(-0.0) returns 0 (loses sign), so short-circuit on zero.
            var argLocal = il.DeclareLocal(ctx.Types.Double);
            il.Emit(OpCodes.Stloc, argLocal);
            var notNaN = il.DefineLabel();
            var notZero = il.DefineLabel();
            var done = il.DefineLabel();
            // NaN check
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Double, "IsNaN", ctx.Types.Double));
            il.Emit(OpCodes.Brfalse, notNaN);
            il.Emit(OpCodes.Ldc_R8, double.NaN);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(notNaN);
            // Zero check (preserves -0/+0 sign): if (x == 0) return x.
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Bne_Un, notZero);
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(notZero);
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Math, "Sign", ctx.Types.Double));
            il.Emit(OpCodes.Conv_R8);
            il.MarkLabel(done);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        MethodInfo? mathMethod = methodName switch
        {
            "abs" => ctx.Types.GetMethod(ctx.Types.Math, "Abs", ctx.Types.Double),
            "floor" => ctx.Types.GetMethod(ctx.Types.Math, "Floor", ctx.Types.Double),
            "ceil" => ctx.Types.GetMethod(ctx.Types.Math, "Ceiling", ctx.Types.Double),
            "sqrt" => ctx.Types.GetMethod(ctx.Types.Math, "Sqrt", ctx.Types.Double),
            "sin" => ctx.Types.GetMethod(ctx.Types.Math, "Sin", ctx.Types.Double),
            "cos" => ctx.Types.GetMethod(ctx.Types.Math, "Cos", ctx.Types.Double),
            "tan" => ctx.Types.GetMethod(ctx.Types.Math, "Tan", ctx.Types.Double),
            "log" => ctx.Types.GetMethod(ctx.Types.Math, "Log", ctx.Types.Double),
            "exp" => ctx.Types.GetMethod(ctx.Types.Math, "Exp", ctx.Types.Double),
            "trunc" => ctx.Types.GetMethod(ctx.Types.Math, "Truncate", ctx.Types.Double),
            "pow" => ctx.Types.GetMethod(ctx.Types.Math, "Pow", ctx.Types.Double, ctx.Types.Double),
            // Inverse trig + hyperbolic — direct .NET Math equivalents.
            "asin" => ctx.Types.GetMethod(ctx.Types.Math, "Asin", ctx.Types.Double),
            "acos" => ctx.Types.GetMethod(ctx.Types.Math, "Acos", ctx.Types.Double),
            "atan" => ctx.Types.GetMethod(ctx.Types.Math, "Atan", ctx.Types.Double),
            "atan2" => ctx.Types.GetMethod(ctx.Types.Math, "Atan2", ctx.Types.Double, ctx.Types.Double),
            "sinh" => ctx.Types.GetMethod(ctx.Types.Math, "Sinh", ctx.Types.Double),
            "cosh" => ctx.Types.GetMethod(ctx.Types.Math, "Cosh", ctx.Types.Double),
            "tanh" => ctx.Types.GetMethod(ctx.Types.Math, "Tanh", ctx.Types.Double),
            "asinh" => ctx.Types.GetMethod(ctx.Types.Math, "Asinh", ctx.Types.Double),
            "acosh" => ctx.Types.GetMethod(ctx.Types.Math, "Acosh", ctx.Types.Double),
            "atanh" => ctx.Types.GetMethod(ctx.Types.Math, "Atanh", ctx.Types.Double),
            "cbrt" => ctx.Types.GetMethod(ctx.Types.Math, "Cbrt", ctx.Types.Double),
            "log10" => ctx.Types.GetMethod(ctx.Types.Math, "Log10", ctx.Types.Double),
            "log2" => ctx.Types.GetMethod(ctx.Types.Math, "Log2", ctx.Types.Double),
            "log1p" => ctx.Types.GetMethod(typeof(System.Math), "Log", ctx.Types.Double), // see Log1p special case below
            "expm1" => ctx.Types.GetMethod(typeof(System.Math), "Exp", ctx.Types.Double), // see Expm1 special case below
            _ => null
        };

        if (mathMethod != null)
        {
            // log1p/expm1 need pre/post adjustments + zero-sign preservation:
            //   log1p(x) = log(x + 1); spec: log1p(-0) = -0.
            //   expm1(x) = exp(x) - 1; spec: expm1(-0) = -0.
            // The naive log(x+1)/exp(x)-1 implementation loses the sign of zero.
            // Stash arg, short-circuit if x == 0 returning x as-is.
            if (methodName == "log1p" || methodName == "expm1")
            {
                var argLocal = il.DeclareLocal(ctx.Types.Double);
                il.Emit(OpCodes.Stloc, argLocal);
                var endLabel = il.DefineLabel();
                var notZeroLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, argLocal);
                il.Emit(OpCodes.Ldc_R8, 0.0);
                il.Emit(OpCodes.Bne_Un, notZeroLabel);
                il.Emit(OpCodes.Ldloc, argLocal);
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(notZeroLabel);
                il.Emit(OpCodes.Ldloc, argLocal);
                if (methodName == "log1p")
                {
                    il.Emit(OpCodes.Ldc_R8, 1.0);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Call, mathMethod);
                if (methodName == "expm1")
                {
                    il.Emit(OpCodes.Ldc_R8, 1.0);
                    il.Emit(OpCodes.Sub);
                }
                il.MarkLabel(endLabel);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            }
            // Math.pow(base, exp): two ECMA-262 spec quirks vs .NET Math.Pow:
            //   1) If exp is NaN, return NaN (.NET returns 1 for Pow(1, NaN)).
            //   2) If abs(base) == 1 and abs(exp) == +∞, return NaN (.NET
            //      follows IEEE-754 fma which returns 1).
            // Stack on entry: [base, exp].
            if (methodName == "pow")
            {
                var expLocal = il.DeclareLocal(ctx.Types.Double);
                var baseLocal = il.DeclareLocal(ctx.Types.Double);
                il.Emit(OpCodes.Stloc, expLocal);
                il.Emit(OpCodes.Stloc, baseLocal);
                var notNaNExpLabel = il.DefineLabel();
                var endLabel = il.DefineLabel();
                // Quirk 1: NaN exponent → NaN.
                il.Emit(OpCodes.Ldloc, expLocal);
                il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Double, "IsNaN", ctx.Types.Double));
                il.Emit(OpCodes.Brfalse, notNaNExpLabel);
                il.Emit(OpCodes.Ldc_R8, double.NaN);
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(notNaNExpLabel);
                // Quirk 2: |base| == 1 && IsInfinity(exp) → NaN.
                var notUnitInfLabel = il.DefineLabel();
                var absBaseLocal = il.DeclareLocal(ctx.Types.Double);
                il.Emit(OpCodes.Ldloc, baseLocal);
                il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Math, "Abs", ctx.Types.Double));
                il.Emit(OpCodes.Stloc, absBaseLocal);
                il.Emit(OpCodes.Ldloc, absBaseLocal);
                il.Emit(OpCodes.Ldc_R8, 1.0);
                il.Emit(OpCodes.Bne_Un, notUnitInfLabel);
                il.Emit(OpCodes.Ldloc, expLocal);
                il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Double, "IsInfinity", ctx.Types.Double));
                il.Emit(OpCodes.Brfalse, notUnitInfLabel);
                il.Emit(OpCodes.Ldc_R8, double.NaN);
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(notUnitInfLabel);
                il.Emit(OpCodes.Ldloc, baseLocal);
                il.Emit(OpCodes.Ldloc, expLocal);
                il.Emit(OpCodes.Call, mathMethod);
                il.MarkLabel(endLabel);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            }
            il.Emit(OpCodes.Call, mathMethod);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Math.hypot(...args) — sqrt(sum(a_i^2)) per spec.
        // ECMA-262 21.3.2.16: Infinity check fires BEFORE NaN check, so
        // Math.hypot(NaN, Infinity) returns Infinity (not NaN). Stack on
        // entry: [arg0, arg1, ..., argN-1] (each a double).
        if (methodName == "hypot")
        {
            if (arguments.Count == 0)
            {
                il.Emit(OpCodes.Ldc_R8, 0.0);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            }
            // Stash all args into locals; deepest arg on bottom of stack → pop reversed.
            var argLocals = new LocalBuilder[arguments.Count];
            for (int i = arguments.Count - 1; i >= 0; i--)
            {
                argLocals[i] = il.DeclareLocal(ctx.Types.Double);
                il.Emit(OpCodes.Stloc, argLocals[i]);
            }
            // First pass: any infinity → return +Infinity.
            // Use Math.Abs(x) > 0 to detect both +∞ and -∞ via comparison with double.MaxValue.
            // Simpler: check IsInfinity directly per arg.
            var endLabel = il.DefineLabel();
            var notInfLabel = il.DefineLabel();
            for (int i = 0; i < arguments.Count; i++)
            {
                il.Emit(OpCodes.Ldloc, argLocals[i]);
                il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Double, "IsInfinity", ctx.Types.Double));
                var notThisInfLabel = il.DefineLabel();
                il.Emit(OpCodes.Brfalse, notThisInfLabel);
                il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                il.Emit(OpCodes.Br, endLabel);
                il.MarkLabel(notThisInfLabel);
            }
            // No arg was Infinity. Compute sum-of-squares: Math.Sqrt(NaN) = NaN
            // automatically propagates if any arg is NaN.
            il.Emit(OpCodes.Ldloc, argLocals[0]);
            il.Emit(OpCodes.Ldloc, argLocals[0]);
            il.Emit(OpCodes.Mul);
            for (int i = 1; i < arguments.Count; i++)
            {
                il.Emit(OpCodes.Ldloc, argLocals[i]);
                il.Emit(OpCodes.Ldloc, argLocals[i]);
                il.Emit(OpCodes.Mul);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Math, "Sqrt", ctx.Types.Double));
            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Math.fround(x) — round to nearest float32 then back to double.
        if (methodName == "fround" && arguments.Count == 1)
        {
            il.Emit(OpCodes.Conv_R4);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Math.f16round(x) — round to nearest binary16 (Float16) then back to
        // double. Per Float16Array proposal in ECMA-262. Uses System.Half's
        // double→Half (op_Explicit) + Half→double (op_Explicit returning double)
        // conversions. Both directions are op_Explicit on Half; need to filter
        // by signature to disambiguate.
        if (methodName == "f16round" && arguments.Count == 1)
        {
            var halfFromDouble = typeof(System.Half).GetMethods()
                .First(m => m.Name == "op_Explicit"
                            && m.ReturnType == typeof(System.Half)
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType == typeof(double));
            var doubleFromHalf = typeof(System.Half).GetMethods()
                .First(m => m.Name == "op_Explicit"
                            && m.ReturnType == typeof(double)
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType == typeof(System.Half));
            il.Emit(OpCodes.Call, halfFromDouble);
            il.Emit(OpCodes.Call, doubleFromHalf);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Math.clz32(x) — count leading zeros of ToUint32(x).
        // ECMA-262 21.3.2.7: ToUint32 modular-reduces the truncated value to
        // [0, 2^32). Conv_U4 from double is undefined for values outside
        // [0, 2^32); JsToInt32 handles NaN/Infinity → 0 + reduction. Cast
        // its int32 result to uint32 by reinterpretation (same bit pattern).
        if (methodName == "clz32" && arguments.Count == 1)
        {
            // Stack: [double]. Box, JsToInt32 → int32, then uint32 view.
            il.Emit(OpCodes.Box, ctx.Types.Double);
            il.Emit(OpCodes.Call, ctx.Runtime!.JsToInt32);
            // int32 → uint32 reinterpret (no-op at IL level)
            il.Emit(OpCodes.Call, typeof(System.Numerics.BitOperations).GetMethod("LeadingZeroCount", [typeof(uint)])!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Math.imul(a, b) — multiply two 32-bit ints, return as int32.
        // ECMA-262 21.3.2.18: a = ToInt32(x), b = ToInt32(y); result = (a*b) mod 2^32
        // returned as int32. Conv_I4 from double is undefined for values outside
        // [-2^31, 2^31); JsToInt32 handles NaN/Infinity → 0 and modular reduction
        // properly. Box/JsToInt32 round-trip is acceptable for spec-correctness.
        if (methodName == "imul" && arguments.Count == 2)
        {
            // Stack: [a_double, b_double]. Stash b → local; box a, call JsToInt32 → int32 a.
            var bDoubleLocal = il.DeclareLocal(ctx.Types.Double);
            il.Emit(OpCodes.Stloc, bDoubleLocal);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            il.Emit(OpCodes.Call, ctx.Runtime!.JsToInt32);
            // Stack: [a_int32]. Box b, call JsToInt32 → int32 b.
            il.Emit(OpCodes.Ldloc, bDoubleLocal);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            il.Emit(OpCodes.Call, ctx.Runtime!.JsToInt32);
            // Stack: [a_int32, b_int32]. Multiply (Mul wraps mod 2^32).
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        return false;
    }

    internal static bool EmitsUnboxedFixedArityMinMax(
        IEmitterContext emitter, string methodName, IReadOnlyList<Expr> arguments)
    {
        if (methodName is not ("min" or "max")
            || emitter.Context.RuntimeFeatures?.UsesMathMutation != false
            || arguments.Any(argument => argument is Expr.Spread))
            return false;

        return arguments.All(argument =>
            emitter.Context.TypeMap?.Get(argument) is
                TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
                or TypeSystem.TypeInfo.NumberLiteral
                or TypeSystem.TypeInfo.Enum { Kind: TypeSystem.EnumKind.Numeric });
    }

    private static void EmitUnboxedFixedArityMinMax(
        IEmitterContext emitter, string methodName, IReadOnlyList<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldc_R8,
                methodName == "min"
                    ? double.PositiveInfinity
                    : double.NegativeInfinity);
            emitter.SetStackType(StackType.Double);
            return;
        }

        // Evaluate every argument exactly once and in source order with a
        // clear evaluation stack. The locals remain native doubles, so this
        // also handles suspending/effectful numeric expressions without an
        // object spill or a ToNumber boundary.
        var values = new LocalBuilder[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            values[i] = il.DeclareLocal(ctx.Types.Double);
            emitter.EmitExpressionAsDouble(arguments[i]);
            il.Emit(OpCodes.Stloc, values[i]);
        }

        MethodInfo fold = methodName == "min"
            ? ctx.Types.GetMethod(ctx.Types.Math, "Min", ctx.Types.Double, ctx.Types.Double)
            : ctx.Types.GetMethod(ctx.Types.Math, "Max", ctx.Types.Double, ctx.Types.Double);
        il.Emit(OpCodes.Ldloc, values[0]);
        for (int i = 1; i < values.Length; i++)
        {
            il.Emit(OpCodes.Ldloc, values[i]);
            il.Emit(OpCodes.Call, fold);
        }
        emitter.SetStackType(StackType.Double);
    }

    /// <summary>
    /// Attempts to emit IL for bare access to a Math static member without
    /// a call — data constants (<c>PI</c>, <c>E</c>) and method references
    /// (<c>var f = Math.floor</c>). Method references emit a
    /// <c>$TSFunction</c> wrapping the matching <c>$Runtime</c> adapter so
    /// subsequent invocations dispatch correctly. See issue #60 for the
    /// motivating lodash pattern (<c>var nativeMax = Math.max, …</c> at IIFE
    /// init).
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Property replacement also affects value-form reads (`const f =
        // Math.min`) and numeric constants. Let ordinary live lookup observe it.
        if (ctx.RuntimeFeatures?.UsesMathMutation == true)
            return false;

        // ECMA-262 21.3.1: numeric constants on the Math object.
        switch (propertyName)
        {
            case "PI":
                il.Emit(OpCodes.Ldc_R8, Math.PI);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "E":
                il.Emit(OpCodes.Ldc_R8, Math.E);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "LN10":
                il.Emit(OpCodes.Ldc_R8, Math.Log(10.0));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "LN2":
                il.Emit(OpCodes.Ldc_R8, Math.Log(2.0));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "LOG10E":
                il.Emit(OpCodes.Ldc_R8, 1.0 / Math.Log(10.0));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "LOG2E":
                il.Emit(OpCodes.Ldc_R8, 1.0 / Math.Log(2.0));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "SQRT2":
                il.Emit(OpCodes.Ldc_R8, Math.Sqrt(2.0));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "SQRT1_2":
                il.Emit(OpCodes.Ldc_R8, Math.Sqrt(0.5));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
        }

        var runtime = ctx.Runtime!;
        // Stage 4z5: tuple of (adapter, jsName, jsLength) so .name reports
        // the JS-spec name (lowercase) instead of the .NET adapter method
        // name (e.g. "MathFloorAdapter") and .length reports the spec length.
        (MethodInfo? adapter, int len) info = ResolveValueFormMethod(runtime, propertyName);
        if (info.adapter == null) return false;

        // $TSFunction.GetOrCreate(MethodInfo, name, length) — cached identity
        // (Math.abs === Math.abs) so delete-and-readd round-trips on the
        // same instance.
        ctx.Types.EmitLoadMethodInfo(il, info.adapter);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldc_I4, info.len);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        return true;
    }

    /// <summary>
    /// Canonical source of the Math static methods exposed in value form, with
    /// the matching <c>$Runtime</c> adapter and ECMA-262 21.3.2 spec length.
    /// Consumed both by <see cref="TryEmitStaticPropertyGet"/> (syntactic
    /// <c>var f = Math.floor</c>) and by the Math singleton populate step that
    /// fills <c>_mathSingleton</c> for value-form receivers
    /// (<c>const m = Math; m.floor(x)</c>, issue #276). Single source of truth so
    /// the two paths can never drift on names/lengths.
    /// </summary>
    internal static IEnumerable<(string Name, MethodInfo? Adapter, int Length)> EnumerateValueFormMethods(EmittedRuntime runtime)
    {
        yield return ("floor",  runtime.MathFloorAdapter, 1);
        yield return ("ceil",   runtime.MathCeilAdapter, 1);
        yield return ("abs",    runtime.MathAbsAdapter, 1);
        yield return ("sqrt",   runtime.MathSqrtAdapter, 1);
        yield return ("round",  runtime.MathRoundAdapter, 1);
        yield return ("trunc",  runtime.MathTruncAdapter, 1);
        yield return ("sign",   runtime.MathSignAdapter, 1);
        yield return ("sin",    runtime.MathSinAdapter, 1);
        yield return ("cos",    runtime.MathCosAdapter, 1);
        yield return ("tan",    runtime.MathTanAdapter, 1);
        yield return ("log",    runtime.MathLogAdapter, 1);
        yield return ("exp",    runtime.MathExpAdapter, 1);
        yield return ("pow",    runtime.MathPowAdapter, 2);
        yield return ("max",    runtime.MathMaxAdapter, 2);
        yield return ("min",    runtime.MathMinAdapter, 2);
        yield return ("random", runtime.Random, 0);
        yield return ("asin",   runtime.MathAsinAdapter, 1);
        yield return ("acos",   runtime.MathAcosAdapter, 1);
        yield return ("atan",   runtime.MathAtanAdapter, 1);
        yield return ("atan2",  runtime.MathAtan2Adapter, 2);
        yield return ("sinh",   runtime.MathSinhAdapter, 1);
        yield return ("cosh",   runtime.MathCoshAdapter, 1);
        yield return ("tanh",   runtime.MathTanhAdapter, 1);
        yield return ("asinh",  runtime.MathAsinhAdapter, 1);
        yield return ("acosh",  runtime.MathAcoshAdapter, 1);
        yield return ("atanh",  runtime.MathAtanhAdapter, 1);
        yield return ("cbrt",   runtime.MathCbrtAdapter, 1);
        yield return ("log10",  runtime.MathLog10Adapter, 1);
        yield return ("log2",   runtime.MathLog2Adapter, 1);
        yield return ("log1p",  runtime.MathLog1pAdapter, 1);
        yield return ("expm1",  runtime.MathExpm1Adapter, 1);
        yield return ("fround", runtime.MathFroundAdapter, 1);
        yield return ("f16round", runtime.MathF16RoundAdapter, 1);
        yield return ("sumPrecise", runtime.MathSumPrecise, 1);
        yield return ("clz32",  runtime.MathClz32Adapter, 1);
        yield return ("imul",   runtime.MathImulAdapter, 2);
        yield return ("hypot",  runtime.MathHypotAdapter, 2);
    }

    /// <summary>
    /// Looks up a single value-form Math method by name. Returns
    /// <c>(null, 0)</c> if the name is not a value-form method.
    /// </summary>
    internal static (MethodInfo? adapter, int len) ResolveValueFormMethod(EmittedRuntime runtime, string propertyName)
    {
        foreach (var (name, adapter, len) in EnumerateValueFormMethods(runtime))
        {
            if (name == propertyName) return (adapter, len);
        }
        return (null, 0);
    }

    public bool HasStaticProperty(string memberName) =>
        memberName is "PI" or "E" or "LN10" or "LN2" or "LOG10E" or "LOG2E"
            or "SQRT2" or "SQRT1_2"
            or "floor" or "ceil" or "abs" or "sqrt"
            or "round" or "trunc" or "sign" or "sin" or "cos" or "tan"
            or "log" or "exp" or "pow" or "max" or "min" or "random"
            or "asin" or "acos" or "atan" or "atan2" or "sinh" or "cosh" or "tanh"
            or "asinh" or "acosh" or "atanh" or "cbrt" or "log10" or "log2"
            or "log1p" or "expm1" or "fround" or "clz32" or "imul" or "hypot"
            or "f16round";
}
