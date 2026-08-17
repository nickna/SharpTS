using System;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Shared base for the two async-FUNCTION MoveNext emitters (<see cref="AsyncMoveNextEmitter"/> and
/// <see cref="AsyncArrowMoveNextEmitter"/>). Holds the await-aware try/catch and the non-local-exit
/// (return / break / continue) finally-routing machinery (#774) in one place so the two emitters
/// cannot drift. The arrow previously carried its own structured-EH copy of try/catch that emitted
/// invalid IL (a runtime InvalidProgramException) on an <c>await</c> inside a <c>try</c>, and skipped
/// <c>finally</c> on a non-local exit out of such a try, because this machinery was never ported to it.
/// </summary>
/// <remarks>
/// The generic exit-scope / finally-routing scaffolding lives in the shared
/// <see cref="StateMachineExitRoutingEmitter"/> base; this class adds the await-aware try/catch and the
/// async-function return completion on top of it. The generator emitters share that same scaffolding
/// base but keep their own suspension-specific try/catch bodies and throw routing.
///
/// The await machinery is emitter-agnostic except for two seams, exposed as hooks:
///   * <see cref="EmitCompleteWithReturnValueOnStack"/> — completes the state machine using the boxed
///     return value currently on the IL stack (async-fn stores it to its return-value local and leaves
///     to its SetResult label; the arrow's EmitSetResult consumes the stack value);
///   * <see cref="EmitAwaitGetResult"/> — wraps the awaiter's GetResult in the flag-based capture
///     try/catch when emitting inside a try-with-awaits; called by each emitter's await step.
/// (The third seam, <c>DefineStateMachineField</c>, is declared on the scaffolding base.)
/// </remarks>
public abstract partial class AsyncFunctionMoveNextEmitter : StateMachineExitRoutingEmitter
{
    protected AsyncFunctionMoveNextEmitter(StateMachineEmitHelpers helpers)
        : base(helpers)
    {
    }

    // For try/catch with awaits: where a caught exception is stored, and where to jump after capturing
    // a rejected await (the try-exit, so the remaining try body is abandoned). Both are set together by
    // EmitTryBodyWithAwaits and consulted by the await emission via EmitAwaitGetResult.
    protected LocalBuilder? _currentTryCatchExceptionLocal = null;
    protected Label? _currentTryCatchSkipLabel = null;

    // ---- Emitter-specific seams ----------------------------------------------------------------

    /// <summary>
    /// Completes the state machine using the boxed return value currently on top of the IL stack.
    /// </summary>
    protected abstract void EmitCompleteWithReturnValueOnStack();

    /// <summary>
    /// Emits <c>awaiter.GetResult()</c> (via <paramref name="emitRawGetResultCall"/>), leaving the
    /// result on the stack. Inside a flag-based try-with-awaits (<see cref="_currentTryCatchExceptionLocal"/>
    /// set) the call is wrapped in a try/catch that captures a rejection into the exception local and
    /// branches to the try-exit so the remaining try body is abandoned (rather than resuming it with a
    /// null result); otherwise the bare call is emitted. Shared by both emitters' await step (#774).
    /// </summary>
    protected void EmitAwaitGetResult(Action emitRawGetResultCall)
    {
        if (_currentTryCatchExceptionLocal != null)
        {
            var getResultDoneLabel = IL.DefineLabel();
            var exceptionCaughtLabel = IL.DefineLabel();
            IL.BeginExceptionBlock();
            emitRawGetResultCall();
            var resultTemp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, resultTemp);
            IL.Emit(OpCodes.Leave, getResultDoneLabel);
            IL.BeginCatchBlock(typeof(Exception));
            IL.Emit(OpCodes.Call, Ctx.Runtime!.WrapException);
            IL.Emit(OpCodes.Stloc, _currentTryCatchExceptionLocal);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stloc, resultTemp);
            IL.Emit(OpCodes.Leave, exceptionCaughtLabel);
            IL.EndExceptionBlock();

            // A rejected awaitable must abandon the rest of the try body, not resume it with a null
            // result (which ran BOTH the success path and the catch). Jump straight to the try-exit so
            // the statement-level catch dispatch in EmitTryCatchWithAwaits sees the exception local.
            IL.MarkLabel(exceptionCaughtLabel);
            if (_currentTryCatchSkipLabel != null)
            {
                IL.Emit(OpCodes.Br, _currentTryCatchSkipLabel.Value);
            }
            // No skip target (shouldn't happen — the local and label are set together): fall through
            // with the null result as before.

            IL.MarkLabel(getResultDoneLabel);
            IL.Emit(OpCodes.Ldloc, resultTemp);
        }
        else
        {
            emitRawGetResultCall();
        }
    }

    // ---- Non-local return with finally routing (#774) ------------------------------------------

    protected override void EmitReturn(Stmt.Return r)
    {
        // Produce the completion value on the stack: the returned expression, or `undefined` for a
        // bare `return;` (resolves the promise with undefined, not null — #587).
        if (r.Value != null)
        {
            EmitExpression(r.Value);
            EnsureBoxed();

            // Async-function return performs PromiseResolve-style adoption. Private async
            // methods have a statically identifiable call node and return Task<object>, so
            // consume the synthetic analyzer state and await it before completing the outer
            // function. This prevents a nested host Task from becoming the JavaScript result.
            if (r.Value is Expr.CallPrivate privateCall)
            {
                int returnState = NextAwaitState();
                if (PrivateCallReturnsTask(privateCall))
                    EmitAwaitFromValueOnStack(returnState);
                else
                {
                    // Analysis conservatively reserves a synthetic state for every private call.
                    // A synchronous private method does not suspend, but its unreachable dispatch
                    // label must still be materialized with the dispatcher's empty-stack shape.
                    var synchronousResult = IL.DeclareLocal(typeof(object));
                    IL.Emit(OpCodes.Stloc, synchronousResult);
                    MarkAwaitResumeLabel(returnState);
                    IL.Emit(OpCodes.Ldloc, synchronousResult);
                }
            }
        }
        else
        {
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
        }

        // If a flag-based finally lies between this return and the function boundary, run it (and any
        // outer ones) before completing: stash the value in a state-machine field (an await in the
        // finally would lose an IL local across the suspension), then route through the finally(s). The
        // return terminal reloads the value and completes (#774).
        var chain = ActiveFinallyFrames();
        if (chain.Count > 0)
        {
            var tmp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, tmp);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldloc, tmp);
            IL.Emit(OpCodes.Stfld, GetPendingReturnValueField());

            RegisterReturnTerminal();
            RouteThroughFinallys(chain, ExitCodeReturn, Ctx.ExceptionBlockDepth > 0 ? OpCodes.Leave : OpCodes.Br);
            return;
        }

        EmitCompleteWithReturnValueOnStack();
    }

    private bool PrivateCallReturnsTask(Expr.CallPrivate call)
    {
        string methodName = call.Name.Lexeme.TrimStart('#');
        string? className = Ctx.CurrentClassName;
        if (className == null || Ctx.ClassRegistry == null)
            return false;

        className = Ctx.ResolvePrivateMethodOwner(className, methodName);
        if (Ctx.ClassRegistry.TryGetPrivateMethod(className, methodName, out var instanceMethod)
            && instanceMethod?.ReturnType == Ctx.Types.TaskOfObject)
            return true;
        if (Ctx.ClassRegistry.TryGetStaticPrivateMethod(className, methodName, out var staticMethod)
            && staticMethod?.ReturnType == Ctx.Types.TaskOfObject)
            return true;
        return false;
    }

    /// <summary>
    /// The terminal for a routed <c>return</c>: a finally that awaited between the <c>return</c> and
    /// here ran on a separate MoveNext invocation, so the return value (stashed in
    /// <c>&lt;&gt;pendingReturnValue</c> at the <c>return</c>) is reloaded onto the stack and used to
    /// complete the state machine via <see cref="EmitCompleteWithReturnValueOnStack"/>.
    /// </summary>
    private void RegisterReturnTerminal() => _exitTerminals.TryAdd(ExitCodeReturn, () =>
    {
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, GetPendingReturnValueField());
        EmitCompleteWithReturnValueOnStack();
    });
}
