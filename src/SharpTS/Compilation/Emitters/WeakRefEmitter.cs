using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for WeakRef method calls.
/// Handles the TypeScript WeakRef method: deref.
/// </summary>
public sealed class WeakRefEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a WeakRef receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (methodName != "deref")
            return false;

        // Emit the WeakRef object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Call WeakRefDeref(weakRef)
        il.Emit(OpCodes.Call, ctx.Runtime!.WeakRefDeref);
        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a WeakRef receiver.
    /// WeakRef doesn't have accessible properties.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a WeakRef receiver.
    /// WeakRef properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }
}
