using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for String static method calls.
/// Handles String.fromCharCode(), String.raw().
/// </summary>
public sealed class StringStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a String static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "fromCharCode":
                // Create an object[] from all arguments (char codes)
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

                il.Emit(OpCodes.Call, ctx.Runtime!.StringFromCharCode);
                return true;

            case "fromCodePoint":
                // Create an object[] from all arguments (code points)
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

                il.Emit(OpCodes.Call, ctx.Runtime!.StringFromCodePoint);
                return true;

            case "raw":
                {
                    // Direct call form: String.raw(template, ...substitutions)
                    // Emit the template object as arg0, and the substitutions
                    // collected into a List<object> as arg1. The legacy
                    // tagged-template-literal path (EmitStringRawTaggedTemplate)
                    // also calls this same MethodBuilder with the same shape.
                    if (arguments.Count == 0)
                    {
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Newobj, ctx.Types.GetDefaultConstructor(ctx.Types.ListOfObject));
                        il.Emit(OpCodes.Call, ctx.Runtime!.StringRaw);
                        return true;
                    }
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    var subsLocal = il.DeclareLocal(ctx.Types.ListOfObject);
                    il.Emit(OpCodes.Newobj, ctx.Types.GetDefaultConstructor(ctx.Types.ListOfObject));
                    il.Emit(OpCodes.Stloc, subsLocal);
                    for (int i = 1; i < arguments.Count; i++)
                    {
                        il.Emit(OpCodes.Ldloc, subsLocal);
                        emitter.EmitExpression(arguments[i]);
                        emitter.EmitBoxIfNeeded(arguments[i]);
                        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.ListOfObject, "Add", [ctx.Types.Object])!);
                    }
                    il.Emit(OpCodes.Ldloc, subsLocal);
                    il.Emit(OpCodes.Call, ctx.Runtime!.StringRaw);
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Emits IL for bare access to a <c>String</c> static member — method
    /// references only (<c>var f = String.fromCharCode</c>). The underlying
    /// runtime helpers already have <c>object[]</c> signatures that
    /// <c>$TSFunction.AdjustArgs</c> can feed via its rest-parameter
    /// handling, so direct wrapping works. See issue #60.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var runtime = ctx.Runtime!;

        // `String.prototype` — return the singleton dict (same pattern as
        // Array.prototype, Stage 4z9). Populated lazily on first access with
        // $TSFunction wrappers around String runtime helpers. Required for
        // `typeof String.prototype.substring === "function"` and Test262
        // isConstructor probes.
        if (propertyName == "prototype")
        {
            var protoIL = ctx.IL;
            protoIL.Emit(OpCodes.Call, runtime.StringPrototypePopulateMethod);
            protoIL.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
            return true;
        }

        // Constructor metadata (ECMA-262 §22.1.2): String.length is 1, name is "String".
        if (propertyName == "length")
        {
            ctx.IL.Emit(OpCodes.Ldc_R8, 1.0);
            ctx.IL.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }
        if (propertyName == "name")
        {
            ctx.IL.Emit(OpCodes.Ldstr, "String");
            return true;
        }

        MethodInfo? method = propertyName switch
        {
            "fromCharCode"  => runtime.StringFromCharCode,
            "fromCodePoint" => runtime.StringFromCodePoint,
            "raw"           => runtime.StringRaw,
            _ => null
        };
        if (method == null) return false;

        // ECMA-262 §17 built-in `name` + spec `length`.
        // String.fromCharCode(...codeUnits) → 1; fromCodePoint(...codePoints) → 1;
        // raw(template, ...substitutions) → 1.
        var il = ctx.IL;
        ctx.Types.EmitLoadMethodInfo(il, method);
        il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        return true;
    }

    public bool HasStaticProperty(string memberName) => memberName is
        "fromCharCode" or "fromCodePoint" or "raw" or
        "length" or "name" or "prototype";
}
