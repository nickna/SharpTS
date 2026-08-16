using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for the <c>primitive:timers</c> primitive module. Dispatches the
/// callback-based timer API (setTimeout/clearTimeout/setInterval/clearInterval/
/// setImmediate/clearImmediate) to the existing <c>$Runtime</c> methods. The
/// user-facing <c>timers</c> module lives in <c>stdlib/node/timers.ts</c> and
/// arity-dispatches rest args into this primitive (the primitive's emitter
/// packs flat trailing args into the object[] that $Runtime expects).
/// </summary>
public sealed class TimersPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:timers";

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
            emitter.EmitExpressionAsDouble(arguments[1]);
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
            emitter.EmitExpressionAsDouble(arguments[1]);
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
    /// Emits an object[] array with the remaining arguments starting from startIndex,
    /// expanding any <see cref="Expr.Spread"/> (<c>...args</c>) at runtime via the shared
    /// spread-aware builder. Leaves an <c>object[]</c> on the stack. Forwarding spreads
    /// here is what lets <c>stdlib/node/timers.ts</c> pass <c>...args</c> straight through
    /// instead of hand-unrolling an arity ladder (#1149).
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments, int startIndex)
    {
        int extraArgCount = Math.Max(0, arguments.Count - startIndex);
        var extra = extraArgCount > 0
            ? arguments.GetRange(startIndex, extraArgCount)
            : new List<Expr>();
        emitter.EmitArgsArrayWithSpread(extra);
    }
}
