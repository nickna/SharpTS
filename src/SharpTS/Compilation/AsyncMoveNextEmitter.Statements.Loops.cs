using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    // EmitWhile: inherited from StatementEmitterBase (identical logic)

    protected override void EmitForOf(Stmt.ForOf f)
    {
        if (f.IsAsync)
        {
            // The shared async-function lowering suspends for iterator next()/return().
            EmitForAwaitOf(f);
            return;
        }

        // Sync for...of: delegate to base (uses DeclareLoopVariable/EmitStoreLoopVariable
        // overrides in this class to handle hoisted state machine fields)
        base.EmitForOf(f);
    }

    // EmitDoWhile: inherited from StatementEmitterBase (identical logic)
    // EmitForIn: inherited from StatementEmitterBase (uses DeclareLoopVariable/EmitStoreLoopVariable
    // overrides in this class to handle hoisted state machine fields)
}
