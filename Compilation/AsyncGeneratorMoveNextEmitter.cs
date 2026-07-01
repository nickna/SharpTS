using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNextAsync method body for an async generator state machine.
/// Handles state dispatch, yield points, await points, and generator completion.
/// </summary>
public partial class AsyncGeneratorMoveNextEmitter : IteratorMoveNextEmitter
{
    private readonly AsyncGeneratorStateMachineBuilder _builder;
    private readonly AsyncGeneratorStateAnalyzer.AsyncGeneratorFunctionAnalysis _analysis;
    private readonly ILGenerator _il;
    private readonly TypeProvider _types;

    // Abstract property implementations for ExpressionEmitterBase
    protected override ILGenerator IL => _il;
    protected override CompilationContext Ctx => _ctx!;
    protected override TypeProvider Types => _types;
    protected override IVariableResolver Resolver => _resolver!;
    protected override FieldBuilder? GetThisField() => _builder.ThisField;

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _returnFalseLabel;

    // Landing pad for a `return` emitted inside a real IL try/finally (EmitSimpleTryCatch) that has no
    // enclosing flag-based finally (#597, async analog of #554). Such a return cannot complete the state
    // machine directly (a `ret`/EmitReturnValueTaskBool is illegal inside a protected region), so it sets
    // Current/state then `Leave`s here — running the enclosing no-yield finally(s) — to return false.
    // Marked only when used; Current already holds the completion value, so it is not reset here.
    private Label _deferredReturnLabel;
    private bool _deferredReturnUsed;

    // Current suspension point being processed
    private int _currentSuspensionState = 0;

    // Label to jump to when __returnRequested is detected at a yield resume point.
    // Set to the afterTryBody label of the enclosing try/finally block (if any).
    private Label? _returnCleanupLabel;

    // While emitting a flag-based try BODY (EmitTryCatchWithSuspensions), these carry that try's
    // exception-capture target down to suspension points, which are emitted at the top level —
    // outside the sync segments' mini try/catch. A rejected `await` inside the try captures its
    // exception into _currentTryExceptionLocal (exactly as a sync segment does), sets the present
    // flag, and `Leave`s to _currentTryCleanupLabel so the try's catch/finally run, instead of
    // escaping MoveNextAsync unhandled (#617). The present flag (not the value's nullness) gates the
    // catch so a rejected `Promise.reject(null)`/`throw null` still engages it (#628, the async
    // analog of #619). Saved/restored around the body, so during a catch/finally body these instead
    // identify the *enclosing* flag-based try — the one whose catch must handle a throw escaping that
    // handler (#632). Null when not inside such a try body (the common case → GetResult is plain).
    private LocalBuilder? _currentTryExceptionLocal;
    private LocalBuilder? _currentTryExceptionPresentLocal;
    private Label _currentTryCleanupLabel;

    // `_exitScopes.Count` captured at the start of the current flag-based try's body emission (after
    // its own finally scope, if any, was pushed). Finally scopes at indices >= this are strictly
    // *inside* that try; a throw escaping a nested handler runs exactly those before reaching this
    // try's catch (#632). Meaningful only while _currentTryExceptionLocal is non-null.
    private int _currentTryScopeDepth;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Variable resolver for hoisted fields and non-hoisted locals
    private IVariableResolver? _resolver;

    public AsyncGeneratorMoveNextEmitter(AsyncGeneratorStateMachineBuilder builder, AsyncGeneratorStateAnalyzer.AsyncGeneratorFunctionAnalysis analysis, TypeProvider types)
        : base(new StateMachineEmitHelpers(builder.MoveNextAsyncMethod.GetILGenerator(), types))
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextAsyncMethod.GetILGenerator();
        _types = types;
        // A value spilled before an await/yield and used after it (e.g. the `p` in `p + (await x)`)
        // must survive the MoveNextAsync re-entry in a state-machine field, not a transient IL local —
        // the async-generator analog of #400. This was the only state-machine emitter that lacked the
        // wiring (the other three enable it), so such a value was silently lost across a suspension.
        _helpers.EnablePersistentSpills(name => _builder.StateMachineType.DefineField(
            name, _types.Object, FieldAttributes.Private));
    }

    // DeclareLoopVariable and EmitStoreLoopVariable are handled by StatementEmitterBase
    // using the GetHoistedVariableField hook (overridden in .Statements.cs).

    /// <summary>
    /// Emits the complete MoveNextAsync method body.
    /// </summary>
    public void EmitMoveNextAsync(List<Stmt>? body, CompilationContext ctx, List<Stmt.Parameter>? parameters = null)
    {
        if (body == null)
        {
            // Empty body - just return false
            EmitReturnValueTaskBool(false);
            return;
        }

        _ctx = ctx;
        // Wire the runtime into the helper now that the context is bound, so MoveNext uses the
        // $Undefined sentinel and JS-spec coercion/comparison helpers rather than null/Convert (#600).
        if (ctx.Runtime != null) _helpers.SetRuntime(ctx.Runtime);

        // Create variable resolver for hoisted fields and non-hoisted locals
        _resolver = new StateMachineVariableResolver(
            _il,
            _builder.GetVariableField,
            ctx.Locals,
            _builder.ThisField,
            ctx.CellBindingLocals,
            ctx.Types.StrongBoxOfObjectValueField);

        // Define labels for each suspension resume point
        foreach (var suspensionPoint in _analysis.SuspensionPoints)
        {
            _stateLabels[suspensionPoint.StateNumber] = _il.DefineLabel();
        }
        _returnFalseLabel = _il.DefineLabel();
        _deferredReturnLabel = _il.DefineLabel();

        // Check if generator is already completed (state == -2)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Beq, _returnFalseLabel);

        // Emit state dispatch switch
        EmitStateSwitch();

        // Apply parameter defaults on initial entry only (state -1 falls through here; resume
        // states jump past via the switch, the completed state short-circuits above). (#737)
        if (parameters != null)
        {
            EmitDefaultParameters(parameters);
        }

        // Emit the function body (will emit yield/await points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Fall through after body completes — the generator ran off the end with no `return`.
        // Per ECMA-262 its completion value is `undefined`, so reset Current to the `$Undefined`
        // sentinel rather than CLR null: it currently still holds the last *yielded* value, which would
        // otherwise surface as `next().value` after done, or as a delegating `yield*`'s completion value
        // (#481, async analog of #443). An explicit `return X` takes the EmitReturn path and stores X.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(false);

        // Deferred-return landing pad (#597, async analog of #554): a `return` inside a real IL
        // try/finally `Leave`s here after its enclosing no-yield finally(s) have run. Current and state
        // were already set at the `return`, so just return false. Marked only if such a return was emitted.
        if (_deferredReturnUsed)
        {
            _il.MarkLabel(_deferredReturnLabel);
            EmitReturnValueTaskBool(false);
        }

        // Return false label — re-entry on an already-completed generator (state == -2): next() called
        // after the generator finished, or after return(v). Per ECMA-262 27.6.1.2 a completed async
        // generator's next() always reports { value: undefined, done: true } — the completion value was
        // already consumed by the call that finished it. Reset Current to the `$Undefined` sentinel so a
        // stale value does not leak: an explicit `return X` left X in Current, and return(v) leaves the
        // last *yielded* value there untouched — either of which the done-path read of Current in next()
        // would otherwise re-surface (#540, async analog of #499). The first MoveNextAsync to complete
        // takes the fall-through / EmitReturn path above (correct completion value), not this label.
        _il.MarkLabel(_returnFalseLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        EmitReturnValueTaskBool(false);
    }

    /// <summary>
    /// Applies parameter defaults at async-generator entry — the async-generator analogue of the sync
    /// generator and async function versions (this state machine previously ran no default prologue, so
    /// defaults never fired in compiled mode). A defaulted parameter whose argument was omitted or
    /// explicitly <c>undefined</c> arrives in its hoisted field as null / the <c>$Undefined</c> sentinel
    /// and is replaced with the evaluated default. Evaluated in declaration order so a later default may
    /// reference an earlier (already-defaulted) parameter. (#737)
    /// </summary>
    private void EmitDefaultParameters(List<Stmt.Parameter> parameters)
    {
        if (!parameters.Any(p => p.DefaultValue != null))
            return;

        foreach (var param in parameters)
        {
            if (param.DefaultValue == null) continue;

            var field = _builder.GetVariableField(param.Name.Lexeme);
            if (field == null) continue; // Parameter not hoisted (unused in body) — default is moot.

            var applyDefault = _il.DefineLabel();
            var checkUndefined = _il.DefineLabel();
            var skipDefault = _il.DefineLabel();

            // if (field == null) apply; else if (field is $Undefined) apply; else keep.
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Brtrue, checkUndefined);
            _il.Emit(OpCodes.Pop);                       // pop the null
            _il.Emit(OpCodes.Br, applyDefault);

            _il.MarkLabel(checkUndefined);
            _il.Emit(OpCodes.Isinst, _ctx!.Runtime!.UndefinedType);
            _il.Emit(OpCodes.Brtrue, applyDefault);
            _il.Emit(OpCodes.Br, skipDefault);

            _il.MarkLabel(applyDefault);
            EmitExpression(param.DefaultValue);
            EnsureBoxed();
            var temp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);

            // A captured-and-mutated parameter's live storage is the function display-class field,
            // not the state-machine field the body no longer reads. Mirror the default into the DC
            // field so a nested arrow observes the default rather than the omitted-arg $Undefined
            // sentinel (#792). Non-captured params return false here and keep the SM-field-only path.
            if (TryGetFunctionDCField(param.Name.Lexeme, out var dcField))
            {
                _il.Emit(OpCodes.Ldloc, temp);
                StoreToDCField(dcField);
            }

            _il.MarkLabel(skipDefault);
        }
    }

    private void EmitStateSwitch()
    {
        if (_analysis.SuspensionPointCount == 0) return;

        // Load state field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);

        // Create labels array for switch
        var labels = new Label[_analysis.SuspensionPointCount];
        for (int i = 0; i < _analysis.SuspensionPointCount; i++)
        {
            labels[i] = _stateLabels[i];
        }

        // switch (state) { case 0: goto State0; case 1: goto State1; ... }
        _il.Emit(OpCodes.Switch, labels);

        // Fall through for state -1 (initial execution)
    }

    /// <summary>
    /// Emits code to return ValueTask&lt;bool&gt; with the specified result.
    /// </summary>
    private void EmitReturnValueTaskBool(bool result)
    {
        // Create ValueTask<bool> from result using constructor
        // new ValueTask<bool>(result)
        _il.Emit(result ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        var vtCtor = _types.ValueTaskOfBool.GetConstructor([_types.Boolean])!;
        _il.Emit(OpCodes.Newobj, vtCtor);
        _il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits code to return ValueTask&lt;bool&gt; wrapping a Task&lt;bool&gt;.
    /// </summary>
    private void EmitReturnValueTaskFromTask()
    {
        // Stack has Task<bool>
        // Create ValueTask<bool> from Task<bool>
        var vtCtor = _types.ValueTaskOfBool.GetConstructor([_types.MakeGenericType(_types.TaskOpen, _types.Boolean)])!;
        _il.Emit(OpCodes.Newobj, vtCtor);
        _il.Emit(OpCodes.Ret);
    }

    #region Helper Method Wrappers - Unique to AsyncGeneratorMoveNextEmitter

    private void SetStackNumber() => _helpers.SetStackType(StackType.Double);
    private void SetStackString() => _helpers.SetStackType(StackType.String);
    private void SetStackBoolean() => _helpers.SetStackType(StackType.Boolean);
    private void SetStackObject() => _helpers.SetStackUnknown();

    #endregion
}
