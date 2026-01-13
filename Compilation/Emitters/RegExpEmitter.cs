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

            case "exec":
                // regex.exec(str) -> array|null
                EmitStringArgOrEmpty(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpExec);
                return true;

            default:
                return false;
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

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (propertyName)
        {
            case "source":
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpGetSource);
                return true;

            case "flags":
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpGetFlags);
                return true;

            case "global":
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpGetGlobal);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "ignoreCase":
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpGetIgnoreCase);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "multiline":
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpGetMultiline);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "lastIndex":
                il.Emit(OpCodes.Call, ctx.Runtime!.RegExpGetLastIndex);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            default:
                return false;
        }
    }

    #region Helper Methods

    /// <summary>
    /// Emits the first argument as a string, or empty string if no arguments.
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
            il.Emit(OpCodes.Ldstr, "");
        }
    }

    #endregion
}
