using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Atomics static method calls.
/// Handles Atomics.load(), Atomics.store(), Atomics.add(), etc.
/// </summary>
public sealed class AtomicsStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for an Atomics static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return methodName switch
        {
            "load" => EmitLoad(emitter, arguments),
            "store" => EmitStore(emitter, arguments),
            "add" => EmitAdd(emitter, arguments),
            "sub" => EmitSub(emitter, arguments),
            "and" => EmitAnd(emitter, arguments),
            "or" => EmitOr(emitter, arguments),
            "xor" => EmitXor(emitter, arguments),
            "exchange" => EmitExchange(emitter, arguments),
            "compareExchange" => EmitCompareExchange(emitter, arguments),
            "wait" => EmitWait(emitter, arguments),
            "notify" => EmitNotify(emitter, arguments),
            "isLockFree" => EmitIsLockFree(emitter, arguments),
            "pause" => EmitPause(emitter, arguments),
            _ => false
        };
    }

    /// <summary>
    /// Attempts to emit IL for an Atomics static property get.
    /// Atomics has no properties, so this always returns false.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "pause")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Ldtoken, ctx.Runtime!.RequireAtomics().Pause);
        il.Emit(OpCodes.Call, ctx.Types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, ctx.Types.MethodInfo);
        il.Emit(OpCodes.Ldstr, "pause");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, ctx.Runtime.FunctionConstruction.GetOrCreate);
        return true;
    }

    private static bool EmitLoad(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 2) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit typedArray and index
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Load);
        return true;
    }

    private static bool EmitStore(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit typedArray, index, value
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Store);
        return true;
    }

    private static bool EmitAdd(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        if (ctx.TypeMap?.Get(arguments[0]) is TypeInfo.TypedArray
            { ElementType: "Int32" or "Uint32" } typedArray)
        {
            emitter.EmitExpression(arguments[0]);
            il.Emit(OpCodes.Castclass, ctx.Runtime!.TypedArrays.RequireImplementation().BaseType);
            emitter.EmitExpressionAsDouble(arguments[1]);
            il.Emit(OpCodes.Conv_I4);
            emitter.EmitExpressionAsDouble(arguments[2]);
            il.Emit(typedArray.ElementType == "Uint32" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, ctx.Runtime.RequireAtomics().AddInt32);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Add);
        return true;
    }

    private static bool EmitSub(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Sub);
        return true;
    }

    private static bool EmitAnd(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().And);
        return true;
    }

    private static bool EmitOr(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Or);
        return true;
    }

    private static bool EmitXor(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Xor);
        return true;
    }

    private static bool EmitExchange(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Exchange);
        return true;
    }

    private static bool EmitCompareExchange(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 4) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);
        emitter.EmitExpression(arguments[3]);
        emitter.EmitBoxIfNeeded(arguments[3]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().CompareExchange);
        return true;
    }

    private static bool EmitWait(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 3) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        // Optional timeout parameter
        if (arguments.Count > 3)
        {
            emitter.EmitExpression(arguments[3]);
            emitter.EmitBoxIfNeeded(arguments[3]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Wait);
        return true;
    }

    private static bool EmitNotify(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 2) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        emitter.EmitExpressionAsDouble(arguments[1]);

        // Optional count parameter
        if (arguments.Count > 2)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Notify);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitIsLockFree(IEmitterContext emitter, List<Expr> arguments)
    {
        if (arguments.Count < 1) return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpressionAsDouble(arguments[0]);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().IsLockFree);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitPause(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
        }
        il.Emit(OpCodes.Call, ctx.Runtime!.RequireAtomics().Pause);
        return true;
    }
}
