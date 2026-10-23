using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
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
    protected abstract void MarkAwaitResumeLabelCore(int stateNumber);

    protected void MarkAwaitResumeLabel(int stateNumber)
    {
        if (_awaitCatchRegion == null)
        {
            MarkAwaitResumeLabelCore(stateNumber);
            return;
        }

        var target = IL.DefineLabel();
        _awaitCatchRegion.Resumes.Add((stateNumber, target));
        IL.MarkLabel(target);
    }

    /// <summary>The awaiter field reserved for <paramref name="stateNumber"/>.</summary>
    protected abstract FieldBuilder AwaiterFieldForState(int stateNumber);

    /// <summary>The state-machine <c>&lt;&gt;1__state</c> field.</summary>
    protected abstract FieldBuilder AsyncStateField { get; }

    /// <summary>The state-machine <c>&lt;&gt;t__builder</c> field.</summary>
    protected abstract FieldBuilder AsyncBuilderField { get; }

    /// <summary>The builder's <c>AwaitUnsafeOnCompleted&lt;TAwaiter, TStateMachine&gt;</c>, specialized for this machine.</summary>
    protected abstract MethodInfo BuilderAwaitUnsafeOnCompletedMethod();

    /// <summary>
    /// Legacy extension flag retained for compatibility with derived emitters. It no longer
    /// controls await lowering; every syntactic await uses the ordinary suspension path.
    /// </summary>
    protected virtual bool AllowSuspensionFreePrimitiveAsyncCoreAwait => false;

    protected override void EmitAwait(Expr.Await a)
    {
        // Even an immediately available native Promise result needs an Await
        // job. Keep every syntactic await in the ordinary suspension path.

        // 1. Emit the awaited expression (should produce Task<object> or $Promise or any value)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2+. Coerce to Task<object>, suspend/resume, and leave the awaited result on the stack.
        EmitAwaitFromValueOnStack(NextAwaitState());
    }

    /// <summary>
    /// Emits the await of a value already on the evaluation stack (boxed): coerces it to
    /// <c>Task&lt;object&gt;</c> using Await's intrinsic PromiseResolve policy,
    /// suspends the state machine until it settles, and leaves the awaited result on the stack. Shared by
    /// <see cref="EmitAwait"/> and the <c>for await…of</c> loop's implicit next()/return() awaits (#631);
    /// <paramref name="stateNumber"/> is the reserved suspension state for this await.
    /// </summary>
    internal void EmitAwaitFromValueOnStack(int stateNumber)
    {
        IL.Emit(OpCodes.Call, Ctx.Runtime!.RequirePromise().AwaitValueToTaskMethod);
        EmitAwaitTaskOnStack(stateNumber);
    }

    /// <summary>
    /// Resolves the final return after all guest handlers have completed. Primitive values
    /// have no then lookup; objects capture their live then synchronously and invoke it as a job.
    /// </summary>
    protected void EmitAdoptCompletionValue(LocalBuilder value, int stateNumber)
    {
        if (NextAwaitState() != stateNumber)
            throw new CompileException("Async return completion state does not follow the body await states.");

        if (!_returnMayNeedAdoption)
        {
            // Bare/fallthrough returns are undefined; canonical primitive-chain awaits also
            // cannot expose a then property. Keep the reserved dispatch target valid.
            MarkAwaitResumeLabel(stateNumber);
            return;
        }

        var completed = IL.DefineLabel();
        var objectValue = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Brfalse, completed);
        var kind = IL.DeclareLocal(Types.String);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Operators.TypeOf);
        IL.Emit(OpCodes.Stloc, kind);
        foreach (string name in new[] { "object", "function" })
        {
            IL.Emit(OpCodes.Ldloc, kind);
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, Types.StringOpEquality);
            IL.Emit(OpCodes.Brtrue, objectValue);
        }
        IL.Emit(OpCodes.Br, completed);
        IL.MarkLabel(objectValue);

        // Check the public promise before an intermediate resolving task can hide its identity.
        var distinct = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldflda, AsyncBuilderField);
        IL.Emit(OpCodes.Call, AsyncBuilder.GetBuilderTaskGetter());
        IL.Emit(OpCodes.Bne_Un, distinct);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime, "Chaining cycle detected for promise");
        IL.MarkLabel(distinct);

        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Call, Ctx.Runtime.RequirePromise().ResolveValueMethod);
        EmitAwaitTaskOnStack(stateNumber);
        IL.Emit(OpCodes.Stloc, value);
        IL.MarkLabel(completed);
    }

    // Shared suspension machinery for an already-normalized Task<object>. Completion resolution
    // and explicit await have different normalization policies but identical resume ownership.
    private void EmitAwaitTaskOnStack(int stateNumber)
    {
        var continueLabel = IL.DefineLabel();
        var awaiterField = AwaiterFieldForState(stateNumber);

        var taskLocal = IL.DeclareLocal(typeof(Task<object>));
        if (Ctx.Runtime!.EventLoop.Hosted is { } hostedEventLoop)
            IL.Emit(OpCodes.Call, hostedEventLoop.PrepareAwait);
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
