using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Static-member strategy for the <c>RegExp</c> constructor. Currently exposes
/// the ES2025 <c>RegExp.escape</c> static, both as a direct call
/// (<c>RegExp.escape(s)</c>) and as a first-class value
/// (<c>typeof RegExp.escape === "function"</c>, <c>const f = RegExp.escape</c>).
/// Both routes target the standalone <c>$RegExp.Escape</c> method, which throws
/// TypeError on non-string input. Unknown members return false so existing
/// RegExp handling (constructor, prototype, etc.) is preserved.
/// </summary>
public sealed class RegExpStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName != "escape")
            return false;

        var ctx = emitter.Context;
        var escape = ctx.Runtime?.TSRegExpEscapeMethod;
        if (escape == null)
            return false;

        var il = emitter.IL;
        if (arguments.Count == 0)
        {
            // No argument → S is undefined → spec step 1 TypeError. $RegExp.Escape
            // does `arg as string`; null is not a string, so it throws.
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        il.Emit(OpCodes.Call, escape);
        return true;
    }

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "escape")
            return false;

        var ctx = emitter.Context;
        var runtime = ctx.Runtime;
        var escape = runtime?.TSRegExpEscapeMethod;
        if (escape == null)
            return false;

        // Wrap $RegExp.Escape (an (object) -> object static) in a $TSFunction so
        // it reads as a callable value with name "escape" and length 1. Mirrors
        // ObjectStaticEmitter's Object.keys-as-value path; GetOrCreate gives the
        // wrapper stable identity across repeated reads.
        var il = emitter.IL;
        ctx.Types.EmitLoadMethodInfo(il, escape);
        il.Emit(OpCodes.Ldstr, "escape");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime!.TSFunctionGetOrCreate);
        return true;
    }
}
