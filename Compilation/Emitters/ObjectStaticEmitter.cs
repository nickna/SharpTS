using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Object static method calls.
/// Handles Object.keys(), Object.values(), Object.entries().
/// </summary>
public sealed class ObjectStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for an Object static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Object methods take exactly one argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        switch (methodName)
        {
            case "keys":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetKeys);
                return true;
            case "values":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetValues);
                return true;
            case "entries":
                il.Emit(OpCodes.Call, ctx.Runtime!.GetEntries);
                return true;
            case "fromEntries":
                // Load Symbol.iterator and runtime type for IterateToList
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIterator);
                il.Emit(OpCodes.Ldtoken, ctx.Runtime!.RuntimeType);
                il.Emit(OpCodes.Call, ctx.Types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectFromEntries);
                return true;
            case "hasOwn":
                // hasOwn takes 2 arguments: obj and key
                // First argument is already on the stack, emit second argument
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectHasOwn);
                // Box the bool result for consistency with other methods
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "assign":
                // Object.assign(target, ...sources)
                // First argument (target) is already on the stack
                // Create a List<object> for all source arguments
                var listType = typeof(List<object?>);
                var listCtor = listType.GetConstructor(Type.EmptyTypes)!;
                var listAdd = listType.GetMethod("Add")!;

                // Create the sources list
                il.Emit(OpCodes.Newobj, listCtor);

                // Add each source argument to the list
                for (int i = 1; i < arguments.Count; i++)
                {
                    il.Emit(OpCodes.Dup);  // Duplicate list reference
                    emitter.EmitExpression(arguments[i]);
                    emitter.EmitBoxIfNeeded(arguments[i]);
                    il.Emit(OpCodes.Callvirt, listAdd);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectAssign);
                return true;
            case "freeze":
                // Object.freeze(obj) - freezes the object and returns it
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectFreeze);
                return true;
            case "seal":
                // Object.seal(obj) - seals the object and returns it
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectSeal);
                return true;
            case "isFrozen":
                // Object.isFrozen(obj) - returns true if the object is frozen
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIsFrozen);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            case "isSealed":
                // Object.isSealed(obj) - returns true if the object is sealed
                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIsSealed);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            default:
                // Pop the argument we pushed and return false
                il.Emit(OpCodes.Pop);
                return false;
        }
    }

    /// <summary>
    /// Object has no static properties.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
