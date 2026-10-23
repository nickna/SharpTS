using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for ArrayBuffer static method calls.
/// Handles ArrayBuffer.isView().
/// </summary>
public sealed class ArrayBufferStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for an ArrayBuffer static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "isView" => EmitIsView(emitter, arguments),
            _ => false
        };
    }

    /// <summary>
    /// Emits an ArrayBuffer static method value through the live constructor property.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "isView") return false;
        var ctx = emitter.Context;
        // Match alias reads: the constructor Type owns live descriptor replacements.
        ctx.IL.Emit(OpCodes.Ldtoken, ctx.Runtime!.RequireArrayBuffer().Type);
        ctx.IL.Emit(OpCodes.Call, ctx.Types.TypeGetTypeFromHandle);
        ctx.IL.Emit(OpCodes.Ldstr, propertyName);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime.ObjectRead.Property);
        return true;
    }

    private static bool EmitIsView(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 1) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the argument
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call the static ArrayBuffer.IsView method
        il.Emit(OpCodes.Call, ctx.Runtime!.RequireArrayBuffer().IsView);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }
}
