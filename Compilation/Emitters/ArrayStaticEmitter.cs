using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Array static method calls.
/// Handles Array.isArray().
/// </summary>
public sealed class ArrayStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for an Array static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "isArray":
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.IsArray);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "from":
                // Emit iterable argument
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // Emit mapFn ($Undefined.Instance for absent so ArrayFrom can
                // distinguish "not provided" from "provided as null" — spec
                // throws TypeError for the latter but not the former).
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }

                // Load Symbol.iterator and runtime type for IterateToList
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIterator);
                il.Emit(OpCodes.Ldtoken, ctx.Runtime!.RuntimeType);
                il.Emit(OpCodes.Call, ctx.Types.TypeGetTypeFromHandle);

                // Emit thisArg (absent → $Undefined). Used as `this` when
                // invoking mapFn per ECMA-262 23.1.2.1 step 5.b.
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFrom);
                return true;
            case "of":
                // Create an object[] from all arguments
                il.Emit(OpCodes.Ldc_I4, arguments.Count);
                il.Emit(OpCodes.Newarr, ctx.Types.Object);

                for (int i = 0; i < arguments.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    emitter.EmitExpression(arguments[i]);
                    emitter.EmitBoxIfNeeded(arguments[i]);
                    il.Emit(OpCodes.Stelem_Ref);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayOf);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Emits IL for bare property access on `Array`, without a call — e.g.
    /// `var f = Array.isArray`. Returns a <c>$TSFunction</c> that wraps the
    /// corresponding runtime helper so downstream <c>f(x)</c> invocations
    /// route to the right builtin.
    /// </summary>
    /// <remarks>
    /// Without this, bare property access fell through to the generic
    /// <c>$Runtime.GetProperty</c> path, which does
    /// <c>P_0.GetType().GetProperty(name, IgnoreCase|Instance|Public)</c>
    /// on the emitted <c>System.Type</c> token for <c>Array</c>. That
    /// leaked <c>System.Type.IsArray</c> (a .NET reflection property) as
    /// the JS value of <c>Array.isArray</c> — returning the boolean
    /// <c>false</c> instead of a callable. lodash caches
    /// <c>var isArray = Array.isArray;</c> at IIFE init, so every
    /// subsequent <c>isArray(x)</c> invoked <c>false(x)</c> and silently
    /// yielded <c>null</c>, which rippled through to <c>_.chunk</c>
    /// returning <c>[]</c> and <c>_.flatten</c> returning its input.
    /// </remarks>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var runtime = ctx.Runtime!;

        // Only isArray wraps cleanly today — its signature `bool IsArray(object)`
        // matches what TSFunction.Invoke can dispatch to via MethodInfo.Invoke
        // (AdjustArgs pads/trims to the method's parameter count, and bool
        // return values auto-box). Array.from / Array.of take variadic or
        // runtime-specific trailing args and need adapter methods before they
        // can be exposed as values the same way.
        // Stage 4z5: route through the WithCache ctor so .name reports the
        // JS-spec name ("isArray" / "from" / "of") instead of the .NET method
        // name ("IsArray" / "ArrayFromAdapter" / "ArrayOf"), and .length
        // reports the spec-defined length.
        // `Array.prototype` — return the singleton dict, lazily populated
        // with $TSFunction wrappers on first read. Required for Test262
        // `isConstructor(Array.prototype.X)` patterns and any code that
        // probes `typeof Array.prototype.sort === "function"`. The pattern
        // matcher in ILEmitter.Calls.cs still intercepts
        // `Array.prototype.X.call(receiver, ...)` syntactically before this
        // path fires, so direct method invocations through the dict aren't
        // load-bearing.
        if (propertyName == "prototype")
        {
            var protoIL = ctx.IL;
            protoIL.Emit(OpCodes.Call, runtime.ArrayPrototypePopulateMethod);
            protoIL.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
            return true;
        }

        // Constructor metadata (ECMA-262 §23.1.2): Array.length is 1, name is "Array".
        if (propertyName == "length")
        {
            ctx.IL.Emit(OpCodes.Ldc_R8, 1.0);
            ctx.IL.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }
        if (propertyName == "name")
        {
            ctx.IL.Emit(OpCodes.Ldstr, "Array");
            return true;
        }

        (MethodInfo? method, string jsName, int jsLength) info = propertyName switch
        {
            "isArray" => (runtime.IsArray, "isArray", 1),
            "from"    => (runtime.ArrayFromAdapter, "from", 1),
            "of"      => (runtime.ArrayOf, "of", 0),
            _ => (null, "", 0)
        };
        if (info.method == null) return false;

        // Use GetOrCreate factory for identity (Array.from === Array.from).
        // Spec doesn't strictly require it but propertyHelper's delete-then-
        // hasOwn round-trip depends on per-instance deletion tracking.
        var il = ctx.IL;
        ctx.Types.EmitLoadMethodInfo(il, info.method);
        il.Emit(OpCodes.Ldstr, info.jsName);
        il.Emit(OpCodes.Ldc_I4, info.jsLength);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        return true;
    }

    public bool HasStaticProperty(string memberName) => memberName is
        "isArray" or "from" or "of" or "fromAsync" or
        "length" or "name" or "prototype";
}
