using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Static method call emission (Math, Object, Array, JSON, Number) for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitMathCall(string methodName, List<Expr> arguments)
    {
        if (methodName == "random")
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Random);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Handle variadic min/max (JavaScript allows any number of arguments)
        if (methodName is "min" or "max")
        {
            var minMaxMethod = methodName == "min"
                ? _ctx.Types.GetMethod(_ctx.Types.Math, "Min", _ctx.Types.Double, _ctx.Types.Double)
                : _ctx.Types.GetMethod(_ctx.Types.Math, "Max", _ctx.Types.Double, _ctx.Types.Double);

            if (arguments.Count == 0)
            {
                // No args: min() returns Infinity, max() returns -Infinity
                IL.Emit(OpCodes.Ldc_R8, methodName == "min" ? double.PositiveInfinity : double.NegativeInfinity);
            }
            else
            {
                // Emit first argument
                EmitExpressionAsDouble(arguments[0]);
                // Chain remaining arguments with min/max calls
                for (int i = 1; i < arguments.Count; i++)
                {
                    EmitExpressionAsDouble(arguments[i]);
                    IL.Emit(OpCodes.Call, minMaxMethod);
                }
            }
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Emit all arguments as doubles
        foreach (var arg in arguments)
        {
            EmitExpressionAsDouble(arg);
        }

        if (methodName == "round")
        {
            // JavaScript rounds half-values toward +infinity: Math.Floor(x + 0.5)
            IL.Emit(OpCodes.Ldc_R8, 0.5);
            IL.Emit(OpCodes.Add);
            var floorMethod = _ctx.Types.GetMethod(_ctx.Types.Math, "Floor", _ctx.Types.Double);
            IL.Emit(OpCodes.Call, floorMethod);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        if (methodName == "sign")
        {
            // Math.Sign returns int, need to convert to double
            var signMethod = _ctx.Types.GetMethod(_ctx.Types.Math, "Sign", _ctx.Types.Double);
            IL.Emit(OpCodes.Call, signMethod);
            IL.Emit(OpCodes.Conv_R8); // Convert int to double
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        MethodInfo? mathMethod = methodName switch
        {
            "abs" => _ctx.Types.GetMethod(_ctx.Types.Math, "Abs", _ctx.Types.Double),
            "floor" => _ctx.Types.GetMethod(_ctx.Types.Math, "Floor", _ctx.Types.Double),
            "ceil" => _ctx.Types.GetMethod(_ctx.Types.Math, "Ceiling", _ctx.Types.Double),
            "sqrt" => _ctx.Types.GetMethod(_ctx.Types.Math, "Sqrt", _ctx.Types.Double),
            "sin" => _ctx.Types.GetMethod(_ctx.Types.Math, "Sin", _ctx.Types.Double),
            "cos" => _ctx.Types.GetMethod(_ctx.Types.Math, "Cos", _ctx.Types.Double),
            "tan" => _ctx.Types.GetMethod(_ctx.Types.Math, "Tan", _ctx.Types.Double),
            "log" => _ctx.Types.GetMethod(_ctx.Types.Math, "Log", _ctx.Types.Double),
            "exp" => _ctx.Types.GetMethod(_ctx.Types.Math, "Exp", _ctx.Types.Double),
            "trunc" => _ctx.Types.GetMethod(_ctx.Types.Math, "Truncate", _ctx.Types.Double),
            "pow" => _ctx.Types.GetMethod(_ctx.Types.Math, "Pow", _ctx.Types.Double, _ctx.Types.Double),
            _ => null
        };

        if (mathMethod != null)
        {
            IL.Emit(OpCodes.Call, mathMethod);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
        }
        else
        {
            throw new Exception($"Compile Error: Unknown Math method '{methodName}'.");
        }
    }

    private void EmitObjectStaticCall(string methodName, List<Expr> arguments)
    {
        // Object methods take exactly one argument
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        switch (methodName)
        {
            case "keys":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetKeys);
                break;
            case "values":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetValues);
                break;
            case "entries":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GetEntries);
                break;
            default:
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitArrayStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "isArray":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.IsArray);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            default:
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitJSONCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "parse":
                // Arg 0: text (required)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "null");
                }

                // Arg 1: reviver (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonParseWithReviver);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonParse);
                }
                break;

            case "stringify":
                // Arg 0: value (required)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }

                // Arg 1: replacer (optional), Arg 2: space (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EmitBoxIfNeeded(arguments[1]);

                    if (arguments.Count > 2)
                    {
                        EmitExpression(arguments[2]);
                        EmitBoxIfNeeded(arguments[2]);
                    }
                    else
                    {
                        IL.Emit(OpCodes.Ldnull);
                    }
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonStringifyFull);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.JsonStringify);
                }
                break;

            default:
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitNumberStaticCall(string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "parseInt":
                EmitGlobalParseInt(arguments);
                break;
            case "parseFloat":
                EmitGlobalParseFloat(arguments);
                break;
            case "isNaN":
                // Number.isNaN is stricter than global isNaN - only returns true for actual NaN
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsNaN);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case "isFinite":
                // Number.isFinite is stricter than global isFinite
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsFinite);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case "isInteger":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsInteger);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case "isSafeInteger":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberIsSafeInteger);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            default:
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    private void EmitGlobalParseInt(List<Expr> arguments)
    {
        // Emit string argument
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Emit radix (default 10)
        if (arguments.Count > 1)
        {
            EmitExpression(arguments[1]);
            EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I4, 10);
            IL.Emit(OpCodes.Box, _ctx.Types.Int32);
        }

        IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberParseInt);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
    }

    private void EmitGlobalParseFloat(List<Expr> arguments)
    {
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        IL.Emit(OpCodes.Call, _ctx.Runtime!.NumberParseFloat);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
    }

    private void EmitGlobalIsNaN(List<Expr> arguments)
    {
        // Global isNaN coerces to number first
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalIsNaN);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
    }

    private void EmitGlobalIsFinite(List<Expr> arguments)
    {
        // Global isFinite coerces to number first
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalIsFinite);
        IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
    }
}
