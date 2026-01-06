using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext method body for a generator state machine.
/// Handles state dispatch, yield points, and generator completion.
/// </summary>
public partial class GeneratorMoveNextEmitter
{
    private readonly GeneratorStateMachineBuilder _builder;
    private readonly GeneratorStateAnalyzer.GeneratorFunctionAnalysis _analysis;
    private readonly ILGenerator _il;

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _returnFalseLabel;

    // Current yield point being processed
    private int _currentYieldState = 0;

    // Stack type tracking
    private StackType _stackType = StackType.Unknown;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Loop label tracking for break/continue
    private readonly Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> _loopLabels = new();

    public GeneratorMoveNextEmitter(GeneratorStateMachineBuilder builder, GeneratorStateAnalyzer.GeneratorFunctionAnalysis analysis)
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextMethod.GetILGenerator();
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx)
    {
        if (body == null) return;

        _ctx = ctx;

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
}
