using System.Reflection;
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

            case "allKeyed":
                EmitSingleArgument(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseAllKeyed);
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

            case "allSettledKeyed":
                EmitSingleArgument(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseAllSettledKeyed);
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

            case "withResolvers":
                // Promise.withResolvers() - returns Task<object?> wrapping {promise, resolve, reject}
                il.Emit(OpCodes.Call, ctx.Runtime!.PromiseWithResolvers);
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
        var ctx = emitter.Context;
        var runtime = ctx.Runtime!;
        // Stage 4y: Promise.* statics as values for `let r = Promise.resolve;
        // r(42).then(...)` patterns + test262 isConstructor harness.
        // ECMA-262 §27.2.5.1: value-form Promise.resolve/reject route through
        // wrappers that validate `this` is Object before delegating. Direct
        // syntactic dispatch (`Promise.resolve(x)`) skips the check (since
        // `Promise` is always an Object).
        MethodInfo? method = propertyName switch
        {
            "resolve"        => runtime.PromiseResolveStatic,
            "reject"         => runtime.PromiseRejectStatic,
            "all"            => runtime.PromiseAllStatic,
            "allKeyed"       => runtime.PromiseAllKeyedStatic,
            "race"           => runtime.PromiseRaceStatic,
            "allSettled"     => runtime.PromiseAllSettledStatic,
            "allSettledKeyed" => runtime.PromiseAllSettledKeyedStatic,
            "any"            => runtime.PromiseAnyStatic,
            "withResolvers"  => runtime.PromiseWithResolvers,
            _ => null
        };
        if (method == null) return false;

        // ECMA-262 §17 built-in `name` + spec `length`. resolve/reject/all/race/
        // allSettled/any take one arg; withResolvers takes none.
        int specLength = propertyName == "withResolvers" ? 0 : 1;
        var il = ctx.IL;
        ctx.Types.EmitLoadMethodInfo(il, method);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldc_I4, specLength);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        return true;
    }

    public bool HasStaticProperty(string memberName) =>
        memberName is "resolve" or "reject" or "all" or "race"
            or "allKeyed" or "allSettled" or "allSettledKeyed"
            or "any" or "withResolvers";

    private static void EmitSingleArgument(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            emitter.Context.IL.Emit(OpCodes.Ldnull);
        }
    }
}
