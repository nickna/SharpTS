using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext method body for an async state machine.
/// This is the heart of async IL generation, handling state dispatch,
/// await points, and result/exception completion.
/// </summary>
public partial class AsyncMoveNextEmitter : AsyncFunctionMoveNextEmitter, IEmitterContext
{
    private readonly AsyncStateMachineBuilder _builder;
    private readonly AsyncStateAnalyzer.AsyncFunctionAnalysis _analysis;
    private readonly ILGenerator _il;
    private readonly TypeProvider _types;

    // Abstract property implementations for ExpressionEmitterBase
    protected override ILGenerator IL => _il;
    protected override CompilationContext Ctx => _ctx!;
    protected override TypeProvider Types => _types;
    protected override IVariableResolver Resolver => _resolver!;
    protected override FieldBuilder? GetThisField() => _builder.ThisField;

    #region IEmitterContext Implementation

    /// <summary>
    /// Provides access to the compilation context for type emitter strategies.
    /// </summary>
    public CompilationContext Context => _ctx!;

    /// <summary>
    /// Provides access to the IL generator via IEmitterContext.
    /// </summary>
    ILGenerator IEmitterContext.IL => _il;

    /// <summary>
    /// Boxes the value on the stack if needed.
    /// In async context, we use the simpler EnsureBoxed approach.
    /// </summary>
    public void EmitBoxIfNeeded(Expr expr) => EnsureBoxed();

    /// <summary>
    /// Emits an expression and ensures the result is an unboxed double on the stack.
    /// </summary>
    public override void EmitExpressionAsDouble(Expr expr)
    {
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            // Literal double - push directly
            _il.Emit(OpCodes.Ldc_R8, d);
            _helpers.StackType = StackType.Double;
        }
        else if (expr is Expr.Literal intLit && intLit.Value is int i)
        {
            _il.Emit(OpCodes.Ldc_R8, (double)i);
            _helpers.StackType = StackType.Double;
        }
        else
        {
            // Other expressions - emit and convert if needed
            EmitExpression(expr);
            EnsureDouble();
        }
    }

    /// <summary>
    /// Marks the stack as containing an unknown/object type.
    /// Part of IEmitterContext interface for type emitter strategies.
    /// </summary>
    void IEmitterContext.SetStackUnknown() => _helpers.SetStackUnknown();

    /// <summary>
    /// Marks the stack as containing a specific type.
    /// Part of IEmitterContext interface for type emitter strategies.
    /// </summary>
    void IEmitterContext.SetStackType(StackType type) => _helpers.SetStackType(type);

    #endregion

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _endLabel;
    private Label _setResultLabel;

    // Current await point being processed
    private int _currentAwaitState = 0;

    // Exception handling
    private LocalBuilder? _exceptionLocal;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Variable resolver for hoisted fields and non-hoisted locals
    private IVariableResolver? _resolver;

    // Return value storage
    private LocalBuilder? _returnValueLocal;
    private bool _hasReturnValue;

    // DeclareLoopVariable and EmitStoreLoopVariable are handled by StatementEmitterBase
    // using the GetHoistedVariableField hook (overridden in .Statements.Variables.cs).

    // ---- AsyncFunctionMoveNextEmitter hooks (the await-aware try/catch + exit-routing base) ----

    protected override FieldBuilder DefineStateMachineField(string name, System.Type type)
        => _builder.StateMachineType.DefineField(name, type, FieldAttributes.Private);

    /// <summary>
    /// Completes the async-function state machine using the boxed value on the IL stack: store it to
    /// the return-value local (or discard it for a void async function), then leave to the SetResult
    /// epilogue (which sets state -2 and calls builder.SetResult).
    /// </summary>
    protected override void EmitCompleteWithReturnValueOnStack()
    {
        if (_returnValueLocal != null)
            _il.Emit(OpCodes.Stloc, _returnValueLocal);
        else
            _il.Emit(OpCodes.Pop);
        _il.Emit(OpCodes.Leave, _setResultLabel);
    }

    // @lock decorator support for async methods
    private Label _lockResumeLabel;  // Resume point after lock acquisition await

    // Lock state constant: state -3 is used for lock acquisition await
    private const int LockAcquireState = -3;

    public AsyncMoveNextEmitter(AsyncStateMachineBuilder builder, AsyncStateAnalyzer.AsyncFunctionAnalysis analysis, TypeProvider types)
        : base(new StateMachineEmitHelpers(builder.MoveNextMethod.GetILGenerator(), types))
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextMethod.GetILGenerator();
        _types = types;
        // A spilled value live across an await must survive the MoveNext re-entry in a
        // state-machine field, not a transient IL local (#400).
        _helpers.EnablePersistentSpills(name => _builder.StateMachineType.DefineField(
            name, _types.Object, System.Reflection.FieldAttributes.Private));
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx, Type returnType)
        => EmitMoveNext(body, ctx, returnType, null);

    /// <summary>
    /// Emits the complete MoveNext method body with optional default parameter handling.
    /// </summary>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx, Type returnType, List<Stmt.Parameter>? parameters)
    {
        if (body == null) return;

        _ctx = ctx;
        // Wire the runtime into the helper now that the context is bound, so MoveNext uses the
        // $Undefined sentinel and JS-spec coercion/comparison helpers rather than null/Convert (#600).
        if (ctx.Runtime != null) _helpers.SetRuntime(ctx.Runtime);
        _hasReturnValue = returnType != _types.Void;

        // Create variable resolver for hoisted fields and non-hoisted locals
        _resolver = new StateMachineVariableResolver(
            _il,
            _builder.GetVariableField,
            ctx.Locals,
            _builder.ThisField,
            ctx.CellBindingLocals,
            ctx.Types.StrongBoxOfObjectValueField);

        // Declare exception local for catch block
        _exceptionLocal = _il.DeclareLocal(_types.Exception);

        // Declare return value local if needed
        if (_hasReturnValue)
        {
            _returnValueLocal = _il.DeclareLocal(_types.Object);
        }

        // Define labels for each await resume point
        foreach (var awaitPoint in _analysis.AwaitPoints)
        {
            _stateLabels[awaitPoint.StateNumber] = _il.DefineLabel();
        }
        _endLabel = _il.DefineLabel();
        _setResultLabel = _il.DefineLabel();

        // Define lock resume label if needed (uses state machine's reference fields)
        bool hasLock = _builder.HasLockDecorator;
        if (hasLock)
        {
            _lockResumeLabel = _il.DefineLabel();
        }

        // Begin outer try block
        _il.BeginExceptionBlock();

        // Emit state dispatch switch (includes lock state if needed)
        EmitStateSwitch();

        // For @lock: emit lock acquisition prologue after state switch
        // This runs on initial entry (state -1) and handles reentrancy tracking
        if (hasLock)
        {
            EmitLockAcquisitionPrologue();
        }

        // Emit default parameter handling at the beginning of the body
        // This checks if hoisted parameters are undefined and assigns default values
        if (parameters != null)
        {
            EmitDefaultParametersWithResumeGuard(parameters);
        }

        // Emit the function body (will emit await points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Falling off the end of the body completes the async function with `undefined`,
        // not null (#587) — the default-null _returnValueLocal would otherwise resolve the
        // promise with null. Explicit `return` statements Leave straight to _setResultLabel
        // and never reach this fall-through, so this only sets the implicit-completion value.
        if (_returnValueLocal != null)
        {
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
            _il.Emit(OpCodes.Stloc, _returnValueLocal);
        }

        // Jump to set result
        _il.Emit(OpCodes.Br, _setResultLabel);

        // Set result label
        _il.MarkLabel(_setResultLabel);

        // For @lock: emit lock release before SetResult
        if (hasLock)
        {
            EmitLockReleaseEpilogue();
        }

        // this.<>1__state = -2
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetResult(returnValue)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        if (_hasReturnValue && _returnValueLocal != null)
            _il.Emit(OpCodes.Ldloc, _returnValueLocal);
        else
            _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetResultMethod());
        _il.Emit(OpCodes.Leave, _endLabel);

        // Begin catch block
        _il.BeginCatchBlock(_types.Exception);
        _il.Emit(OpCodes.Stloc, _exceptionLocal);

        // For @lock: emit lock release before SetException
        if (hasLock)
        {
            EmitLockReleaseEpilogue();
        }

        // this.<>1__state = -2
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetException(exception)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _exceptionLocal);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetExceptionMethod());
        _il.Emit(OpCodes.Leave, _endLabel);

        // End exception block
        _il.EndExceptionBlock();

        // End label
        _il.MarkLabel(_endLabel);
        _il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Applies parameter defaults at async-function entry via the shared
    /// StateMachineExitRoutingEmitter core, wrapped in the DefaultsAppliedField guard so a state
    /// machine resume does not re-evaluate defaults.
    /// </summary>
    private void EmitDefaultParametersWithResumeGuard(List<Stmt.Parameter> parameters)
    {
        if (!parameters.Any(p => p.DefaultValue != null)) return;

        // Check if defaults have already been applied (skip on resume)
        var skipAllDefaults = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.DefaultsAppliedField!);
        _il.Emit(OpCodes.Brtrue, skipAllDefaults);

        EmitDefaultParameters(parameters);

        // Mark defaults as applied
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Stfld, _builder.DefaultsAppliedField!);

        _il.MarkLabel(skipAllDefaults);
    }

    /// <summary>
    /// Emits the lock acquisition prologue for @lock async methods.
    /// This handles reentrancy tracking and awaits the semaphore if needed.
    /// Uses state machine's lock reference fields to avoid casting issues with TypeBuilder.
    /// </summary>
    private void EmitLockAcquisitionPrologue()
    {
        var lockAcquiredLabel = _il.DefineLabel();
        var afterLockCheckLabel = _il.DefineLabel();

        // int prevReentrancy = this.<>__lockReentrancyRef.Value;
        // this.<>__prevReentrancy = prevReentrancy;
        _il.Emit(OpCodes.Ldarg_0);  // this (state machine)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockReentrancyRefField!);  // Use state machine's reference
        _il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.GetMethod!);
        _il.Emit(OpCodes.Stfld, _builder.LockPrevReentrancyField!);

        // this.<>__lockReentrancyRef.Value = prevReentrancy + 1;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockReentrancyRefField!);  // Use state machine's reference
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockPrevReentrancyField!);  // Non-null when HasLockDecorator
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

        // this.<>__lockAcquired = false; (will be set to true after acquiring)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stfld, _builder.LockAcquiredField!);

        // if (prevReentrancy == 0) { await _asyncLock.WaitAsync(); }
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockPrevReentrancyField!);  // Non-null when HasLockDecorator
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Bne_Un, afterLockCheckLabel);  // Skip lock acquisition if reentrant

        // Call this.<>__asyncLockRef.WaitAsync().GetAwaiter()
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.AsyncLockRefField!);  // Use state machine's reference
        _il.Emit(OpCodes.Callvirt, AsyncStateMachineBuilder.GetSemaphoreWaitAsyncMethod());
        _il.Emit(OpCodes.Call, AsyncStateMachineBuilder.GetTaskAwaiterMethod());

        // Store awaiter in state machine
        var awaiterLocal = _il.DeclareLocal(typeof(TaskAwaiter));
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, _builder.LockAwaiterField!);

        // Check if already completed
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.LockAwaiterField!);  // Non-null when HasLockDecorator
        _il.Emit(OpCodes.Call, AsyncStateMachineBuilder.GetLockAwaiterIsCompletedGetter());
        _il.Emit(OpCodes.Brtrue, lockAcquiredLabel);

        // Not completed - suspend and wait
        // this.<>1__state = -3;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, LockAcquireState);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.LockAwaiterField!);  // Non-null when HasLockDecorator
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, _builder.GetBuilderAwaitUnsafeOnCompletedMethodForLock());

        // return (exit MoveNext)
        _il.Emit(OpCodes.Leave, _endLabel);

        // Lock resume point (jumped to from state switch when state == -3)
        _il.MarkLabel(_lockResumeLabel);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // lockAcquiredLabel: awaiter.GetResult() to propagate any exceptions
        _il.MarkLabel(lockAcquiredLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.LockAwaiterField!);  // Non-null when HasLockDecorator
        _il.Emit(OpCodes.Call, AsyncStateMachineBuilder.GetLockAwaiterGetResultMethod());

        // Mark lock as acquired
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Stfld, _builder.LockAcquiredField!);  // Non-null when HasLockDecorator

        // afterLockCheckLabel: continue with user code
        _il.MarkLabel(afterLockCheckLabel);
    }

    /// <summary>
    /// Emits the lock release epilogue for @lock async methods.
    /// This restores the reentrancy counter and releases the semaphore if we acquired it.
    /// Uses state machine's lock reference fields to avoid casting issues with TypeBuilder.
    /// </summary>
    private void EmitLockReleaseEpilogue()
    {
        var skipReleaseLabel = _il.DefineLabel();

        // Restore reentrancy counter: this.<>__lockReentrancyRef.Value = this.<>__prevReentrancy;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockReentrancyRefField!);  // Use state machine's reference
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockPrevReentrancyField!);
        _il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

        // if (this.<>__lockAcquired) { this.<>__asyncLockRef.Release(); }
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.LockAcquiredField!);
        _il.Emit(OpCodes.Brfalse, skipReleaseLabel);

        // Release the semaphore using state machine's reference field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.AsyncLockRefField!);  // Use state machine's reference
        _il.Emit(OpCodes.Callvirt, AsyncStateMachineBuilder.GetSemaphoreReleaseMethod());
        _il.Emit(OpCodes.Pop);  // Discard the return value (previous count)

        _il.MarkLabel(skipReleaseLabel);
    }

    private void EmitStateSwitch()
    {
        bool hasLock = _builder.HasLockDecorator;

        // Check for lock resume state first (state == -3)
        if (hasLock)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.StateField);
            _il.Emit(OpCodes.Ldc_I4, LockAcquireState);
            _il.Emit(OpCodes.Beq, _lockResumeLabel);
        }

        if (_analysis.AwaitPointCount == 0) return;

        // Load state field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);

        // Create labels array for switch
        var labels = new Label[_analysis.AwaitPointCount];
        for (int i = 0; i < _analysis.AwaitPointCount; i++)
        {
            labels[i] = _stateLabels[i];
        }

        // switch (state) { case 0: goto State0; case 1: goto State1; ... }
        _il.Emit(OpCodes.Switch, labels);

        // Fall through for state -1 (initial execution)
    }
}
