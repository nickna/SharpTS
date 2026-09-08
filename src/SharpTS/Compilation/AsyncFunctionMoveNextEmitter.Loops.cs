using System.Collections;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class AsyncFunctionMoveNextEmitter
{
    private int _syncEnumeratorCount;

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
}
