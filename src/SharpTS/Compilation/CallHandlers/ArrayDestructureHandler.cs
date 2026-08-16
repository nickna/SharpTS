using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles the internal <c>__arrayDestructure</c> helper that normalizes an array binding-pattern
/// source through the iterator protocol (#685). Index-addressable sources (arrays, tuples) are
/// emitted as-is — the desugared positional index access reads them directly and the type checker
/// assigns them a matching pass-through type, so no runtime work is needed (preserves the fast path
/// and tuple positional element types). Every other source — including <b>strings</b> — is routed
/// through the emitted <c>ArrayDestructureSource</c> runtime helper, which materializes non-indexable
/// iterables (generators, Set, Map, strings, <c>[Symbol.iterator]</c> objects) into an array. Strings
/// are deliberately not on the fast path so a rest element binds a fresh character array rather than
/// the trailing substring (#753); non-rest character values are identical either way.
/// </summary>
public class ArrayDestructureHandler : ICallHandler
{
    public int Priority => 16;  // Internal helper, right after ObjectRest (15)

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v || v.Name.Lexeme != BuiltInNames.ArrayDestructure)
            return false;
        if (call.Arguments.Count != 1)
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;
        var arg = call.Arguments[0];

        // Emit the source value (boxed to object).
        emitter.EmitExpression(arg);
        emitter.EmitBoxIfNeeded(arg);

        // Fast path: a statically index-addressable source needs no normalization. The type
        // checker passes these through with their precise type (see
        // TypeChecker.NormalizeArrayDestructureSourceType), so the runtime value is already an
        // array/tuple/string the index access reads directly — and tuple positional element types
        // stay intact.
        if (IsIndexAddressable(ctx.TypeMap?.Get(arg)))
            return true;

        // Otherwise normalize at runtime:
        //   ArrayDestructureSource(value, Symbol.iterator, runtimeType)
        il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIterator);
        il.Emit(OpCodes.Ldtoken, ctx.Runtime!.RuntimeType);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Type, "GetTypeFromHandle"));
        il.Emit(OpCodes.Call, ctx.Runtime!.ArrayDestructureSource);
        return true;
    }

    // Strings are intentionally excluded: they must materialize through ArrayDestructureSource so a
    // rest element collects a fresh character array, not the trailing substring (#753).
    private static bool IsIndexAddressable(TypeInfo? type) =>
        type is TypeInfo.Array or TypeInfo.Tuple;
}
