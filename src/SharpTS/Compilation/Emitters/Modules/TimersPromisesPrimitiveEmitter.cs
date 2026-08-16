using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for the <c>primitive:timers/promises</c> primitive module.
/// Promise-based timer operations (setTimeout/setImmediate/setInterval) with
/// AbortSignal support via <c>options.signal</c>. Dispatches to existing
/// <c>$Runtime.SetTimeoutPromise[WithSignal]</c> etc. methods. The user-facing
/// <c>timers/promises</c> module lives in <c>stdlib/node/timers/promises.ts</c>
/// and re-exports these without spread issues (these signatures take positional
/// delay/value/options — no rest params).
/// </summary>
public sealed class TimersPromisesPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:timers/promises";

    private static readonly string[] _exportedMembers =
    [
        "setTimeout", "setImmediate", "setInterval"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "setTimeout" => EmitSetTimeout(emitter, arguments),
            "setImmediate" => EmitSetImmediate(emitter, arguments),
            "setInterval" => EmitSetInterval(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }

    /// <summary>
    /// Emits: $Runtime.SetTimeoutPromise(delay, value) → $Promise
    /// Or with 3rd arg: $Runtime.SetTimeoutPromiseWithSignal(delay, value, options) → $Promise
    /// </summary>
    private static bool EmitSetTimeout(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // delay (default 0)
        if (arguments.Count > 0)
        {
            emitter.EmitExpressionAsDouble(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }

        // value (default null/undefined)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count >= 3)
        {
            // options
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
            il.Emit(OpCodes.Call, ctx.Runtime!.SetTimeoutPromiseWithSignal);
        }
        else
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.SetTimeoutPromise);
        }
        return true;
    }

    /// <summary>
    /// Emits: $Runtime.SetImmediatePromise(value) → $Promise
    /// Or with 2nd arg: $Runtime.SetImmediatePromiseWithSignal(value, options) → $Promise
    /// </summary>
    private static bool EmitSetImmediate(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // value (default null/undefined)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
            il.Emit(OpCodes.Call, ctx.Runtime!.SetImmediatePromiseWithSignal);
        }
        else
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.SetImmediatePromise);
        }
        return true;
    }

    /// <summary>
    /// Emits: $Runtime.SetIntervalAsyncIterable(delay, value) → async iterable dict
    /// Or with 3rd arg: $Runtime.SetIntervalAsyncIterableWithSignal(delay, value, options)
    /// </summary>
    private static bool EmitSetInterval(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // delay (default 0)
        if (arguments.Count > 0)
        {
            emitter.EmitExpressionAsDouble(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }

        // value (default null/undefined)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count >= 3)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
            il.Emit(OpCodes.Call, ctx.Runtime!.SetIntervalAsyncIterableWithSignal);
        }
        else
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.SetIntervalAsyncIterable);
        }
        return true;
    }
}
