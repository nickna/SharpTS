using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase
    // EmitCall and call helpers are inherited from ExpressionEmitterBase.CallHelpers.cs
    //
    // EmitAwait / EmitAwaitFromValueOnStack are the shared await suspension dance in
    // AsyncFunctionMoveNextEmitter (#1122); this emitter supplies only the seams below.

    protected override AsyncBuilderBase AsyncBuilder => _builder;
    protected override Label AwaitExitLabel => _endLabel;
    protected override int NextAwaitState() => _currentAwaitState++;
    protected override void MarkAwaitResumeLabel(int stateNumber) => _il.MarkLabel(_stateLabels[stateNumber]);
    protected override FieldBuilder AwaiterFieldForState(int stateNumber) => _builder.AwaiterFields[stateNumber];
    protected override FieldBuilder AsyncStateField => _builder.StateField;
    protected override FieldBuilder AsyncBuilderField => _builder.BuilderField;
    protected override MethodInfo BuilderAwaitUnsafeOnCompletedMethod() => _builder.GetBuilderAwaitUnsafeOnCompletedMethod();
}
