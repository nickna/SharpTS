using System.Collections;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class AsyncFunctionMoveNextEmitter
{
    private int _syncEnumeratorCount;
    private int _forInIterationCount;

    protected override Action StoreForOfEnumerator(Stmt.ForOf loop)
    {
        if (!StmtContainsSuspension(loop.Body)) return base.StoreForOfEnumerator(loop);
        var field = DefineStateMachineField($"<>7__syncEnumerator{_syncEnumeratorCount++}", typeof(IEnumerator));
        var temporary = IL.DeclareLocal(typeof(IEnumerator));
        IL.Emit(OpCodes.Stloc, temporary);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, temporary);
        IL.Emit(OpCodes.Stfld, field);
        return () =>
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, field);
        };
    }

    protected override ForInIterationState StoreForInIterationState(Stmt.ForIn loop)
    {
        if (!StmtContainsSuspension(loop.Body)) return base.StoreForInIterationState(loop);

        int id = _forInIterationCount++;
        var receiverField = DefineStateMachineField($"<>7__forInReceiver{id}", Types.Object);
        var keysField = DefineStateMachineField($"<>7__forInKeys{id}", Types.ListOfObject);
        var indexField = DefineStateMachineField($"<>7__forInIndex{id}", Types.Int32);
        var receiver = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, receiver);

        // Loop entry runs again for an enclosing backedge, but never on await resumption.
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Stfld, receiverField);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectKeys.Keys);
        IL.Emit(OpCodes.Stfld, keysField);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stfld, indexField);

        var nextIndex = IL.DeclareLocal(Types.Int32);
        return new ForInIterationState(
            () => { IL.Emit(OpCodes.Ldarg_0); IL.Emit(OpCodes.Ldfld, receiverField); },
            () => { IL.Emit(OpCodes.Ldarg_0); IL.Emit(OpCodes.Ldfld, keysField); },
            () => { IL.Emit(OpCodes.Ldarg_0); IL.Emit(OpCodes.Ldfld, indexField); },
            () =>
            {
                IL.Emit(OpCodes.Stloc, nextIndex);
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldloc, nextIndex);
                IL.Emit(OpCodes.Stfld, indexField);
            });
    }
}
