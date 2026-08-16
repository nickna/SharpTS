using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    // EmitCall and call helpers inherited from ExpressionEmitterBase.CallHelpers.cs
    //
    // EmitAwait / EmitAwaitFromValueOnStack are the shared await suspension dance in
    // AsyncFunctionMoveNextEmitter (#1122); this emitter supplies only the seams below. The exit-label
    // seam is what had drifted: this emitter Leaves to `_exitLabel` while the async function uses
    // `_endLabel`.

    protected override AsyncBuilderBase AsyncBuilder => _builder;
    protected override Label AwaitExitLabel => _exitLabel;
    protected override int NextAwaitState() => _currentState++;

    protected override void MarkAwaitResumeLabel(int stateNumber)
    {
        if (stateNumber < _stateLabels.Count)
            _il.MarkLabel(_stateLabels[stateNumber]);
    }

    protected override FieldBuilder AwaiterFieldForState(int stateNumber)
    {
        if (!_builder.AwaiterFields.TryGetValue(stateNumber, out var awaiterField))
            throw new CompileException($"No awaiter field found for state {stateNumber}");
        return awaiterField;
    }

    protected override FieldBuilder AsyncStateField => _builder.StateField;
    protected override FieldBuilder AsyncBuilderField => _builder.BuilderField;
    protected override MethodInfo BuilderAwaitUnsafeOnCompletedMethod() => _builder.GetBuilderAwaitUnsafeOnCompletedMethod();
}
