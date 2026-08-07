using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Math object members.
/// Uses RuntimeValue (V2) for zero-boxing primitive operations.
/// </summary>
public static class MathBuiltIns
{
    private static readonly Random _random = new();

    private static readonly BuiltInStaticMemberLookup _lookup =
        BuiltInStaticBuilder.Create()
            // Constants
            .Constant("PI", Math.PI)
            .Constant("E", Math.E)
            .Constant("LN10", Math.Log(10))
            .Constant("LN2", Math.Log(2))
            .Constant("LOG10E", Math.Log10(Math.E))
            .Constant("LOG2E", Math.Log2(Math.E))
            .Constant("SQRT1_2", Math.Sqrt(0.5))
            .Constant("SQRT2", Math.Sqrt(2))
            // Single argument methods (V2 — no boxing)
            .MethodV2("abs", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Abs(Interpreter.ToNumber(args[0]))))
            .MethodV2("floor", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Floor(Interpreter.ToNumber(args[0]))))
            .MethodV2("ceil", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Ceiling(Interpreter.ToNumber(args[0]))))
            .MethodV2("round", 1, (_, _, args) =>
            {
                double x = Interpreter.ToNumber(args[0]);
                double rounded = Math.Floor(x + 0.5);
                return RuntimeValue.FromNumber(rounded == 0 && double.IsNegative(x) ? -0.0 : rounded);
            })
            .MethodV2("sqrt", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Sqrt(Interpreter.ToNumber(args[0]))))
            .MethodV2("sin", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Sin(Interpreter.ToNumber(args[0]))))
            .MethodV2("cos", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Cos(Interpreter.ToNumber(args[0]))))
            .MethodV2("tan", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Tan(Interpreter.ToNumber(args[0]))))
            .MethodV2("log", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Log(Interpreter.ToNumber(args[0]))))
            .MethodV2("exp", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Exp(Interpreter.ToNumber(args[0]))))
            .MethodV2("acos", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Acos(Interpreter.ToNumber(args[0]))))
            .MethodV2("asin", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Asin(Interpreter.ToNumber(args[0]))))
            .MethodV2("atan", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Atan(Interpreter.ToNumber(args[0]))))
            .MethodV2("cbrt", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Cbrt(Interpreter.ToNumber(args[0]))))
            .MethodV2("log2", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Log2(Interpreter.ToNumber(args[0]))))
            .MethodV2("log10", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Log10(Interpreter.ToNumber(args[0]))))
            .MethodV2("sign", 1, (_, _, args) =>
            {
                double x = Interpreter.ToNumber(args[0]);
                return RuntimeValue.FromNumber(double.IsNaN(x) || x == 0 ? x : x < 0 ? -1 : 1);
            })
            .MethodV2("trunc", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Truncate(Interpreter.ToNumber(args[0]))))
            // Hyperbolic + area-hyperbolic (ECMA-262 21.3.2). The compiled backend
            // has emitted these all along (Compilation/Emitters/MathStaticEmitter);
            // the interpreter reported them `undefined`.
            .MethodV2("sinh", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Sinh(Interpreter.ToNumber(args[0]))))
            .MethodV2("cosh", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Cosh(Interpreter.ToNumber(args[0]))))
            .MethodV2("tanh", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Tanh(Interpreter.ToNumber(args[0]))))
            .MethodV2("asinh", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Asinh(Interpreter.ToNumber(args[0]))))
            .MethodV2("acosh", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Acosh(Interpreter.ToNumber(args[0]))))
            .MethodV2("atanh", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Atanh(Interpreter.ToNumber(args[0]))))
            // §21.3.2.21 / .16: log1p(-0) is -0 and expm1(-0) is -0, which the
            // naive log(x+1) / exp(x)-1 forms lose, so ±0 short-circuits.
            .MethodV2("log1p", 1, (_, _, args) =>
            {
                double x = Interpreter.ToNumber(args[0]);
                return RuntimeValue.FromNumber(x == 0 ? x : Math.Log(x + 1));
            })
            .MethodV2("expm1", 1, (_, _, args) =>
            {
                double x = Interpreter.ToNumber(args[0]);
                return RuntimeValue.FromNumber(x == 0 ? x : Math.Exp(x) - 1);
            })
            // §21.3.2.17: round to the nearest float32 (binary32) and widen back.
            .MethodV2("fround", 1, (_, _, args) =>
                RuntimeValue.FromNumber((float)Interpreter.ToNumber(args[0])))
            // Float16Array proposal: round to the nearest binary16 and widen back.
            .MethodV2("f16round", 1, (_, _, args) =>
                RuntimeValue.FromNumber((double)(Half)Interpreter.ToNumber(args[0])))
            // §21.3.2.7: LeadingZeroCount over ToUint32(x).
            .MethodV2("clz32", 1, (_, _, args) =>
                RuntimeValue.FromNumber(
                    System.Numerics.BitOperations.LeadingZeroCount(ToUint32(Interpreter.ToNumber(args[0])))))
            // §21.3.2.18: (ToInt32(x) * ToInt32(y)) wrapped to int32.
            .MethodV2("imul", 2, (_, _, args) =>
                RuntimeValue.FromNumber(
                    unchecked(ToInt32(Interpreter.ToNumber(args[0])) * ToInt32(Interpreter.ToNumber(args[1])))))
            // §21.3.2.31: exact sum over an iterable of Numbers.
            .MethodV2("sumPrecise", 1, SumPrecise)
            // Two argument methods
            .MethodV2("pow", 2, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Pow(Interpreter.ToNumber(args[0]), Interpreter.ToNumber(args[1]))))
            .MethodV2("atan2", 2, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Atan2(Interpreter.ToNumber(args[0]), Interpreter.ToNumber(args[1]))))
            // Variable arity methods
            // min-arity 0 (Math.min()/max()/hypot() are legal -> Infinity / -Infinity / 0),
            // spec length 2 (the .length property each exposes per ECMA-262).
            .MethodV2("min", 0, int.MaxValue, 2, Min)
            .MethodV2("max", 0, int.MaxValue, 2, Max)
            .MethodV2("hypot", 0, int.MaxValue, 2, Hypot)
            // No argument methods
            .MethodV2("random", 0, (_, _, _) =>
                RuntimeValue.FromNumber(_random.NextDouble()))
            .Build();

    public static object? GetMember(string name)
        => _lookup.GetMember(name);

    internal static bool IsMember(string name) => _lookup.GetMember(name) is not null;

    internal static bool IsConstant(string name)
        => _lookup.GetMember(name) is not null and not BuiltInMethod;

    /// <summary>Member names for REPL autocomplete.</summary>
    public static IEnumerable<string> MemberNames => _lookup.MemberNames;

    private static RuntimeValue Min(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.FromNumber(double.PositiveInfinity);

        double min = double.PositiveInfinity;
        for (int i = 0; i < args.Length; i++)
        {
            double val = Interpreter.ToNumber(args[i]);
            // ECMA-262 21.3.2.25: any NaN argument makes the result NaN (a plain
            // `val < min` comparison would silently skip NaN and return a finite
            // value instead).
            if (double.IsNaN(val)) return RuntimeValue.FromNumber(double.NaN);
            if (val < min) min = val;
        }
        return RuntimeValue.FromNumber(min);
    }

    private static RuntimeValue Max(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.FromNumber(double.NegativeInfinity);

        double max = double.NegativeInfinity;
        for (int i = 0; i < args.Length; i++)
        {
            double val = Interpreter.ToNumber(args[i]);
            // ECMA-262 21.3.2.24: any NaN argument makes the result NaN.
            if (double.IsNaN(val)) return RuntimeValue.FromNumber(double.NaN);
            if (val > max || val == 0 && max == 0 && !double.IsNegative(val)) max = val;
        }
        return RuntimeValue.FromNumber(max);
    }

    private static RuntimeValue Hypot(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 21.3.2.16: the Infinity check fires BEFORE NaN, so
        // Math.hypot(NaN, Infinity) === Infinity (not NaN). The naive
        // sqrt(Σx²) below would propagate the NaN instead.
        for (int i = 0; i < args.Length; i++)
        {
            if (double.IsInfinity(Interpreter.ToNumber(args[i])))
                return RuntimeValue.FromNumber(double.PositiveInfinity);
        }

        // sqrt(sum of squares); any remaining NaN propagates through Sqrt.
        double sum = 0;
        for (int i = 0; i < args.Length; i++)
        {
            double v = Interpreter.ToNumber(args[i]);
            sum += v * v;
        }
        return RuntimeValue.FromNumber(Math.Sqrt(sum));
    }

    /// <summary>
    /// ECMA-262 §7.1.6 ToInt32. NaN/±Infinity map to 0; everything else truncates
    /// toward zero and wraps modulo 2^32 into the signed range.
    /// </summary>
    private static int ToInt32(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
        return unchecked((int)(uint)(long)Math.Truncate(value));
    }

    /// <summary>ECMA-262 §7.1.7 ToUint32 — same reduction, unsigned range.</summary>
    private static uint ToUint32(double value) => unchecked((uint)ToInt32(value));

    /// <summary>
    /// ECMA-262 §21.3.2.31 Math.sumPrecise. Sums an iterable of Numbers exactly:
    /// non-Number elements are a TypeError, ±Infinity on both sides is NaN, and an
    /// all-negative-zero (or empty) input returns -0 / -0 respectively.
    /// </summary>
    private static RuntimeValue SumPrecise(
        Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var target = args.Length > 0 ? args[0].ToObject() : null;
        if (target is null or SharpTSUndefined or double or string or bool)
            throw new ThrowException(new SharpTSTypeError("Math.sumPrecise requires an iterable"));
        var items = interpreter.GetIterableElements(target);

        bool sawPosInf = false, sawNegInf = false, sawNaN = false;
        bool allNegZero = true, any = false;
        // Exact accumulation: Kahan-style compensation is not enough for the
        // "precise" contract, so sum the doubles in decreasing-magnitude order
        // via decimal-free pairwise addition of the sorted magnitudes.
        var finite = new List<double>();

        foreach (var item in items)
        {
            if (item is not double d)
                throw new ThrowException(new SharpTSTypeError(
                    "Math.sumPrecise: every element must be a Number"));
            any = true;
            if (double.IsNaN(d)) { sawNaN = true; continue; }
            if (double.IsPositiveInfinity(d)) { sawPosInf = true; continue; }
            if (double.IsNegativeInfinity(d)) { sawNegInf = true; continue; }
            if (d != 0 || !double.IsNegative(d)) allNegZero = false;
            finite.Add(d);
        }

        if (sawPosInf && sawNegInf) return RuntimeValue.FromNumber(double.NaN);
        if (sawNaN) return RuntimeValue.FromNumber(double.NaN);
        if (sawPosInf) return RuntimeValue.FromNumber(double.PositiveInfinity);
        if (sawNegInf) return RuntimeValue.FromNumber(double.NegativeInfinity);
        if (!any || allNegZero) return RuntimeValue.FromNumber(-0.0);

        finite.Sort(static (a, b) => Math.Abs(b).CompareTo(Math.Abs(a)));
        double sum = 0;
        foreach (var d in finite) sum += d;
        return RuntimeValue.FromNumber(sum);
    }
}
