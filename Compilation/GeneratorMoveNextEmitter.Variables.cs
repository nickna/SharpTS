using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    // Per-binding storage names for block-scoped let/const shadows (#711), shared with the analyzer
    // via the analysis. Empty for the common no-shadow case. The block-scope-rename + function-DC
    // variable overrides that consume this live in the shared IteratorMoveNextEmitter base (#1124).
    protected override IReadOnlyDictionary<object, string> BlockScopeRenames => _analysis.BlockScopeRenames;

    // #767: a nested-block shadow captured (read-only) by an inner arrow is renamed to its own storage;
    // pivot the arrow's capture SOURCE to that storage so it reads the shadow's value, not the outer
    // same-named binding's hoisted field. The base EmitCapturingArrowViaHooks consults this. Async
    // generators do not support async arrows, so this pivot is generator-only.
    protected override string ResolveCaptureSourceName(Expr.ArrowFunction af, string capturedVar) =>
        PivotCaptureSource(_analysis.BlockScopeCaptureRenames, af, capturedVar);

    // Expose the state machine's function display class field to the base arrow emitter so a
    // capturing arrow inside the generator body gets its $functionDC threaded in (#674).
    protected override FieldBuilder? GetFunctionDCField() => _builder.FunctionDCField;
}
