using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class AsyncFunctionMoveNextEmitter
{
    // Per-method counter giving each for await…of loop unique iterator/protocol field names.
    private int _forAwaitCounter;

    // Throw completions retain their original error if a later iterator close also fails.
    private readonly HashSet<int> _iteratorCloseThrowExits = [];

    /// <summary>
    /// Resumable for-await lowering shared by ordinary async functions and async arrows (#2848).
    /// The generic statement base blocks on GetResult; this implementation instead suspends
    /// the state machine on each next()/return() await, so a genuinely-async step (e.g. a setTimeout-backed
    /// await inside the iterator) doesn't deadlock the event-loop thread (#631). The analyzer reserved one
    /// suspension state for each await (AsyncStateAnalyzer.VisitForOf), consumed below in the same order.
    /// Both async-function emitters use their existing shared await machinery instead of blocking jobs.
    /// </summary>
    protected override void EmitForAwaitOf(Stmt.ForOf f) => EmitForAwaitOf(f, labelNames: null);

    /// <summary>
    /// Emits a <c>for await…of</c> loop, optionally carrying the wrapping statement labels
    /// <paramref name="labelNames"/> (a chain <c>a: b: for await</c> contributes several) so a labeled
    /// <c>break</c>/<c>continue &lt;label&gt;</c> targets it. The labeled path delegates here so it gets the
    /// same suspending async-iterator lowering as the unlabeled case, rather than enumerating the async
    /// iterator synchronously and leaving the reserved await-state labels unmarked — #728.
    /// </summary>
    protected void EmitForAwaitOf(Stmt.ForOf f, IReadOnlyList<string>? labelNames)
    {
        // for await…of drives an async iterator: resolve it (Symbol.asyncIterator, else assume the
        // value is itself an async iterator / $IAsyncGenerator), then each iteration await
        // iterator.next() and, on an early `break`, await iterator.return(). Both awaits SUSPEND the
        // enclosing async function via its state machine rather than blocking on GetResult — so a
        // genuinely-async step (e.g. a setTimeout-backed await inside the iterator) no longer deadlocks
        // the event-loop thread (#631). The analyzer reserved one suspension state for each await
        // (AsyncStateAnalyzer.VisitForOf), consumed below in the same order.

        string varName = GetBindingStorageName(f, f.Variable.Lexeme);
        var varField = AsyncBuilder.GetVariableField(varName);

        var iterableLocal = IL.DeclareLocal(Types.Object);

        // The iterator and its protocol kind must survive the per-iteration suspensions (a MoveNext
        // re-entry wipes IL locals), so store them in state-machine fields. Unique per loop so nested /
        // sequential for-await loops don't collide. The type is still open here (CreateType runs after
        // MoveNext), so defining fields now is valid.
        int loopId = _forAwaitCounter++;
        var iteratorField = AsyncBuilder.StateMachineType.DefineField($"<>7__aiter{loopId}", Types.Object, FieldAttributes.Private);
        var isCustomField = AsyncBuilder.StateMachineType.DefineField($"<>7__aiterCustom{loopId}", Types.Boolean, FieldAttributes.Private);

        var nextField = AsyncBuilder.StateMachineType.DefineField($"<>7__aiterNext{loopId}", Types.Object, FieldAttributes.Private);

        // Synchronous prelude: evaluate the iterable and resolve the iterator + protocol kind:
        //   isCustom == true  → iterable[Symbol.asyncIterator]() ; step via the captured next method or a live "return".
        //   isCustom == false → an $IAsyncGenerator, including the emitted async-from-sync adapter.
        // Evaluating the iterable, or calling its [Symbol.asyncIterator], can throw synchronously (e.g. a
        // pre-aborted signal) — guarded below so that throw reaches an enclosing try's catch.
        void EmitPrelude()
        {
            EmitExpression(f.Iterable);
            EnsureBoxed();
            IL.Emit(OpCodes.Stloc, iterableLocal);

            EmitResolveAsyncIterator(iterableLocal, iteratorField, isCustomField, nextField);
        }

        // The prelude is await-free unless the iterable expression itself awaits; only then can it not
        // sit in a real IL try (its resume label would be branched into) — in that case the iterable's
        // own await handling routes rejections, and the (rare) synchronous resolution throw is unguarded.
        if (ExprContainsSuspension(f.Iterable))
            EmitPrelude();
        else
            EmitGuardedSyncSegment(EmitPrelude);

        int nextState = NextAwaitState();   // iterator.next() await (reused each iteration)

        var startLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var cleanupLabel = IL.DefineLabel();
        var continueLabel = IL.DefineLabel();

        // break → cleanup (await iterator.return()); natural done → endLabel (no return() per spec).
        // The chain's labels (when present) let `break`/`continue <label>` resolve to this loop; each is
        // registered as its own scope at the same targets (the #704 multi-label convention).
        // Below this loop's own scopes: its continue does not close, and its break already
        // targets cleanupLabel. An exit to an outer scope must close this iterator first.
        var closeFrame = new FinallyScope { CleanupLabel = cleanupLabel };
        _exitScopes.Add(closeFrame);
        bool labeled = labelNames is { Count: > 0 };
        if (labeled)
        {
            foreach (var name in labelNames!)
                EnterLoop(cleanupLabel, continueLabel, name);
        }
        else
            EnterLoop(cleanupLabel, continueLabel);

        IL.MarkLabel(startLabel);

        // result = await iterator.next()
        var stepLocal = IL.DeclareLocal(Types.Object);
        EmitAsyncStep(stepLocal, iteratorField, isCustomField, nextField, isReturn: false);
        IL.Emit(OpCodes.Ldloc, stepLocal);
        SetStackUnknown();
        EmitAwaitFromValueOnStack(nextState);
        var resultLocal = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, resultLocal);

        // if (result.done) exit without calling return()
        var runtime = Ctx.Runtime!;
        var doneLocal = IL.DeclareLocal(Types.Boolean);
        EmitGuardedSyncSegment(() =>
        {
            IL.Emit(OpCodes.Ldloc, resultLocal);
            IL.Emit(OpCodes.Call, runtime.IteratorRecords.RequireObject);
            IL.Emit(OpCodes.Call, runtime.IteratorProtocol.Done);
            IL.Emit(OpCodes.Stloc, doneLocal);
        });
        IL.Emit(OpCodes.Ldloc, doneLocal);
        IL.Emit(OpCodes.Brtrue, endLabel);

        // loopVar = result.value
        IL.Emit(OpCodes.Ldloc, resultLocal);
        IL.Emit(OpCodes.Call, runtime.IteratorProtocol.Value);
        if (!f.IsDeclaration || f.IsVar)
        {
            SetStackUnknown();
            EmitStoreAssignmentLoopVariable(varName);
        }
        else if (TryInitializeLexicalCell(f.Variable, fresh: true)) { }
        else if (varField != null)
        {
            var valueTemp = IL.DeclareLocal(Types.Object);
            IL.Emit(OpCodes.Stloc, valueTemp);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldloc, valueTemp);
            IL.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = IL.DeclareLocal(Types.Object);
            RegisterLoopLocal(varName, varLocal);
            IL.Emit(OpCodes.Stloc, varLocal);
        }

        EmitForAwaitBody(f.Body);

        IL.MarkLabel(continueLabel);
        IL.Emit(OpCodes.Br, startLabel);

        // Cleanup runs outside its own exit scopes. A close failure can then route through
        // outer iterator/finally scopes without re-entering this close.
        if (labeled)
        {
            for (int i = 0; i < labelNames!.Count; i++)
                ExitLoop();
        }
        else
            ExitLoop();
        _exitScopes.RemoveAt(_exitScopes.Count - 1);

        // Reuse the analyzer's one return-await state, allocated after the body as before.
        int returnState = NextAwaitState();
        IL.MarkLabel(cleanupLabel);
        void EmitClose()
        {
            var returnStepLocal = IL.DeclareLocal(Types.Object);
            EmitAsyncStep(returnStepLocal, iteratorField, isCustomField, nextField, isReturn: true);
            IL.Emit(OpCodes.Ldloc, returnStepLocal);
            SetStackUnknown();
            EmitAwaitFromValueOnStack(returnState);
            IL.Emit(OpCodes.Pop);
        }

        if (closeFrame.Dispatch.Count == 0)
        {
            // Preserve the existing own-loop break path when no routed exit crosses this loop.
            EmitClose();
        }
        else
        {
            // IL locals reset on a pending return() resumption. Preserve the completion in a
            // per-loop field, clearing the shared flag before either the call or await can fail.
            var pendingExit = DefineStateMachineField($"<>7__aiterExit{loopId}", Types.Int32);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, GetPendingExitField());
            IL.Emit(OpCodes.Stfld, pendingExit);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stfld, GetPendingExitField());

            var closeError = IL.DeclareLocal(Types.Object);
            var closeFailed = IL.DeclareLocal(Types.Boolean);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stloc, closeError);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, closeFailed);
            EmitTryBodyWithAwaitsCore(EmitClose, closeError, closeFailed);

            var dispatch = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, closeFailed);
            IL.Emit(OpCodes.Brfalse, dispatch);
            foreach (int code in closeFrame.Dispatch.Keys.Where(_iteratorCloseThrowExits.Contains))
            {
                // AsyncIteratorClose attempts outer cleanup but preserves an existing throw.
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, pendingExit);
                IL.Emit(OpCodes.Ldc_I4, code);
                IL.Emit(OpCodes.Beq, dispatch);
            }
            EmitIteratorCloseThrow(closeError);

            IL.MarkLabel(dispatch);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, pendingExit);
            IL.Emit(OpCodes.Stfld, GetPendingExitField());
            EmitFinallyDispatch(closeFrame);
        }

        IL.MarkLabel(endLabel);
    }

    // A failed close replaces a break/continue/return completion. Attempt the remaining
    // outer cleanups before delivering that failure to the enclosing catch or Task boundary.
    private void EmitIteratorCloseThrow(LocalBuilder error)
    {
        if (_currentTryCatchExceptionLocal != null && _currentTryCatchSkipLabel != null)
        {
            RouteThrowIntoCurrentTry(() => IL.Emit(OpCodes.Ldloc, error));
            return;
        }

        var chain = ActiveFinallyFrames();
        if (chain.Count == 0)
        {
            IL.Emit(OpCodes.Ldloc, error);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.CreateException);
            IL.Emit(OpCodes.Throw);
            return;
        }

        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, error);
        IL.Emit(OpCodes.Stfld, GetPendingExceptionField());
        int code = _nextExitCode++;
        _iteratorCloseThrowExits.Add(code);
        _exitTerminals[code] = () =>
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, GetPendingExceptionField());
            IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.CreateException);
            IL.Emit(OpCodes.Throw);
        };
        RouteThroughFinallys(chain, code, ProtectedRegionDepth > 0 ? OpCodes.Leave : OpCodes.Br);
    }

    // Iterator step failures do not close the iterator, but every abrupt body completion
    // does. Capture the complete resumable body separately from an enclosing guest handler
    // so its failure traverses this loop's close frame before reaching that handler.
    private void EmitForAwaitBody(Stmt body)
    {
        var error = IL.DeclareLocal(Types.Object);
        var failed = IL.DeclareLocal(Types.Boolean);
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Stloc, error);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, failed);
        EmitTryBodyWithAwaitsCore(() => EmitStatement(body), error, failed);

        var completed = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, failed);
        IL.Emit(OpCodes.Brfalse, completed);
        EmitIteratorCloseThrow(error);
        IL.MarkLabel(completed);
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
        if (_currentTryCatchExceptionLocal == null || _currentTryCatchSkipLabel == null
            || _throwRoutingGuardDepth > 0)
        {
            emit();
            return;
        }
        var exLocal = _currentTryCatchExceptionLocal;
        IL.BeginExceptionBlock();
        // This real-IL try already captures a throw from the span into the enclosing flag-based catch, so
        // the #914 throw routing must defer to it (a routed `br` out of this region would be illegal IL).
        _throwRoutingGuardDepth++;
        emit();
        _throwRoutingGuardDepth--;
        IL.BeginCatchBlock(typeof(Exception));
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.WrapException);
        IL.Emit(OpCodes.Stloc, exLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, _currentTryCatchExceptionPresentLocal!);
        IL.EndExceptionBlock();
        IL.Emit(OpCodes.Ldloc, _currentTryCatchExceptionPresentLocal!);
        IL.Emit(OpCodes.Brtrue, _currentTryCatchSkipLabel.Value);
    }

    /// <summary>
    /// Produces one async-iterator protocol step into <paramref name="resultLocal"/>, guarding the call
    /// itself. The protocol call (iterator.next()/return()) can throw <em>synchronously</em> — e.g. a
    /// pre-aborted signal makes next() throw rather than reject — and that throw happens before the await,
    /// so when inside a try-with-awaits it must be captured into the try's exception local and routed to
    /// the catch (otherwise it escapes the state machine). Outside a try, it propagates normally. (#631)
    /// </summary>
    private void EmitAsyncStep(LocalBuilder resultLocal, FieldBuilder iteratorField, FieldBuilder isCustomField, FieldBuilder nextField, bool isReturn)
        => EmitGuardedSyncSegment(() => EmitProduceAsyncStep(resultLocal, iteratorField, isCustomField, nextField, isReturn));

}
