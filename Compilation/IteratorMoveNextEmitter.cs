using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Shared base for the two <em>iterator</em> state-machine MoveNext emitters —
/// <see cref="GeneratorMoveNextEmitter"/> and <see cref="AsyncGeneratorMoveNextEmitter"/>. An async
/// generator is a generator that also awaits, so the two families share more than either shares with
/// the async functions: a real-IL protected-region counter (their <c>break</c>/<c>continue</c>/
/// <c>return</c> routing keys off it rather than off <see cref="CompilationContext.ExceptionBlockDepth"/>
/// alone), the suspension-agnostic simple try/catch body, the hoisted-field-aware catch-parameter
/// binding, and (since #1124) the block-scope-rename + function-display-class variable overrides.
/// </summary>
/// <remarks>
/// This layer holds only the pieces that are byte-identical between the two iterator families. The
/// async functions (<see cref="AsyncFunctionMoveNextEmitter"/>) deliberately keep their own simple
/// try/catch and catch-param binding: they bind the caught exception to a plain IL local rather than
/// routing through the hoisted-field-aware resolver, and drive their protected-region choice off the
/// default <see cref="StateMachineExitRoutingEmitter.ProtectedRegionDepth"/>. The suspension-specific
/// try/catch bodies, the yield/await IL, and the family-specific return/throw terminals stay in each
/// concrete emitter.
/// </remarks>
public abstract partial class IteratorMoveNextEmitter : StateMachineExitRoutingEmitter
{
    protected IteratorMoveNextEmitter(StateMachineEmitHelpers helpers)
        : base(helpers)
    {
    }

    protected override int ProtectedRegionDepth => _protectedRegionDepth;

    // Depth of real IL exception blocks (EmitSimpleTryCatch / the flag path's sync segments) open around
    // the current emission point. While > 0, a `br`/`ret` out of the region would be illegal, so exits
    // are left to the existing per-path handling instead of being routed through the finally machinery.
    protected int _protectedRegionDepth;

    /// <summary>
    /// The simple (no-suspension) try/catch/finally lowering, used when neither the try, catch, nor
    /// finally body contains a yield/await. A real IL protected region is open across the whole
    /// construct, so a routed <c>br</c>/<c>ret</c> out of it would be illegal; the exit overrides fall
    /// back to their per-path handling while <see cref="_protectedRegionDepth"/> is raised, and
    /// <see cref="CompilationContext.ExceptionBlockDepth"/> (raised in lock-step here, but never in the
    /// flag path's sync segments) drives the Leave-vs-Br choice so an internal branch inside a segment
    /// stays a legal <c>Br</c>.
    /// </summary>
    protected void EmitSimpleTryCatch(Stmt.TryCatch t)
    {
        _protectedRegionDepth++;
        Ctx.ExceptionBlockDepth++;
        IL.BeginExceptionBlock();

        foreach (var stmt in t.TryBlock)
            EmitStatement(stmt);

        if (t.CatchBlock != null)
        {
            IL.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                // Stack has the .NET exception; wrap to the TS value and bind to the catch param,
                // honouring a hoisted field if the param is read across a yield/await (#569).
                IL.Emit(OpCodes.Call, Ctx.Runtime!.WrapException);
                StoreCaughtExceptionToParam(t.CatchParam.Lexeme);
            }
            else
            {
                IL.Emit(OpCodes.Pop);
            }

            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }

        if (t.FinallyBlock != null)
        {
            IL.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
                EmitStatement(stmt);
        }

        IL.EndExceptionBlock();
        Ctx.ExceptionBlockDepth--;
        _protectedRegionDepth--;
    }

    /// <summary>
    /// Emits the resume dispatch at MoveNext entry: switch on the state field to each suspension
    /// point's resume label; state -1 (initial execution) falls through the switch. No-op when the
    /// body has no suspension points. The sync generator passes its yield-point count, the async
    /// generator its combined yield+await count — the IL is otherwise identical.
    /// </summary>
    protected void EmitStateSwitch(FieldBuilder stateField, int suspensionPointCount, IReadOnlyDictionary<int, Label> stateLabels)
    {
        if (suspensionPointCount == 0) return;

        // Load state field
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, stateField);

        // Create labels array for switch
        var labels = new Label[suspensionPointCount];
        for (int i = 0; i < suspensionPointCount; i++)
        {
            labels[i] = stateLabels[i];
        }

        // switch (state) { case 0: goto State0; case 1: goto State1; ... }
        IL.Emit(OpCodes.Switch, labels);

        // Fall through for state -1 (initial execution)
    }

    /// <summary>
    /// Materializes an emitted typed array or Buffer into the runtime's standalone-safe
    /// <c>List&lt;object&gt;</c> representation before a <c>yield*</c> setup casts its operand to
    /// <see cref="System.Collections.IEnumerable"/>. The emitted <c>$TypedArray</c> and
    /// <c>$Buffer</c> types deliberately do not implement that interface.
    /// </summary>
    protected void NormalizeYieldStarTypedArrayOrBuffer(LocalBuilder iterableLocal)
    {
        var runtime = Ctx.Runtime!;
        var typedArrayType = runtime.TypedArrayBaseType;
        var bufferType = runtime.TSBufferType;
        if (typedArrayType is null && bufferType is null)
            return;

        var materializeLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        if (typedArrayType is not null)
        {
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Isinst, typedArrayType);
            IL.Emit(OpCodes.Brtrue, materializeLabel);
        }

        if (bufferType is not null)
        {
            IL.Emit(OpCodes.Ldloc, iterableLocal);
            IL.Emit(OpCodes.Isinst, bufferType);
            IL.Emit(OpCodes.Brtrue, materializeLabel);
        }

        IL.Emit(OpCodes.Br, doneLabel);

        IL.MarkLabel(materializeLabel);
        IL.Emit(OpCodes.Ldloc, iterableLocal);
        IL.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        IL.Emit(OpCodes.Ldtoken, runtime.RuntimeType);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, runtime.IterateToList);
        IL.Emit(OpCodes.Stloc, iterableLocal);

        IL.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Binds the caught exception value (on the IL stack) to the catch parameter, honouring whether the
    /// parameter was hoisted to a state-machine field (because it is read across a yield/await in the
    /// catch body) or lives in an IL local. Storing to a fresh local unconditionally — the original
    /// behaviour — lost the value whenever the catch parameter was hoisted, because reads resolve the
    /// field first (#569).
    /// </summary>
    protected void StoreCaughtExceptionToParam(string name)
    {
        if (GetHoistedVariableField(name) == null)
        {
            // Not hoisted: register a local so the catch body's reads resolve to it.
            var exLocal = IL.DeclareLocal(typeof(object));
            Ctx.Locals.RegisterLocal(name, exLocal);
        }

        // Resolver stores to the hoisted field if present, otherwise the registered local.
        Resolver.TryStoreVariable(name);
    }
}
