using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'util' module.
/// </summary>
public sealed class UtilModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "util";

    private static readonly string[] _exportedMembers =
    [
        "format", "inspect", "isDeepStrictEqual", "parseArgs", "toUSVString", "deprecate", "callbackify", "inherits", "TextEncoder", "TextDecoder", "types"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "format" => EmitFormat(emitter, arguments),
            "inspect" => EmitInspect(emitter, arguments),
            "isDeepStrictEqual" => EmitIsDeepStrictEqual(emitter, arguments),
            "parseArgs" => EmitParseArgs(emitter, arguments),
            "toUSVString" => EmitToUSVString(emitter, arguments),
            // Handle util.types.* nested calls - use emitted methods for standalone execution
            "types.isArray" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsArray),
            "types.isFunction" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsFunction),
            "types.isNull" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsNull),
            "types.isUndefined" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsUndefined),
            "types.isDate" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsDate),
            "types.isPromise" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsPromise),
            "types.isRegExp" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsRegExp),
            "types.isMap" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsMap),
            "types.isSet" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsSet),
            "types.isTypedArray" => EmitTypesCall(emitter, arguments, ctx => ctx.Runtime!.UtilTypesIsTypedArray),
            "deprecate" => EmitDeprecate(emitter, arguments),
            "callbackify" => EmitCallbackify(emitter, arguments),
            "inherits" => EmitInherits(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "TextEncoder" => EmitPlaceholder(il, "[TextEncoder]"),
            "TextDecoder" => EmitPlaceholder(il, "[TextDecoder]"),
            // util.types is a nested object - handled via nested call pattern in BuiltInModuleHandler
            _ => false
        };
    }

    private static bool EmitPlaceholder(ILGenerator il, string marker)
    {
        // Emit a placeholder string marker for constructor access.
        // The actual construction happens via the TypeEmitterRegistry.
        il.Emit(OpCodes.Ldstr, marker);
        return true;
    }

    private static bool EmitFormat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Build array of arguments
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

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilFormat);
        return true;
    }

    private static bool EmitInspect(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit object to inspect
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit options (or null)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilInspect);
        return true;
    }

    private static bool EmitIsDeepStrictEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit first argument (or null)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit second argument (or null)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilIsDeepStrictEqual);

        // Box the boolean result
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitParseArgs(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit config argument (or null)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilParseArgs);

        return true;
    }

    private static bool EmitToUSVString(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit argument (or empty string)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilToUSVString);

        return true;
    }

    /// <summary>
    /// Emits a call to one of the util.types.* methods using the emitted $Runtime method.
    /// </summary>
    private static bool EmitTypesCall(IEmitterContext emitter, List<Expr> arguments, Func<CompilationContext, System.Reflection.Emit.MethodBuilder> getMethod)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the argument (or null if no argument)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call the emitted $Runtime method
        var method = getMethod(ctx);
        il.Emit(OpCodes.Call, method);

        // Box the boolean result
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitDeprecate(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            // util.deprecate requires at least 2 arguments
            return false;
        }

        // Emit the function argument
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit the message argument
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Convert message to string
        var toStringMethod = ctx.Types.Object.GetMethod("ToString", Type.EmptyTypes)!;
        il.Emit(OpCodes.Callvirt, toStringMethod);

        // Call emitted $Runtime.UtilDeprecate(fn, message) - returns $DeprecatedFunction
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilDeprecate);

        // No boxing needed - $DeprecatedFunction is a reference type
        return true;
    }

    private static bool EmitCallbackify(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 1)
        {
            return false;
        }

        // Emit the function argument
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call emitted $Runtime.UtilCallbackify(fn)
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilCallbackify);

        // Result is already object type, no boxing needed
        return true;
    }

    private static bool EmitInherits(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            return false;
        }

        // Emit the constructor argument
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit the superConstructor argument
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Call emitted $Runtime.UtilInherits(ctor, superCtor)
        il.Emit(OpCodes.Call, ctx.Runtime!.UtilInherits);

        // inherits returns void, push null for consistency
        il.Emit(OpCodes.Ldnull);
        return true;
    }
}
