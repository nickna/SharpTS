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
    /// Delegates to the appropriate static emitter for known built-ins.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // globalThis doesn't have direct methods - calls like globalThis.parseInt()
        // are handled through property access (globalThis.parseInt) then call
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a globalThis property get.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Self-reference: globalThis.globalThis
        if (propertyName == "globalThis")
        {
            // Push null as a marker - globalThis is handled specially in property access chains
            // When we see globalThis.globalThis.Math, the outer globalThis gets the same treatment
            il.Emit(OpCodes.Ldnull);
            return true;
        }

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
            case "Array":
            case "Date":
            case "RegExp":
            case "Map":
            case "Set":
            case "WeakMap":
            case "WeakSet":
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
}
