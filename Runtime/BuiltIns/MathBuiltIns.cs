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

    // Cache static methods to avoid allocation on every access
    private static readonly BuiltInMethod _abs = new("abs", 1, Abs);
    private static readonly BuiltInMethod _floor = new("floor", 1, Floor);
    private static readonly BuiltInMethod _ceil = new("ceil", 1, Ceil);
    private static readonly BuiltInMethod _round = new("round", 1, Round);
    private static readonly BuiltInMethod _sqrt = new("sqrt", 1, Sqrt);
    private static readonly BuiltInMethod _sin = new("sin", 1, Sin);
    private static readonly BuiltInMethod _cos = new("cos", 1, Cos);
    private static readonly BuiltInMethod _tan = new("tan", 1, Tan);
    private static readonly BuiltInMethod _log = new("log", 1, Log);
    private static readonly BuiltInMethod _exp = new("exp", 1, Exp);
    private static readonly BuiltInMethod _sign = new("sign", 1, Sign);
    private static readonly BuiltInMethod _trunc = new("trunc", 1, Trunc);
    private static readonly BuiltInMethod _pow = new("pow", 2, Pow);
    private static readonly BuiltInMethod _min = new("min", 2, int.MaxValue, Min);
    private static readonly BuiltInMethod _max = new("max", 2, int.MaxValue, Max);
    private static readonly BuiltInMethod _randomMethod = new("random", 0, RandomMethod);

    public static object? GetMember(string name)
    {
        return name switch
        {
            // Constants
            "PI" => Math.PI,
            "E" => Math.E,

            // Single argument methods
            "abs" => _abs,
            "floor" => _floor,
            "ceil" => _ceil,
            "round" => _round,
            "sqrt" => _sqrt,
            "sin" => _sin,
            "cos" => _cos,
            "tan" => _tan,
            "log" => _log,
            "exp" => _exp,
            "sign" => _sign,
            "trunc" => _trunc,

            // Two argument methods
            "pow" => _pow,
            "min" => _min,
            "max" => _max,

            // No argument methods
            "random" => _randomMethod,

            _ => null
        };
    }

    private static object? Abs(Interpreter i, object? r, List<object?> args) => Math.Abs((double)args[0]!);
    private static object? Floor(Interpreter i, object? r, List<object?> args) => Math.Floor((double)args[0]!);
    private static object? Ceil(Interpreter i, object? r, List<object?> args) => Math.Ceiling((double)args[0]!);
    private static object? Round(Interpreter i, object? r, List<object?> args) => Math.Floor((double)args[0]! + 0.5); // JS rounds half towards +∞
    private static object? Sqrt(Interpreter i, object? r, List<object?> args) => Math.Sqrt((double)args[0]!);
    private static object? Sin(Interpreter i, object? r, List<object?> args) => Math.Sin((double)args[0]!);
    private static object? Cos(Interpreter i, object? r, List<object?> args) => Math.Cos((double)args[0]!);
    private static object? Tan(Interpreter i, object? r, List<object?> args) => Math.Tan((double)args[0]!);
    private static object? Log(Interpreter i, object? r, List<object?> args) => Math.Log((double)args[0]!);
    private static object? Exp(Interpreter i, object? r, List<object?> args) => Math.Exp((double)args[0]!);
    private static object? Sign(Interpreter i, object? r, List<object?> args) => (double)Math.Sign((double)args[0]!);
    private static object? Trunc(Interpreter i, object? r, List<object?> args) => Math.Truncate((double)args[0]!);
    private static object? Pow(Interpreter i, object? r, List<object?> args) => Math.Pow((double)args[0]!, (double)args[1]!);
    private static object? RandomMethod(Interpreter i, object? r, List<object?> args) => _random.NextDouble();

    private static object? Min(Interpreter i, object? r, List<object?> args)
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

    private static object? Max(Interpreter i, object? r, List<object?> args)
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
