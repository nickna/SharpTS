using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Math object members.
/// </summary>
/// <remarks>
/// Contains constants (PI, E) and method implementations (abs, floor, ceil, sqrt, sin, cos,
/// pow, min, max, random, etc.) that back the <c>Math.x</c> syntax in TypeScript.
/// Called by <see cref="Interpreter"/> when resolving property access on <see cref="SharpTSMath"/>.
/// Methods are returned as <see cref="BuiltInMethod"/> instances for uniform invocation.
/// </remarks>
/// <seealso cref="SharpTSMath"/>
/// <seealso cref="BuiltInMethod"/>
public static class MathBuiltIns
{
    private static readonly Random _random = new();

    private static readonly BuiltInStaticMemberLookup _lookup =
        BuiltInStaticBuilder.Create()
            // Constants
            .Constant("PI", Math.PI)
            .Constant("E", Math.E)
            // Single argument methods
            .Method("abs", 1, Abs)
            .Method("floor", 1, Floor)
            .Method("ceil", 1, Ceil)
            .Method("round", 1, Round)
            .Method("sqrt", 1, Sqrt)
            .Method("sin", 1, Sin)
            .Method("cos", 1, Cos)
            .Method("tan", 1, Tan)
            .Method("log", 1, Log)
            .Method("exp", 1, Exp)
            .Method("sign", 1, Sign)
            .Method("trunc", 1, Trunc)
            // Two argument methods
            .Method("pow", 2, Pow)
            .Method("min", 2, int.MaxValue, Min)
            .Method("max", 2, int.MaxValue, Max)
            // No argument methods
            .Method("random", 0, RandomMethod)
            .Build();

    public static object? GetMember(string name)
        => _lookup.GetMember(name);

    private static object? Abs(Interpreter _, List<object?> args)
        => Math.Abs((double)args[0]!);

    private static object? Floor(Interpreter _, List<object?> args)
        => Math.Floor((double)args[0]!);

    private static object? Ceil(Interpreter _, List<object?> args)
        => Math.Ceiling((double)args[0]!);

    private static object? Round(Interpreter _, List<object?> args)
        => Math.Floor((double)args[0]! + 0.5); // JS rounds half towards +Infinity

    private static object? Sqrt(Interpreter _, List<object?> args)
        => Math.Sqrt((double)args[0]!);

    private static object? Sin(Interpreter _, List<object?> args)
        => Math.Sin((double)args[0]!);

    private static object? Cos(Interpreter _, List<object?> args)
        => Math.Cos((double)args[0]!);

    private static object? Tan(Interpreter _, List<object?> args)
        => Math.Tan((double)args[0]!);

    private static object? Log(Interpreter _, List<object?> args)
        => Math.Log((double)args[0]!);

    private static object? Exp(Interpreter _, List<object?> args)
        => Math.Exp((double)args[0]!);

    private static object? Sign(Interpreter _, List<object?> args)
        => (double)Math.Sign((double)args[0]!);

    private static object? Trunc(Interpreter _, List<object?> args)
        => Math.Truncate((double)args[0]!);

    private static object? Pow(Interpreter _, List<object?> args)
        => Math.Pow((double)args[0]!, (double)args[1]!);

    private static object? RandomMethod(Interpreter _, List<object?> args)
        => _random.NextDouble();

    private static object? Min(Interpreter _, List<object?> args)
    {
        if (args.Count == 0) return double.PositiveInfinity;

        double min = double.PositiveInfinity;
        foreach (var arg in args)
        {
            double val = (double)arg!;
            if (val < min) min = val;
        }
        return min;
    }

    private static object? Max(Interpreter _, List<object?> args)
    {
        if (args.Count == 0) return double.NegativeInfinity;

        double max = double.NegativeInfinity;
        foreach (var arg in args)
        {
            double val = (double)arg!;
            if (val > max) max = val;
        }
        return max;
    }
}
