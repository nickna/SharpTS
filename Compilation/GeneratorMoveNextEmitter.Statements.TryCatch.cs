using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    // The exit-scope stack, finally-routing scaffolding (scope types, RouteThroughFinallys,
    // EmitFinallyDispatch, break/continue, the loop-scope overrides, and the <>pendingExit /
    // <>pendingReturnValue fields) live in the shared StateMachineExitRoutingEmitter base. This emitter
    // adds the generator-specific pieces below: its own protected-region depth, throw routing
    // (<>pendingException), the yield-aware try/catch body, and the return/throw terminals.

    protected override FieldBuilder DefineStateMachineField(string name, Type type) =>
        _builder.StateMachineType.DefineField(name, type, FieldAttributes.Private);

    protected override int ProtectedRegionDepth => _protectedRegionDepth;

    // Depth of real IL exception blocks (EmitSimpleTryCatch / EmitSyncSegmentInTry) open around the
    // current emission point. While > 0, a `br`/`ret` out of the region would be illegal, so exits are
    // left to the existing per-path handling instead of being routed through the finally machinery.
    private int _protectedRegionDepth;

    // True while emitting a catch or finally body. A `throw` there must run the enclosing finally(s);
    // a `throw` in a try body is instead captured by its sync-segment mini try/catch (and so must not
    // be routed). Saved/restored around each region so nesting is handled correctly.
    private bool _inHandlerBody;

    // The innermost flag-based try whose *body* is currently being emitted: its catch/finally entry
    // (afterTryBodyLabel), the local capturing a try-body exception, the boolean flag recording whether
    // one was captured, and `_exitScopes.Count` at the start of its body (ScopeDepth — finally scopes at
    // indices >= it are strictly inside this try). An external throw() injected at a yield in this body
    // behaves as if the body threw there — it stores the error into the local, sets the flag, and
    // branches to the cleanup so the catch/finally run (#526). The flag (not the value's nullness) gates
    // the catch so an injected throw(null)/throw(undefined) still engages it (#619). Saved/restored
    // around the try-body emission, so while emitting a catch/finally body it instead identifies the
    // *enclosing* flag-based try (or is null) — the one whose catch must handle a throw escaping that
    // handler, after the finally(s) inside it have run (#632).
    private (Label AfterTryBody, LocalBuilder CaughtException, LocalBuilder ExceptionPresent, int ScopeDepth)? _tryBodyContext;

    // `<>pendingException` (object): the value of a `throw` being routed through finally(s), held
    // across any suspension in those finallys until the terminal dispatch rethrows it.
    private FieldBuilder? _pendingExceptionField;

    private FieldBuilder GetPendingExceptionField() =>
        _pendingExceptionField ??= _builder.StateMachineType.DefineField(
            "<>pendingException", typeof(object), FieldAttributes.Private);

    // Per-construct fields holding a try-body exception across a *yielding* finally in a try/finally
    // with no catch (#599). The exception is captured into an IL local during the try body, but that
    // local resets when the yielding finally suspends MoveNext, so the post-finally rethrow would see
    // null and silently drop it. Persisting to a field before the finally keeps it alive. Each
    // qualifying construct gets its own field rather than sharing one: a nested persisting construct
    // inside the finally body would otherwise clobber the outer's captured exception.
    private int _caughtExceptionFieldCounter;

    private FieldBuilder DefineCaughtExceptionField() =>
        _builder.StateMachineType.DefineField(
            $"<>caughtException{_caughtExceptionFieldCounter++}", typeof(object), FieldAttributes.Private);

    // Companion to `<>caughtException{n}`: the exception-present flag (#619) that must likewise survive
    // a *yielding* finally in a catch-less try/finally. Gating the catch/rethrow on this boolean rather
    // than the captured value's nullness is what lets a thrown null/undefined be caught (a null CLR ref
    // would otherwise read as "no exception"). Own counter so each construct gets a distinct field.
    private int _exceptionPresentFieldCounter;

    private FieldBuilder DefineExceptionPresentField() =>
        _builder.StateMachineType.DefineField(
            $"<>exceptionPresent{_exceptionPresentFieldCounter++}", typeof(bool), FieldAttributes.Private);

    // ---- Throw routing (generator-specific) -----------------------------------------------------
    // The loop-scope methods and break/continue (with their finally routing) are inherited from
    // StateMachineExitRoutingEmitter. A generator additionally routes a `throw` escaping a catch /
    // finally body through the enclosing flag-based finally(s) to the correct handler:

    /// <summary>
    /// A <c>throw</c> in a catch or finally body propagates to the enclosing flag-based try (the one
    /// whose body lexically contains this handler): it runs the finally(s) inside that try, then lands
    /// in its catch — rather than a real IL <c>throw</c> that bypasses the flag-based catch (#632). With
    /// no enclosing flag-based try it runs the active finally(s) and propagates out of MoveNext. A throw
    /// in a try body is captured by its sync-segment mini try/catch (handled by the catch arm), not here.
    /// </summary>
    protected override void EmitThrow(Stmt.Throw t)
    {
        if (_inHandlerBody && _protectedRegionDepth == 0)
        {
            if (_tryBodyContext is { } encl)
            {
                EmitThrowIntoEnclosingTry(encl, () => { EmitExpression(t.Value); EnsureBoxed(); });
                return;
            }

            // No enclosing flag-based try (this handler belongs to the outermost try), but its own
            // finally may still be active and must run before the throw leaves MoveNext.
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
    /// Propagates a guest exception escaping a handler body into the enclosing flag-based try
    /// <paramref name="encl"/>: stores the value into that try's capture local and sets its present
    /// flag, then branches to its cleanup entry so its catch runs (or, catch-less, its finally then its
    /// own propagation). Any finally(s) strictly inside that try run first; because such a finally can
    /// yield, the value is held in <c>&lt;&gt;pendingException</c> across them and moved into the capture
    /// local by the routing terminal. This is the catch-side analog of the finally routing already used
    /// for a routed return/throw (#632). <paramref name="loadValue"/> pushes the boxed guest value.
    /// </summary>
    private void EmitThrowIntoEnclosingTry((Label AfterTryBody, LocalBuilder CaughtException, LocalBuilder ExceptionPresent, int ScopeDepth) encl, Action loadValue)
    {
        var chain = FinallyFramesInside(encl.ScopeDepth);
        if (chain.Count == 0)
        {
            // No intervening finally: store straight into the enclosing try and branch to its catch.
            loadValue();
            _il.Emit(OpCodes.Stloc, encl.CaughtException);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, encl.ExceptionPresent);
            _il.Emit(OpCodes.Br, encl.AfterTryBody);
            return;
        }

        // Intervening finally(s) may yield, so hold the value in a field across them; the routing
        // terminal moves it into the enclosing try's capture local and branches to its catch.
        _il.Emit(OpCodes.Ldarg_0);
        loadValue();
        _il.Emit(OpCodes.Stfld, GetPendingExceptionField());
        int code = _nextExitCode++;
        _exitTerminals[code] = () =>
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, GetPendingExceptionField());
            _il.Emit(OpCodes.Stloc, encl.CaughtException);
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, encl.ExceptionPresent);
            _il.Emit(OpCodes.Br, encl.AfterTryBody);
        };
        RouteThroughFinallys(chain, code, OpCodes.Br);
    }

    // ---- Routing helpers ------------------------------------------------------------------------

    /// <summary>
    /// The finally scopes strictly inside the flag-based try whose body began at <paramref
    /// name="scopeDepth"/> (= <c>_exitScopes.Count</c> at that point), innermost first. Excludes the
    /// try's own finally (which lives just below scopeDepth) and everything outside it. These are the
    /// finallys a throw escaping a nested handler must run before reaching that try's catch (#632).
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
        // (#555). With no yielding finally this is a no-op (Current still holds the same value).
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, GetPendingReturnValueField());
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // The generator completes. State -2 is re-asserted here because a yielding finally between the
        // `return` and this point overwrote it with the finally's resume state.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
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

    // ---- External return()/throw() injection at a suspended yield (#526) -------------------------

    /// <summary>
    /// At a yield resume point, consult the injection fields a suspended generator's
    /// return()/throw() set (#526) and, if one is pending, perform that abrupt completion here —
    /// running active try/finally(/catch) — instead of resuming normally. Emits nothing that
    /// transfers control when no injection is pending, so the caller's normal-resume code runs. A
    /// no-op when the $IGenerator methods (hence the injection fields) were not emitted.
    /// </summary>
    private void EmitResumeInjectionCheck()
    {
        var kindField = _builder.InjectedKindField;
        var valueField = _builder.InjectedValueField;
        if (kindField == null || valueField == null) return;

        void LoadInjectedValue()
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, valueField);
        }

        // return(v): behaves as `return v` at this point (consume the kind first so a yielding
        // finally that re-enters MoveNext does not re-inject).
        var afterReturn = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, kindField);
        _il.Emit(OpCodes.Ldc_I4, GeneratorStateMachineBuilder.InjectKindReturn);
        _il.Emit(OpCodes.Bne_Un, afterReturn);
        ClearInjectedKind();
        EmitRoutedReturn(LoadInjectedValue);
        _il.MarkLabel(afterReturn);

        // throw(e): behaves as `throw e` at this point.
        var afterThrow = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, kindField);
        _il.Emit(OpCodes.Ldc_I4, GeneratorStateMachineBuilder.InjectKindThrow);
        _il.Emit(OpCodes.Bne_Un, afterThrow);
        ClearInjectedKind();
        EmitRoutedThrow(LoadInjectedValue);
        _il.MarkLabel(afterThrow);
    }

    private void ClearInjectedKind()
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, GeneratorStateMachineBuilder.InjectKindNone);
        _il.Emit(OpCodes.Stfld, _builder.InjectedKindField!);
    }

    /// <summary>
    /// Emits an abrupt <c>return &lt;value&gt;</c> at a top-level resume point: store the value into
    /// Current, mark the generator done, and route through any enclosing flag-based finally(s) so
    /// they run before completion. <paramref name="loadValue"/> pushes the boxed completion value.
    /// Mirrors <see cref="EmitReturn"/>'s chain logic, but the value is supplied (not evaluated from
    /// an expression) and the resume point is always at the top level, so the route uses <c>Br</c>.
    /// </summary>
    private void EmitRoutedReturn(Action loadValue)
    {
        _il.Emit(OpCodes.Ldarg_0);
        loadValue();
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        var chain = ActiveFinallyFrames();
        if (chain.Count > 0)
        {
            // Stash the completion value: a yielding finally overwrites Current; the return terminal
            // restores it after the finally has run (#555).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.CurrentField);
            _il.Emit(OpCodes.Stfld, GetPendingReturnValueField());

            RegisterReturnTerminal();
            RouteThroughFinallys(chain, ExitCodeReturn, OpCodes.Br);
            return;
        }

        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a <c>throw &lt;value&gt;</c> at a top-level resume point (an external <c>throw()</c>
    /// injected at a suspended yield). Inside a try body it behaves as if the body threw there (store
    /// into the try's caught-exception local and branch to its cleanup, so the catch/finally run);
    /// inside a catch/finally body it propagates to the enclosing flag-based try's catch the same way a
    /// lexical handler-body throw does (#632); with no enclosing try it runs the active finally(s) and
    /// propagates out of MoveNext. <paramref name="loadValue"/> pushes the boxed guest error value.
    /// </summary>
    private void EmitRoutedThrow(Action loadValue)
    {
        if (_tryBodyContext is { } ctx)
        {
            if (!_inHandlerBody)
            {
                // In a try body: capture exactly like a try-body exception so the catch/finally at
                // afterTryBodyLabel handle it. A catch-less yielding finally persists this local to a
                // field before suspending, so it survives (#599). Set the present flag (not the value's
                // nullness) so an injected throw(null)/throw(undefined) still engages the catch (#619).
                loadValue();
                _il.Emit(OpCodes.Stloc, ctx.CaughtException);
                _il.Emit(OpCodes.Ldc_I4_1);
                _il.Emit(OpCodes.Stloc, ctx.ExceptionPresent);
                _il.Emit(OpCodes.Br, ctx.AfterTryBody);
                return;
            }

            // In a catch/finally body: run the finally(s) inside the enclosing try, then land in its
            // catch — the injection-path analog of the lexical handler-body throw fix (#632).
            EmitThrowIntoEnclosingTry(ctx, loadValue);
            return;
        }

        var chain = ActiveFinallyFrames();
        if (chain.Count > 0)
        {
            // Outermost try's catch/finally body: run its own finally(s), then rethrow at the terminal.
            _il.Emit(OpCodes.Ldarg_0);
            loadValue();
            _il.Emit(OpCodes.Stfld, GetPendingExceptionField());
            RegisterThrowTerminal();
            RouteThroughFinallys(chain, ExitCodeThrow, OpCodes.Br);
            return;
        }

        // No enclosing try: mark done and propagate out of MoveNext.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        loadValue();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits try/catch/finally. When a yield crosses the protected region, real IL exception
    /// blocks cannot be used (the state-dispatch switch can't branch into a protected region,
    /// and `yield`'s `ret` is illegal inside one), so a flag-based scheme is emitted instead.
    /// </summary>
    protected override void EmitTryCatch(Stmt.TryCatch t)
    {
        bool hasYields = AnyStmtContainsSuspension(t.TryBlock)
            || (t.CatchBlock != null && AnyStmtContainsSuspension(t.CatchBlock))
            || (t.FinallyBlock != null && AnyStmtContainsSuspension(t.FinallyBlock));

        // A return/break/continue lexically inside the finally body can never be lowered with the
        // real-IL path: none of `ret`/`br`/`Leave` may exit a .NET `finally` region, so it would emit
        // invalid IL (LeaveOutOfFinally). Route the whole construct through the flag-based scheme even
        // with no yield, so the finally is emitted as top-level statements and the exit is dispatched
        // legally (#598, the finally-side analog of #554, which handles exits in the try/catch body).
        // An exit targeting a loop *inside* the finally stays local and does not count as escaping.
        bool finallyHasEscapingExit = t.FinallyBlock != null
            && ContainsEscapingExit2(t.FinallyBlock, insideLoop: false, insideSwitch: false);

        if (hasYields || finallyHasEscapingExit)
            EmitTryCatchWithYields(t);
        else
            EmitSimpleTryCatch(t);
    }

    /// <summary>
    /// No yield crosses the protected region — real IL exception blocks are correct and cheapest.
    /// This is the original generator try/catch emission, unchanged.
    /// </summary>
    private void EmitSimpleTryCatch(Stmt.TryCatch t)
    {
        // A real IL protected region is open. A `br`/`ret` directly out of it is illegal, so a
        // non-local exit crossing it must use `Leave` instead — which also runs this (no-yield)
        // finally. _protectedRegionDepth tells the exit overrides a real block is open (so they pick
        // `Leave` and, when also inside flag-based finally(s), route out via the innermost flag
        // cleanup); ExceptionBlockDepth drives the Leave-vs-Br choice in EmitBranchToLabel. The latter
        // is incremented only here (not in the flag path's sync segments) so internal branches inside
        // a sync segment stay `Br` and do not illegally leave the mini try/catch.
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
                // Stack has the .NET exception; wrap to the TS value and bind to the catch param.
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
    /// Binds the caught exception value (on the IL stack) to the catch parameter, honouring
    /// whether the parameter was hoisted to a state-machine field (used across a yield) or lives
    /// in an IL local. Storing to a fresh local unconditionally — the previous behaviour — lost
    /// the value whenever the catch parameter was hoisted, because reads resolve the field first.
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
    /// Flag-based try/catch/finally for the case where a yield (or yield*) lives inside the
    /// protected region. Synchronous segments of the try body are wrapped in mini IL try/catch
    /// blocks that capture any exception into a flag local; suspension points and non-local exits
    /// are emitted at the top level (outside any protected region) so their resume labels are
    /// reachable from the state-dispatch switch and their `ret`/`br` are legal.
    /// </summary>
    private void EmitTryCatchWithYields(Stmt.TryCatch t)
    {
        var caughtExceptionLocal = _il.DeclareLocal(typeof(object));
        // Whether the try body raised an exception, tracked separately from caughtExceptionLocal's
        // nullness: a thrown null/undefined captures as a null CLR reference, which a value-nullness
        // gate misreads as "no exception" — skipping the catch and dropping the post-finally rethrow
        // (#619). This flag records presence regardless of the captured value.
        var exceptionPresentLocal = _il.DeclareLocal(typeof(bool));
        var afterTryBodyLabel = _il.DefineLabel();

        // #599: in a try/finally with no catch whose finally can yield, the captured try-body
        // exception must survive the finally's suspension. The IL local resets on MoveNext re-entry,
        // so persist it to a dedicated field before the finally and read that field in the
        // post-finally rethrow. Allocated only for that shape; null means "use the local". The
        // present flag needs the same persistence (read by the rethrow gate after the finally, #619).
        bool persistAcrossYieldingFinally =
            t.CatchBlock == null && t.FinallyBlock != null && AnyStmtContainsSuspension(t.FinallyBlock);
        FieldBuilder? caughtExceptionField = persistAcrossYieldingFinally ? DefineCaughtExceptionField() : null;
        FieldBuilder? exceptionPresentField = persistAcrossYieldingFinally ? DefineExceptionPresentField() : null;

        void EmitLoadCaughtException()
        {
            if (caughtExceptionField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, caughtExceptionField);
            }
            else
            {
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
            }
        }

        void EmitLoadExceptionPresent()
        {
            if (exceptionPresentField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, exceptionPresentField);
            }
            else
            {
                _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
            }
        }

        // No exception captured yet.
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, exceptionPresentLocal);

        // A non-local exit inside this try (or its catch) must run the finally before transferring
        // control, so register a finally scope whose cleanup is the catch/finally entry. On those exit
        // paths the exception flag is null, so the catch is skipped and the finally runs. Without a
        // finally there is nothing to route through, so no scope is pushed and exits go directly.
        FinallyScope? frame = null;
        if (t.FinallyBlock != null)
        {
            frame = new FinallyScope { CleanupLabel = afterTryBodyLabel };
            _exitScopes.Add(frame);
        }

        // Throws in the try body are captured by their sync segments, not routed. While emitting the
        // body, expose this try as the injected-throw target so an external throw() at a yield here
        // engages this try's catch/finally (#526).
        bool previousInHandler = _inHandlerBody;
        var previousTryBody = _tryBodyContext;
        _inHandlerBody = false;
        _tryBodyContext = (afterTryBodyLabel, caughtExceptionLocal, exceptionPresentLocal, _exitScopes.Count);
        EmitTryBodyWithYields(t.TryBlock, caughtExceptionLocal, exceptionPresentLocal, afterTryBodyLabel);
        _tryBodyContext = previousTryBody;
        _inHandlerBody = previousInHandler;

        _il.MarkLabel(afterTryBodyLabel);

        // Catch: runs only when the try body captured an exception. The finally scope is still open, so
        // a non-local exit (including a throw) from the catch body runs this finally too.
        if (t.CatchBlock != null)
        {
            // Gate on the present flag, not the value's nullness, so a caught null/undefined enters the
            // catch (#619). In the with-catch shape no field is allocated, so the local is authoritative.
            var skipCatchLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
            _il.Emit(OpCodes.Brfalse, skipCatchLabel);

            if (t.CatchParam != null)
            {
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                StoreCaughtExceptionToParam(t.CatchParam.Lexeme);
            }

            // Catch handles it; clear the present flag so the post-finally rethrow below is skipped —
            // and so a routed exit re-entering afterTryBodyLabel skips the catch rather than re-running
            // it. The flag (not the value) is the gate now, so clearing it is what matters (#619).
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
            // #599: persist the captured exception (null on the normal/routed-exit paths) before the
            // finally so a suspension inside it does not wipe the IL local out from under the rethrow.
            if (caughtExceptionField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Stfld, caughtExceptionField);

                // Persist the present flag alongside the value so the post-finally rethrow gate reads a
                // live flag after a yielding finally (#619, same survival reason as the value, #599).
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
                _il.Emit(OpCodes.Stfld, exceptionPresentField!);
            }

            _inHandlerBody = true;
            EmitHandlerBodyWithCapture(t.FinallyBlock);
            _inHandlerBody = previousInHandler;

            // After the finally, dispatch any pending non-local exit that routed through here. (A real
            // exception arising in the finally body itself was already routed out by
            // EmitHandlerBodyWithCapture, superseding any pending exit, so this is reached only on the
            // finally's normal completion.)
            EmitFinallyDispatch(frame!);
        }

        // Propagate an uncaught exception once the finally has run (try/finally with no catch).
        if (t.CatchBlock == null)
        {
            var noExceptionLabel = _il.DefineLabel();
            EmitLoadExceptionPresent();
            _il.Emit(OpCodes.Brfalse, noExceptionLabel);

            if (_tryBodyContext is { } encl)
            {
                // The finally has run; the still-uncaught exception now propagates to the enclosing
                // flag-based try's catch (not out of MoveNext), so an outer catch still handles it — the
                // try/finally analog of the handler-body throw routing (#632).
                EmitThrowIntoEnclosingTry(encl, EmitLoadCaughtException);
            }
            else
            {
                // No enclosing flag-based try: mark the generator done and propagate out of MoveNext.
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldc_I4, -2);
                _il.Emit(OpCodes.Stfld, _builder.StateField);
                EmitLoadCaughtException();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
                _il.Emit(OpCodes.Throw);
            }

            _il.MarkLabel(noExceptionLabel);
        }
    }

    /// <summary>
    /// Walks the try body, wrapping runs of plain statements in mini IL try/catch blocks while
    /// emitting suspension points and non-local exits (return/break/continue) at the top level.
    /// </summary>
    private void EmitTryBodyWithYields(List<Stmt> tryBody, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal, Label afterTryLabel)
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
                // Gate on the present flag so a thrown null/undefined still short-circuits here (#619).
                _il.Emit(OpCodes.Ldloc, exceptionPresentLocal);
                _il.Emit(OpCodes.Brtrue, afterTryLabel);

                // Emitted at the top level: a yield's `ret`/resume label and a return's `br` are
                // only legal outside a protected region.
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
        // a prior thrown null/undefined still suppresses this segment (#619).
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
        // as "no exception" at the gates above (#619).
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Stloc, exceptionPresentLocal);
        _il.EndExceptionBlock();
        _protectedRegionDepth--;

        _il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits a flag-based <c>catch</c>/<c>finally</c> body, capturing any real CLR exception that arises
    /// at its top level and routing it to the enclosing flag-based try (or out of MoveNext when there is
    /// none) instead of letting it escape the state machine unhandled. The motivating case is an
    /// exception escaping a nested no-yield (real IL) <c>try</c>/<c>catch</c> whose handler throws: that
    /// throw is correctly a real IL <c>throw</c> (it is inside a real protected region), so the lexical
    /// <see cref="EmitThrow"/> routing never sees it; once it leaves the nested block it was previously
    /// in flight in an unprotected region and bypassed the enclosing flag-based catch (#675). A runtime
    /// error (e.g. a call on <c>undefined</c>) at the handler's top level is covered the same way.
    ///
    /// <para>Mirrors <see cref="EmitTryBodyWithYields"/>: runs of plain statements are wrapped in mini IL
    /// try/catch segments (<see cref="EmitSyncSegmentInTry"/>) that record a thrown exception into a
    /// handler-local flag, while suspension points and non-local exits stay at the top level so their
    /// resume labels / branches remain legal. After the body, a captured exception is propagated by
    /// <see cref="EmitRoutedThrow"/> — the same routing used for a lexical handler-body <c>throw</c>:
    /// it runs the finally(s) inside the enclosing try, then lands in its catch. A lexical <c>throw</c>
    /// reached at the handler's top level (e.g. after a yield, outside any segment) is still routed
    /// directly by <see cref="EmitThrow"/>; this method only adds coverage for exceptions that arrive
    /// already in flight, which no <c>throw</c> statement intercepts. Caller sets <c>_inHandlerBody</c>.</para>
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

        // A real exception captured in the handler body propagates to the enclosing flag-based try's
        // catch (running the finally(s) inside that try first), or out of MoveNext when there is none —
        // exactly the routing EmitRoutedThrow performs for an in-flight handler-body throw. Gating on the
        // present flag (not the value's nullness) keeps a captured null/undefined routable (#619).
        var noHandlerException = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, handlerPresent);
        _il.Emit(OpCodes.Brfalse, noHandlerException);
        EmitRoutedThrow(() => _il.Emit(OpCodes.Ldloc, handlerCaught));
        _il.MarkLabel(noHandlerException);
    }

    #region Suspension / control-exit detection

    /// <summary>
    /// A statement must be emitted at the top level (rather than inside a mini try/catch segment)
    /// if it contains a suspension point or a control-flow exit that leaves the try region. Both
    /// would otherwise produce illegal IL inside the segment's protected region.
    /// </summary>
    private static bool IsSegmentBreaker(Stmt stmt) =>
        StmtContainsSuspension(stmt) || ContainsEscapingExit(stmt, insideLoop: false, insideSwitch: false);

    // The yield-suspension statement/expression walkers now live in ExpressionEmitterBase as the single
    // StmtContainsSuspension/ExprContainsSuspension pair shared by every state-machine family (#1121); the
    // three hand-maintained copies had repeatedly drifted into illegal-BranchIntoTry bugs (#631/#850/#914).

    // ContainsEscapingExit / ContainsEscapingExit2 are shared across the suspension-aware emitters and
    // live in StatementEmitterBase (the generator, async-generator, and async-function emitters all
    // segment a flag-based try body around non-local exits using the same conservative analysis).

    #endregion
}
