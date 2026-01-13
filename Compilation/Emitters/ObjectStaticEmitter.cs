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
