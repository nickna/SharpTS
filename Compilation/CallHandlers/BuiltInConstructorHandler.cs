using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
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

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v)
            return false;

        return v.Name.Lexeme switch
        {
            "Symbol" => EmitSymbol(emitter, call),
            "BigInt" => EmitBigInt(emitter, call),
            "Date" => EmitDate(emitter, call),
            "Array" => EmitArray(emitter, call),
            "RegExp" => EmitRegExp(emitter, call),
            "Error" or "TypeError" or "RangeError" or "ReferenceError" or
            "SyntaxError" or "URIError" or "EvalError" or "AggregateError" =>
                EmitError(emitter, call, v.Name.Lexeme),
            _ => false
        };
    }

    private static bool EmitSymbol(IEmitterContext emitter, Expr.Call call)
    {
        var il = emitter.IL;
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
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            var description = il.DeclareLocal(ctx.Types.Object);
            var stringify = il.DefineLabel();
            var ready = il.DefineLabel();
            il.Emit(OpCodes.Stloc, description);
            il.Emit(OpCodes.Ldloc, description);
            il.Emit(OpCodes.Isinst, ctx.Runtime!.UndefinedType);
            il.Emit(OpCodes.Brfalse, stringify);
            // Explicit undefined is the same as an omitted description.
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Br, ready);
            il.MarkLabel(stringify);
            il.Emit(OpCodes.Ldloc, description);
            il.Emit(OpCodes.Call, ctx.Runtime!.ToJsString);
            il.MarkLabel(ready);
        }
        // Create new $TSSymbol instance
        il.Emit(OpCodes.Newobj, ctx.Runtime!.TSSymbolCtor);
        return true;
    }

    private static bool EmitBigInt(IEmitterContext emitter, Expr.Call call)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        if (call.Arguments.Count != 1)
            throw new CompileException("BigInt() requires exactly one argument.");

        emitter.EmitExpression(call.Arguments[0]);
        emitter.EmitBoxIfNeeded(call.Arguments[0]);
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateBigInt);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitDate(IEmitterContext emitter, Expr.Call call)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        // Date() without 'new' returns current date as string
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateDateNoArgs);
        il.Emit(OpCodes.Call, ctx.Runtime!.DateToString);
        return true;
    }

    /// <summary>
    /// Emits <c>Array(…)</c> called without <c>new</c> (issue #61). Per
    /// ECMAScript §23.1.1 the call form is identical to the construct form:
    /// <c>Array(3)</c> === <c>new Array(3)</c>. Route through the same
    /// <c>$Runtime.ArrayConstructor</c> helper as the <c>new</c> path.
    /// </summary>
    private static bool EmitArray(IEmitterContext emitter, Expr.Call call)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;

        il.Emit(OpCodes.Ldc_I4, call.Arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(call.Arguments[i]);
            emitter.EmitBoxIfNeeded(call.Arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.ArrayConstructor);
        emitter.SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits <c>RegExp(…)</c> called without <c>new</c>. Per ECMA-262 §22.2.4.1
    /// the call form is (for these purposes) identical to the construct form:
    /// <c>RegExp("a","g")</c> produces a RegExp object. Route through the same
    /// <c>RegExpFromArgs</c> helper as <c>new RegExp</c> (it handles the
    /// pattern-is-RegExp and undefined-argument coercions). Without this the
    /// call falls through to the generic value-call path and returns null, so a
    /// RegExp is never produced (e.g. test262 S15.10.7_A1_T2).
    /// </summary>
    private static bool EmitRegExp(IEmitterContext emitter, Expr.Call call)
    {
        var il = emitter.IL;
        var ctx = emitter.Context;
        var runtime = ctx.Runtime;
        if (runtime?.RegExpFromArgs == null)
            return false;

        var patternLocal = il.DeclareLocal(ctx.Types.Object);
        var flagsLocal = il.DeclareLocal(ctx.Types.Object);

        // pattern → patternLocal (null when no arg)
        if (call.Arguments.Count >= 1)
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Stloc, patternLocal);
        // flags → flagsLocal (null when no arg → treated as undefined)
        if (call.Arguments.Count >= 2)
        {
            emitter.EmitExpression(call.Arguments[1]);
            emitter.EmitBoxIfNeeded(call.Arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Stloc, flagsLocal);

        // ECMA-262 §22.2.4.1 step 1: the RegExp *call* form returns the SAME
        // object when flags is undefined AND IsRegExp(pattern) (pattern[Symbol.match]
        // truthy) AND pattern.constructor is %RegExp%. `new RegExp(re)` copies
        // (separate path). test262 S15.10.3.1 + from-regexp-like-short-circuit;
        // the brand checks keep call_with_regexp_{not_same_constructor,match_falsy}
        // copying as required.
        var doFromArgs = il.DefineLabel();
        var done = il.DefineLabel();
        if (runtime.TSRegExpType != null)
        {
            var flagsOk = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, flagsLocal);
            il.Emit(OpCodes.Brfalse, flagsOk);                       // null flags → ok
            il.Emit(OpCodes.Ldloc, flagsLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brfalse, doFromArgs);                    // defined flags → copy
            il.MarkLabel(flagsOk);
            // pattern must be a non-null, non-string object
            il.Emit(OpCodes.Ldloc, patternLocal);
            il.Emit(OpCodes.Brfalse, doFromArgs);
            il.Emit(OpCodes.Ldloc, patternLocal);
            il.Emit(OpCodes.Isinst, ctx.Types.String);
            il.Emit(OpCodes.Brtrue, doFromArgs);
            // IsRegExp: pattern[Symbol.match] truthy
            il.Emit(OpCodes.Ldloc, patternLocal);
            il.Emit(OpCodes.Ldsfld, runtime.SymbolMatch);
            il.Emit(OpCodes.Call, runtime.GetIndex);
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brfalse, doFromArgs);
            // pattern.constructor === %RegExp% (the $RegExp Type token)
            il.Emit(OpCodes.Ldloc, patternLocal);
            il.Emit(OpCodes.Ldstr, "constructor");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ldtoken, runtime.TSRegExpType);
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle", ctx.Types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, doFromArgs);                     // constructor !== RegExp → copy
            // short-circuit: return pattern unchanged
            il.Emit(OpCodes.Ldloc, patternLocal);
            il.Emit(OpCodes.Br, done);
        }
        il.MarkLabel(doFromArgs);
        il.Emit(OpCodes.Ldloc, patternLocal);
        il.Emit(OpCodes.Ldloc, flagsLocal);
        il.Emit(OpCodes.Call, runtime.RegExpFromArgs);
        il.MarkLabel(done);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitError(IEmitterContext emitter, Expr.Call call, string errorTypeName)
    {
        var il = emitter.IL;
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
