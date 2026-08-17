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
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetAdd);
                return true;

            case "has":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetHas);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetDelete);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "clear":
                il.Emit(OpCodes.Call, ctx.Runtime!.SetClear);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime.UndefinedInstance);
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
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.SetForEach);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                return true;

            // ES2025 Set Operations
            case "union":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetUnion);
                return true;

            case "intersection":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIntersection);
                return true;

            case "difference":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetDifference);
                return true;

            case "symmetricDifference":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetSymmetricDifference);
                return true;

            case "isSubsetOf":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIsSubsetOf);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isSupersetOf":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.SetIsSupersetOf);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isDisjointFrom":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
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

}
