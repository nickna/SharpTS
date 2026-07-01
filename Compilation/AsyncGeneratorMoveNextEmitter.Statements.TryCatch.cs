using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    // The exit-scope stack, finally-routing scaffolding (scope types, RouteThroughFinallys,
    // EmitFinallyDispatch, break/continue, the loop-scope overrides, and the <>pendingExit /
    // <>pendingReturnValue fields) live in the shared StateMachineExitRoutingEmitter base. This emitter
    // adds the generator-specific pieces below: its own protected-region depth, throw routing
    // (<>pendingException), the yield/await-aware try/catch body, and the return/throw terminals.
    //
    // It coexists with the pre-existing external `generator.return()` path (the `__returnRequested`
    // flag checked at each yield resume and after each finally — see EmitYield and
    // EmitTryCatchWithSuspensions): the external path uses `__returnRequested` and is consulted before
    // the dispatch, while in-body exits use `<>pendingExit` and are dispatched only when
    // `__returnRequested` is clear.

    protected override FieldBuilder DefineStateMachineField(string name, Type type) =>
        _builder.StateMachineType.DefineField(name, type, FieldAttributes.Private);

    protected override int ProtectedRegionDepth => _protectedRegionDepth;

    // Depth of real IL exception blocks (EmitSimpleTryCatch / EmitSyncSegmentInTry) open around the
    // current emission point. While > 0, a `br`/`ret` out of the region would be illegal, so exits are
    // left to the existing per-path handling instead of being routed through the finally machinery.
    // This is independent of CompilationContext.ExceptionBlockDepth (which only counts simple try
    // blocks and drives the Leave-vs-Br choice in EmitBranchToLabel).
    private int _protectedRegionDepth;

    // True while emitting a catch or finally body. A `throw` there must run the enclosing finally(s);
    // a `throw` in a try body is instead captured by its sync-segment mini try/catch (and so must not
    // be routed). Saved/restored around each region so nesting is handled correctly.
    private bool _inHandlerBody;

    // `<>pendingException` (object): the value of a `throw` being routed through finally(s), held
    // across any suspension in those finallys until the terminal dispatch rethrows it.
    private FieldBuilder? _pendingExceptionField;

    private FieldBuilder GetPendingExceptionField() =>
        _pendingExceptionField ??= _builder.StateMachineType.DefineField(
            "<>pendingException", typeof(object), FieldAttributes.Private);

    // ---- Throw routing (generator-specific) -----------------------------------------------------
    // The loop-scope methods and break/continue (with their finally routing) are inherited from
    // StateMachineExitRoutingEmitter. An async generator additionally routes a `throw` escaping a
    // catch / finally body through the enclosing flag-based finally(s) to the correct handler:

    /// <summary>
    /// A <c>throw</c> in a catch or finally body propagates to the enclosing flag-based try (the one
    /// whose body lexically contains this handler): it runs the finally(s) inside that try, then lands
    /// in its catch — rather than a real IL <c>throw</c> that bypasses the flag-based catch (#632). With
    /// no enclosing flag-based try it runs the active finally(s) and propagates out of MoveNextAsync. A
    /// throw in a try body is captured by its sync-segment mini try/catch, not here.
    /// </summary>
    protected override void EmitThrow(Stmt.Throw t)
    {
        if (_inHandlerBody && _protectedRegionDepth == 0)
        {
            if (_currentTryExceptionLocal != null)
            {
                EmitThrowIntoEnclosingTry(() => { EmitExpression(t.Value); EnsureBoxed(); });
                return;
            }

            // No enclosing flag-based try (this handler belongs to the outermost try), but its own
            // finally may still be active and must run before the throw leaves MoveNextAsync.
            var chain = ActiveFinallyFrames();
            if (chain.Count > 0)
            {
                EmitExpression(t.Value);
                EnsureBoxed();
                var thrown = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, thrown);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, thrown);
                _il.Emit(OpCodes.Stfld, GetPendingExceptionField());

                RegisterThrowTerminal();
                RouteThroughFinallys(chain, ExitCodeThrow, OpCodes.Br);
                return;
            }
        }

        base.EmitThrow(t);
    }

    /// <summary>
    /// Propagates a guest exception escaping a handler body into the enclosing flag-based try (tracked
    /// by <see cref="_currentTryExceptionLocal"/> et al. while emitting a catch/finally body): stores
    /// the value into that try's capture local and sets its present flag, then branches to its cleanup
    /// entry so its catch runs. Any finally(s) strictly inside that try run first; because such a
    /// finally can yield/await, the value is held in <c>&lt;&gt;pendingException</c> across them and
    /// moved into the capture local by the routing terminal (#632, async analog).
    /// </summary>
    private void EmitThrowIntoEnclosingTry(Action loadValue)
    {
        var enclException = _currentTryExceptionLocal!;
        var enclPresent = _currentTryExceptionPresentLocal!;
        var enclCleanup = _currentTryCleanupLabel;
        var chain = FinallyFramesInside(_currentTryScopeDepth);
        if (chain.Count == 0)
        {
            // No intervening finally: store straight into the enclosing try and branch to its catch.
            loadValue();
            _il.Emit(OpCodes.Stloc, enclException);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, enclPresent);
            _il.Emit(OpCodes.Br, enclCleanup);
            return;
        }

        // Intervening finally(s) may yield/await, so hold the value in a field across them; the routing
        // terminal moves it into the enclosing try's capture local and branches to its catch.
        _il.Emit(OpCodes.Ldarg_0);
        loadValue();
        _il.Emit(OpCodes.Stfld, GetPendingExceptionField());
        int code = _nextExitCode++;
        _exitTerminals[code] = () =>
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, GetPendingExceptionField());
            _il.Emit(OpCodes.Stloc, enclException);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, enclPresent);
            _il.Emit(OpCodes.Br, enclCleanup);
        };
        RouteThroughFinallys(chain, code, OpCodes.Br);
    }

    // ---- Routing helpers ------------------------------------------------------------------------

    /// <summary>
    /// The finally scopes strictly inside the flag-based try whose body began at <paramref
    /// name="scopeDepth"/> (= <c>_exitScopes.Count</c> at that point), innermost first. Excludes the
    /// try's own finally (just below scopeDepth) and everything outside it — the finallys a throw
    /// escaping a nested handler must run before reaching that try's catch (#632).
    /// </summary>
    private List<FinallyScope> FinallyFramesInside(int scopeDepth)
    {
        var result = new List<FinallyScope>();
        for (int i = _exitScopes.Count - 1; i >= scopeDepth; i--)
            if (_exitScopes[i] is FinallyScope fs)
                result.Add(fs);
        return result;
    }

    private void RegisterReturnTerminal() => _exitTerminals.TryAdd(ExitCodeReturn, () =>
    {
        // Restore the completion value into Current: a yielding finally between the `return` and this
        // point overwrote Current with its yielded value, so re-load the value stashed at the `return`
        // (#597, async analog of #555). With no yielding finally this is a no-op (Current is unchanged).
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, GetPendingReturnValueField());
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // The generator completes. State -2 is re-asserted here because a yielding finally between the
        // `return` and this point overwrote it with the finally's resume state.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(false);
    });

    private void RegisterThrowTerminal() => _exitTerminals.TryAdd(ExitCodeThrow, () =>
    {
        // The routed exception has run every enclosing finally; propagate it now. The generator is
        // completing (throwing), so mark it done first.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, GetPendingExceptionField());
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    });

    // ---- Try/catch emission ---------------------------------------------------------------------

    protected override void EmitTryCatch(Stmt.TryCatch t)
    {
        // Check if this try block contains any suspension points (yield or await)
        bool hasSuspensionsInTry = AnyStmtContainsSuspension(t.TryBlock);
        bool hasSuspensionsInCatch = t.CatchBlock != null && AnyStmtContainsSuspension(t.CatchBlock);
        bool hasSuspensionsInFinally = t.FinallyBlock != null && AnyStmtContainsSuspension(t.FinallyBlock);

        if (hasSuspensionsInTry || hasSuspensionsInCatch || hasSuspensionsInFinally)
        {
            // Cannot use IL exception blocks when suspension points exist inside them
            // because: (1) state switch can't jump into protected regions,
            // (2) yield/return can't use 'ret' inside try blocks,
            // (3) 'leave' from try/finally would trigger finally prematurely on yield.
            // Use flag-based exception tracking instead.
            EmitTryCatchWithSuspensions(t);
        }
        else
        {
            // No suspension points - safe to use IL exception blocks
            EmitSimpleTryCatch(t);
        }
    }

    private void EmitSimpleTryCatch(Stmt.TryCatch t)
    {
        // A real IL protected region is open: a routed `br`/`ret` out of it would be illegal, so
        // exits emitted inside fall back to their per-path handling (see the exit overrides).
        _protectedRegionDepth++;
        _ctx!.ExceptionBlockDepth++;
        _il.BeginExceptionBlock();

        foreach (var stmt in t.TryBlock)
            EmitStatement(stmt);

        if (t.CatchBlock != null)
        {
            _il.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                // Stack has the .NET exception; wrap to the TS value and bind to the catch param,
                // honouring a hoisted field if the param is read across a suspension (#569).
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
                StoreCaughtExceptionToParam(t.CatchParam.Lexeme);
            }
            else
            {
                _il.Emit(OpCodes.Pop);
            }

            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }

        if (t.FinallyBlock != null)
        {
            _il.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
                EmitStatement(stmt);
        }

        _il.EndExceptionBlock();
        _ctx!.ExceptionBlockDepth--;
        _protectedRegionDepth--;
    }

    /// <summary>
    /// Binds the caught exception value (on the IL stack) to the catch parameter, honouring whether
    /// the parameter was hoisted to a state-machine field (because it is read across a yield/await in
    /// the catch body) or lives in an IL local. Storing to a fresh local unconditionally — the
    /// previous behaviour — lost the value whenever the catch parameter was hoisted, because reads
    /// resolve the field first (#569, async analog of GeneratorMoveNextEmitter.StoreCaughtExceptionToParam).
    /// </summary>
    private void StoreCaughtExceptionToParam(string name)
    {
        if (GetHoistedVariableField(name) == null)
        {
            // Not hoisted: register a local so the catch body's reads resolve to it.
            var exLocal = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(name, exLocal);
        }

        // Resolver stores to the hoisted field if present, otherwise the registered local.
        Resolver.TryStoreVariable(name);
    }

    /// <summary>
    /// Flag-based try/catch/finally for the case where a suspension (yield / yield* / await) lives
    /// inside the protected region. Synchronous segments of the try body are wrapped in mini IL
    /// try/catch blocks that capture any exception into a flag local; suspension points and non-local
    /// exits are emitted at the top level (outside any protected region) so their resume labels are
    /// reachable from the state-dispatch switch and their `ret`/`br` are legal.
    /// </summary>
    private void EmitTryCatchWithSuspensions(Stmt.TryCatch t)
    {
        var caughtExceptionLocal = _il.DeclareLocal(typeof(object));
        // Whether the try body raised an exception, tracked separately from caughtExceptionLocal's
        // nullness: a thrown/rejected null/undefined captures as a null CLR reference, which a
        // value-nullness gate misreads as "no exception" — skipping the catch and dropping the
        // post-finally rethrow (#628, the async analog of #619). This flag records presence regardless
        // of the captured value.
        var exceptionPresentLocal = _il.DeclareLocal(typeof(bool));
        var afterTryBodyLabel = _il.DefineLabel();
        var afterTryCatchLabel = _il.DefineLabel();

        // No exception captured yet.
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, exceptionPresentLocal);

        // Two finally-running mechanisms share `afterTryBodyLabel` as the cleanup entry:
        //   1. External `generator.return()`: a suspended yield observes `__returnRequested` on resume
        //      and jumps here to run the finally (see EmitYield). `_returnCleanupLabel` carries the
        //      label down to those yields and is only meaningful while emitting *this* try's body.
        //   2. In-body non-local exits (#559): break/continue/return/throw register a FinallyScope and
        //      route through it; on those paths the exception flag is null, so the catch is skipped and
        //      the finally runs.
        // Without a finally there is nothing to run, so neither is set up and exits go directly.
        var previousCleanupLabel = _returnCleanupLabel;
        FinallyScope? frame = null;
        if (t.FinallyBlock != null)
        {
            _returnCleanupLabel = afterTryBodyLabel;
            frame = new FinallyScope { CleanupLabel = afterTryBodyLabel };
            _exitScopes.Add(frame);
        }

        // Throws in the try body are captured by their sync segments, not routed.
        bool previousInHandler = _inHandlerBody;
        _inHandlerBody = false;
        // Carry this try's capture target down to the suspension points emitted at the top level, so a
        // rejected `await` inside the try routes its exception into the same flag + catch/finally
        // instead of escaping MoveNextAsync (#617), setting the present flag so a null rejection still
        // engages the catch (#628). After the body these revert to the enclosing try, which a throw
        // escaping this try's catch/finally must route to (#632). Saved/restored for correct nesting.
        var previousTryExceptionLocal = _currentTryExceptionLocal;
        var previousTryExceptionPresentLocal = _currentTryExceptionPresentLocal;
        var previousTryCleanupLabel = _currentTryCleanupLabel;
        var previousTryScopeDepth = _currentTryScopeDepth;
        _currentTryExceptionLocal = caughtExceptionLocal;
        _currentTryExceptionPresentLocal = exceptionPresentLocal;
        _currentTryCleanupLabel = afterTryBodyLabel;
        _currentTryScopeDepth = _exitScopes.Count;
        EmitTryBodyWithSuspensions(t.TryBlock, caughtExceptionLocal, exceptionPresentLocal, afterTryBodyLabel);
        _currentTryExceptionLocal = previousTryExceptionLocal;
        _currentTryExceptionPresentLocal = previousTryExceptionPresentLocal;
        _currentTryCleanupLabel = previousTryCleanupLabel;
        _currentTryScopeDepth = previousTryScopeDepth;
        _inHandlerBody = previousInHandler;

        // Restore the enclosing try's cleanup label (its yields must route to it, not ours).
        _returnCleanupLabel = previousCleanupLabel;

        _il.MarkLabel(afterTryBodyLabel);

        // Catch: runs only when the try body captured an exception. The finally scope is still open, so
        // a non-local exit (including a throw) from the catch body runs this finally too.
        if (t.CatchBlock != null)
        {
            // Gate on the present flag, not the value's nullness, so a caught null/undefined enters the
            // catch (#628). The value local stays authoritative for the binding below.
            var skipCatchLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
            _il.Emit(OpCodes.Brfalse, skipCatchLabel);

            // Bind the captured value to the catch param, honouring a hoisted field when the param is
            // read across a suspension in the catch body (#569). Storing to a fresh local
            // unconditionally lost the value whenever the param was hoisted (reads resolve the field).
            // This binding is also what lets a rejected await routed here (#617) surface its reason.
            if (t.CatchParam != null)
            {
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                StoreCaughtExceptionToParam(t.CatchParam.Lexeme);
            }

            // Catch handles it; clear the present flag so the post-finally rethrow below is skipped —
            // and so a routed exit re-entering afterTryBody skips the catch rather than re-running it.
            // The flag (not the value) is the gate now, so clearing it is what matters (#628).
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stloc, exceptionPresentLocal);

            _inHandlerBody = true;
            EmitHandlerBodyWithCapture(t.CatchBlock);
            _inHandlerBody = previousInHandler;

            _il.MarkLabel(skipCatchLabel);
        }

        // The finally itself is outside its own scope: an exit within it runs the *enclosing* finallys.
        if (frame != null)
            _exitScopes.RemoveAt(_exitScopes.Count - 1);

        // Finally: always runs — on normal completion, after a caught exception, or on a routed exit.
        if (t.FinallyBlock != null)
        {
            _inHandlerBody = true;
            EmitHandlerBodyWithCapture(t.FinallyBlock);
            _inHandlerBody = previousInHandler;

            // A real exception arising in the finally body itself was already routed out by
            // EmitHandlerBodyWithCapture (superseding any pending exit / external return), so the
            // mechanisms below run only on the finally's normal completion.

            // External return() (mechanism 1): __returnRequested set → complete the generator now that
            // the finally has run. Checked before the in-body dispatch because the two use different
            // flags (__returnRequested vs <>pendingExit) and an external return never sets the latter.
            var noReturnRequestedLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.ReturnRequestedField);
            _il.Emit(OpCodes.Brfalse, noReturnRequestedLabel);

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, -2);
            _il.Emit(OpCodes.Stfld, _builder.StateField);
            EmitReturnValueTaskBool(false);

            _il.MarkLabel(noReturnRequestedLabel);

            // In-body non-local exit (mechanism 2): dispatch any pending exit that routed through here.
            EmitFinallyDispatch(frame!);

            // Propagate an uncaught exception once the finally has run (try/finally with no catch).
            if (t.CatchBlock == null)
            {
                // Gate on the present flag so a thrown/rejected null/undefined still propagates (#628).
                var noExceptionLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
                _il.Emit(OpCodes.Brfalse, noExceptionLabel);

                if (_currentTryExceptionLocal != null)
                {
                    // The finally has run; the still-uncaught exception now propagates to the enclosing
                    // flag-based try's catch (not out of MoveNextAsync), so an outer catch still handles
                    // it — the try/finally analog of the handler-body throw routing (#632).
                    EmitThrowIntoEnclosingTry(() => _il.Emit(OpCodes.Ldloc, caughtExceptionLocal));
                }
                else
                {
                    // No enclosing flag-based try: mark done and propagate out of MoveNextAsync.
                    _il.Emit(OpCodes.Ldarg_0);
                    _il.Emit(OpCodes.Ldc_I4, -2);
                    _il.Emit(OpCodes.Stfld, _builder.StateField);
                    _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
                    _il.Emit(OpCodes.Throw);
                }

                _il.MarkLabel(noExceptionLabel);
            }
        }

        _il.MarkLabel(afterTryCatchLabel);
    }

    /// <summary>
    /// Walks the try body, wrapping runs of plain statements in mini IL try/catch blocks while
    /// emitting suspension points and non-local exits (return/break/continue) at the top level.
    /// </summary>
    private void EmitTryBodyWithSuspensions(List<Stmt> tryBody, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal, Label afterTryLabel)
    {
        List<Stmt> syncSegment = [];

        foreach (var stmt in tryBody)
        {
            if (IsSegmentBreaker(stmt))
            {
                // Flush the accumulated plain statements first.
                if (syncSegment.Count > 0)
                {
                    EmitSyncSegmentInTry(syncSegment, caughtExceptionLocal, exceptionPresentLocal);
                    syncSegment.Clear();
                }

                // If an earlier segment threw, skip the suspension/exit and head to catch/finally.
                // Gate on the present flag so a thrown null/undefined still short-circuits here (#628).
                _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
                _il.Emit(OpCodes.Brtrue, afterTryLabel);

                // Emitted at the top level: a yield/await's `ret`/resume label and a return's `ret` or a
                // break/continue's `br` are only legal outside a protected region.
                EmitStatement(stmt);
            }
            else
            {
                syncSegment.Add(stmt);
            }
        }

        if (syncSegment.Count > 0)
            EmitSyncSegmentInTry(syncSegment, caughtExceptionLocal, exceptionPresentLocal);
    }

    /// <summary>
    /// Emits a run of plain (non-suspending, non-exiting) statements inside a real IL try/catch
    /// that records any thrown exception into <paramref name="caughtExceptionLocal"/>.
    /// </summary>
    private void EmitSyncSegmentInTry(List<Stmt> statements, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal)
    {
        // An earlier segment may already have thrown — don't run this one. Gate on the present flag so
        // a prior thrown null/undefined still suppresses this segment (#628).
        var skipLabel = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
        _il.Emit(OpCodes.Brtrue, skipLabel);

        // A real IL protected region is open across the segment body (see _protectedRegionDepth).
        _protectedRegionDepth++;
        _il.BeginExceptionBlock();
        foreach (var stmt in statements)
            EmitStatement(stmt);

        _il.BeginCatchBlock(typeof(Exception));
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);
        // Record presence with the flag, not the value: a caught null/undefined would otherwise read
        // as "no exception" at the gates above (#628).
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Stloc, exceptionPresentLocal);
        _il.EndExceptionBlock();
        _protectedRegionDepth--;

        _il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits a flag-based <c>catch</c>/<c>finally</c> body, capturing any real CLR exception that arises
    /// at its top level and routing it to the enclosing flag-based try (or out of MoveNextAsync when
    /// there is none) instead of letting it escape the state machine unhandled. The motivating case is an
    /// exception escaping a nested no-suspension (real IL) <c>try</c>/<c>catch</c> whose handler throws:
    /// that throw is correctly a real IL <c>throw</c> (it is inside a real protected region), so the
    /// lexical <see cref="EmitThrow"/> routing never sees it; once it leaves the nested block it was
    /// previously in flight in an unprotected region and bypassed the enclosing flag-based catch (#675,
    /// async-generator analog). A runtime error at the handler's top level is covered the same way.
    ///
    /// <para>Mirrors <see cref="EmitTryBodyWithSuspensions"/>: runs of plain statements are wrapped in
    /// mini IL try/catch segments (<see cref="EmitSyncSegmentInTry"/>) that record a thrown exception
    /// into a handler-local flag, while suspension points and non-local exits stay at the top level so
    /// their resume labels / branches remain legal. After the body, a captured exception is propagated by
    /// <see cref="EmitRouteCapturedHandlerException"/>. A lexical <c>throw</c> reached at the handler's
    /// top level (e.g. after a yield/await, outside any segment) is still routed directly by
    /// <see cref="EmitThrow"/>; this method only adds coverage for exceptions that arrive already in
    /// flight, which no <c>throw</c> statement intercepts. Caller sets <c>_inHandlerBody</c>.</para>
    /// </summary>
    private void EmitHandlerBodyWithCapture(List<Stmt> body)
    {
        var handlerCaught = _il.DeclareLocal(typeof(object));
        var handlerPresent = _il.DeclareLocal(typeof(bool));
        var afterHandlerLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stloc, handlerCaught);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, handlerPresent);

        List<Stmt> syncSegment = [];

        foreach (var stmt in body)
        {
            if (IsSegmentBreaker(stmt))
            {
                if (syncSegment.Count > 0)
                {
                    EmitSyncSegmentInTry(syncSegment, handlerCaught, handlerPresent);
                    syncSegment.Clear();
                }

                // If an earlier segment threw, skip the suspension/exit and route the exception out.
                _il.Emit(OpCodes.Ldloc, handlerPresent);
                _il.Emit(OpCodes.Brtrue, afterHandlerLabel);

                EmitStatement(stmt);
            }
            else
            {
                syncSegment.Add(stmt);
            }
        }

        if (syncSegment.Count > 0)
            EmitSyncSegmentInTry(syncSegment, handlerCaught, handlerPresent);

        _il.MarkLabel(afterHandlerLabel);

        // Gate on the present flag (not the value's nullness) so a captured null/undefined is still
        // routed (#628).
        var noHandlerException = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, handlerPresent);
        _il.Emit(OpCodes.Brfalse, noHandlerException);
        EmitRouteCapturedHandlerException(() => _il.Emit(OpCodes.Ldloc, handlerCaught));
        _il.MarkLabel(noHandlerException);
    }

    /// <summary>
    /// Propagates an exception captured in a <c>catch</c>/<c>finally</c> body
    /// (<see cref="EmitHandlerBodyWithCapture"/>): to the enclosing flag-based try's catch when one is
    /// open (running the finally(s) inside it first), else through the active finally(s) and out, else
    /// mark the generator done and throw out of MoveNextAsync. This is the in-flight-exception analog of
    /// the handler-body arm of <see cref="EmitThrow"/>, which handles a lexical handler <c>throw</c>.
    /// </summary>
    private void EmitRouteCapturedHandlerException(Action loadValue)
    {
        if (_currentTryExceptionLocal != null)
        {
            EmitThrowIntoEnclosingTry(loadValue);
            return;
        }

        var chain = ActiveFinallyFrames();
        if (chain.Count > 0)
        {
            _il.Emit(OpCodes.Ldarg_0);
            loadValue();
            _il.Emit(OpCodes.Stfld, GetPendingExceptionField());
            RegisterThrowTerminal();
            RouteThroughFinallys(chain, ExitCodeThrow, OpCodes.Br);
            return;
        }

        // No enclosing flag-based try and no active finally: the exception leaves the generator.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        loadValue();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    }

    #region Suspension / control-exit detection

    /// <summary>
    /// A statement must be emitted at the top level (rather than inside a mini try/catch segment)
    /// if it contains a suspension point or a control-flow exit that leaves the try region. Both
    /// would otherwise produce illegal IL inside the segment's protected region.
    /// </summary>
    private static bool IsSegmentBreaker(Stmt stmt) =>
        StmtContainsSuspension(stmt) || ContainsEscapingExit(stmt, insideLoop: false, insideSwitch: false);

    // The await/yield-suspension statement/expression walkers now live in ExpressionEmitterBase as the
    // single StmtContainsSuspension/ExprContainsSuspension pair shared by every state-machine family
    // (#1121); the three hand-maintained copies had repeatedly drifted into illegal-BranchIntoTry bugs
    // (#631/#850/#914).

    // ContainsEscapingExit / ContainsEscapingExit2 are shared across the suspension-aware emitters and
    // live in StatementEmitterBase (the generator, async-generator, and async-function emitters all
    // segment a flag-based try body around non-local exits using the same conservative analysis).

    #endregion
}
