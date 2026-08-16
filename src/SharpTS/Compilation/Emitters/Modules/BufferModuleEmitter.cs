using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'buffer' module.
/// The main export is the Buffer constructor with static methods; the module also
/// exports the standalone helpers atob/btoa, isUtf8/isAscii, transcode, SlowBuffer
/// and the constants/kMaxLength/kStringMaxLength/INSPECT_MAX_BYTES values.
/// </summary>
/// <remarks>
/// When you do `import { Buffer } from 'buffer'`, the Buffer variable is stored
/// with a placeholder value. Actual method calls like `Buffer.from()` are dispatched
/// via the TypeEmitterRegistry which looks up "Buffer" by name and uses BufferStaticEmitter.
/// The helper functions/properties route to pure-BCL $Runtime.BufferXxx methods.
/// </remarks>
public sealed class BufferModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "buffer";

    private static readonly string[] _exportedMembers =
    [
        "Buffer",
        "atob", "btoa",
        "isUtf8", "isAscii",
        "transcode", "SlowBuffer",
        "constants", "kMaxLength", "kStringMaxLength", "INSPECT_MAX_BYTES",
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "atob": return EmitUnary(emitter, arguments, ctx.Runtime!.BufferAtob);
            case "btoa": return EmitUnary(emitter, arguments, ctx.Runtime!.BufferBtoa);
            case "isUtf8": return EmitUnary(emitter, arguments, ctx.Runtime!.BufferIsUtf8);
            case "isAscii": return EmitUnary(emitter, arguments, ctx.Runtime!.BufferIsAscii);
            case "SlowBuffer": return EmitUnary(emitter, arguments, ctx.Runtime!.BufferSlowBuffer);
            case "transcode": return EmitTranscode(emitter, arguments);
            default: return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (propertyName)
        {
            case "Buffer":
                // Placeholder — static dispatch (Buffer.from/alloc/...) goes through
                // BufferStaticEmitter by variable name, not this stored value.
                il.Emit(OpCodes.Ldstr, "[Buffer]");
                return true;
            case "constants":
                il.Emit(OpCodes.Call, ctx.Runtime!.BufferModuleConstants);
                return true;
            case "kMaxLength":
                EmitDouble(il, ctx, 4294967296.0);
                return true;
            case "kStringMaxLength":
                EmitDouble(il, ctx, 536870888.0);
                return true;
            case "INSPECT_MAX_BYTES":
                EmitDouble(il, ctx, 50.0);
                return true;
            default:
                return false;
        }
    }

    private static void EmitDouble(ILGenerator il, CompilationContext ctx, double value)
    {
        il.Emit(OpCodes.Ldc_R8, value);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    /// <summary>Emits a 1-arg module function call routed to a $Runtime helper.</summary>
    private static bool EmitUnary(IEmitterContext emitter, List<Expr> arguments, MethodBuilder target)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        il.Emit(OpCodes.Call, target);
        return true;
    }

    private static bool EmitTranscode(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        for (int i = 0; i < 3; i++)
        {
            if (arguments.Count > i)
            {
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.BufferTranscode);
        return true;
    }
}
