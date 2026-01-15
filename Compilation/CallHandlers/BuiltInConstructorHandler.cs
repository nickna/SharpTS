using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles built-in constructor-like function calls: Symbol(), BigInt(), Date().
/// Note: Date() without 'new' returns current date as string.
/// </summary>
public class BuiltInConstructorHandler : ICallHandler
{
    public int Priority => 60;

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v)
            return false;

        return v.Name.Lexeme switch
        {
            "Symbol" => EmitSymbol(emitter, call),
            "BigInt" => EmitBigInt(emitter, call),
            "Date" => EmitDate(emitter, call),
            _ => false
        };
    }

    private static bool EmitSymbol(ILEmitter emitter, Expr.Call call)
    {
        var il = emitter.ILGen;
        var ctx = emitter.Context;

        if (call.Arguments.Count == 0)
        {
            // Symbol() with no description
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            // Symbol(description) - emit the description argument
            emitter.EmitExpression(call.Arguments[0]);
            // Convert to string if needed
            il.Emit(OpCodes.Call, ctx.Runtime!.Stringify);
        }
        // Create new $TSSymbol instance
        il.Emit(OpCodes.Newobj, ctx.Runtime!.TSSymbolCtor);
        return true;
    }

    private static bool EmitBigInt(ILEmitter emitter, Expr.Call call)
    {
        var il = emitter.ILGen;
        var ctx = emitter.Context;

        if (call.Arguments.Count != 1)
            throw new Exception("BigInt() requires exactly one argument.");

        emitter.EmitExpression(call.Arguments[0]);
        emitter.EmitBoxIfNeeded(call.Arguments[0]);
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateBigInt);
        emitter.ResetStackType();
        return true;
    }

    private static bool EmitDate(ILEmitter emitter, Expr.Call call)
    {
        var il = emitter.ILGen;
        var ctx = emitter.Context;

        // Date() without 'new' returns current date as string
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateDateNoArgs);
        il.Emit(OpCodes.Call, ctx.Runtime!.DateToString);
        return true;
    }
}
