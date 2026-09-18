using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    // Statement dispatch is now inherited from StatementEmitterBase

    protected override void EmitForOf(Stmt.ForOf f)
    {
        if (f.IsAsync)
        {
            // for await...of must drive the async-iterator protocol, not a synchronous
            // IEnumerable enumeration. Without this override the loop fell through to the
            // sync for-of path in StatementEmitterBase and threw InvalidCastException
            // casting the async-generator state machine to IEnumerable (#430/#645).
            EmitForAwaitOf(f);
            return;
        }

        // Sync for...of: delegate to base (uses DeclareLoopVariable/EmitStoreLoopVariable
        // overrides to handle hoisted state machine fields)
        base.EmitForOf(f);
    }

    // Falling off the end of a block-bodied async arrow completes with `undefined`, not null
    // (#587). Explicit returns are handled by EmitReturn; this is only the implicit-completion
    // fall-through emitted after the body.
    private void EmitImplicitReturnUndefined()
    {
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        EmitSetResult();
        _il.Emit(OpCodes.Leave, _exitLabel);
    }

    private void EmitSetResult()
    {
        // Store result
        _il.Emit(OpCodes.Stloc, _resultLocal!);

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetResult(result)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _resultLocal!);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetResultMethod());
    }

    private void EmitCatchBlock()
    {
        // Store exception
        _il.Emit(OpCodes.Stloc, _exceptionLocal!);

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetException(exception)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _exceptionLocal!);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetExceptionMethod());
    }
}
