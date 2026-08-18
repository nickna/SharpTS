using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles global built-in functions: parseInt, parseFloat, isNaN, isFinite, String, Number, Boolean,
/// encodeURIComponent, decodeURIComponent.
/// </summary>
public class GlobalFunctionHandler : ICallHandler
{
    public int Priority => 50;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v)
            return false;

        var il = emitter.IL;
        var ctx = emitter.Context;

        return v.Name.Lexeme switch
        {
            "eval" => EmitEval(emitter, il, ctx, call),
            "parseInt" => EmitParseInt(emitter, il, ctx, call),
            "parseFloat" => EmitParseFloat(emitter, il, ctx, call),
            "isNaN" => EmitIsNaN(emitter, il, ctx, call),
            "isFinite" => EmitIsFinite(emitter, il, ctx, call),
            "String" => EmitStringConversion(emitter, il, ctx, call),
            "Number" => EmitNumberConversion(emitter, il, ctx, call),
            "Boolean" => EmitBooleanConversion(emitter, il, ctx, call),
            "encodeURIComponent" => EmitEncodeURIComponent(emitter, il, ctx, call),
            "decodeURIComponent" => EmitDecodeURIComponent(emitter, il, ctx, call),
            "atob" => EmitBase64Global(emitter, il, ctx, call, ctx.Runtime!.BufferAtob),
            "btoa" => EmitBase64Global(emitter, il, ctx, call, ctx.Runtime!.BufferBtoa),
            _ => false
        };
    }

    /// <summary>
    /// Emits a compiled global <c>atob(x)</c>/<c>btoa(x)</c> by routing to the pure-BCL
    /// $Runtime.BufferAtob/BufferBtoa helper (the same one the buffer module export uses).
    /// </summary>
    private static bool EmitBase64Global(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il,
        CompilationContext ctx, Expr.Call call, System.Reflection.Emit.MethodBuilder target)
    {
        if (call.Arguments.Count == 0)
        {
            il.Emit(System.Reflection.Emit.OpCodes.Ldnull);
        }
        else
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
        }
        il.Emit(System.Reflection.Emit.OpCodes.Call, target);
        emitter.SetStackType(StackType.String);
        return true;
    }

    /// <summary>
    /// Emits a compiled <c>eval(arg)</c>. Compiled output has no live interpreter/scope, so this
    /// reflectively invokes <c>SharpTS.Execution.EvalBridge.Eval(object)</c> (indirect, global-scope
    /// eval) only when the SharpTS runtime is present, degrading to a deterministic throw otherwise.
    /// The reflection pattern keeps the output DLL free of a hard SharpTS.dll reference.
    /// </summary>
    private static bool EmitEval(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        // ECMA-262 PerformEval: an omitted source argument evaluates to
        // undefined. Handle it locally so the compiled boundary does not turn
        // the absence into CLR null (and so no interpreter bridge is needed).
        if (call.Arguments.Count == 0)
        {
            il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
            emitter.SetStackUnknown();
            return true;
        }

        // Evaluate every argument before performing eval. Only arg0 supplies source,
        // but ordinary ArgumentListEvaluation still observes side effects from extras.
        var argLocal = il.DeclareLocal(ctx.Types.Object);
        emitter.EmitExpression(call.Arguments[0]);
        emitter.EmitBoxIfNeeded(call.Arguments[0]);
        il.Emit(System.Reflection.Emit.OpCodes.Stloc, argLocal);
        for (int i = 1; i < call.Arguments.Count; i++)
        {
            emitter.EmitExpression(call.Arguments[i]);
            il.Emit(System.Reflection.Emit.OpCodes.Pop);
        }

        // A literal expression-only source can be lowered into the current sync
        // emitter and therefore has genuine direct-eval access to caller bindings.
        // Dynamic source and declaration-bearing programs retain the interpreter
        // bridge below because they require runtime parsing/hoisting machinery.
        if (call.Arguments[0] is Expr.Literal { Value: string source }
            && emitter is ILEmitter syncEmitter
            && syncEmitter.TryEmitStaticDirectEval(call, source))
        {
            return true;
        }

        // Dynamic eval routes through EvalBridge in the SharpTS runtime — record the
        // soft dependency so the build co-locates SharpTS.dll with the output.
        ctx.Runtime?.RequireSharpTSRuntime("eval()");

        // Type t = Type.GetType("SharpTS.Execution.EvalBridge, SharpTS");
        il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "SharpTS.Execution.EvalBridge, SharpTS");
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetType", ctx.Types.String));

        // Graceful degradation: if the SharpTS runtime isn't present, t is null — throw a clear error
        // instead of letting the subsequent virtual calls NRE.
        var present = il.DefineLabel();
        il.Emit(System.Reflection.Emit.OpCodes.Dup);
        il.Emit(System.Reflection.Emit.OpCodes.Brtrue, present);
        il.Emit(System.Reflection.Emit.OpCodes.Pop);
        il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "eval is not supported in standalone compiled output (SharpTS runtime not present).");
        il.Emit(System.Reflection.Emit.OpCodes.Newobj, ctx.Types.ExceptionCtorString);
        il.Emit(System.Reflection.Emit.OpCodes.Throw);
        il.MarkLabel(present);

        // MethodInfo m = t.GetMethod("Eval");
        il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "Eval");
        il.Emit(System.Reflection.Emit.OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.Type, "GetMethod", ctx.Types.String));

        // return (object) m.Invoke(null, new object[] { arg });
        il.Emit(System.Reflection.Emit.OpCodes.Ldnull);
        il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);
        il.Emit(System.Reflection.Emit.OpCodes.Newarr, ctx.Types.Object);
        il.Emit(System.Reflection.Emit.OpCodes.Dup);
        il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
        il.Emit(System.Reflection.Emit.OpCodes.Ldloc, argLocal);
        il.Emit(System.Reflection.Emit.OpCodes.Stelem_Ref);
        il.Emit(System.Reflection.Emit.OpCodes.Callvirt, ctx.Types.GetMethod(
            ctx.Types.MethodInfo, "Invoke", ctx.Types.Object, ctx.Types.ObjectArray));

        // eval runs in the interpreter and may return an interpreter-side boxed
        // primitive wrapper (a SharpTSObject for `eval("new Number")`), whose CLR
        // type the compiled runtime's `Isinst $Object` boxed-primitive checks don't
        // match — so `== 0` wouldn't coerce and `valueOf` wouldn't dispatch. Re-wrap
        // it into the native $Object representation at the boundary so all downstream
        // handling works uniformly. (Test262 new/S11.2.2_A1.1, A1.2.)
        if (ctx.Runtime?.NormalizeForeignEvalValueMethod != null)
            il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime.NormalizeForeignEvalValueMethod);

        // Result is an arbitrary JS value (boxed object) of statically unknown type.
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitParseInt(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count > 0) { emitter.EmitExpression(call.Arguments[0]); emitter.EmitBoxIfNeeded(call.Arguments[0]); } else { il.Emit(System.Reflection.Emit.OpCodes.Ldnull); }
        if (call.Arguments.Count > 1) { emitter.EmitExpression(call.Arguments[1]); emitter.EmitBoxIfNeeded(call.Arguments[1]); } else { il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, 10); il.Emit(System.Reflection.Emit.OpCodes.Box, ctx.Types.Int32); }
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.NumberParseInt);
        emitter.SetStackType(StackType.Double);
        return true;
    }

    private static bool EmitParseFloat(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count > 0) { emitter.EmitExpression(call.Arguments[0]); emitter.EmitBoxIfNeeded(call.Arguments[0]); } else { il.Emit(System.Reflection.Emit.OpCodes.Ldnull); }
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.NumberParseFloat);
        emitter.SetStackType(StackType.Double);
        return true;
    }

    private static bool EmitIsNaN(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count > 0) { emitter.EmitExpression(call.Arguments[0]); emitter.EmitBoxIfNeeded(call.Arguments[0]); } else { il.Emit(System.Reflection.Emit.OpCodes.Ldnull); }
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.GlobalIsNaN);
        emitter.SetStackType(StackType.Boolean);
        return true;
    }

    private static bool EmitIsFinite(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count > 0) { emitter.EmitExpression(call.Arguments[0]); emitter.EmitBoxIfNeeded(call.Arguments[0]); } else { il.Emit(System.Reflection.Emit.OpCodes.Ldnull); }
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.GlobalIsFinite);
        emitter.SetStackType(StackType.Boolean);
        return true;
    }

    private static bool EmitStringConversion(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count == 0)
        {
            // String() with no args returns ""
            il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "");
        }
        else
        {
            // StringFromValue (not Stringify): runs the spec ToString chain —
            // user toString/@@toPrimitive on objects — with the §22.1.1.1
            // Symbol exemption. Keeps this path consistent with the sync
            // emitter's String(x) handling in ILEmitter.Calls.cs.
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.StringFromValueMethod);
        }
        emitter.SetStackType(StackType.String);
        return true;
    }

    private static bool EmitNumberConversion(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count == 0)
        {
            // Number() with no args returns 0
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_R8, 0.0);
        }
        else
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.ConvertToNumber);
        }
        emitter.SetStackType(StackType.Double);
        return true;
    }

    private static bool EmitBooleanConversion(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count == 0)
        {
            // Boolean() with no args returns false
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
        }
        else
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.IsTruthy);
        }
        emitter.SetStackType(StackType.Boolean);
        return true;
    }

    private static bool EmitEncodeURIComponent(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        // JS: encodeURIComponent() throws; encodeURIComponent(undefined) returns "undefined".
        // We match the "undefined" coercion and let the runtime throw if truly missing.
        if (call.Arguments.Count == 0)
        {
            il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "undefined");
        }
        else
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.Stringify);
        }
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Types.UriEscapeDataString);
        emitter.SetStackType(StackType.String);
        return true;
    }

    private static bool EmitDecodeURIComponent(IEmitterContext emitter, System.Reflection.Emit.ILGenerator il, CompilationContext ctx, Expr.Call call)
    {
        if (call.Arguments.Count == 0)
        {
            il.Emit(System.Reflection.Emit.OpCodes.Ldstr, "undefined");
        }
        else
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Runtime!.Stringify);
        }
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctx.Types.UriUnescapeDataString);
        emitter.SetStackType(StackType.String);
        return true;
    }
}
