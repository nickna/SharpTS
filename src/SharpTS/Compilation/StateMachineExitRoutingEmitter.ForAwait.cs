using System.Reflection.Emit;

namespace SharpTS.Compilation;

public abstract partial class StateMachineExitRoutingEmitter
{
    /// <summary>Resolves the custom async iterator or adapts the synchronous iterable.</summary>
    protected void EmitResolveAsyncIterator(LocalBuilder iterableLocal, FieldBuilder iteratorField, FieldBuilder isCustomField, FieldBuilder nextField)
    {
        EmitTryResolveCustomAsyncIterator(iterableLocal, iteratorField, isCustomField, nextField);
        var afterSetup = IL.DefineLabel();
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, isCustomField);
        IL.Emit(OpCodes.Brtrue, afterSetup);

        // No async iterator: adapt any synchronous iterable per
        // CreateAsyncFromSyncIterator semantics (#1287).
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.RequireAsyncGenerators().RequireFromSync().Adapt);
        IL.Emit(OpCodes.Stfld, iteratorField);
        IL.MarkLabel(afterSetup);
    }

    protected void EmitTryResolveCustomAsyncIterator(
        LocalBuilder iterableLocal, FieldBuilder iteratorField, FieldBuilder isCustomField, FieldBuilder nextField)
    {
        var runtime = Ctx.Runtime!;
        var asyncIterFnLocal = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldsfld, runtime.Symbols.AsyncIterator);
        IL.Emit(OpCodes.Call, runtime.IteratorProtocol.Function);
        IL.Emit(OpCodes.Stloc, asyncIterFnLocal);

        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stfld, isCustomField);

        var afterSetup = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, asyncIterFnLocal);
        IL.Emit(OpCodes.Brfalse, afterSetup);
        IL.Emit(OpCodes.Ldloc, asyncIterFnLocal);
        IL.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        IL.Emit(OpCodes.Brtrue, afterSetup);

        var iteratorTemp = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldloc, asyncIterFnLocal);
        IL.Emit(OpCodes.Call, runtime.Invocation.Method0);
        IL.Emit(OpCodes.Stloc, iteratorTemp);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, iteratorTemp);
        IL.Emit(OpCodes.Call, runtime.IteratorRecords.NextMethod);
        IL.Emit(OpCodes.Stfld, nextField);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, iteratorTemp);
        IL.Emit(OpCodes.Stfld, iteratorField);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stfld, isCustomField);

        IL.MarkLabel(afterSetup);
    }

    /// <summary>
    /// Stores into <paramref name="resultLocal"/> the result of one async-iterator protocol call —
    /// <c>iterator.next()</c> when <paramref name="isReturn"/> is false, else <c>iterator.return()</c> —
    /// selecting the call by the protocol kind in <paramref name="isCustomField"/>. The value (a
    /// Task&lt;object&gt;, $Promise, or plain value) is later coerced and awaited by
    /// the caller. A custom iterator with no <c>return</c> method stores
    /// <c>undefined</c> (awaited as an already-resolved value), so the reserved return-await state is
    /// still consumed and its resume label stays marked.
    /// </summary>
    protected void EmitProduceAsyncStep(LocalBuilder resultLocal, FieldBuilder iteratorField, FieldBuilder isCustomField, FieldBuilder nextField, bool isReturn)
    {
        var customLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, isCustomField);
        IL.Emit(OpCodes.Brtrue, customLabel);

        // ----- $IAsyncGenerator path: invoke the interface method (returns Task<object>) -----
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, iteratorField);
        IL.Emit(OpCodes.Castclass, Ctx.Runtime!.RequireAsyncGenerators().Type);
        if (isReturn)
        {
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime.RequireAsyncGenerators().Return);
        }
        else
        {
            // for-await-of never sends a value; pass undefined so the yield expression sees undefined
            // (not null) if the consumer somehow observes it (#473).
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime.Sentinels.UndefinedInstance);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime.RequireAsyncGenerators().Next);
        }
        IL.Emit(OpCodes.Stloc, resultLocal);
        IL.Emit(OpCodes.Br, doneLabel);

        // ----- custom Symbol.asyncIterator path -----
        IL.MarkLabel(customLabel);
        if (isReturn)
        {
            // fn = GetProperty(iterator, "return"); absent/undefined → no-op close (push undefined).
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, iteratorField);
            IL.Emit(OpCodes.Ldstr, "return");
            IL.Emit(OpCodes.Call, Ctx.Runtime.ObjectRead.Property);
            var fnLocal = IL.DeclareLocal(Types.Object);
            IL.Emit(OpCodes.Stloc, fnLocal);

            var noFnLabel = IL.DefineLabel();
            var haveFnLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, fnLocal);
            IL.Emit(OpCodes.Brfalse, noFnLabel);
            IL.Emit(OpCodes.Ldloc, fnLocal);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime.Sentinels.UndefinedType);
            IL.Emit(OpCodes.Brtrue, noFnLabel);

            // InvokeMethodValue(iterator, fn, [])
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, iteratorField);
            IL.Emit(OpCodes.Ldloc, fnLocal);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, Types.Object);
            IL.Emit(OpCodes.Call, Ctx.Runtime.Invocation.Method);
            IL.Emit(OpCodes.Stloc, resultLocal);
            IL.Emit(OpCodes.Br, haveFnLabel);

            IL.MarkLabel(noFnLabel);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime.Sentinels.UndefinedInstance);
            IL.Emit(OpCodes.Stloc, resultLocal);
            IL.MarkLabel(haveFnLabel);
        }
        else
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, iteratorField);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, nextField);
            IL.Emit(OpCodes.Call, Ctx.Runtime.Invocation.Method0);
            IL.Emit(OpCodes.Stloc, resultLocal);
        }

        IL.MarkLabel(doneLabel);
    }
}
