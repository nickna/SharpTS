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

    public static object? GetMember(string name)
    {
        return name switch
        {
            // Constants
            "PI" => Math.PI,
            "E" => Math.E,

            // Single argument methods
            "abs" => new BuiltInMethod("abs", 1, (_, _, args) =>
                Math.Abs((double)args[0]!)),

            "floor" => new BuiltInMethod("floor", 1, (_, _, args) =>
                Math.Floor((double)args[0]!)),

            "ceil" => new BuiltInMethod("ceil", 1, (_, _, args) =>
                Math.Ceiling((double)args[0]!)),

            "round" => new BuiltInMethod("round", 1, (_, _, args) =>
                Math.Floor((double)args[0]! + 0.5)),  // JS rounds half towards +∞

            "sqrt" => new BuiltInMethod("sqrt", 1, (_, _, args) =>
                Math.Sqrt((double)args[0]!)),

            "sin" => new BuiltInMethod("sin", 1, (_, _, args) =>
                Math.Sin((double)args[0]!)),

            "cos" => new BuiltInMethod("cos", 1, (_, _, args) =>
                Math.Cos((double)args[0]!)),

            "tan" => new BuiltInMethod("tan", 1, (_, _, args) =>
                Math.Tan((double)args[0]!)),

            "log" => new BuiltInMethod("log", 1, (_, _, args) =>
                Math.Log((double)args[0]!)),

            "exp" => new BuiltInMethod("exp", 1, (_, _, args) =>
                Math.Exp((double)args[0]!)),

            "sign" => new BuiltInMethod("sign", 1, (_, _, args) =>
                (double)Math.Sign((double)args[0]!)),

            "trunc" => new BuiltInMethod("trunc", 1, (_, _, args) =>
                Math.Truncate((double)args[0]!)),

            // Two argument methods
            "pow" => new BuiltInMethod("pow", 2, (_, _, args) =>
                Math.Pow((double)args[0]!, (double)args[1]!)),

            "min" => new BuiltInMethod("min", 2, int.MaxValue, (_, _, args) =>
                args.Select(a => (double)a!).Min()),

            "max" => new BuiltInMethod("max", 2, int.MaxValue, (_, _, args) =>
                args.Select(a => (double)a!).Max()),

            // No argument methods
            "random" => new BuiltInMethod("random", 0, (_, _, _) =>
                _random.NextDouble()),

            _ => null
        };
    }
}
