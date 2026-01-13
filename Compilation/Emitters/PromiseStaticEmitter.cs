using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Promise static method calls.
/// Handles Promise.resolve(), Promise.reject(), Promise.all(), Promise.race(), Promise.allSettled(), Promise.any().
/// </summary>
public sealed class PromiseStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a Promise static method call.
    /// Returns Task&lt;object?&gt; on the stack - does NOT synchronously await.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "resolve":
                // Promise.resolve(value?) - returns Task<object?> directly
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseResolve);
                return true;

            case "reject":
                // Promise.reject(reason) - returns Task<object?> directly
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseReject);
                return true;

            case "all":
                // Promise.all(iterable) - returns Task<object?> directly
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseAll);
                return true;

            case "race":
                // Promise.race(iterable) - returns Task<object?> directly
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseRace);
                return true;

            case "allSettled":
                // Promise.allSettled(iterable) - returns Task<object?> directly
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseAllSettled);
                return true;

            case "any":
                // Promise.any(iterable) - returns Task<object?> directly
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseAny);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Promise has no static properties.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
