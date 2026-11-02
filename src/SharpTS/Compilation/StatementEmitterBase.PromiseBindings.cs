using System.Reflection.Emit;

namespace SharpTS.Compilation;

public abstract partial class StatementEmitterBase
{
    // Observe actual boxed promise values without awaiting or consuming the value.
    // Primitive locals retain their original stack representation and hot path.
    protected void EmitObservePublishedPromiseValue()
    {
        if (Ctx.Runtime?.Promise is not { } promise || StackType != StackType.Unknown)
            return;
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Call, promise.ShouldAutoAwaitPromiseMethod);
        IL.Emit(OpCodes.Brfalse, done);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Call, promise.ObserveDiscardedPromiseResult);
        IL.MarkLabel(done);
    }
}
