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
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Add);
                return true;

            case "has":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Has);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Delete);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "clear":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Clear);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime.UndefinedInstance);
                return true;

            case "keys":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Keys);
                return true;

            case "values":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Values);
                return true;

            case "entries":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Entries);
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
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().ForEach);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                return true;

            // ES2025 Set Operations
            case "union":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Union);
                return true;

            case "intersection":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Intersection);
                return true;

            case "difference":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Difference);
                return true;

            case "symmetricDifference":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().SymmetricDifference);
                return true;

            case "isSubsetOf":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().IsSubsetOf);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isSupersetOf":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().IsSupersetOf);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "isDisjointFrom":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().IsDisjointFrom);
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
        il.Emit(OpCodes.Call, ctx.Runtime!.RequireSet().Size);
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
