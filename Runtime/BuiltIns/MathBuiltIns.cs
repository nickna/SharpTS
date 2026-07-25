using SharpTS.Execution;
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
            // Single argument methods (V2 — no boxing)
            .MethodV2("abs", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Abs(args[0].AsNumber())))
            .MethodV2("floor", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Floor(args[0].AsNumber())))
            .MethodV2("ceil", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Ceiling(args[0].AsNumber())))
            .MethodV2("round", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Floor(args[0].AsNumber() + 0.5)))
            .MethodV2("sqrt", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Sqrt(args[0].AsNumber())))
            .MethodV2("sin", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Sin(args[0].AsNumber())))
            .MethodV2("cos", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Cos(args[0].AsNumber())))
            .MethodV2("tan", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Tan(args[0].AsNumber())))
            .MethodV2("log", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Log(args[0].AsNumber())))
            .MethodV2("exp", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Exp(args[0].AsNumber())))
            .MethodV2("cbrt", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Cbrt(args[0].AsNumber())))
            .MethodV2("log2", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Log2(args[0].AsNumber())))
            .MethodV2("log10", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Log10(args[0].AsNumber())))
            .MethodV2("sign", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Sign(args[0].AsNumber())))
            .MethodV2("trunc", 1, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Truncate(args[0].AsNumber())))
            // Two argument methods
            .MethodV2("pow", 2, (_, _, args) =>
                RuntimeValue.FromNumber(Math.Pow(args[0].AsNumber(), args[1].AsNumber())))
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

    /// <summary>Member names for REPL autocomplete.</summary>
    public static IEnumerable<string> MemberNames => _lookup.MemberNames;

    private static RuntimeValue Min(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0) return RuntimeValue.FromNumber(double.PositiveInfinity);

        double min = double.PositiveInfinity;
        for (int i = 0; i < args.Length; i++)
        {
            double val = args[i].AsNumber();
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
            double val = args[i].AsNumber();
            // ECMA-262 21.3.2.24: any NaN argument makes the result NaN.
            if (double.IsNaN(val)) return RuntimeValue.FromNumber(double.NaN);
            if (val > max) max = val;
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
            if (double.IsInfinity(args[i].AsNumber()))
                return RuntimeValue.FromNumber(double.PositiveInfinity);
        }

        // sqrt(sum of squares); any remaining NaN propagates through Sqrt.
        double sum = 0;
        for (int i = 0; i < args.Length; i++)
        {
            double v = args[i].AsNumber();
            sum += v * v;
        }
        return RuntimeValue.FromNumber(Math.Sqrt(sum));
    }
}
