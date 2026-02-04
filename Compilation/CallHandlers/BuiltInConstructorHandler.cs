using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles built-in constructor-like function calls: Symbol(), BigInt(), Date(), Error(), etc.
/// Note: Date() without 'new' returns current date as string.
/// Note: Error() without 'new' still creates an error object (same as with 'new').
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
            "Error" or "TypeError" or "RangeError" or "ReferenceError" or
            "SyntaxError" or "URIError" or "EvalError" or "AggregateError" =>
                EmitError(emitter, call, v.Name.Lexeme),
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
            throw new CompileException("BigInt() requires exactly one argument.");

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

    private static bool EmitError(ILEmitter emitter, Expr.Call call, string errorTypeName)
    {
        var il = emitter.ILGen;
        var ctx = emitter.Context;

        // Error() and subtypes called without 'new' still create error objects
        // Push the error type name
        il.Emit(OpCodes.Ldstr, errorTypeName);

        // Create arguments list
        il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(call.Arguments[i]);
            emitter.EmitBoxIfNeeded(call.Arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        // Call runtime CreateError(errorTypeName, args)
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateError);
        return true;
    }
}
