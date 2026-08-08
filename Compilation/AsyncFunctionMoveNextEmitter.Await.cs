using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class AsyncFunctionMoveNextEmitter
{
    // ---- The await suspension IL dance, shared by both async-function emitters (#1122) ----------
    //
    // The ~90-line sequence below — Promise/Task coercion → GetAwaiter → store awaiter field →
    // IsCompleted → persist spills / suspend / Leave → resume / rehydrate → GetResult — was copied
    // near-byte-identically into AsyncMoveNextEmitter and AsyncArrowMoveNextEmitter. The two had already
    // drifted (the exit label was `_endLabel` in one and `_exitLabel` in the other); pull the single copy
    // up here and expose the handful of genuinely emitter-specific seams as hooks. The async generator
    // keeps its own third copy (it suspends by returning a ValueTask<bool> from AsyncGeneratorAwaitContinue
    // rather than calling the builder's AwaitUnsafeOnCompleted, so only the coercion prologue is common).

    /// <summary>The builder for this async-function state machine, exposing the shared awaiter accessors.</summary>
    protected abstract AsyncBuilderBase AsyncBuilder { get; }

    /// <summary>The label a not-yet-completed await <c>Leave</c>s to (the MoveNext exit).</summary>
    protected abstract Label AwaitExitLabel { get; }

    /// <summary>Allocates the next await suspension-state number for this emitter.</summary>
    protected abstract int NextAwaitState();

    /// <summary>Marks the resume label for <paramref name="stateNumber"/> (the state-switch jumps here).</summary>
    protected abstract void MarkAwaitResumeLabel(int stateNumber);

    /// <summary>The awaiter field reserved for <paramref name="stateNumber"/>.</summary>
    protected abstract FieldBuilder AwaiterFieldForState(int stateNumber);

    /// <summary>The state-machine <c>&lt;&gt;1__state</c> field.</summary>
    protected abstract FieldBuilder AsyncStateField { get; }

    /// <summary>The state-machine <c>&lt;&gt;t__builder</c> field.</summary>
    protected abstract FieldBuilder AsyncBuilderField { get; }

    /// <summary>The builder's <c>AwaitUnsafeOnCompleted&lt;TAwaiter, TStateMachine&gt;</c>, specialized for this machine.</summary>
    protected abstract MethodInfo BuilderAwaitUnsafeOnCompletedMethod();

    protected override void EmitAwait(Expr.Await a)
    {
        // 1. Emit the awaited expression (should produce Task<object> or $Promise or any value)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2+. Coerce to Task<object>, suspend/resume, and leave the awaited result on the stack.
        EmitAwaitFromValueOnStack(NextAwaitState());
    }

    /// <summary>
    /// Emits the await of a value already on the evaluation stack (boxed): coerces it to
    /// <c>Task&lt;object&gt;</c> (unwrapping $Promise / adopting thenables / wrapping plain values),
    /// suspends the state machine until it settles, and leaves the awaited result on the stack. Shared by
    /// <see cref="EmitAwait"/> and the <c>for await…of</c> loop's implicit next()/return() awaits (#631);
    /// <paramref name="stateNumber"/> is the reserved suspension state for this await.
    /// </summary>
    internal void EmitAwaitFromValueOnStack(int stateNumber)
    {
        var continueLabel = IL.DefineLabel();
        var awaiterField = AwaiterFieldForState(stateNumber);

        // 2. Convert to Task<object> - handle $Promise, Task<object>, or non-Task values
        var taskLocal = IL.DeclareLocal(typeof(Task<object>));
        var isPromiseLabel = IL.DefineLabel();
        var isTaskLabel = IL.DefineLabel();
        var wrapValueLabel = IL.DefineLabel();
        var haveTaskLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.TSPromiseType);
        IL.Emit(OpCodes.Brtrue, isPromiseLabel);

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, typeof(Task<object>));
        IL.Emit(OpCodes.Brtrue, isTaskLabel);

        IL.MarkLabel(wrapValueLabel);
        // Adopt an ordinary thenable (e.g. a general non-Promise then/catch/finally
        // species result, #349); non-thenables become Task.FromResult(value).
        IL.Emit(OpCodes.Call, Ctx.Runtime!.CoerceAwaitableToTaskMethod);
        IL.Emit(OpCodes.Stloc, taskLocal);
        IL.Emit(OpCodes.Br, haveTaskLabel);

        IL.MarkLabel(isTaskLabel);
        IL.Emit(OpCodes.Castclass, typeof(Task<object>));
        IL.Emit(OpCodes.Stloc, taskLocal);
        IL.Emit(OpCodes.Br, haveTaskLabel);

        IL.MarkLabel(isPromiseLabel);
        IL.Emit(OpCodes.Castclass, Ctx.Runtime.TSPromiseType);
        IL.Emit(OpCodes.Callvirt, Ctx.Runtime.TSPromiseTaskGetter);
        IL.Emit(OpCodes.Stloc, taskLocal);

        IL.MarkLabel(haveTaskLabel);
        IL.Emit(OpCodes.Ldloc, taskLocal);
        if (Ctx.Runtime.EventLoopPrepareHostedAwait is not null)
            IL.Emit(OpCodes.Call, Ctx.Runtime.EventLoopPrepareHostedAwait);
        IL.Emit(OpCodes.Stloc, taskLocal);
        IL.Emit(OpCodes.Ldloc, taskLocal);

        // 3. Get awaiter: task.GetAwaiter()
        IL.Emit(OpCodes.Call, AsyncBuilder.GetTaskGetAwaiterMethod());

        // 4. Store awaiter to field
        var awaiterLocal = IL.DeclareLocal(AsyncBuilder.AwaiterType);
        IL.Emit(OpCodes.Stloc, awaiterLocal);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldloc, awaiterLocal);
        IL.Emit(OpCodes.Stfld, awaiterField);

        // 5. Check IsCompleted
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldflda, awaiterField);
        IL.Emit(OpCodes.Call, AsyncBuilder.GetAwaiterIsCompletedGetter());
        IL.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldc_I4, stateNumber);
        IL.Emit(OpCodes.Stfld, AsyncStateField);

        // Mirror live spill temps to fields before AwaitUnsafeOnCompleted boxes the state
        // machine: IL locals do not survive the MoveNext re-entry, and writes after the box
        // would not reach the continuation's snapshot (#400). Suspending path only.
        _helpers.PersistLiveSpillsBeforeSuspend();

        int yieldOffset = IL.ILOffset;
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldflda, AsyncBuilderField);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldflda, awaiterField);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Call, BuilderAwaitUnsafeOnCompletedMethod());

        IL.Emit(OpCodes.Leave, AwaitExitLabel);

        // 7. Resume point (jumped to from the state switch)
        MarkAwaitResumeLabel(stateNumber);
        if (Ctx.DebugScope is { } debugScope &&
            Ctx.CurrentMethod is { } currentMethod)
        {
            debugScope.RecordAsyncStep(
                currentMethod,
                yieldOffset,
                IL.ILOffset);
        }
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldc_I4_M1);
        IL.Emit(OpCodes.Stfld, AsyncStateField);

        // Restore spill temps from their fields — only on the resumed path; the
        // synchronously-completed path (below) never persisted and keeps its locals.
        _helpers.RehydrateLiveSpillsAfterResume();

        // 8. Continue point
        IL.MarkLabel(continueLabel);

        // 9. Get result — wrapped in the flag-based exception capture when inside a try-with-awaits
        // (see EmitAwaitGetResult).
        EmitAwaitGetResult(() =>
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldflda, awaiterField);
            IL.Emit(OpCodes.Call, AsyncBuilder.GetAwaiterGetResultMethod());
        });

        SetStackUnknown();
    }
}
