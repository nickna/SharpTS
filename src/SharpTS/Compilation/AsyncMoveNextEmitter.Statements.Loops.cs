using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    // Per-method counter giving each for await…of loop unique iterator/protocol field names.
    private int _forAwaitCounter;

    // EmitWhile: inherited from StatementEmitterBase (identical logic)

    protected override void EmitForOf(Stmt.ForOf f)
    {
        if (f.IsAsync)
        {
            // for await...of: EmitForAwaitOf is overridden below to suspend (vs the blocking shared base).
            EmitForAwaitOf(f);
            return;
        }

        // Sync for...of: delegate to base (uses DeclareLoopVariable/EmitStoreLoopVariable
        // overrides in this class to handle hoisted state machine fields)
        base.EmitForOf(f);
    }

    /// <summary>
    /// Async-function override of the shared <see cref="StatementEmitterBase.EmitForAwaitOf"/> (#430/#645).
    /// The shared base drives the async iterator with a blocking GetResult; this override instead SUSPENDS
    /// the state machine on each next()/return() await, so a genuinely-async step (e.g. a setTimeout-backed
    /// await inside the iterator) doesn't deadlock the event-loop thread (#631). The analyzer reserved one
    /// suspension state for each await (AsyncStateAnalyzer.VisitForOf), consumed below in the same order.
    /// The base lowering still serves async arrows and async generators (the latter tracked by #697).
    /// </summary>
    protected override void EmitForAwaitOf(Stmt.ForOf f) => EmitForAwaitOf(f, labelNames: null);

    /// <summary>
    /// Emits a <c>for await…of</c> loop, optionally carrying the wrapping statement labels
    /// <paramref name="labelNames"/> (a chain <c>a: b: for await</c> contributes several) so a labeled
    /// <c>break</c>/<c>continue &lt;label&gt;</c> targets it. The labeled path delegates here so it gets the
    /// same suspending async-iterator lowering as the unlabeled case, rather than enumerating the async
    /// iterator synchronously and leaving the reserved await-state labels unmarked — #728.
    /// </summary>
    private void EmitForAwaitOf(Stmt.ForOf f, IReadOnlyList<string>? labelNames)
    {
        // for await…of drives an async iterator: resolve it (Symbol.asyncIterator, else assume the
        // value is itself an async iterator / $IAsyncGenerator), then each iteration await
        // iterator.next() and, on an early `break`, await iterator.return(). Both awaits SUSPEND the
        // enclosing async function via its state machine rather than blocking on GetResult — so a
        // genuinely-async step (e.g. a setTimeout-backed await inside the iterator) no longer deadlocks
        // the event-loop thread (#631). The analyzer reserved one suspension state for each await
        // (AsyncStateAnalyzer.VisitForOf), consumed below in the same order.

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        var iterableLocal = _il.DeclareLocal(_types.Object);
        var asyncIterFnLocal = _il.DeclareLocal(_types.Object);

        // The iterator and its protocol kind must survive the per-iteration suspensions (a MoveNext
        // re-entry wipes IL locals), so store them in state-machine fields. Unique per loop so nested /
        // sequential for-await loops don't collide. The type is still open here (CreateType runs after
        // MoveNext), so defining fields now is valid.
        int loopId = _forAwaitCounter++;
        var iteratorField = _builder.StateMachineType.DefineField($"<>7__aiter{loopId}", _types.Object, FieldAttributes.Private);
        var isCustomField = _builder.StateMachineType.DefineField($"<>7__aiterCustom{loopId}", _types.Boolean, FieldAttributes.Private);

        // Synchronous prelude: evaluate the iterable and resolve the iterator + protocol kind:
        //   isCustom == true  → iterable[Symbol.asyncIterator]() ; step via InvokeIteratorNext / "return".
        //   isCustom == false → an $IAsyncGenerator, including the emitted async-from-sync adapter.
        // Evaluating the iterable, or calling its [Symbol.asyncIterator], can throw synchronously (e.g. a
        // pre-aborted signal) — guarded below so that throw reaches an enclosing try's catch.
        void EmitPrelude()
        {
            EmitExpression(f.Iterable);
            EnsureBoxed();
            _il.Emit(OpCodes.Stloc, iterableLocal);

            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolAsyncIterator);
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorFunction);
            _il.Emit(OpCodes.Stloc, asyncIterFnLocal);

            var customSetup = _il.DefineLabel();
            var adaptSyncIteratorLabel = _il.DefineLabel();
            var afterSetup = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, asyncIterFnLocal);
            _il.Emit(OpCodes.Brfalse, adaptSyncIteratorLabel);
            _il.Emit(OpCodes.Ldloc, asyncIterFnLocal);
            _il.Emit(OpCodes.Isinst, _ctx.Runtime.UndefinedType);
            _il.Emit(OpCodes.Brfalse, customSetup);

            // No async iterator: adapt any synchronous iterable per
            // CreateAsyncFromSyncIterator semantics (#1287).
            _il.MarkLabel(adaptSyncIteratorLabel);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Call, _ctx.Runtime.AdaptSyncIterableToAsyncGenerator);
            _il.Emit(OpCodes.Stfld, iteratorField);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stfld, isCustomField);
            _il.Emit(OpCodes.Br, afterSetup);

            // iterator = iterable[Symbol.asyncIterator](); isCustom = true
            _il.MarkLabel(customSetup);
            var iteratorTemp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Ldloc, iterableLocal);
            _il.Emit(OpCodes.Ldloc, asyncIterFnLocal);
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Newarr, _types.Object);
            _il.Emit(OpCodes.Call, _ctx.Runtime.InvokeMethodValue);
            _il.Emit(OpCodes.Stloc, iteratorTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, iteratorTemp);
            _il.Emit(OpCodes.Stfld, iteratorField);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stfld, isCustomField);

            _il.MarkLabel(afterSetup);
        }

        // The prelude is await-free unless the iterable expression itself awaits; only then can it not
        // sit in a real IL try (its resume label would be branched into) — in that case the iterable's
        // own await handling routes rejections, and the (rare) synchronous resolution throw is unguarded.
        if (ExprContainsSuspension(f.Iterable))
            EmitPrelude();
        else
            EmitGuardedSyncSegment(EmitPrelude);

        int nextState = _currentAwaitState++;   // iterator.next() await (reused each iteration)

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var cleanupLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // break → cleanup (await iterator.return()); natural done → endLabel (no return() per spec).
        // The chain's labels (when present) let `break`/`continue <label>` resolve to this loop; each is
        // registered as its own scope at the same targets (the #704 multi-label convention).
        bool labeled = labelNames is { Count: > 0 };
        if (labeled)
            EnterLabeledLoop(cleanupLabel, continueLabel, labelNames!);
        else
            EnterLoop(cleanupLabel, continueLabel);

        _il.MarkLabel(startLabel);

        // result = await iterator.next()
        var stepLocal = _il.DeclareLocal(_types.Object);
        EmitAsyncStep(stepLocal, iteratorField, isCustomField, isReturn: false);
        _il.Emit(OpCodes.Ldloc, stepLocal);
        SetStackUnknown();
        EmitAwaitFromValueOnStack(nextState);
        var resultLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, resultLocal);

        // if (result.done) exit without calling return()
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIteratorDone);
        _il.Emit(OpCodes.Brtrue, endLabel);

        // loopVar = result.value
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
        if (varField != null)
        {
            var valueTemp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, valueTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, valueTemp);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(_types.Object);
            _ctx.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitForAwaitBody(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        // Cleanup on break: await iterator.return() (runs the iterator's finally blocks), discard result.
        int returnState = _currentAwaitState++;   // allocated after the body, matching the analyzer
        _il.MarkLabel(cleanupLabel);
        var returnStepLocal = _il.DeclareLocal(_types.Object);
        EmitAsyncStep(returnStepLocal, iteratorField, isCustomField, isReturn: true);
        _il.Emit(OpCodes.Ldloc, returnStepLocal);
        SetStackUnknown();
        EmitAwaitFromValueOnStack(returnState);
        _il.Emit(OpCodes.Pop);

        _il.MarkLabel(endLabel);
        if (labeled)
            ExitLabeledLoop(labelNames!);
        else
            ExitLoop();
    }

    /// <summary>
    /// Emits the for-await loop body. When the loop sits inside a try-with-awaits (flag-based, so there is
    /// no real IL try around it), a synchronous <c>throw</c> in the body must still reach that try's catch.
    /// The loop as a whole can't sit in a real IL try (its await resume labels would be branched into), so
    /// the body is instead segmented around its awaits AND its escaping <c>break</c>/<c>continue</c> — the
    /// same shape the generator's <c>EmitTryBodyWithYields</c> uses: each await-free, jump-free run is
    /// wrapped in a real IL try (<see cref="EmitGuardedSyncSegment"/>) that captures a throw into the
    /// enclosing try's exception local and exits the loop to the catch dispatch, while an await or an
    /// escaping break/continue is emitted between segments where its resume label / <c>br</c> stays legal.
    /// This protects a synchronous throw in a body that also awaits or breaks/continues (#691) — the gap
    /// left when #631 stopped the loop from sitting in a real IL try. (A throw nested *inside* an
    /// await/break-containing statement remains unguarded, the same accepted limitation the generator
    /// try-body segmentation has for a throw nested in a yield statement.)
    /// </summary>
    private void EmitForAwaitBody(Stmt body)
    {
        // Outside a try-with-awaits there is nothing to route a synchronous throw to: emit the body
        // plainly (a throw propagates out of MoveNext and rejects the async function's promise; a
        // break/continue branches directly and a return Leaves — all legal, no real IL try is open here).
        if (_currentTryCatchExceptionLocal == null || _currentTryCatchSkipLabel == null)
        {
            EmitStatement(body);
            return;
        }

        if (body is Stmt.Block block)
        {
            // Emit the block's statements directly so they can be segmented, preserving the ES6 block
            // scope EmitBlock would otherwise establish for let/const declared in the body.
            _ctx!.Locals.EnterScope();
            try { EmitGuardedForAwaitSpan(block.Statements); }
            finally { _ctx!.Locals.ExitScope(); }
        }
        else
        {
            EmitGuardedForAwaitSpan([body]);
        }
    }

    /// <summary>
    /// Emits the statements of a for-await body inside the enclosing try-with-awaits, segmenting around
    /// suspension points and escaping break/continue (see <see cref="EmitForAwaitBody"/>). A run of plain
    /// statements is wrapped in one <see cref="EmitGuardedSyncSegment"/> — which, on catching a throw,
    /// branches out of the loop to the enclosing try's catch dispatch, so a later segment/await is not
    /// reached after a throw and no extra "already threw" gate is needed. A statement that awaits or
    /// contains an escaping break/continue is emitted directly between segments. A <c>return</c> stays in
    /// a segment: it lowers to <c>Leave</c>, which legally exits the segment's real IL try (and keeps a
    /// throw elsewhere in a return-containing statement guarded).
    /// </summary>
    private void EmitGuardedForAwaitSpan(IReadOnlyList<Stmt> statements)
    {
        List<Stmt> syncRun = [];
        void FlushSyncRun()
        {
            if (syncRun.Count == 0) return;
            var run = syncRun;
            EmitGuardedSyncSegment(() => { foreach (var s in run) EmitStatement(s); });
            syncRun = [];
        }

        foreach (var stmt in statements)
        {
            if (StmtContainsSuspension(stmt) || ContainsEscapingBreakOrContinue(stmt, insideLoop: false, insideSwitch: false))
            {
                FlushSyncRun();
                EmitStatement(stmt);
            }
            else
            {
                syncRun.Add(stmt);
            }
        }

        FlushSyncRun();
    }

    /// <summary>
    /// Runs <paramref name="emit"/> (a synchronous, suspension-free IL span that leaves the eval stack
    /// balanced) under the enclosing try-with-awaits, if any: a throw from the span is captured into the
    /// try's exception local and control jumps to the catch dispatch. Outside such a try it just runs the
    /// span. Lets the for-await prelude / step / body route synchronous throws to a guest catch even though
    /// the loop as a whole can't sit in a real IL try (its awaits would be branched into). (#631)
    /// </summary>
    private void EmitGuardedSyncSegment(System.Action emit)
    {
        if (_currentTryCatchExceptionLocal == null || _currentTryCatchSkipLabel == null)
        {
            emit();
            return;
        }
        var exLocal = _currentTryCatchExceptionLocal;
        _il.BeginExceptionBlock();
        // This real-IL try already captures a throw from the span into the enclosing flag-based catch, so
        // the #914 throw routing must defer to it (a routed `br` out of this region would be illegal IL).
        _throwRoutingGuardDepth++;
        emit();
        _throwRoutingGuardDepth--;
        _il.BeginCatchBlock(typeof(Exception));
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
        _il.Emit(OpCodes.Stloc, exLocal);
        _il.EndExceptionBlock();
        _il.Emit(OpCodes.Ldloc, exLocal);
        _il.Emit(OpCodes.Brtrue, _currentTryCatchSkipLabel.Value);
    }

    /// <summary>
    /// True if <paramref name="stmt"/> contains a <c>break</c>/<c>continue</c> that would transfer control
    /// out of the for-await body (so it must be emitted between guarded segments, where its <c>br</c> is
    /// legal — inside a real IL try it would be illegal). Tracks nested loops/switches so a break/continue
    /// targeting one of them (which stays inside the body) is NOT treated as escaping; a labeled
    /// break/continue is conservatively always escaping. A <c>return</c> is deliberately excluded: it
    /// lowers to <c>Leave</c>, which legally exits a guarded segment, so it can stay in a sync run and keep
    /// any throw in the same statement guarded. Mirrors the generator's <c>ContainsEscapingExit</c>.
    /// </summary>
    private static bool ContainsEscapingBreakOrContinue(Stmt stmt, bool insideLoop, bool insideSwitch)
    {
        switch (stmt)
        {
            case Stmt.Break b:
                return b.Label != null || !(insideLoop || insideSwitch);
            case Stmt.Continue c:
                return c.Label != null || !insideLoop;
            case Stmt.If i:
                return ContainsEscapingBreakOrContinue(i.ThenBranch, insideLoop, insideSwitch)
                    || (i.ElseBranch != null && ContainsEscapingBreakOrContinue(i.ElseBranch, insideLoop, insideSwitch));
            case Stmt.Block b:
                return b.Statements != null && ContainsEscapingBreakOrContinue2(b.Statements, insideLoop, insideSwitch);
            case Stmt.Sequence seq:
                return ContainsEscapingBreakOrContinue2(seq.Statements, insideLoop, insideSwitch);
            case Stmt.While w:
                return ContainsEscapingBreakOrContinue(w.Body, insideLoop: true, insideSwitch);
            case Stmt.DoWhile dw:
                return ContainsEscapingBreakOrContinue(dw.Body, insideLoop: true, insideSwitch);
            case Stmt.For f:
                return ContainsEscapingBreakOrContinue(f.Body, insideLoop: true, insideSwitch);
            case Stmt.ForOf fo:
                return ContainsEscapingBreakOrContinue(fo.Body, insideLoop: true, insideSwitch);
            case Stmt.ForIn fi:
                return ContainsEscapingBreakOrContinue(fi.Body, insideLoop: true, insideSwitch);
            case Stmt.Switch s:
                foreach (var c in s.Cases)
                    if (ContainsEscapingBreakOrContinue2(c.Body, insideLoop, insideSwitch: true)) return true;
                return s.DefaultBody != null && ContainsEscapingBreakOrContinue2(s.DefaultBody, insideLoop, insideSwitch: true);
            case Stmt.LabeledStatement ls:
                return ContainsEscapingBreakOrContinue(ls.Statement, insideLoop, insideSwitch);
            case Stmt.TryCatch t:
                return ContainsEscapingBreakOrContinue2(t.TryBlock, insideLoop, insideSwitch)
                    || (t.CatchBlock != null && ContainsEscapingBreakOrContinue2(t.CatchBlock, insideLoop, insideSwitch))
                    || (t.FinallyBlock != null && ContainsEscapingBreakOrContinue2(t.FinallyBlock, insideLoop, insideSwitch));
            default:
                return false;
        }
    }

    private static bool ContainsEscapingBreakOrContinue2(List<Stmt> statements, bool insideLoop, bool insideSwitch)
    {
        foreach (var s in statements)
            if (ContainsEscapingBreakOrContinue(s, insideLoop, insideSwitch)) return true;
        return false;
    }

    /// <summary>
    /// Produces one async-iterator protocol step into <paramref name="resultLocal"/>, guarding the call
    /// itself. The protocol call (iterator.next()/return()) can throw <em>synchronously</em> — e.g. a
    /// pre-aborted signal makes next() throw rather than reject — and that throw happens before the await,
    /// so when inside a try-with-awaits it must be captured into the try's exception local and routed to
    /// the catch (otherwise it escapes the state machine). Outside a try, it propagates normally. (#631)
    /// </summary>
    private void EmitAsyncStep(LocalBuilder resultLocal, FieldBuilder iteratorField, FieldBuilder isCustomField, bool isReturn)
        => EmitGuardedSyncSegment(() => EmitProduceAsyncStep(resultLocal, iteratorField, isCustomField, isReturn));

    /// <summary>
    /// Stores into <paramref name="resultLocal"/> the result of one async-iterator protocol call —
    /// <c>iterator.next()</c> when <paramref name="isReturn"/> is false, else <c>iterator.return()</c> —
    /// selecting the call by the protocol kind in <paramref name="isCustomField"/>. The value (a
    /// Task&lt;object&gt;, $Promise, or plain value) is later coerced and awaited by
    /// <see cref="EmitAwaitFromValueOnStack"/>. A custom iterator with no <c>return</c> method stores
    /// <c>undefined</c> (awaited as an already-resolved value), so the reserved return-await state is
    /// still consumed and its resume label stays marked.
    /// </summary>
    private void EmitProduceAsyncStep(LocalBuilder resultLocal, FieldBuilder iteratorField, FieldBuilder isCustomField, bool isReturn)
    {
        var stepLocal = resultLocal;
        var customLabel = _il.DefineLabel();
        var doneLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, isCustomField);
        _il.Emit(OpCodes.Brtrue, customLabel);

        // ----- $IAsyncGenerator path: invoke the interface method (returns Task<object>) -----
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, iteratorField);
        _il.Emit(OpCodes.Castclass, _ctx!.Runtime!.AsyncGeneratorInterfaceType);
        if (isReturn)
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorReturnMethod);
        }
        else
        {
            // for-await-of never sends a value; pass undefined so the yield expression sees undefined
            // (not null) if the consumer somehow observes it (#473).
            _il.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
            _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorNextMethod);
        }
        _il.Emit(OpCodes.Stloc, stepLocal);
        _il.Emit(OpCodes.Br, doneLabel);

        // ----- custom Symbol.asyncIterator path -----
        _il.MarkLabel(customLabel);
        if (isReturn)
        {
            // fn = GetProperty(iterator, "return"); absent/undefined → no-op close (push undefined).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, iteratorField);
            _il.Emit(OpCodes.Ldstr, "return");
            _il.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);
            var fnLocal = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, fnLocal);

            var noFnLabel = _il.DefineLabel();
            var haveFnLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, fnLocal);
            _il.Emit(OpCodes.Brfalse, noFnLabel);
            _il.Emit(OpCodes.Ldloc, fnLocal);
            _il.Emit(OpCodes.Isinst, _ctx.Runtime.UndefinedType);
            _il.Emit(OpCodes.Brtrue, noFnLabel);

            // InvokeMethodValue(iterator, fn, [])
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, iteratorField);
            _il.Emit(OpCodes.Ldloc, fnLocal);
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Newarr, _types.Object);
            _il.Emit(OpCodes.Call, _ctx.Runtime.InvokeMethodValue);
            _il.Emit(OpCodes.Stloc, stepLocal);
            _il.Emit(OpCodes.Br, haveFnLabel);

            _il.MarkLabel(noFnLabel);
            _il.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
            _il.Emit(OpCodes.Stloc, stepLocal);
            _il.MarkLabel(haveFnLabel);
        }
        else
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, iteratorField);
            _il.Emit(OpCodes.Call, _ctx.Runtime.InvokeIteratorNext);
            _il.Emit(OpCodes.Stloc, stepLocal);
        }

        _il.MarkLabel(doneLabel);
        // Result is left in resultLocal (== stepLocal); the caller loads and awaits it.
    }

    // EmitDoWhile: inherited from StatementEmitterBase (identical logic)
    // EmitForIn: inherited from StatementEmitterBase (uses DeclareLoopVariable/EmitStoreLoopVariable
    // overrides in this class to handle hoisted state machine fields)
}
