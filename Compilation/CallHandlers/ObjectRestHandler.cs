using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles the internal __objectRest helper function for object rest patterns.
/// </summary>
public class ObjectRestHandler : ICallHandler
{
    public int Priority => 15;  // High priority internal function

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable restVar || restVar.Name.Lexeme != "__objectRest")
            return false;

        if (call.Arguments.Count < 2)
            return false;

        var il = emitter.ILGen;
        var ctx = emitter.Context;

        // Emit source object (now accepts object to support both dictionaries and class instances)
        emitter.EmitExpression(call.Arguments[0]);
        emitter.EmitBoxIfNeeded(call.Arguments[0]);

        // Emit exclude keys (List<object>)
        emitter.EmitExpression(call.Arguments[1]);
        emitter.EmitBoxIfNeeded(call.Arguments[1]);
        il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);

        il.Emit(OpCodes.Call, ctx.Runtime!.ObjectRest);
        return true;
    }
}
