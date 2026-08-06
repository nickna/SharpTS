using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for RegExp method calls and property access.
/// Handles TypeScript RegExp methods (test, exec) and properties (source, flags, global, etc.).
/// </summary>
public sealed class RegExpEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a RegExp receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Bail before emitting the receiver for unknown methods — emitting
        // first and returning false would leave an orphaned stack value.
        if (methodName is not ("test" or "exec"))
            return false;

        // Emit the RegExp object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "test":
                // regex.test(str) -> bool
                EmitStringArgOrEmpty(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpTest);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            default:
                // exec: regex.exec(str) -> array|null
                EmitStringArgOrEmpty(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpExec);
                return true;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a RegExp receiver.
    /// Handles: source, flags, global, ignoreCase, multiline, lastIndex.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Resolve the handler BEFORE emitting the receiver: returning false
        // after emitting would leave an orphaned value on the IL stack and
        // produce an invalid program when the caller falls back to another
        // emission path.
        var getter = propertyName switch
        {
            "source" => ctx.Runtime!.RegExpGetSource,
            "flags" => ctx.Runtime!.RegExpGetFlags,
            "global" => ctx.Runtime!.RegExpGetGlobal,
            "ignoreCase" => ctx.Runtime!.RegExpGetIgnoreCase,
            "multiline" => ctx.Runtime!.RegExpGetMultiline,
            "sticky" => ctx.Runtime!.RegExpGetSticky,
            "unicode" => ctx.Runtime!.RegExpGetUnicode,
            "dotAll" => ctx.Runtime!.RegExpGetDotAll,
            "hasIndices" => ctx.Runtime!.RegExpGetHasIndices,
            "unicodeSets" => ctx.Runtime!.RegExpGetUnicodeSets,
            // lastIndex may hold any assigned JS value until RegExpBuiltinExec
            // performs ToLength, so it must use the object-valued property path.
            "lastIndex" => ctx.Runtime!.GetProperty,
            _ => null
        };
        if (getter is null)
            return false;

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        if (propertyName == "lastIndex")
            il.Emit(OpCodes.Ldstr, propertyName);
        il.Emit(OpCodes.Call, getter);

        switch (propertyName)
        {
            case "source":
            case "flags":
                break; // already a string reference
            case "lastIndex":
                break; // GetProperty already returns a boxed JS value.
            default:
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                break;
        }
        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a RegExp receiver.
    /// RegExp writes use the generic descriptor-aware property path.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Emits the first argument as a string, or <c>"undefined"</c> when no
    /// arguments are supplied. ECMA-262 §22.2.6.{2,8} test/exec begin with
    /// <c>S = ? ToString(string)</c>; passing no string yields
    /// <c>ToString(undefined) === "undefined"</c>. Test262 patterns like
    /// <c>/undefined/.exec()</c> rely on that coercion.
    /// </summary>
    private static void EmitStringArgOrEmpty(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Call, ctx.Runtime!.Stringify);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "undefined");
        }
    }

    #endregion
}
