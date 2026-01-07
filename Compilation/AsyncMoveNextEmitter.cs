using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext method body for an async state machine.
/// This is the heart of async IL generation, handling state dispatch,
/// await points, and result/exception completion.
/// </summary>
public partial class AsyncMoveNextEmitter
{
    private readonly AsyncStateMachineBuilder _builder;
    private readonly AsyncStateAnalyzer.AsyncFunctionAnalysis _analysis;
    private readonly ILGenerator _il;
    private readonly TypeProvider _types;

    // Labels for state dispatch
    private readonly Dictionary<int, Label> _stateLabels = [];
    private Label _endLabel;
    private Label _setResultLabel;

    // Current await point being processed
    private int _currentAwaitState = 0;

    // Stack type tracking (mirrors ILEmitter)
    private StackType _stackType = StackType.Unknown;

    // Exception handling
    private LocalBuilder? _exceptionLocal;

    // Compilation context for access to functions, classes, etc.
    private CompilationContext? _ctx;

    // Return value storage
    private LocalBuilder? _returnValueLocal;
    private bool _hasReturnValue;

    // Loop label tracking for break/continue
    private readonly Stack<(Label BreakLabel, Label ContinueLabel, string? LabelName)> _loopLabels = new();

    // For try/catch with awaits: track where to store caught exceptions
    private LocalBuilder? _currentTryCatchExceptionLocal = null;
    private Label? _currentTryCatchSkipLabel = null;

    // For return inside try with finally-with-awaits: track pending return
    private LocalBuilder? _pendingReturnFlagLocal = null;
    private Label? _afterFinallyLabel = null;

    public AsyncMoveNextEmitter(AsyncStateMachineBuilder builder, AsyncStateAnalyzer.AsyncFunctionAnalysis analysis, TypeProvider types)
    {
        _builder = builder;
        _analysis = analysis;
        _il = builder.MoveNextMethod.GetILGenerator();
        _types = types;
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt>? body, CompilationContext ctx, Type returnType)
    {
        if (body == null) return;

        _ctx = ctx;
        _hasReturnValue = returnType != _types.Void;

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

        // Begin outer try block
        _il.BeginExceptionBlock();

        // Emit state dispatch switch
        EmitStateSwitch();

        // Emit the function body (will emit await points inline)
        foreach (var stmt in body)
        {
            EmitStatement(stmt);
        }

        // Jump to set result
        _il.Emit(OpCodes.Br, _setResultLabel);

        // Set result label
        _il.MarkLabel(_setResultLabel);

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

    private void EmitStateSwitch()
    {
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
