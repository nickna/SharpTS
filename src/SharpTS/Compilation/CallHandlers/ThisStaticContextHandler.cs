using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles this.method() in static context (static blocks, static methods).
/// In static context, 'this' refers to the class constructor, so this.method() calls static methods.
/// </summary>
public class ThisStaticContextHandler : ICallHandler
{
    public int Priority => 76;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Get thisGet ||
            thisGet.Object is not Expr.This)
            return false;

        var ctx = emitter.Context;

        // Only applies in static context
        if (ctx.IsInstanceMethod || ctx.CurrentClassBuilder == null)
            return false;

        string? currentClassName = ctx.CurrentClassName;
        if (currentClassName == null)
            return false;

        if (!ctx.ClassRegistry!.TryGetCallableStaticMethod(currentClassName, thisGet.Name.Lexeme, ctx.CurrentClassBuilder, out var thisStaticMethod))
            return false;

        var il = emitter.IL;
        var methodParams = thisStaticMethod!.GetParameters();
        emitter.EmitStaticCallArguments(call.Arguments, methodParams);
        il.Emit(OpCodes.Call, thisStaticMethod);
        emitter.SetStackUnknown();
        return true;
    }
}
