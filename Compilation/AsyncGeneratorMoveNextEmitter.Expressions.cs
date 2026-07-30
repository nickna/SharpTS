using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase

    #region Yield Expressions

    protected override void EmitYield(Expr.Yield y)
    {
        int stateNumber = _currentSuspensionState++;
        var resumeLabel = _stateLabels[stateNumber];

        // Handle yield* delegation
        if (y.IsDelegating && y.Value != null)
        {
            EmitYieldStar(y, stateNumber, resumeLabel);
            return;
        }

        // 1. Emit the yield value (or null if no value)
        if (y.Value != null)
        {
            EmitExpression(y.Value);
            EnsureBoxed();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
        }

        // 2. Store value in <>2__current field
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // 3. Set state to the resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Mirror live spill temps to fields before the yield suspends (a value spilled before this yield
        // and used after it would otherwise be lost across the MoveNextAsync re-entry — #400 analog).
        _helpers.PersistLiveSpillsBeforeSuspend();

        // 4. Return ValueTask<bool>(true) - has value
        EmitReturnValueTaskBool(true);

        // 5. Mark the resume label (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // Restore spill temps from their fields on the resumed path (reached only via the state switch).
        _helpers.RehydrateLiveSpillsAfterResume();

        // 5a. Check __returnRequested flag (set by generator.return())
        // If true, jump to the enclosing finally cleanup path or complete the generator
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.ReturnRequestedField);
        var continueNormalLabel = _il.DefineLabel();
        _il.Emit(OpCodes.Brfalse, continueNormalLabel);

        if (_returnCleanupLabel != null)
        {
            // Inside a try/finally - jump to the afterTryBody label to execute finally
            _il.Emit(OpCodes.Br, _returnCleanupLabel.Value);
        }
        else
        {
            // Not inside try/finally - just complete the generator
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldc_I4, -2);
            _il.Emit(OpCodes.Stfld, _builder.StateField);
            EmitReturnValueTaskBool(false);
        }

        _il.MarkLabel(continueNormalLabel);

        // 6. Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 7. The resumed `yield` expression evaluates to the value passed to next(v) — stored in SentField
        // by next() before driving MoveNextAsync (#473). Bare next() seeds SentField to $Undefined so
        // `const r = yield 1` without a sent value gives undefined, not null (#481/#443 analog).
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.SentField);
        SetStackUnknown();
    }

    private void EmitYieldStar(Expr.Yield y, int stateNumber, Label resumeLabel)
    {
        // yield* delegates to another iterable (sync or async)
        var delegatedField = _builder.DelegatedAsyncEnumeratorField;

        if (delegatedField == null)
        {
            EmitYieldStarSync(y, stateNumber, resumeLabel);
            return;
        }

        // Check the type of the yield* expression to determine sync vs async
        // For now, try async first (check if it's IAsyncEnumerator), fall back to sync
        EmitYieldStarWithTypeCheck(y, stateNumber, resumeLabel, delegatedField);
    }

    private void EmitYieldStarWithTypeCheck(Expr.Yield y, int stateNumber, Label resumeLabel, FieldBuilder delegatedField)
    {
        // Structure:
        // 1. First-entry path: evaluate expression, check type, set up iteration, goto appropriate loop
        // 2. Resume path: check field type, dispatch to appropriate loop
        // 3. Sync loop
        // 4. Async loop
        // 5. End/cleanup

        var syncLoopLabel = _il.DefineLabel();
        var asyncLoopLabel = _il.DefineLabel();
        var syncSetupLabel = _il.DefineLabel();
        var asyncSetupLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        // === First entry path: evaluate and check type ===
        EmitExpression(y.Value!);
        EnsureBoxed();

        var iterableTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, iterableTemp);

        // Reserve the suspension state for the delegated iterator's next() await (consumed by the async
        // arm below, #688). Allocated AFTER the value expression so any awaits inside it take earlier
        // states — matching AsyncGeneratorStateAnalyzer.VisitYield, which records this synthetic await
        // point after visiting the yield value. Unused by the sync arm, but its resume label is always
        // marked (the async arm is always emitted), so the state switch stays valid either way.
        int awaitState = _currentSuspensionState++;

        // Check if it's IAsyncEnumerator<object> (async generators implement this)
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Isinst, _types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Brtrue, asyncSetupLabel);

        // Sync setup
        _il.MarkLabel(syncSetupLabel);
        // Emitted typed arrays and Buffers are synchronous iterables but do not implement
        // IEnumerable. Materialize them before the existing sync setup cast (#1289).
        NormalizeYieldStarTypedArrayOrBuffer(iterableTemp);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        _il.Emit(OpCodes.Callvirt, getEnumerator);
        // Store in field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, delegatedField); // load field address for swap
        _il.Emit(OpCodes.Pop); // pop address
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);
        _il.Emit(OpCodes.Br, syncLoopLabel);

        // Async setup
        _il.MarkLabel(asyncSetupLabel);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);
        var asyncEnumTemp = _il.DeclareLocal(_types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Stloc, asyncEnumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, asyncEnumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);
        _il.Emit(OpCodes.Br, asyncLoopLabel);

        // === Resume path ===
        _il.MarkLabel(resumeLabel);
        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        // Check field type to determine sync vs async
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Isinst, _types.IAsyncEnumeratorOfObject);
        _il.Emit(OpCodes.Brtrue, asyncLoopLabel);
        _il.Emit(OpCodes.Br, syncLoopLabel);

        // === Sync loop ===
        var syncLoopEnd = _il.DefineLabel();
        _il.MarkLabel(syncLoopLabel);
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, syncLoopEnd);

        // Get current
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, current);

        // Store in current field
        var syncValueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, syncValueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, syncValueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state and return
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(true);

        _il.MarkLabel(syncLoopEnd);
        _il.Emit(OpCodes.Br, endLabel);

        // === Async loop ===
        // Drive the delegated async iterator via its $IAsyncGenerator.next() — a Task<object> holding the
        // { value, done } iterator result — and SUSPEND the enclosing async generator on it, rather than
        // blocking on a synchronous ValueTask GetResult (which deadlocks a genuinely-async delegate the
        // same way next() did before #631). next() maps directly onto the async-gen await mechanism; the
        // reserved `awaitState` backs that suspension. Everything that reaches this arm
        // (Isinst IAsyncEnumerator<object> succeeded) is an emitted async generator, which implements
        // $IAsyncGenerator (#688).
        var asyncLoopEnd = _il.DefineLabel();
        _il.MarkLabel(asyncLoopLabel);

        // result = await delegated.next(SentField) — forward the outer sent value to the inner generator
        // (#473). The inner generator ignores the argument on its first call (per spec), so passing
        // SentField on initial entry (which holds undefined or the outer first-next value) is safe.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, _ctx!.Runtime!.AsyncGeneratorInterfaceType);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.SentField);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorNextMethod);
        SetStackUnknown();
        EmitAwaitFromValueOnStack(awaitState);
        var asyncResultLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, asyncResultLocal);

        // if (result.done) the delegation is finished.
        _il.Emit(OpCodes.Ldloc, asyncResultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorDone);
        _il.Emit(OpCodes.Brtrue, asyncLoopEnd);

        // <>2__current = result.value
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, asyncResultLocal);
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetIteratorValue);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Re-yield the delegated value to our own consumer: suspend at the re-yield state and return true.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(true);

        _il.MarkLabel(asyncLoopEnd);

        // === End/cleanup ===
        _il.MarkLabel(endLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to undefined — load the `$Undefined` sentinel, not CLR null (#481). (A
        // delegated iterator's own return value as the yield* result is a separate, deeper gap; this
        // path drives via IAsyncEnumerator/IEnumerator, which carry no return value.)
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        SetStackUnknown();
    }

    private void EmitYieldStarSync(Expr.Yield y, int stateNumber, Label resumeLabel)
    {
        // Sync yield* delegation using IEnumerable
        // The enumerator must be stored in a FIELD (not local) to persist across suspensions

        var delegatedField = _builder.DelegatedAsyncEnumeratorField;
        if (delegatedField == null)
        {
            // No field available - shouldn't happen if HasYieldStar was detected
            EmitExpression(y.Value!);
            EnsureBoxed();
            _il.Emit(OpCodes.Pop);
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        var loopStart = _il.DefineLabel();
        var loopEnd = _il.DefineLabel();

        // Emit the iterable expression and get its enumerator
        var iterableTemp = _il.DeclareLocal(typeof(object));
        EmitExpression(y.Value!);
        EnsureBoxed();
        _il.Emit(OpCodes.Stloc, iterableTemp);
        NormalizeYieldStarTypedArrayOrBuffer(iterableTemp);
        _il.Emit(OpCodes.Ldloc, iterableTemp);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        // Store enumerator in field (persists across suspensions)
        var enumTemp = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, enumTemp);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // Fall through to loop start
        _il.Emit(OpCodes.Br, loopStart);

        // Resume label - jumped to from state switch when resuming after yield
        _il.MarkLabel(resumeLabel);
        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Loop start - check if more elements
        _il.MarkLabel(loopStart);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current value
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, delegatedField);
        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Callvirt, current);

        // Store in <>2__current
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, valueTemp);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Set state to resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Return true (has value)
        EmitReturnValueTaskBool(true);

        // Loop end - delegation finished
        _il.MarkLabel(loopEnd);

        // Clear the delegated field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, delegatedField);

        // yield* evaluates to undefined — load the `$Undefined` sentinel, not CLR null (#481).
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        SetStackUnknown();
    }

    #endregion

    #region Await Expressions

    protected override void EmitAwait(Expr.Await a)
    {
        int stateNumber = _currentSuspensionState++;

        // 1. Emit the awaited expression (should produce Task<object> or $Promise or any value)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2+. Coerce to Task<object>, suspend the async generator until it settles, and leave the
        // awaited result on the stack.
        EmitAwaitFromValueOnStack(stateNumber);
    }

    /// <summary>
    /// Emits the await of a value already on the evaluation stack (boxed): coerces it to
    /// <c>Task&lt;object&gt;</c> (unwrapping $Promise / adopting thenables / wrapping plain values),
    /// suspends the async-generator state machine until it settles (via
    /// <see cref="EmitAwaitSuspensionReturn"/> and the emitted AsyncGeneratorAwaitContinue), then leaves
    /// the awaited result on the stack. Shared by <see cref="EmitAwait"/>, the <c>for await…of</c> loop's
    /// implicit next()/return() awaits (#697), and <c>yield*</c> delegation's next() await (#688);
    /// <paramref name="stateNumber"/> is the reserved suspension state for this await. The shared
    /// AwaiterField/AwaitedTaskField are safe to reuse because the state machine only ever has one
    /// suspension in flight at a time.
    /// </summary>
    internal void EmitAwaitFromValueOnStack(int stateNumber)
    {
        var resumeLabel = _stateLabels[stateNumber];
        var continueLabel = _il.DefineLabel();

        // 2. Convert to Task<object> - handle $Promise, Task<object>, or non-Task values
        // If it's a $Promise, extract its Task property
        // If it's already a Task<object>, use it directly
        // Otherwise, wrap in Task.FromResult (for non-promise values like numbers, strings, etc.)
        var taskLocal = _il.DeclareLocal(typeof(Task<object>));
        var isPromiseLabel = _il.DefineLabel();
        var isTaskLabel = _il.DefineLabel();
        var wrapValueLabel = _il.DefineLabel();
        var haveTaskLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.TSPromiseType);
        _il.Emit(OpCodes.Brtrue, isPromiseLabel);

        // Not a $Promise - check if it's a Task<object>
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Isinst, typeof(Task<object>));
        _il.Emit(OpCodes.Brtrue, isTaskLabel);

        // Not a Promise or Task - adopt an ordinary thenable (e.g. a general
        // non-Promise then/catch/finally species result, #349); non-thenables
        // become Task.FromResult(value).
        _il.MarkLabel(wrapValueLabel);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CoerceAwaitableToTaskMethod);
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Br, haveTaskLabel);

        // Is a Task<object> - use directly
        _il.MarkLabel(isTaskLabel);
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Br, haveTaskLabel);

        // Is a $Promise - extract its Task property
        _il.MarkLabel(isPromiseLabel);
        _il.Emit(OpCodes.Castclass, _ctx.Runtime.TSPromiseType);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.TSPromiseTaskGetter);
        _il.Emit(OpCodes.Stloc, taskLocal);

        _il.MarkLabel(haveTaskLabel);

        // 2b. Store the task in AwaitedTaskField (needed for continuation if not completed)
        // Stack: []
        _il.Emit(OpCodes.Ldarg_0);                // Stack: [this]
        _il.Emit(OpCodes.Ldloc, taskLocal);       // Stack: [this, task]
        _il.Emit(OpCodes.Stfld, _builder.AwaitedTaskField); // Stack: []
        _il.Emit(OpCodes.Ldloc, taskLocal);       // Stack: [task]

        // 3. Get awaiter: task.GetAwaiter()
        var getAwaiterMethod = typeof(Task<object>).GetMethod("GetAwaiter")!;
        _il.Emit(OpCodes.Call, getAwaiterMethod);

        // 4. Store awaiter to field
        var awaiterLocal = _il.DeclareLocal(_types.TaskAwaiterOfObject);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, _builder.AwaiterField);

        // 5. Check IsCompleted
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.AwaiterField);
        var isCompletedGetter = _types.GetProperty(_types.TaskAwaiterOfObject, "IsCompleted")!.GetGetMethod()!;
        _il.Emit(OpCodes.Call, isCompletedGetter);
        _il.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend and return a pending ValueTask<bool>
        // Set state to resume point
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Mirror live spill temps to fields before the state machine suspends: IL locals do not survive
        // the MoveNextAsync re-entry, so a value spilled before this await and used after it would be lost
        // (#400 analog). Suspending path only. (#688/#697 exercise this via `param + (await …)` bodies.)
        _helpers.PersistLiveSpillsBeforeSuspend();

        // For async generators, we need to return a ValueTask<bool> that will complete when the await completes
        // The simplest approach: wrap the continuing task
        // Create a continuation that resumes MoveNextAsync
        int yieldOffset = _il.ILOffset;
        EmitAwaitSuspensionReturn();

        // 7. Resume point (jumped to from state switch)
        _il.MarkLabel(resumeLabel);
        if (_ctx.DebugScope is { } debugScope &&
            _ctx.CurrentMethod is { } currentMethod)
        {
            debugScope.RecordAsyncStep(
                currentMethod,
                yieldOffset,
                _il.ILOffset);
        }

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // Restore spill temps from their fields — only on the resumed path; the synchronously-completed
        // path (continueLabel below) never persisted and keeps its locals.
        _helpers.RehydrateLiveSpillsAfterResume();

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult(). A rejected awaited task throws here.
        var getResultMethod = _types.GetMethod(_types.TaskAwaiterOfObject, "GetResult")!;
        if (_currentTryExceptionLocal != null)
        {
            // Inside a flag-based try: capture the rejection into the try's exception flag (as a sync
            // segment would) and `Leave` to the try's catch/finally, rather than letting it escape
            // MoveNextAsync unhandled (#617). The eval stack is empty here — the resume/continue labels
            // are state-switch targets — so opening a protected region is legal.
            var resultTemp = _il.DeclareLocal(typeof(object));
            _il.BeginExceptionBlock();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, _builder.AwaiterField);
            _il.Emit(OpCodes.Call, getResultMethod);
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.BeginCatchBlock(typeof(Exception));
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
            _il.Emit(OpCodes.Stloc, _currentTryExceptionLocal);
            // Record presence with the flag, not the value, so a rejected null/undefined still engages
            // the catch rather than reading as "no exception" (#628).
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, _currentTryExceptionPresentLocal!);
            _il.Emit(OpCodes.Leave, _currentTryCleanupLabel); // skip the rest of the try body → catch/finally
            _il.EndExceptionBlock();
            _il.Emit(OpCodes.Ldloc, resultTemp); // normal path: push the awaited value
        }
        else
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, _builder.AwaiterField);
            _il.Emit(OpCodes.Call, getResultMethod);
        }

        // Result is now on stack
        SetStackUnknown();
    }

    private void EmitAwaitSuspensionReturn()
    {
        // Call emitted AsyncGeneratorAwaitContinue(task, generator)
        // This creates a proper continuation that calls MoveNextAsync after the await completes

        // Load the awaited task from field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.AwaitedTaskField);

        // Load this (the generator) as IAsyncEnumerator<object>
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Castclass, _types.IAsyncEnumeratorOfObject);

        // Call AsyncGeneratorAwaitContinue(task, generator) - use emitted method for standalone support
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.AsyncGeneratorAwaitContinue);

        // Returns ValueTask<bool>, return it
        _il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Arrow Function Expressions

    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // Check for async arrow functions first
        if (af.IsAsync)
        {
            EmitAsyncArrowFunction(af);
            return;
        }

        // The async-generator state machine has no function display class wired yet (#674 lifts the
        // sync free-function generator case; the async-generator path is tracked separately), so an
        // arrow that WRITES a captured generator local would snapshot it by value and silently drop
        // the write. Fail fast with a clear message instead of miscompiling to a wrong result.
        CapturedWriteAnalysis.ThrowIfCapturedWriteWouldBeLost(af, _ctx?.DisplayClassFields);

        // Get the method for this arrow function (pre-compiled)
        if (_ctx!.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            // Fallback if not found
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        // Check if this is a capturing arrow (has display class)
        if (_ctx.DisplayClasses != null && _ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            EmitCapturingArrowFunction(af, method, displayClass);
        }
        else
        {
            EmitNonCapturingArrowFunction(method);
        }
    }

    private void EmitAsyncArrowFunction(Expr.ArrowFunction af)
    {
        // For now, fallback to null for async arrow functions in async generators
        // Full implementation would need AsyncArrowBuilder support
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitCapturingArrowFunction(Expr.ArrowFunction af, MethodBuilder method, TypeBuilder displayClass)
    {
        if (_ctx!.DisplayClassConstructors == null || !_ctx.DisplayClassConstructors.TryGetValue(af, out var displayCtor))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        _il.Emit(OpCodes.Newobj, displayCtor);

        // Thread the entry-point display class into the arrow's $entryPointDC field so it reads
        // captured TOP-LEVEL variables through shared storage (the async-generator analog of #732).
        if (_ctx.ArrowEntryPointDCFields?.TryGetValue(af, out var entryPointDCField) == true &&
            _ctx.EntryPointDisplayClassStaticField != null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            _il.Emit(OpCodes.Stfld, entryPointDCField);
        }

        // Thread the state machine's function display class into the arrow's $functionDC field so a
        // write to a captured-and-mutated generator local reaches shared storage instead of a by-value
        // snapshot — the case the compile-time guard previously rejected (#725).
        if (_ctx.ArrowFunctionDCFields?.TryGetValue(af, out var functionDCField) == true &&
            _builder.FunctionDCField != null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.FunctionDCField);
            _il.Emit(OpCodes.Stfld, functionDCField);
        }

        if (_ctx.DisplayClassFields == null || !_ctx.DisplayClassFields.TryGetValue(af, out var fieldMap))
        {
            Types.EmitLoadMethodInfoViaHandle(_il, method);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Populate captured fields
        foreach (var (capturedVar, field) in fieldMap)
        {
            _il.Emit(OpCodes.Dup);

            // Per-iteration cell capture (#650): snapshot the StrongBox REFERENCE.
            if (_ctx.CellBindingLocals.TryGetValue(capturedVar, out var cellLocal))
            {
                _il.Emit(OpCodes.Ldloc, cellLocal);
                _il.Emit(OpCodes.Stfld, field);
                continue;
            }

            // #767: pivot a captured nested-block shadow to its renamed storage (identity otherwise).
            var sourceVar = PivotCaptureSource(_analysis.BlockScopeCaptureRenames, af, capturedVar);

            var hoistedField = _builder.GetVariableField(sourceVar);
            if (hoistedField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, hoistedField);
            }
            else if (capturedVar == "this" && _builder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.ThisField);
            }
            else if (_ctx.Locals.TryGetLocal(sourceVar, out var local))
            {
                _il.Emit(OpCodes.Ldloc, local);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Stfld, field);
        }

        Types.EmitLoadMethodInfoViaHandle(_il, method);
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    private void EmitNonCapturingArrowFunction(MethodBuilder method)
    {
        _il.Emit(OpCodes.Ldnull);
        Types.EmitLoadMethodInfoViaHandle(_il, method);
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }

    #endregion

    // EmitSuper is inherited from ExpressionEmitterBase (#1105): the base loads the
    // hoisted `this` and resolves through GetSuperMethod, which works in the async
    // generator state machine. The old override here pushed `null`, silently
    // miscompiling `super.x` inside an async generator body.
}
