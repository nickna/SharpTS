using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for globalThis property access and method calls.
/// Delegates to appropriate emitters for built-in globals (Math, console, JSON, etc.)
/// and handles user-assigned properties via runtime helpers.
/// </summary>
public sealed class GlobalThisStaticEmitter : IStaticTypeEmitterStrategy
{
    private readonly TypeEmitterRegistry _registry;

    public GlobalThisStaticEmitter(TypeEmitterRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Attempts to emit IL for a globalThis method call.
    /// Emits direct runtime calls for parseInt/parseFloat/isNaN/isFinite.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "parseInt":
                if (arguments.Count > 0) { emitter.EmitExpression(arguments[0]); emitter.EmitBoxIfNeeded(arguments[0]); } else { il.Emit(OpCodes.Ldnull); }
                if (arguments.Count > 1) { emitter.EmitExpression(arguments[1]); emitter.EmitBoxIfNeeded(arguments[1]); } else { il.Emit(OpCodes.Ldc_I4, 10); il.Emit(OpCodes.Box, ctx.Types.Int32); }
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseInt);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "parseFloat":
                if (arguments.Count > 0) { emitter.EmitExpression(arguments[0]); emitter.EmitBoxIfNeeded(arguments[0]); } else { il.Emit(OpCodes.Ldnull); }
                il.Emit(OpCodes.Call, ctx.Runtime!.NumberParseFloat);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "isNaN":
                if (arguments.Count > 0) { emitter.EmitExpression(arguments[0]); emitter.EmitBoxIfNeeded(arguments[0]); } else { il.Emit(OpCodes.Ldnull); }
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalIsNaN);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isFinite":
                if (arguments.Count > 0) { emitter.EmitExpression(arguments[0]); emitter.EmitBoxIfNeeded(arguments[0]); } else { il.Emit(OpCodes.Ldnull); }
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalIsFinite);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a globalThis property get.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Self-reference: globalThis.globalThis (and the Node alias global).
        // Emit the runtime sentinel so the leaf access matches bare `globalThis`
        // in value position — `globalThis.globalThis === globalThis` (#271).
        // Deeper chains (globalThis.globalThis.Math.PI) are intercepted earlier by
        // TryEmitGlobalThisChainedProperty, so this only fires for the leaf.
        if (propertyName == "globalThis" || propertyName == "global")
        {
            il.Emit(OpCodes.Ldsfld, ctx.Runtime!.GlobalThisSingletonField);
            return true;
        }

        // Built-in class constructors — emit the actual .NET Type so identity and
        // typeof work (`typeof globalThis.Array === "function"`, `globalThis.Array === Array`).
        // Must come before the static-emitter fallback because some built-in names
        // (Array, Map, Set) have both a static emitter (for Array.from, Map.groupBy,
        // etc.) and a Type identity — the Type identity wins when used as a value.
        if (TryEmitBuiltInClassType(il, ctx, propertyName))
            return true;

        // Check if this is a known built-in that has its own static emitter
        var staticEmitter = _registry.GetStaticStrategy(propertyName);
        if (staticEmitter != null)
        {
            // For singletons like Math, process - just emit null marker like direct access does
            // The subsequent property access or call will handle it correctly
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Known built-in namespaces without static emitters but with special handling
        switch (propertyName)
        {
            case "console":
            case "Object":
            case "Error":
            case "Reflect":
                // These are accessed through property access chains
                il.Emit(OpCodes.Ldnull);
                return true;

            case "parseInt":
                // Global parseInt - return function reference
                // For now, emit a call to the runtime helper
                il.Emit(OpCodes.Ldstr, "parseInt");
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalThisGetProperty);
                return true;

            case "parseFloat":
                il.Emit(OpCodes.Ldstr, "parseFloat");
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalThisGetProperty);
                return true;

            case "isNaN":
                il.Emit(OpCodes.Ldstr, "isNaN");
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalThisGetProperty);
                return true;

            case "isFinite":
                il.Emit(OpCodes.Ldstr, "isFinite");
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalThisGetProperty);
                return true;

            case "fetch":
                il.Emit(OpCodes.Ldstr, "fetch");
                il.Emit(OpCodes.Call, ctx.Runtime!.GlobalThisGetProperty);
                return true;

            case "undefined":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                return true;

            case "NaN":
                il.Emit(OpCodes.Ldc_R8, double.NaN);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "Infinity":
                il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
        }

        // For user-assigned properties, use runtime helper
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Call, ctx.Runtime!.GlobalThisGetProperty);
        return true;
    }

    /// <summary>
    /// Emits `Ldtoken T; call GetTypeFromHandle` for built-in class constructor names
    /// so `globalThis.Array`, `globalThis.Date`, etc. match the identity emitted by a
    /// bare reference to the same name (<see cref="ILEmitter.TryEmitBuiltInClassType"/>).
    /// Matters for lodash-style feature detection: <c>root.Object === Object</c>,
    /// <c>typeof root.Array === "function"</c>.
    /// </summary>
    private static bool TryEmitBuiltInClassType(ILGenerator il, CompilationContext ctx, string name)
    {
        Type? t = name switch
        {
            "Array" => ctx.Types.IListOfObject,
            "Date" => ctx.Runtime!.TSDateType,
            "RegExp" => ctx.Runtime!.TSRegExpType,
            "Map" => ctx.Types.DictionaryObjectObject,
            "Set" => ctx.Types.HashSetOfObject,
            "WeakMap" => ctx.Types.ConditionalWeakTableObjectObject,
            "WeakSet" => ctx.Types.ConditionalWeakTableObjectObject,
            "Promise" => ctx.Types.TaskOfObject,
            "Buffer" => ctx.Runtime!.TSBufferType,
            "Function" => ctx.Runtime!.TSFunctionType,
            "TextEncoder" => ctx.Runtime!.TSTextEncoderType,
            "TextDecoder" => ctx.Runtime!.TSTextDecoderType,
            _ => null
        };
        if (t == null) return false;
        il.Emit(OpCodes.Ldtoken, t);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle", ctx.Types.RuntimeTypeHandle));
        return true;
    }
}
