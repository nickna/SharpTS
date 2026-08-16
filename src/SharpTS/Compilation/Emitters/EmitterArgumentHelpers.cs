using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Shared argument-emission helpers for strategy emitters. Consolidates the
/// EmitSingleArgOrNull / EmitSecondArgOrNull / EmitBoxedArg copies that were repeated across
/// Map, Set, WeakMap, WeakSet, Iterator, Array, and Process emitters.
/// </summary>
/// <remarks>
/// The emitted default is <c>null</c> (Ldnull) — NOT the <c>$Undefined</c> sentinel. Call sites
/// whose omitted argument must observe <c>undefined</c> semantics use <c>EmitOmittedArgument</c>
/// instead; the two are not interchangeable. Helpers with other defaults (e.g. a string-coerced
/// argument defaulting to <c>""</c>) stay separately named at their call sites.
/// </remarks>
internal static class EmitterArgumentHelpers
{
    /// <summary>
    /// Emits the argument at <paramref name="index"/> boxed, or <c>null</c> when absent.
    /// When <paramref name="preEvaluated"/> is supplied (await-safe pre-spilled args, #850) the
    /// value is loaded from the local rather than re-evaluated, preserving evaluation order.
    /// </summary>
    public static void EmitBoxedArgumentOrNull(
        IEmitterContext emitter,
        List<Expr> arguments,
        int index,
        LocalBuilder[]? preEvaluated = null)
    {
        var il = emitter.Context.IL;

        if (index < arguments.Count)
        {
            if (preEvaluated != null)
            {
                il.Emit(OpCodes.Ldloc, preEvaluated[index]);
            }
            else
            {
                emitter.EmitExpression(arguments[index]);
                emitter.EmitBoxIfNeeded(arguments[index]);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }
}
