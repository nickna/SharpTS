using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'timers' module.
/// </summary>
/// <remarks>
/// Re-exports the global timer functions (setTimeout, setInterval, etc.)
/// as module exports.
/// </remarks>
public sealed class TimersModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "timers";

    private static readonly string[] _exportedMembers =
    [
        "setTimeout", "clearTimeout",
        "setInterval", "clearInterval",
        "setImmediate", "clearImmediate"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "setTimeout" => EmitSetTimeout(emitter, arguments),
            "clearTimeout" => EmitClearTimeout(emitter, arguments),
            "setInterval" => EmitSetInterval(emitter, arguments),
            "clearInterval" => EmitClearInterval(emitter, arguments),
            "setImmediate" => EmitSetImmediate(emitter, arguments),
            "clearImmediate" => EmitClearImmediate(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // timers module has no properties, only methods
        return false;
    }

    private static bool EmitSetTimeout(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit callback - first argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit delay - second argument (default 0)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            if (arguments[1] is not Expr.Literal { Value: double })
            {
                il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }

        // Emit args array - remaining arguments
        EmitArgsArray(emitter, arguments, 2);

        // Call $Runtime.SetTimeout(callback, delay, args)
        il.Emit(OpCodes.Call, ctx.Runtime!.SetTimeout);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitClearTimeout(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit handle - first argument (or null if not provided)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.ClearTimeout(handle)
        il.Emit(OpCodes.Call, ctx.Runtime!.ClearTimeout);

        // clearTimeout returns void, push null for expression result
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitSetInterval(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit callback - first argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit delay - second argument (default 0)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            if (arguments[1] is not Expr.Literal { Value: double })
            {
                il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }

        // Emit args array - remaining arguments
        EmitArgsArray(emitter, arguments, 2);

        // Call $Runtime.SetInterval(callback, delay, args)
        il.Emit(OpCodes.Call, ctx.Runtime!.SetInterval);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitClearInterval(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit handle - first argument (or null if not provided)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.ClearInterval(handle)
        il.Emit(OpCodes.Call, ctx.Runtime!.ClearInterval);

        // clearInterval returns void, push null for expression result
        il.Emit(OpCodes.Ldnull);
        return true;
    }

    private static bool EmitSetImmediate(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // setImmediate is setTimeout with 0 delay
        // Emit callback - first argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Delay is always 0 for setImmediate
        il.Emit(OpCodes.Ldc_R8, 0.0);

        // Emit args array - remaining arguments (starting from index 1)
        EmitArgsArray(emitter, arguments, 1);

        // Call $Runtime.SetTimeout(callback, 0, args)
        il.Emit(OpCodes.Call, ctx.Runtime!.SetTimeout);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitClearImmediate(IEmitterContext emitter, List<Expr> arguments)
    {
        // clearImmediate is the same as clearTimeout
        return EmitClearTimeout(emitter, arguments);
    }

    /// <summary>
    /// Emits an object[] array with remaining arguments starting from startIndex.
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments, int startIndex)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        int extraArgCount = Math.Max(0, arguments.Count - startIndex);

        if (extraArgCount > 0)
        {
            // Create array with remaining arguments
            il.Emit(OpCodes.Ldc_I4, extraArgCount);
            il.Emit(OpCodes.Newarr, ctx.Types.Object);

            for (int i = startIndex; i < arguments.Count; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i - startIndex);
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // Empty args array
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, ctx.Types.Object);
        }
    }
}
