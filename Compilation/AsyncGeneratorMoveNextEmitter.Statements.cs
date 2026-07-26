using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    // Per-method counter giving each for await…of loop a unique hoisted iterator field name (#697).
    private int _forAwaitCounter;

    // EmitStatement: inherited from StatementEmitterBase (handles all statement types
    // including Using, DeclareModule, DeclareGlobal that were previously missing here)

    protected override FieldBuilder? GetHoistedVariableField(string name) => _builder.GetVariableField(name);

    protected override void EmitReturn(Stmt.Return r)
    {
        // Async generator return — store the completion value in Current (read back as next().value when
        // done), then complete the state machine, running any enclosing finally(s) first.
        if (r.Value != null)
        {
            // Evaluate fully before touching the frame: the value may itself contain a yield/await
            // whose suspension `ret`s out, which is only legal with an empty evaluation stack (so the
            // `this` for the Stfld is loaded only after the value is in a local).
            EmitExpression(r.Value);
            EnsureBoxed();
            var returnValueTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, returnValueTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, returnValueTemp);
            _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        }
        else
        {
            // Bare `return;` completes with `undefined`. Store the `$Undefined` sentinel into Current so
            // the completion value is undefined, not the stale last-yielded value (#481/#540).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
            _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        }

        // Set state to -2 (completed). The direct path below returns immediately; a routed or deferred
        // return re-asserts this at its terminal / landing pad, since a yielding finally between here and
        // there would overwrite it with the finally's resume state.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // A non-local `return` must run any enclosing flag-based finally(s) before the generator
        // completes. See AsyncGeneratorMoveNextEmitter.Statements.TryCatch.cs.
        var chain = ActiveFinallyFrames();
        if (chain.Count > 0)
        {
            // Stash the completion value: a finally that yields overwrites Current with its yielded
            // value, and the return terminal restores it from here after the finally has run (#597,
            // async analog of #555). From inside a real IL block the route uses `Leave` (running that
            // block's no-yield finally) to reach the innermost flag cleanup label (#597/#554).
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.CurrentField);
            _il.Emit(OpCodes.Stfld, GetPendingReturnValueField());

            RegisterReturnTerminal();
            RouteThroughFinallys(chain, ExitCodeReturn, _protectedRegionDepth > 0 ? OpCodes.Leave : OpCodes.Br);
            return;
        }

        if (_protectedRegionDepth > 0)
        {
            // Inside a real IL try/finally with no enclosing flag finally: completing directly (a
            // `ret`/EmitReturnValueTaskBool) is illegal inside the protected region. Current/state are
            // set above; `Leave` the deferred-return landing pad, which runs the enclosing no-yield
            // finally(s) and returns false (#597, async analog of #554).
            _deferredReturnUsed = true;
            _il.Emit(OpCodes.Leave, _deferredReturnLabel);
            return;
        }

        EmitReturnValueTaskBool(false);
    }

    // EmitIf, EmitWhile: inherited from StatementEmitterBase (identical logic)

    protected override void EmitForOf(Stmt.ForOf f)
    {
        if (f.IsAsync)
        {
            EmitForAwaitOf(f);
            return;
        }

        // Check if this loop needs a hoisted enumerator (contains yield/await)
        var enumeratorField = _builder.GetEnumeratorField(f);

        if (enumeratorField == null)
        {
            // No suspension inside this loop - delegate to base (uses
            // DeclareLoopVariable/EmitStoreLoopVariable overrides for hoisted fields)
            base.EmitForOf(f);
            return;
        }

        // Loop contains yield/await - use hoisted enumerator field
        EmitForOfWithHoistedEnumerator(f, enumeratorField);
    }

    private void EmitForOfWithHoistedEnumerator(Stmt.ForOf f, FieldBuilder enumeratorField)
    {
        // For...of loop with hoisted enumerator (contains yield/await)
        // The enumerator is stored in a state machine field so it persists across suspension boundaries
        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        // Emit the iterable and get enumerator
        EmitExpression(f.Iterable);
        EnsureBoxed();
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        // Store enumerator to hoisted field (need temp local for the stack swap)
        var tempLocal = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, tempLocal);
        _il.Emit(OpCodes.Ldarg_0);  // this
        _il.Emit(OpCodes.Ldloc, tempLocal);
        _il.Emit(OpCodes.Stfld, enumeratorField);

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        EnterLoop(endLabel, continueLabel);

        _il.MarkLabel(startLabel);

        // Check MoveNext - load enumerator from hoisted field
        _il.Emit(OpCodes.Ldarg_0);  // this
        _il.Emit(OpCodes.Ldfld, enumeratorField);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable from Current (loaded from hoisted enumerator field)
        if (varField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldarg_0);  // this for enumerator field
            _il.Emit(OpCodes.Ldfld, enumeratorField);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Ldarg_0);  // this for enumerator field
            _il.Emit(OpCodes.Ldfld, enumeratorField);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        ExitLoop();
    }

    /// <summary>
    /// Async-generator override of the shared <see cref="StatementEmitterBase.EmitForAwaitOf"/>: a
    /// <c>for await…of</c> inside an async generator drives an async iterator and SUSPENDS the enclosing
    /// async-generator state machine on each <c>iterator.next()</c>/<c>iterator.return()</c> await,
    /// instead of blocking on a synchronous <c>GetResult</c>. Blocking deadlocks a genuinely-async source
    /// (a not-yet-settled await inside the iterator needs the very event-loop thread the block holds) —
    /// the async-generator sibling of the async-function deadlock fixed in #631 (this is #697). The
    /// analyzer reserved two suspension states (next + return) in
    /// <c>AsyncGeneratorStateAnalyzer.VisitForOf</c>, consumed here in the same order.
    /// </summary>
    /// <remarks>
    /// The iterator is driven through the <c>$IAsyncGenerator</c> interface. Synchronous iterables are
    /// first wrapped by the emitted async-from-sync adapter; custom <c>Symbol.asyncIterator</c> sources
    /// remain a separate, pre-existing gap in this emitter. <c>next()</c> returns a
    /// <c>Task&lt;object&gt;</c> ({ value, done }) that maps directly onto the async-gen await mechanism
    /// (<see cref="EmitAwaitFromValueOnStack"/>), so rejected steps propagate / reach an enclosing try
    /// exactly as a normal <c>await</c> does.
    /// </remarks>
    protected override void EmitForAwaitOf(Stmt.ForOf f)
    {
        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);
        var asyncGenInterface = _ctx!.Runtime!.AsyncGeneratorInterfaceType;

        // The iterator must survive the per-iteration suspensions (a MoveNextAsync re-entry wipes IL
        // locals — and the loop body itself may yield/await), so store it in a state-machine field,
        // unique per loop. The type is still open here (CreateType runs after MoveNextAsync), so defining
        // a field now is valid.
        int loopId = _forAwaitCounter++;
        var iteratorField = _builder.StateMachineType.DefineField(
            $"<>7__aiter{loopId}", _types.Object, FieldAttributes.Private);

        // Evaluate the iterable, adapt synchronous sources, and store the
        // resulting async iterator. Casting validates non-iterable inputs.
        EmitExpression(f.Iterable);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx.Runtime.AdaptSyncIterableToAsyncGenerator);
        _il.Emit(OpCodes.Castclass, asyncGenInterface);
        var iterTemp = _il.DeclareLocal(asyncGenInterface);
        _il.Emit(OpCodes.Stloc, iterTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, iterTemp);
        _il.Emit(OpCodes.Stfld, iteratorField);

        int nextState = _currentSuspensionState++;   // iterator.next() await (reused each iteration)

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var cleanupLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // break → cleanup (await iterator.return()); natural done → endLabel (no return() per spec).
        EnterLoop(cleanupLabel, continueLabel);

        _il.MarkLabel(startLabel);

        // result = await iterator.next(undefined)  — suspends if next() is not yet settled.
        // for-await-of never sends a value; pass undefined (#473 — next() now takes one argument).
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, iteratorField);
        _il.Emit(OpCodes.Castclass, asyncGenInterface);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorNextMethod);  // Task<object>
        SetStackUnknown();
        EmitAwaitFromValueOnStack(nextState);
        var resultLocal = _il.DeclareLocal(_types.Object);
        _il.Emit(OpCodes.Stloc, resultLocal);

        // if (result.done) exit without calling return().
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
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

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        // Cleanup on break: await iterator.return() (runs the iterator's finally blocks), discard result.
        int returnState = _currentSuspensionState++;   // allocated after the body, matching the analyzer
        _il.MarkLabel(cleanupLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, iteratorField);
        _il.Emit(OpCodes.Castclass, asyncGenInterface);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorReturnMethod);  // Task<object>
        SetStackUnknown();
        EmitAwaitFromValueOnStack(returnState);
        _il.Emit(OpCodes.Pop);

        _il.MarkLabel(endLabel);
        ExitLoop();
    }

    // EmitPrint, EmitDoWhile, EmitForIn, EmitSwitch:
    // inherited from StatementEmitterBase (identical logic; EmitForIn uses
    // DeclareLoopVariable/EmitStoreLoopVariable overrides for hoisted fields)
    // Note: base EmitSwitch also fixes a bug where labeled breaks inside switch
    // cases were incorrectly treated as switch breaks.
    //
    // EmitReturn, EmitThrow and the yield/await-aware try/catch emission live in
    // AsyncGeneratorMoveNextEmitter.Statements.TryCatch.cs. The exit-scope stack, the loop-scope
    // methods, break/continue and EmitBranchToLabel that route non-local exits through enclosing
    // finallys (#559) are inherited from StateMachineExitRoutingEmitter.
}
