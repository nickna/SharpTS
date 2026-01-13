using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext body for an async arrow function's state machine.
/// Similar to AsyncMoveNextEmitter but handles captured variable access
/// through the outer state machine reference.
/// </summary>
public partial class AsyncArrowMoveNextEmitter : ExpressionEmitterBase
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

    // Stack type tracking via shared helpers (use base class _helpers)
    private StackType _stackType
    {
        get => _helpers.StackType;
        set => _helpers.StackType = value;
    }

    // Non-hoisted local variables (live within a single MoveNext invocation)
    private readonly Dictionary<string, LocalBuilder> _locals = [];

    // Variable resolver for hoisted fields, locals, and captured variables
    private IVariableResolver? _resolver;

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
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt> body, CompilationContext ctx, Type returnType)
    {
        // Note: _il is initialized in constructor via GetILGenerator()
        _ctx = ctx;

        // Create variable resolver for hoisted fields, locals, and captured variables
        _resolver = new AsyncArrowVariableResolver(_il, _builder, _locals);

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

        // Emit the body
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Default return null
        EmitReturnNull();

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

    #region Helper Method Wrappers - Not in ExpressionEmitterBase

    // Note: EnsureBoxed, SetStackUnknown, SetStackType, EmitNullConstant, EmitDoubleConstant,
    // EmitBoolConstant, EmitStringConstant are inherited from ExpressionEmitterBase

    private void EmitTruthyCheck() => _helpers.EmitTruthyCheck(_ctx!.Runtime!.IsTruthy);
    private void EmitBoxedDoubleConstant(double value) => _helpers.EmitBoxedDoubleConstant(value);
    private void EmitBoxedBoolConstant(bool value) => _helpers.EmitBoxedBoolConstant(value);
    private void EmitBoxDouble() => _helpers.EmitBoxDouble();
    private void EmitBoxBool() => _helpers.EmitBoxBool();
    private void EmitAdd_Double() => _helpers.EmitAdd_Double();
    private void EmitSub_Double() => _helpers.EmitSub_Double();
    private void EmitMul_Double() => _helpers.EmitMul_Double();
    private void EmitDiv_Double() => _helpers.EmitDiv_Double();
    private void EmitRem_Double() => _helpers.EmitRem_Double();
    private void EmitNeg_Double() => _helpers.EmitNeg_Double();
    private void EmitClt_Boolean() => _helpers.EmitClt_Boolean();
    private void EmitCgt_Boolean() => _helpers.EmitCgt_Boolean();
    private void EmitCeq_Boolean() => _helpers.EmitCeq_Boolean();
    private void EmitLessOrEqual_Boolean() => _helpers.EmitLessOrEqual_Boolean();
    private void EmitGreaterOrEqual_Boolean() => _helpers.EmitGreaterOrEqual_Boolean();
    private void EmitCallUnknown(MethodInfo method) => _helpers.EmitCallUnknown(method);
    private void EmitCallvirtUnknown(MethodInfo method) => _helpers.EmitCallvirtUnknown(method);
    private void EmitCallString(MethodInfo method) => _helpers.EmitCallString(method);
    private void EmitCallBoolean(MethodInfo method) => _helpers.EmitCallBoolean(method);
    private void EmitCallDouble(MethodInfo method) => _helpers.EmitCallDouble(method);
    private void EmitCallAndBoxDouble(MethodInfo method) => _helpers.EmitCallAndBoxDouble(method);
    private void EmitCallAndBoxBool(MethodInfo method) => _helpers.EmitCallAndBoxBool(method);
    private void EmitLdlocUnknown(LocalBuilder local) => _helpers.EmitLdlocUnknown(local);
    private void EmitLdargUnknown(int argIndex) => _helpers.EmitLdargUnknown(argIndex);
    private void EmitLdfldUnknown(FieldInfo field) => _helpers.EmitLdfldUnknown(field);
    private void EmitNewobjUnknown(ConstructorInfo ctor) => _helpers.EmitNewobjUnknown(ctor);
    private void EmitConvertToDouble() => _helpers.EmitConvertToDouble();
    private void EmitConvR8AndBox() => _helpers.EmitConvR8AndBox();
    private void EmitObjectEqualsBoxed() => _helpers.EmitObjectEqualsBoxed();
    private void EmitObjectNotEqualsBoxed() => _helpers.EmitObjectNotEqualsBoxed();

    #endregion
}
