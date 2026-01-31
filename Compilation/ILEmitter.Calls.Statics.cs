using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Global function emission (parseInt, parseFloat, isNaN, isFinite) for the IL emitter.
/// Note: Static type methods (Math, JSON, Object, Array, Number, Promise) are now handled
/// by IStaticTypeEmitterStrategy implementations in the TypeEmitterRegistry.
/// </summary>
public partial class ILEmitter
{
    internal void EmitGlobalParseInt(List<Expr> arguments)
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

    internal void EmitGlobalParseFloat(List<Expr> arguments)
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

    internal void EmitGlobalIsNaN(List<Expr> arguments)
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

    internal void EmitGlobalIsFinite(List<Expr> arguments)
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

    internal void EmitStructuredClone(List<Expr> arguments)
    {
        // structuredClone(value, options?)
        // First argument: value to clone
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Second argument: options object (optional) with { transfer: [...] }
        if (arguments.Count > 1)
        {
            EmitExpression(arguments[1]);
            EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        IL.Emit(OpCodes.Call, _ctx.Runtime!.StructuredCloneClone);
    }
}
