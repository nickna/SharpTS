using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Number static method calls and property access.
/// Handles Number.parseInt(), Number.parseFloat(), Number.isNaN(), etc.
/// and Number.MAX_VALUE, Number.MIN_VALUE, Number.NaN, etc.
/// </summary>
public sealed class NumberStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a Number static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "parseInt":
                EmitParseInt(emitter, arguments);
                return true;
            case "parseFloat":
                EmitParseFloat(emitter, arguments);
                return true;
            case "isNaN":
                // Number.isNaN is stricter than global isNaN - only returns true for actual NaN
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsNaN);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "isFinite":
                // Number.isFinite is stricter than global isFinite
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsFinite);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "isInteger":
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsInteger);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "isSafeInteger":
                EmitSingleArgMethod(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberIsSafeInteger);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a Number static property get.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (propertyName)
        {
            case "MAX_VALUE":
                il.Emit(OpCodes.Ldc_R8, double.MaxValue);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "MIN_VALUE":
                il.Emit(OpCodes.Ldc_R8, double.Epsilon); // JS MIN_VALUE = smallest positive
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "NaN":
                il.Emit(OpCodes.Ldc_R8, double.NaN);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "POSITIVE_INFINITY":
                il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "NEGATIVE_INFINITY":
                il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "MAX_SAFE_INTEGER":
                il.Emit(OpCodes.Ldc_R8, 9007199254740991.0); // 2^53 - 1
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "MIN_SAFE_INTEGER":
                il.Emit(OpCodes.Ldc_R8, -9007199254740991.0); // -(2^53 - 1)
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            case "EPSILON":
                il.Emit(OpCodes.Ldc_R8, 2.220446049250313e-16); // 2^-52
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
            default:
                return false;
        }
    }

    #region Helper Methods

    private static void EmitSingleArgMethod(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    private static void EmitParseInt(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit string argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit radix (default 10)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4, 10);
            il.Emit(OpCodes.Box, ctx.Types.Int32);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseInt);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    private static void EmitParseFloat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseFloat);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    #endregion
}
