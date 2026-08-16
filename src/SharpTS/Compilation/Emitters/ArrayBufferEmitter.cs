using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for ArrayBuffer instance method calls and property access.
/// Handles ArrayBuffer methods like slice() and properties like byteLength.
/// </summary>
public sealed class ArrayBufferEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on an ArrayBuffer receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        // Check if we can handle this method BEFORE emitting anything
        if (methodName is not "slice")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Now we know we can handle it, emit the receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "slice":
                // begin argument (default 0)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                // end argument (default int.MaxValue which signals "use full length")
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4, int.MaxValue);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.TSArrayBufferSlice);
                return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an ArrayBuffer receiver.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // Check if we can handle this property BEFORE emitting anything
        if (propertyName is not "byteLength")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Now we know we can handle it, emit the receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (propertyName)
        {
            case "byteLength":
                il.Emit(OpCodes.Call, ctx.Runtime!.TSArrayBufferByteLengthGetter);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
        }

        return false;
    }

    /// <summary>
    /// ArrayBuffer doesn't have settable properties.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        // ArrayBuffer has no settable properties
        return false;
    }
}
