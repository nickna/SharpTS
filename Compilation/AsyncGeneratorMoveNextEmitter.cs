using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNextAsync method body for an async generator state machine.
/// Handles state dispatch, yield points, await points, and generator completion.
/// </summary>
public partial class AsyncGeneratorMoveNextEmitter : ExpressionEmitterBase
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

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _returnFalseLabel;

    // Current suspension point being processed
    private int _currentSuspensionState = 0;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Variable resolver for hoisted fields and non-hoisted locals
    private IVariableResolver? _resolver;

    // Loop label tracking for break/continue
    private readonly Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> _loopLabels = new();

    public AsyncGeneratorMoveNextEmitter(AsyncGeneratorStateMachineBuilder builder, AsyncGeneratorStateAnalyzer.AsyncGeneratorFunctionAnalysis analysis, TypeProvider types)
        : base(new StateMachineEmitHelpers(builder.MoveNextAsyncMethod.GetILGenerator(), types))
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextAsyncMethod.GetILGenerator();
        _types = types;
    }

    /// <summary>
    /// Emits the complete MoveNextAsync method body.
    /// </summary>
    public void EmitMoveNextAsync(List<Stmt>? body, CompilationContext ctx)
    {
        if (body == null)
        {
            // Empty body - just return false
            EmitReturnValueTaskBool(false);
            return;
        }

        _ctx = ctx;

        // Create variable resolver for hoisted fields and non-hoisted locals
        _resolver = new StateMachineVariableResolver(
            _il,
            _builder.GetVariableField,
            ctx.Locals,
            _builder.ThisField);

        // Define labels for each suspension resume point
        foreach (var suspensionPoint in _analysis.SuspensionPoints)
        {
            _stateLabels[suspensionPoint.StateNumber] = _il.DefineLabel();
        }
        _returnFalseLabel = _il.DefineLabel();

        // Check if generator is already completed (state == -2)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Beq, _returnFalseLabel);

        // Emit state dispatch switch
        EmitStateSwitch();

        // Emit the function body (will emit yield/await points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Fall through after body completes - clear current value, mark as done and return false
        // (Implicit return has value: null)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(false);

        // Return false label (generator completed)
        _il.MarkLabel(_returnFalseLabel);
        EmitReturnValueTaskBool(false);
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

    #region Helper Method Wrappers - Not in ExpressionEmitterBase

    // Note: EnsureBoxed, SetStackUnknown, SetStackType, EmitNullConstant, EmitDoubleConstant,
    // EmitBoolConstant, EmitStringConstant are inherited from ExpressionEmitterBase

    private void EmitTruthyCheck() => _helpers.EmitTruthyCheck(_ctx!.Runtime!.IsTruthy);
    private void EmitBoxedDoubleConstant(double value) => _helpers.EmitBoxedDoubleConstant(value);
    private void EmitBoxedBoolConstant(bool value) => _helpers.EmitBoxedBoolConstant(value);
    private void EmitBoxDouble() => _helpers.EmitBoxDouble();
    private void EmitBoxBool() => _helpers.EmitBoxBool();
    private void SetStackNumber() => _helpers.SetStackType(StackType.Double);
    private void SetStackString() => _helpers.SetStackType(StackType.String);
    private void SetStackBoolean() => _helpers.SetStackType(StackType.Boolean);
    private void SetStackObject() => _helpers.SetStackUnknown();
    private void EmitCallUnknown(MethodInfo method) => _helpers.EmitCallUnknown(method);
    private void EmitCallvirtUnknown(MethodInfo method) => _helpers.EmitCallvirtUnknown(method);
    private void EmitLdlocUnknown(LocalBuilder local) => _helpers.EmitLdlocUnknown(local);
    private void EmitLdargUnknown(int argIndex) => _helpers.EmitLdargUnknown(argIndex);
    private void EmitLdfldUnknown(FieldInfo field) => _helpers.EmitLdfldUnknown(field);
    private void EmitNewobjUnknown(ConstructorInfo ctor) => _helpers.EmitNewobjUnknown(ctor);
    private void EmitConvertToDouble() => _helpers.EmitConvertToDouble();

    #endregion
}
