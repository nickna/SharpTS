using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext method body for a generator state machine.
/// Handles state dispatch, yield points, and generator completion.
/// </summary>
public partial class GeneratorMoveNextEmitter : IteratorMoveNextEmitter
{
    private readonly GeneratorStateMachineBuilder _builder;
    private readonly GeneratorStateAnalyzer.GeneratorFunctionAnalysis _analysis;
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
    // enclosing flag-based finally (#554). Such a return cannot `ret` directly, so it sets Current/state
    // then `Leave`s here — running the enclosing no-yield finally(s) — to perform the actual `ret false`.
    // Marked only when used; Current already holds the completion value, so it is not reset here.
    private Label _deferredReturnLabel;
    private bool _deferredReturnUsed;

    // Current yield point being processed
    private int _currentYieldState = 0;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Variable resolver for hoisted fields and non-hoisted locals
    private IVariableResolver? _resolver;

    // DeclareLoopVariable and EmitStoreLoopVariable are handled by StatementEmitterBase
    // using the GetHoistedVariableField hook (overridden in .Statements.cs).

    public GeneratorMoveNextEmitter(GeneratorStateMachineBuilder builder, GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis, TypeProvider types)
        : base(new StateMachineEmitHelpers(builder.MoveNextMethod.GetILGenerator(), types))
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextMethod.GetILGenerator();
        _types = types;
        // A value spilled across a yield must survive the MoveNext re-entry in a
        // state-machine field, not a transient IL local (#400/#414).
        _helpers.EnablePersistentSpills(name => _builder.StateMachineType.DefineField(
            name, _types.Object, System.Reflection.FieldAttributes.Private));
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    /// <param name="parameters">The generator's declared parameters. When supplied, a default-parameter
    /// prologue runs on initial entry so an omitted or explicit-<c>undefined</c> argument fires its
    /// default (#737). Null skips it (callers with no params).</param>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx, List<Stmt.Parameter>? parameters = null)
    {
        if (body == null) return;

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

        // Define labels for each yield resume point
        foreach (var yieldPoint in _analysis.YieldPoints)
        {
            _stateLabels[yieldPoint.StateNumber] = _il.DefineLabel();
        }
        _returnFalseLabel = _il.DefineLabel();
        _deferredReturnLabel = _il.DefineLabel();

        // Check if generator is already completed (state == -2)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Beq, _returnFalseLabel);

        // Emit state dispatch switch
        EmitStateSwitch(_builder.StateField, _analysis.YieldPointCount, _stateLabels);

        // Apply parameter defaults on initial entry. The state switch jumps every resume state
        // (>= 0) to its yield label and the completed state (-2) short-circuits above, so only the
        // initial entry (state -1) falls through to here — the defaults run exactly once, before the
        // body, with no extra guard field needed. (#737)
        if (parameters != null)
        {
            EmitDefaultParameters(parameters);
        }

        // Emit the function body (will emit yield points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Fall through after body completes — the generator ran off the end with no `return`.
        // Per ECMA-262 its completion value is `undefined`, so reset Current to the `$Undefined`
        // sentinel: it currently still holds the last *yielded* value, which would otherwise
        // surface as the completion value via `gen.next().value` after done, or as the result of
        // a delegating `yield* thisGenerator()` (#443). An explicit `return X` takes the
        // EmitReturn path instead and stores X in Current, so this only affects the no-return case.
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);

        // Mark as done and return false
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);

        // Deferred-return landing pad (#554): a `return` inside a real IL try/finally `Leave`s here
        // after its enclosing no-yield finally(s) have run. Current and state were already set at the
        // `return`, so just `ret false`. Marked only if such a return was emitted.
        if (_deferredReturnUsed)
        {
            _il.MarkLabel(_deferredReturnLabel);
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Ret);
        }

        // Return false label — re-entry on an already-completed generator (state == -2):
        // `gen.next()` called after the generator finished, or after `gen.return(v)`.
        // Per ECMA-262 27.5.1.2, a completed generator's `next()` always reports
        // `{ value: undefined, done: true }` — the completion value was already consumed by the
        // call that finished it. Reset Current to the `$Undefined` sentinel so a stale value does
        // not leak: an explicit `return X` left X in Current, and `gen.return(v)`
        // (GeneratorStateMachineBuilder) leaves the last *yielded* value there untouched — either
        // of which the done-path read of Current in `next()` would otherwise re-surface (#499).
        // The first MoveNext to complete takes the fall-through / EmitReturn path above (which set
        // the correct completion value), not this label, so genuine completion values are kept.
        _il.MarkLabel(_returnFalseLabel);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
    }

}
