using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the MoveNext body for an async arrow function's state machine.
/// Similar to AsyncMoveNextEmitter but handles captured variable access
/// through the outer state machine reference.
/// </summary>
public partial class AsyncArrowMoveNextEmitter
{
    private readonly AsyncArrowStateMachineBuilder _builder;
    private readonly AsyncStateAnalyzer.AsyncFunctionAnalysis _analysis;
    private readonly TypeProvider _types;
    private ILGenerator _il = null!;
    private CompilationContext? _ctx;
    private int _currentState = 0;
    private readonly List<Label> _stateLabels = [];
    private Label _exitLabel;
    private StackType _stackType = StackType.Unknown;
    private LocalBuilder? _resultLocal;
    private LocalBuilder? _exceptionLocal;

    // Non-hoisted local variables (live within a single MoveNext invocation)
    private readonly Dictionary<string, LocalBuilder> _locals = [];

    public AsyncArrowMoveNextEmitter(
        AsyncArrowStateMachineBuilder builder,
        AsyncStateAnalyzer.AsyncFunctionAnalysis analysis,
        TypeProvider types)
    {
        _builder = builder;
        _analysis = analysis;
        _types = types;
    }

    /// <summary>
    /// Emits the complete MoveNext method body.
    /// </summary>
    public void EmitMoveNext(List<Stmt> body, CompilationContext ctx, Type returnType)
    {
        _il = _builder.MoveNextMethod.GetILGenerator();
        _ctx = ctx;

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
}
