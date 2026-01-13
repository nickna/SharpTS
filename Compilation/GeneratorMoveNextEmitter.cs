using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext method body for a generator state machine.
/// Handles state dispatch, yield points, and generator completion.
/// </summary>
public partial class GeneratorMoveNextEmitter : ExpressionEmitterBase
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

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _returnFalseLabel;

    // Current yield point being processed
    private int _currentYieldState = 0;

    // Stack type tracking via shared helpers (use base class _helpers)
    private StackType _stackType
    {
        get => _helpers.StackType;
        set => _helpers.StackType = value;
    }

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Variable resolver for hoisted fields and non-hoisted locals
    private IVariableResolver? _resolver;

    // Loop label tracking for break/continue
    private readonly Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> _loopLabels = new();

    public GeneratorMoveNextEmitter(GeneratorStateMachineBuilder builder, GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis, TypeProvider types)
        : base(new StateMachineEmitHelpers(builder.MoveNextMethod.GetILGenerator(), types))
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextMethod.GetILGenerator();
        _types = types;
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx)
    {
        if (body == null) return;

        _ctx = ctx;

        // Create variable resolver for hoisted fields and non-hoisted locals
        _resolver = new StateMachineVariableResolver(
            _il,
            _builder.GetVariableField,
            ctx.Locals,
            _builder.ThisField);

        // Define labels for each yield resume point
        foreach (var yieldPoint in _analysis.YieldPoints)
        {
            _stateLabels[yieldPoint.StateNumber] = _il.DefineLabel();
        }
        _returnFalseLabel = _il.DefineLabel();

        // Check if generator is already completed (state == -2)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Beq, _returnFalseLabel);

        // Emit state dispatch switch
        EmitStateSwitch();

        // Emit the function body (will emit yield points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Fall through after body completes - mark as done and return false
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);

        // Return false label (generator completed)
        _il.MarkLabel(_returnFalseLabel);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
    }

    private void EmitStateSwitch()
    {
        if (_analysis.YieldPointCount == 0) return;

        // Load state field
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);

        // Create labels array for switch
        var labels = new Label[_analysis.YieldPointCount];
        for (int i = 0; i < _analysis.YieldPointCount; i++)
        {
            labels[i] = _stateLabels[i];
        }

        // switch (state) { case 0: goto State0; case 1: goto State1; ... }
        _il.Emit(OpCodes.Switch, labels);

        // Fall through for state -1 (initial execution)
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
