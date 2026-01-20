using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Timer function emission (setTimeout, clearTimeout) for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Emits IL for setTimeout(callback, delay?, ...args).
    /// Calls $Runtime.SetTimeout($TSFunction, double, object[]).
    /// </summary>
    internal void EmitSetTimeout(List<Expr> arguments)
    {
        // Emit callback - first argument
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Emit delay - second argument (default 0)
        if (arguments.Count > 1)
        {
            EmitExpression(arguments[1]);
            // Convert to double if needed
            if (!IsDoubleExpression(arguments[1]))
            {
                IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
            }
        }
        else
        {
            IL.Emit(OpCodes.Ldc_R8, 0.0);
        }

        // Emit args array - remaining arguments
        if (arguments.Count > 2)
        {
            // Create array with remaining arguments
            IL.Emit(OpCodes.Ldc_I4, arguments.Count - 2);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

            for (int i = 2; i < arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i - 2);
                EmitExpression(arguments[i]);
                EmitBoxIfNeeded(arguments[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // Empty args array
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        }

        // Call $Runtime.SetTimeout(callback, delay, args)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetTimeout);

        // SetTimeout returns $TSTimeout, mark stack as reference type
        SetStackUnknown();
    }

    /// <summary>
    /// Emits IL for clearTimeout(handle?).
    /// Calls $Runtime.ClearTimeout(object).
    /// </summary>
    internal void EmitClearTimeout(List<Expr> arguments)
    {
        // Emit handle - first argument (or null if not provided)
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.ClearTimeout(handle)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ClearTimeout);

        // clearTimeout returns void, push null for expression result if needed
        IL.Emit(OpCodes.Ldnull);
    }

    /// <summary>
    /// Emits IL for setInterval(callback, delay?, ...args).
    /// Calls $Runtime.SetInterval($TSFunction, double, object[]).
    /// </summary>
    internal void EmitSetInterval(List<Expr> arguments)
    {
        // Emit callback - first argument
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Emit delay - second argument (default 0)
        if (arguments.Count > 1)
        {
            EmitExpression(arguments[1]);
            // Convert to double if needed
            if (!IsDoubleExpression(arguments[1]))
            {
                IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Double);
            }
        }
        else
        {
            IL.Emit(OpCodes.Ldc_R8, 0.0);
        }

        // Emit args array - remaining arguments
        if (arguments.Count > 2)
        {
            // Create array with remaining arguments
            IL.Emit(OpCodes.Ldc_I4, arguments.Count - 2);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

            for (int i = 2; i < arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i - 2);
                EmitExpression(arguments[i]);
                EmitBoxIfNeeded(arguments[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // Empty args array
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        }

        // Call $Runtime.SetInterval(callback, delay, args)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetInterval);

        // SetInterval returns $TSTimeout, mark stack as reference type
        SetStackUnknown();
    }

    /// <summary>
    /// Emits IL for clearInterval(handle?).
    /// Calls $Runtime.ClearInterval(object).
    /// </summary>
    internal void EmitClearInterval(List<Expr> arguments)
    {
        // Emit handle - first argument (or null if not provided)
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.ClearInterval(handle)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ClearInterval);

        // clearInterval returns void, push null for expression result if needed
        IL.Emit(OpCodes.Ldnull);
    }

    private bool IsDoubleExpression(Expr expr)
    {
        return expr is Expr.Literal { Value: double };
    }
}
