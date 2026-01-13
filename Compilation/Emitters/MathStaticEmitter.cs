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

        if (methodName == "random")
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.Random);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Handle variadic min/max (JavaScript allows any number of arguments)
        if (methodName is "min" or "max")
        {
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
                // Emit first argument
                emitter.EmitExpressionAsDouble(arguments[0]);
                // Chain remaining arguments with min/max calls
                for (int i = 1; i < arguments.Count; i++)
                {
                    emitter.EmitExpressionAsDouble(arguments[i]);
                    il.Emit(OpCodes.Call, minMaxMethod);
                }
            }
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        // Emit all arguments as doubles
        foreach (var arg in arguments)
        {
            emitter.EmitExpressionAsDouble(arg);
        }

        if (methodName == "round")
        {
            // JavaScript rounds half-values toward +infinity: Math.Floor(x + 0.5)
            il.Emit(OpCodes.Ldc_R8, 0.5);
            il.Emit(OpCodes.Add);
            var floorMethod = ctx.Types.GetMethod(ctx.Types.Math, "Floor", ctx.Types.Double);
            il.Emit(OpCodes.Call, floorMethod);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        if (methodName == "sign")
        {
            // Math.Sign returns int, need to convert to double
            var signMethod = ctx.Types.GetMethod(ctx.Types.Math, "Sign", ctx.Types.Double);
            il.Emit(OpCodes.Call, signMethod);
            il.Emit(OpCodes.Conv_R8); // Convert int to double
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
            _ => null
        };

        if (mathMethod != null)
        {
            il.Emit(OpCodes.Call, mathMethod);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a Math static property get (PI, E).
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

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
            default:
                return false;
        }
    }
}
