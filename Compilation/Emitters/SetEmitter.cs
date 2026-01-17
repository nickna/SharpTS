using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Set method calls and property access.
/// Handles all TypeScript Set methods including ES2025 set operations.
/// </summary>
public sealed class SetEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a Set receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the Set object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "add":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetAdd);
                return true;

            case "has":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetHas);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetDelete);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "clear":
                il.Emit(OpCodes.Call, ctx.Runtime!.SetClear);
                il.Emit(OpCodes.Ldnull); // clear returns undefined
                return true;

            case "keys":
                il.Emit(OpCodes.Call, ctx.Runtime!.SetKeys);
                return true;

            case "values":
                il.Emit(OpCodes.Call, ctx.Runtime!.SetValues);
                return true;

            case "entries":
                il.Emit(OpCodes.Call, ctx.Runtime!.SetEntries);
                return true;

            case "forEach":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetForEach);
                il.Emit(OpCodes.Ldnull); // forEach returns undefined
                return true;

            // ES2025 Set Operations
            case "union":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetUnion);
                return true;

            case "intersection":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIntersection);
                return true;

            case "difference":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetDifference);
                return true;

            case "symmetricDifference":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetSymmetricDifference);
                return true;

            case "isSubsetOf":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIsSubsetOf);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isSupersetOf":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIsSupersetOf);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isDisjointFrom":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIsDisjointFrom);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a Set receiver.
    /// Handles the 'size' property.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        if (propertyName != "size")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        il.Emit(OpCodes.Call, ctx.Runtime!.SetSize);
        il.Emit(OpCodes.Box, ctx.Types.Double);

        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a Set receiver.
    /// Set properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    #region Helper Methods

    private static void EmitSingleArgOrNull(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    #endregion
}
