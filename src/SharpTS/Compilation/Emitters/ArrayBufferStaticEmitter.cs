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
    /// Attempts to emit IL for an ArrayBuffer static property get.
    /// ArrayBuffer has no static properties, so this always returns false.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // ArrayBuffer has no static properties
        return false;
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
        il.Emit(OpCodes.Call, ctx.Runtime!.TSArrayBufferIsView);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }
}
