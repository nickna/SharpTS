using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class AsyncFunctionMoveNextEmitter
{
    private sealed class AwaitCatchRegion
    {
        public List<(int State, Label Target)> Resumes { get; } = [];
    }

    private AwaitCatchRegion? _awaitCatchRegion;
    private int _finallyExceptionCount;

    protected override void EmitPendingExitBranch(Label target) => EmitBranchToLabel(target);

    // Resumable protected regions normally own throws from a try or catch body.
    // Keep explicit routing for a guard-free span with an active logical handler:
    // it must run intervening flag-based finally scopes before catch dispatch.

    // A real guest handler already owns throws while this is positive. Kept
    // separate from ExceptionBlockDepth, which selects Leave for non-local
    // exits, because legacy suspension-free mini guards do not own loop exits.
    protected int _throwRoutingGuardDepth;

    // _exitScopes.Count captured at the start of the flag-based try body whose exception local is
    // _currentTryCatchExceptionLocal; selects the finally(s) strictly inside that try for a routed throw.
    private int _currentTryCatchScopeDepth;

    // <>pendingException (object): holds a routed throw value across an intervening finally that itself
    // suspends, until the routing terminal moves it into the try's exception local.
    private FieldBuilder? _pendingExceptionField;
    private FieldBuilder GetPendingExceptionField() =>
        _pendingExceptionField ??= DefineStateMachineField("<>pendingException", typeof(object));

    protected override void EmitThrow(Stmt.Throw t)
    {
        // Only a top-level throw inside a flag-based try body reaches here un-guarded; route it to that
        // try's catch. Everything else (guarded throws, handler-body throws, throws with no enclosing
        // flag-based try) falls through to the base raw throw, which the relevant real-IL handler or the
        // outer MoveNext catch (→ Task fault) already handles.
        if (_currentTryCatchExceptionLocal != null && _currentTryCatchSkipLabel != null
            && _throwRoutingGuardDepth == 0)
        {
            RouteThrowIntoCurrentTry(() => { EmitExpression(t.Value); EnsureBoxed(); });
            return;
        }

        base.EmitThrow(t);
    }

    /// <summary>
    /// Routes a guest throw escaping the top level of a flag-based try body into that try: stores the
    /// value into its exception local and branches to its catch dispatch (<see
    /// cref="_currentTryCatchSkipLabel"/>), setting presence independently from a possibly-null value.
    /// Any finally(s) strictly inside the try run first; because such a finally can await, the
    /// value is held in <c>&lt;&gt;pendingException</c> across them and moved into the exception local by
    /// the routing terminal (#914, the async-function analog of the generators' throw routing).
    /// </summary>
    private void RouteThrowIntoCurrentTry(Action loadValue)
    {
        var exLocal = _currentTryCatchExceptionLocal!;
        var presentLocal = _currentTryCatchExceptionPresentLocal!;
        var skip = _currentTryCatchSkipLabel!.Value;
        var chain = FinallyFramesInside(_currentTryCatchScopeDepth);
        if (chain.Count == 0)
        {
            loadValue();
            IL.Emit(OpCodes.Stloc, exLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Stloc, presentLocal);
            EmitPendingExitBranch(skip);
            return;
        }

        // Intervening finally(s) may await, so hold the value in a field across them; the routing terminal
        // moves it into the try's exception local and branches to its catch dispatch.
        IL.Emit(OpCodes.Ldarg_0);
        loadValue();
        IL.Emit(OpCodes.Stfld, GetPendingExceptionField());
        int code = _nextExitCode++;
        _iteratorCloseThrowExits.Add(code);
        _exitTerminals[code] = () =>
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, GetPendingExceptionField());
            IL.Emit(OpCodes.Stloc, exLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Stloc, presentLocal);
            EmitPendingExitBranch(skip);
        };
        RouteThroughFinallys(chain, code, ProtectedRegionDepth > 0 ? OpCodes.Leave : OpCodes.Br);
    }

    protected override void EmitTryCatch(Stmt.TryCatch t)
    {
        // Check if this try block contains any await points
        bool hasAwaitsInTry = AnyStmtContainsSuspension(t.TryBlock);
        bool hasAwaitsInCatch = t.CatchBlock != null && AnyStmtContainsSuspension(t.CatchBlock);
        bool hasAwaitsInFinally = t.FinallyBlock != null && AnyStmtContainsSuspension(t.FinallyBlock);

        // Even a wholly synchronous finally needs the flag-based exit routing in
        // an async state machine.  Completing the Task from a return inside a
        // real IL try/finally publishes the result before the finally runs; if
        // that finally throws, the outer MoveNext catch then attempts to fault an
        // already-completed Task.  Routing every async try/finally through the
        // pending-exit machinery defers completion until after the finally.
        if (hasAwaitsInTry || hasAwaitsInCatch || hasAwaitsInFinally || t.FinallyBlock != null)
        {
            // Complex case: await inside protected region
            EmitTryCatchWithAwaits(t, hasAwaitsInTry, hasAwaitsInCatch, hasAwaitsInFinally);
        }
        else
        {
            // Simple case: no awaits in protected regions
            EmitSimpleTryCatch(t);
        }
    }

    private void EmitSimpleTryCatch(Stmt.TryCatch t)
    {
        // A real IL protected region is open across the whole try/catch/finally. A `br`/`ret` directly
        // out of it is illegal, so a non-local break/continue crossing it must use `Leave` instead —
        // which also runs this (no-await) finally. ExceptionBlockDepth drives the Leave-vs-Br choice in
        // EmitBranchToLabel; it is incremented only here (not in the flag path's mini try/catch
        // segments), so a break targeting a loop nested inside the try stays a legal in-region `Br`
        // while an escaping break/continue leaves via `Leave` (#727).
        Ctx.ExceptionBlockDepth++;
        _throwRoutingGuardDepth++;
        IL.BeginExceptionBlock();

        // Emit try block statements
        EmitGeneratorScopedStatements(t.TryBlock);

        // Emit catch block if present
        if (t.CatchBlock != null)
        {
            IL.BeginCatchBlock(typeof(Exception));
            var caught = IL.DeclareLocal(Types.Object);
            if (t.CatchParam != null)
            {
                IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.WrapException);
                IL.Emit(OpCodes.Stloc, caught);
            }
            else IL.Emit(OpCodes.Pop);
            WithGeneratorLexicalScope(t.CatchBlock, () =>
            {
                EmitGeneratorCatchBinding(t, caught);
                foreach (var statement in t.CatchBlock) EmitStatement(statement);
            }, t, t.CatchParam);
        }

        // Emit finally block if present
        if (t.FinallyBlock != null)
        {
            IL.BeginFinallyBlock();
            EmitGeneratorScopedStatements(t.FinallyBlock);
        }

        IL.EndExceptionBlock();
        Ctx.ExceptionBlockDepth--;
        _throwRoutingGuardDepth--;
    }

    private void EmitGeneratorCatchBinding(Stmt.TryCatch declaration, LocalBuilder caught)
    {
        if (declaration.CatchParam is not { } parameter) return;
        IL.Emit(OpCodes.Ldloc, caught);
        if (TryInitializeLexicalCell(parameter)) return;
        string name = GetBindingStorageName(declaration, parameter.Lexeme);
        if (GetHoistedVariableField(name) == null)
        {
            // A suspending catch uses its existing field. Registering an IL local
            // would make async-arrow reads and writes bypass that field (#2850).
            var local = IL.DeclareLocal(Types.Object);
            Ctx.Locals.RegisterLocal(name, local);
            RegisterLoopLocal(name, local);
        }
        EmitStoreVariable(name);
    }

    private void EmitTryCatchWithAwaits(Stmt.TryCatch t, bool hasAwaitsInTry, bool hasAwaitsInCatch, bool hasAwaitsInFinally)
    {
        // Real handlers capture try/catch failures, while explicit dispatch
        // keeps catch and finally bodies resumable and routes pending exits.

        // Create locals for exception tracking
        var caughtExceptionLocal = IL.DeclareLocal(typeof(object));
        var exceptionPresentLocal = IL.DeclareLocal(typeof(bool));
        var skipCatchLabel = IL.DefineLabel();
        var afterTryCatchLabel = IL.DefineLabel();

        // A null guest value is a real failure, not the absence of one.
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Stloc, caughtExceptionLocal);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Stloc, exceptionPresentLocal);

        // A finally must run on every path that leaves the try — including a non-local break / continue
        // / return that crosses it. Push a FinallyScope before emitting the try body so those exits
        // (emitted at the top level by EmitTryBodyWithAwaits / EmitReturn) route through it before
        // transferring control; the cleanup label is the finally's entry. The frame stays open across
        // the catch too, so an exit from the catch body runs this finally as well (#774).
        FinallyScope? frame = null;
        Label cleanupLabel = default;
        if (t.FinallyBlock != null)
        {
            cleanupLabel = IL.DefineLabel();
            frame = new FinallyScope { CleanupLabel = cleanupLabel };
            _exitScopes.Add(frame);
        }

        if (hasAwaitsInTry)
        {
            // Resume gateways keep the complete try body protected.
            EmitTryBodyWithAwaits(t.TryBlock, caughtExceptionLocal, exceptionPresentLocal);
        }
        else if (t.FinallyBlock != null)
        {
            // No awaits in try, but a finally still needs us to capture an exception from try
            // so the flag-based path can run the finally before rethrowing. The try body runs inside a real
            // IL exception block, so a non-local exit crossing it must Leave, not Br — bump
            // ExceptionBlockDepth so EmitBranchToLabel / the routing pick Leave to the cleanup (#774).
            Ctx.ExceptionBlockDepth++;
            _throwRoutingGuardDepth++;
            IL.BeginExceptionBlock();
            EmitGeneratorScopedStatements(t.TryBlock);

            // Always catch to capture exception for finally handling
            IL.BeginCatchBlock(typeof(Exception));
            IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.WrapException);
            IL.Emit(OpCodes.Stloc, caughtExceptionLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Stloc, exceptionPresentLocal);

            IL.EndExceptionBlock();
            Ctx.ExceptionBlockDepth--;
            _throwRoutingGuardDepth--;
        }
        else
        {
            // No awaits in try or finally - use normal try block. Same real-IL-block reasoning as above:
            // bump ExceptionBlockDepth so a non-local exit crossing this try Leaves to the cleanup (#774).
            Ctx.ExceptionBlockDepth++;
            _throwRoutingGuardDepth++;
            IL.BeginExceptionBlock();
            EmitGeneratorScopedStatements(t.TryBlock);

            if (t.CatchBlock != null)
            {
                IL.BeginCatchBlock(typeof(Exception));
                IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.WrapException);
                IL.Emit(OpCodes.Stloc, caughtExceptionLocal);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, exceptionPresentLocal);
            }
            IL.EndExceptionBlock();
            Ctx.ExceptionBlockDepth--;
            _throwRoutingGuardDepth--;
        }

        // Cleanup entry: normal completion falls here, and a routed non-local exit branches here. The
        // presence flag remains false on a non-exception exit, so those paths skip the catch and
        // flow straight into the finally (#774).
        if (frame != null)
            IL.MarkLabel(cleanupLabel);

        // Check if we need to run catch block
        if (t.CatchBlock != null)
        {
            IL.Emit(OpCodes.Ldloc, exceptionPresentLocal);
            IL.Emit(OpCodes.Brfalse, skipCatchLabel);

            WithGeneratorLexicalScope([], () =>
            {
                EmitGeneratorCatchBinding(t, caughtExceptionLocal);

                // The catch handled this failure; a new catch-body failure sets presence again.
                IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Stloc, caughtExceptionLocal);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Stloc, exceptionPresentLocal);

                // A catch-body failure must run this try's finally before it
                // propagates, without re-entering this same catch. Resume gateways
                // protect its operand evaluation just as they protect the try body.
                EmitTryBodyWithAwaits(t.CatchBlock, caughtExceptionLocal, exceptionPresentLocal);
            }, t, t.CatchParam);

            // Fall through to finally (don't skip it)
            IL.MarkLabel(skipCatchLabel);
        }

        // The finally itself is outside its own scope: an exit within the finally body runs the
        // *enclosing* finallys, not this one. Pop before emitting the body (#774).
        if (frame != null)
            _exitScopes.RemoveAt(_exitScopes.Count - 1);

        // Finally block - must always execute
        if (t.FinallyBlock != null)
        {
            if (hasAwaitsInFinally)
            {
                // Finally with awaits - need special handling
                // The finally must run regardless of exception, so we emit it
                // and track if there's a pending exception to rethrow after
                EmitFinallyBodyWithAwaits(t.FinallyBlock, caughtExceptionLocal, exceptionPresentLocal);
            }
            else
            {
                // No awaits in finally - emit directly
                EmitGeneratorScopedStatements(t.FinallyBlock);
            }

            // Dispatch any pending non-local exit (return / break / continue) that routed through this
            // finally: run the terminal action, or chain to the next outer finally (#774). On a normal
            // or exception path <>pendingExit is 0 and this falls through unchanged.
            EmitFinallyDispatch(frame!);

        }

        // Entering a catch clears presence. An unhandled try failure or a new catch-body failure
        // must propagate even when its exact value is null.
        var noExceptionLabel = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, exceptionPresentLocal);
        IL.Emit(OpCodes.Brfalse, noExceptionLabel);
        IL.Emit(OpCodes.Ldloc, caughtExceptionLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.CreateException);
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(noExceptionLabel);

        IL.MarkLabel(afterTryCatchLabel);
    }

    private void EmitFinallyBodyWithAwaits(List<Stmt> finallyBody, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal)
    {
        // Unlike try, we don't wrap in try/catch - finally must run even if
        // statements throw, but a finally failure replaces the original one.
        // Preserve the pending failure across a genuine MoveNext resumption.

        int id = _finallyExceptionCount++;
        var pendingException = DefineStateMachineField($"<>finallyException{id}", typeof(object));
        var pendingExceptionPresent = DefineStateMachineField($"<>finallyExceptionPresent{id}", typeof(bool));
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, caughtExceptionLocal);
        IL.Emit(OpCodes.Stfld, pendingException);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, exceptionPresentLocal);
        IL.Emit(OpCodes.Stfld, pendingExceptionPresent);
        EmitGeneratorScopedStatements(finallyBody);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, pendingException);
        IL.Emit(OpCodes.Stloc, caughtExceptionLocal);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, pendingExceptionPresent);
        IL.Emit(OpCodes.Stloc, exceptionPresentLocal);
    }

    private void EmitTryBodyWithAwaits(List<Stmt> tryBody, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal)
        => WithGeneratorLexicalScope(tryBody, () => EmitTryBodyWithAwaitsCore(tryBody, caughtExceptionLocal, exceptionPresentLocal));

    private void EmitTryBodyWithAwaitsCore(List<Stmt> tryBody, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal)
        => EmitTryBodyWithAwaitsCore(() =>
        {
            foreach (var stmt in tryBody)
                EmitStatement(stmt);
        }, caughtExceptionLocal, exceptionPresentLocal);

    // The same resumable protected region can guard a generated iterator-close span.
    private void EmitTryBodyWithAwaitsCore(Action emitBody, LocalBuilder caughtExceptionLocal, LocalBuilder exceptionPresentLocal)
    {
        var previousExceptionLocal = _currentTryCatchExceptionLocal;
        var previousExceptionPresentLocal = _currentTryCatchExceptionPresentLocal;
        var previousSkipLabel = _currentTryCatchSkipLabel;
        var previousScopeDepth = _currentTryCatchScopeDepth;
        var previousRegion = _awaitCatchRegion;
        var region = new AwaitCatchRegion();
        var entry = IL.DefineLabel();
        var initial = IL.DefineLabel();
        var dispatch = IL.DefineLabel();
        var afterTryLabel = IL.DefineLabel();
        _currentTryCatchExceptionLocal = caughtExceptionLocal;
        _currentTryCatchExceptionPresentLocal = exceptionPresentLocal;
        _currentTryCatchSkipLabel = afterTryLabel;
        _currentTryCatchScopeDepth = _exitScopes.Count;

        // The global state switch enters through an instruction outside this
        // protected region. Dispatch to the actual resume point only after
        // entering normally, so operand evaluation and GetResult share the
        // guest handler without any branch into a CLR try.
        IL.MarkLabel(entry);
        IL.Emit(OpCodes.Nop);
        IL.BeginExceptionBlock();
        Ctx.ExceptionBlockDepth++;
        _throwRoutingGuardDepth++;
        _awaitCatchRegion = region;
        IL.Emit(OpCodes.Br, dispatch);
        IL.MarkLabel(initial);
        emitBody();
        IL.Emit(OpCodes.Leave, afterTryLabel);

        IL.MarkLabel(dispatch);
        foreach (var (state, target) in region.Resumes)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, AsyncStateField);
            IL.Emit(OpCodes.Ldc_I4, state);
            IL.Emit(OpCodes.Beq, target);
        }
        IL.Emit(OpCodes.Br, initial);

        _awaitCatchRegion = previousRegion;
        _throwRoutingGuardDepth--;
        Ctx.ExceptionBlockDepth--;

        IL.BeginCatchBlock(typeof(Exception));
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Errors.WrapException);
        IL.Emit(OpCodes.Stloc, caughtExceptionLocal);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Stloc, exceptionPresentLocal);
        IL.EndExceptionBlock();
        IL.Emit(OpCodes.Br, afterTryLabel);

        // A nested region registers these gateways in its enclosing region;
        // only the outermost region marks the global switch's labels.
        foreach (var (state, _) in region.Resumes)
        {
            MarkAwaitResumeLabel(state);
            IL.Emit(OpCodes.Br, entry);
        }
        IL.MarkLabel(afterTryLabel);

        _currentTryCatchExceptionLocal = previousExceptionLocal;
        _currentTryCatchExceptionPresentLocal = previousExceptionPresentLocal;
        _currentTryCatchSkipLabel = previousSkipLabel;
        _currentTryCatchScopeDepth = previousScopeDepth;
    }

    // The await-suspension statement/expression walkers now live in ExpressionEmitterBase as the single
    // StmtContainsSuspension/ExprContainsSuspension pair shared by every state-machine family (#1121); the
    // three hand-maintained copies had repeatedly drifted into illegal-BranchIntoTry bugs (#631/#850/#914).
}
