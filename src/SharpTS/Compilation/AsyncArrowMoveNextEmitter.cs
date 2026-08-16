using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext body for an async arrow function's state machine.
/// Similar to AsyncMoveNextEmitter but handles captured variable access
/// through the outer state machine reference.
/// </summary>
public partial class AsyncArrowMoveNextEmitter : AsyncFunctionMoveNextEmitter, IEmitterContext
{
    private readonly AsyncArrowStateMachineBuilder _builder;
    private readonly AsyncStateAnalyzer.AsyncFunctionAnalysis _analysis;
    private readonly TypeProvider _types;
    private readonly ILGenerator _il;

    // Abstract property implementations for ExpressionEmitterBase
    protected override ILGenerator IL => _il;
    protected override CompilationContext Ctx => _ctx!;
    protected override TypeProvider Types => _types;
    protected override IVariableResolver Resolver => _resolver!;

    private CompilationContext? _ctx;
    private int _currentState = 0;
    private readonly List<Label> _stateLabels = [];
    private Label _exitLabel;
    private LocalBuilder? _resultLocal;
    private LocalBuilder? _exceptionLocal;

    // Non-hoisted local variables (live within a single MoveNext invocation)
    private readonly Dictionary<string, LocalBuilder> _locals = [];

    // Variable resolver for hoisted fields, locals, and captured variables
    private IVariableResolver? _resolver;

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
    /// In the async arrow context, uses EnsureBoxed from helpers.
    /// </summary>
    public void EmitBoxIfNeeded(Expr expr) => EnsureBoxed();

    /// <summary>
    /// Emits an expression and ensures the result is an unboxed double on the stack.
    /// </summary>
    public override void EmitExpressionAsDouble(Expr expr)
    {
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            _il.Emit(OpCodes.Ldc_R8, d);
            return;
        }

        EmitExpression(expr);
        EnsureBoxed();

        // Try to unbox if it's a boxed double
        var skipUnbox = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Isinst, typeof(double));
        _il.Emit(OpCodes.Brfalse, skipUnbox);

        // It's a boxed double - unbox it
        _il.Emit(OpCodes.Unbox_Any, typeof(double));
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(skipUnbox);
        // Not a double - use runtime ToNumber conversion
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.ToNumber);

        _il.MarkLabel(endLabel);
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

    public AsyncArrowMoveNextEmitter(
        AsyncArrowStateMachineBuilder builder,
        AsyncStateAnalyzer.AsyncFunctionAnalysis analysis,
        TypeProvider types)
        : base(new StateMachineEmitHelpers(builder.MoveNextMethod.GetILGenerator(), types))
    {
        _builder = builder;
        _analysis = analysis;
        _types = types;
        _il = builder.MoveNextMethod.GetILGenerator();
        // A value spilled across an await must survive the MoveNext re-entry in a
        // state-machine field, not a transient IL local (#400/#414).
        _helpers.EnablePersistentSpills(name => _builder.StateMachineType.DefineField(
            name, _types.Object, System.Reflection.FieldAttributes.Private));
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt> body, CompilationContext ctx, Type returnType)
        => EmitMoveNext(body, ctx, returnType, null);

    public void EmitMoveNext(List<Stmt> body, CompilationContext ctx, Type returnType, List<Stmt.Parameter>? parameters)
    {
        // Note: _il is initialized in constructor via GetILGenerator()
        _ctx = ctx;
        // Wire the runtime into the helper now that the context is bound, so MoveNext uses the
        // $Undefined sentinel and JS-spec coercion/comparison helpers rather than null/Convert (#600).
        if (ctx.Runtime != null) _helpers.SetRuntime(ctx.Runtime);

        // Create variable resolver for hoisted fields, locals, and captured variables
        _resolver = new AsyncArrowVariableResolver(_il, _builder, _locals,
            ctx.CellBindingLocals, ctx.Types.StrongBoxOfObjectValueField);

        // Create labels for each await state
        for (int i = 0; i < _analysis.AwaitPointCount; i++)
        {
            _stateLabels.Add(_il.DefineLabel());
        }
        _exitLabel = _il.DefineLabel();

        // Declare locals
        _resultLocal = _il.DeclareLocal(_types.Object);
        _exceptionLocal = _il.DeclareLocal(_types.Exception);

        // Begin try block
        _il.BeginExceptionBlock();

        // State dispatch switch
        EmitStateDispatch();

        // Follow-up to #838: instantiate this arrow's own function display class once, so a nested sync
        // arrow that writes one of the arrow's locals shares the reference cell. Null-guarded: a resume
        // re-enters MoveNext but the field is already set, so it is created exactly once on initial entry.
        if (_builder.FunctionDCField != null && _builder.FunctionDCCtor != null)
        {
            var dcAlreadySet = _il.DefineLabel();
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _builder.FunctionDCField);
            _il.Emit(OpCodes.Brtrue, dcAlreadySet);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Newobj, _builder.FunctionDCCtor);
            _il.Emit(OpCodes.Stfld, _builder.FunctionDCField);
            _il.MarkLabel(dcAlreadySet);
        }

        // Apply parameter defaults on initial entry (#646). Placed after the state-dispatch
        // switch so a resume (state >= 0) jumps straight to its await label past this code; on
        // initial entry (state -1) the switch falls through to here. Arrow parameter fields are
        // always object-typed (AsyncArrowStateMachineBuilder), so the null / $Undefined check
        // is valid — unlike the sync-arrow path, no slot widening is needed.
        if (parameters != null)
        {
            EmitDefaultParameters(parameters);
        }

        // Emit the body
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Implicit completion: a body that runs off the end resolves with `undefined`.
        EmitImplicitReturnUndefined();

        // Catch block
        _il.BeginCatchBlock(_types.Exception);
        EmitCatchBlock();

        _il.EndExceptionBlock();

        // Exit label and return
        _il.MarkLabel(_exitLabel);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitStateDispatch()
    {
        if (_stateLabels.Count == 0)
        {
            // No awaits, just run through
            return;
        }

        var defaultLabel = _il.DefineLabel();

        // Load state
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);

        // Switch on state
        _il.Emit(OpCodes.Switch, [.. _stateLabels]);

        // Default case - continue with normal execution
        _il.MarkLabel(defaultLabel);
    }

    // Parameter defaults (#646) are applied by the shared StateMachineExitRoutingEmitter
    // EmitDefaultParameters; invoked only on initial entry (see EmitMoveNext placement), so no
    // defaults-applied guard field is required here.

    #region StatementEmitterBase Overrides

    protected override FieldBuilder? GetHoistedVariableField(string name) => _builder.GetVariableField(name);

    protected override void RegisterLoopLocal(string name, LocalBuilder local)
    {
        _locals[name] = local;
    }

    protected override void EmitVarDeclaration(Stmt.Var v)
    {
        // Route a shadowing block-scoped binding to its own storage (#766).
        if (BlockScopeRenames.TryGetValue(v, out var renamed))
            v = v with { Name = RenameToken(v.Name, renamed) };

        if (v.Initializer != null)
        {
            EmitExpression(v.Initializer);
            EnsureBoxed();
            StoreVariable(v.Name.Lexeme);
        }
        else
        {
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.UndefinedInstance);
            StoreVariable(v.Name.Lexeme);
        }
    }

    // EmitReturn, EmitTryCatch, EmitBranchToLabel and the break/continue/finally exit-routing are
    // inherited from AsyncFunctionMoveNextEmitter (the await-aware try/catch machinery, #774). The arrow
    // previously had its own structured-EH EmitTryCatch here that crashed (InvalidProgramException) on
    // an `await` inside a `try`; the shared base fixes it. Only the two completion seams differ:

    protected override FieldBuilder DefineStateMachineField(string name, System.Type type)
        => _builder.StateMachineType.DefineField(name, type, FieldAttributes.Private);

    /// <summary>
    /// Completes the async-arrow state machine using the boxed value on the IL stack: the arrow's
    /// <see cref="EmitSetResult"/> consumes it into <c>_resultLocal</c> and calls builder.SetResult,
    /// then leave to the single Ret epilogue.
    /// </summary>
    protected override void EmitCompleteWithReturnValueOnStack()
    {
        EmitSetResult();
        _il.Emit(OpCodes.Leave, _exitLabel);
    }

    #endregion
}
