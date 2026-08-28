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
    protected abstract void MarkAwaitResumeLabel(int stateNumber);

    /// <summary>The awaiter field reserved for <paramref name="stateNumber"/>.</summary>
    protected abstract FieldBuilder AwaiterFieldForState(int stateNumber);

    /// <summary>The state-machine <c>&lt;&gt;1__state</c> field.</summary>
    protected abstract FieldBuilder AsyncStateField { get; }

    /// <summary>The state-machine <c>&lt;&gt;t__builder</c> field.</summary>
    protected abstract FieldBuilder AsyncBuilderField { get; }

    /// <summary>The builder's <c>AwaitUnsafeOnCompleted&lt;TAwaiter, TStateMachine&gt;</c>, specialized for this machine.</summary>
    protected abstract MethodInfo BuilderAwaitUnsafeOnCompletedMethod();

    /// <summary>
    /// True only for an async declaration whose exception routing can accept a direct synchronous
    /// core call. Async arrows and functions with JavaScript try/catch retain the ordinary await path.
    /// </summary>
    protected virtual bool AllowSuspensionFreePrimitiveAsyncCoreAwait => false;

    protected override void EmitAwait(Expr.Await a)
    {
        if (TryEmitStablePrimitivePromiseResolveAwait(a))
            return;

        if (AllowSuspensionFreePrimitiveAsyncCoreAwait
            && Ctx.SuspensionFreePrimitiveAsyncAwaits?.Contains(a) == true)
        {
            if (TryEmitSuspensionFreePrimitiveAsyncCoreCall(a.Expression))
                return;
            throw new CompileException(
                "A pre-proven suspension-free async core call could not be emitted.");
        }

        // 1. Emit the awaited expression (should produce Task<object> or $Promise or any value)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2+. Coerce to Task<object>, suspend/resume, and leave the awaited result on the stack.
        EmitAwaitFromValueOnStack(NextAwaitState());
    }

    /// <summary>
    /// Elides the fresh completed <c>Task&lt;object&gt;</c> created for an immediately
    /// awaited intrinsic <c>Promise.resolve(primitive)</c>. The generated await path
    /// already continues synchronously for that completed task, so its identity and
    /// the boxed primitive cannot be observed. Programs that can replace Promise
    /// behavior, shadow the global binding, or pass a thenable retain ordinary
    /// Promise resolution and await lowering.
    /// </summary>
    private bool TryEmitStablePrimitivePromiseResolveAwait(Expr.Await awaitExpression)
    {
        Expr expression = awaitExpression.Expression;
        if (Ctx.RuntimeFeatures?.UsesPromisePrototypeMutation == true
            || Resolver.HasVariable("Promise")
            || Ctx.HasVisibleValueBinding("Promise")
            || expression is not Expr.Call
            {
                Optional: false,
                Callee: Expr.Get
                {
                    Optional: false,
                    Object: Expr.Variable { Name.Lexeme: "Promise" },
                    Name.Lexeme: "resolve"
                },
                Arguments: [var value]
            }
            || value is Expr.Spread
            || !IsStaticallyNonNullPrimitive(Ctx.TypeMap?.Get(value)))
        {
            return false;
        }

        if (Ctx.SuspensionFreePrimitiveAsyncAwaits?.Contains(awaitExpression) != true)
        {
            // AsyncStateAnalyzer reserved a dispatch label for every syntactic
            // await that was not proven non-suspending before state-machine
            // definition. This path never stores that state, but the persisted
            // switch target must still be marked.
            MarkAwaitResumeLabel(NextAwaitState());
        }
        EmitExpression(value);
        return true;
    }

    private static bool IsStaticallyNonNullPrimitive(TypeSystem.TypeInfo? type) => type is
        TypeSystem.TypeInfo.Primitive
        {
            Type: TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN
        }
        or TypeSystem.TypeInfo.NumberLiteral
        or TypeSystem.TypeInfo.BooleanLiteral
        or TypeSystem.TypeInfo.String
        or TypeSystem.TypeInfo.StringLiteral;

    private bool TryEmitSuspensionFreePrimitiveAsyncCoreCall(Expr expression)
    {
        if (expression is not Expr.Call
            {
                Callee: Expr.Variable functionVariable,
                Arguments: var arguments
            }
            || arguments.Any(argument => argument is Expr.Spread)
            || AnyContainsSuspension(arguments))
        {
            return false;
        }

        string simpleName = functionVariable.Name.Lexeme;
        string resolvedName = Ctx.ResolveFunctionName(simpleName);
        bool isSameScopeDeclaration = string.Equals(
            resolvedName,
            Ctx.GetQualifiedFunctionName(simpleName),
            StringComparison.Ordinal);
        bool shadowedByLocalBinding = Resolver.HasVariable(simpleName);
        if (shadowedByLocalBinding
            && isSameScopeDeclaration
            && Ctx.TopLevelStaticVars?.ContainsKey(simpleName) == true
            && !Ctx.TryGetParameter(simpleName, out _)
            && !Ctx.CellBindingLocals.ContainsKey(simpleName)
            && !Ctx.Locals.HasLocal(simpleName)
            && Ctx.CapturedFunctionLocals?.Contains(simpleName) != true
            && Ctx.CapturedArrowLocals?.Contains(simpleName) != true
            && Ctx.ParentArrowCapturedLocals?.Contains(simpleName) != true
            && Ctx.ExtraArrowScopeBindings?.ContainsKey(simpleName) != true
            && Ctx.CapturedFields?.ContainsKey(simpleName) != true)
        {
            shadowedByLocalBinding = false;
        }

        Dictionary<string, MethodBuilder>? stableCores =
            Ctx.SuspensionFreePrimitiveAsyncCores;
        if (stableCores == null
            || !stableCores.TryGetValue(resolvedName, out MethodBuilder? coreMethod)
            || !isSameScopeDeclaration
            || arguments.Count != coreMethod.GetParameters().Length
            || shadowedByLocalBinding)
        {
            return false;
        }

        ParameterInfo[] parameters = coreMethod.GetParameters();
        var argumentLocals = new LocalBuilder[arguments.Count];
        for (int index = 0; index < arguments.Count; index++)
        {
            EmitExpression(arguments[index]);
            EmitConversionForParameter(arguments[index], parameters[index].ParameterType);
            var argumentLocal = IL.DeclareLocal(parameters[index].ParameterType);
            IL.Emit(OpCodes.Stloc, argumentLocal);
            argumentLocals[index] = argumentLocal;
        }
        foreach (LocalBuilder argumentLocal in argumentLocals)
            IL.Emit(OpCodes.Ldloc, argumentLocal);

        IL.Emit(OpCodes.Call, coreMethod);
        if (coreMethod.ReturnType == typeof(double))
            SetStackType(StackType.Double);
        else if (coreMethod.ReturnType == typeof(bool))
            SetStackType(StackType.Boolean);
        else if (coreMethod.ReturnType == typeof(string))
            SetStackType(StackType.String);
        else
            SetStackUnknown();
        return true;
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
