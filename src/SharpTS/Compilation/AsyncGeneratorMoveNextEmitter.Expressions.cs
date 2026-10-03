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

        EmitInjectedYieldThrow();

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
        _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.RequirePromise().Type);
        _il.Emit(OpCodes.Brtrue, isPromiseLabel);

        // Not a $Promise - check if it's a Task<object>
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Isinst, typeof(Task<object>));
        _il.Emit(OpCodes.Brtrue, isTaskLabel);

        // Not a Promise or Task - adopt an ordinary thenable (e.g. a general
        // non-Promise then/catch/finally species result, #349); non-thenables
        // become Task.FromResult(value).
        _il.MarkLabel(wrapValueLabel);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.RequirePromise().CoerceAwaitableToTaskMethod);
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Br, haveTaskLabel);

        // Is a Task<object> - use directly
        _il.MarkLabel(isTaskLabel);
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Br, haveTaskLabel);

        // Is a $Promise - extract its Task property
        _il.MarkLabel(isPromiseLabel);
        _il.Emit(OpCodes.Castclass, _ctx.Runtime.RequirePromise().Type);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.RequirePromise().TaskGetter);
        _il.Emit(OpCodes.Stloc, taskLocal);

        _il.MarkLabel(haveTaskLabel);
        _il.Emit(OpCodes.Ldloc, taskLocal);
        if (_ctx.Runtime.EventLoop.Hosted is { } hostedEventLoop)
            _il.Emit(OpCodes.Call, hostedEventLoop.PrepareAwait);
        _il.Emit(OpCodes.Stloc, taskLocal);

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
            EmitCaptureTryOperation(() =>
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldflda, _builder.AwaiterField);
                _il.Emit(OpCodes.Call, getResultMethod);
                _il.Emit(OpCodes.Stloc, resultTemp);
            });
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
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.RequireAsyncGenerators().RequireContinuations().AwaitContinue);

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
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.FunctionConstruction.Constructor);
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
            else if (!TryEmitGlobalVariable(sourceVar))
            {
                _il.Emit(OpCodes.Ldnull);
            }

            _il.Emit(OpCodes.Stfld, field);
        }

        Types.EmitLoadMethodInfoViaHandle(_il, method);
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.FunctionConstruction.Constructor);
        SetStackUnknown();
    }

    private void EmitNonCapturingArrowFunction(MethodBuilder method)
    {
        _il.Emit(OpCodes.Ldnull);
        Types.EmitLoadMethodInfoViaHandle(_il, method);
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.FunctionConstruction.Constructor);
        SetStackUnknown();
    }

    #endregion

    // EmitSuper is inherited from ExpressionEmitterBase (#1105): the base loads the
    // hoisted `this` and resolves through GetSuperMethod, which works in the async
    // generator state machine. The old override here pushed `null`, silently
    // miscompiling `super.x` inside an async generator body.
}
