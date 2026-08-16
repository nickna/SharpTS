using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    /// <summary>
    /// Arrow emission inside a generator <c>MoveNext</c>. Delegates the actual emission to the
    /// base <see cref="ExpressionEmitterBase.EmitArrowFunction"/> (which instantiates the display
    /// class for capturing arrows via the GetThisField / GetHoistedVariableField / GetFunctionDCField
    /// hooks), but first rejects any residual shape the generator path cannot yet honour: an arrow
    /// that <em>writes</em> to a captured variable that was NOT lifted into a shared function display
    /// class.
    /// </summary>
    protected override void EmitArrowFunction(Expr.ArrowFunction af)
    {
        // A captured-AND-mutated variable backed by the generator's function display class (#674) is
        // routed through `$functionDC` and so is intentionally absent from the arrow's own by-value
        // snapshot fields — its write reaches shared storage and this guard does not fire. What
        // remains is the still-unsupported subset: a write to a captured variable that is only ever
        // snapshotted by value (e.g. inside an instance generator method, whose state machine has no
        // function DC wired). Fail fast there rather than miscompiling `arr.forEach(n => sum += n)`.
        CapturedWriteAnalysis.ThrowIfCapturedWriteWouldBeLost(af, _ctx?.DisplayClassFields);

        base.EmitArrowFunction(af);
    }
}
