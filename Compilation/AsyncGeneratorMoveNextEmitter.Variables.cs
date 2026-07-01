using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    // Expose the state machine's function display class field to the base arrow emitter so a
    // capturing arrow inside the async generator body gets its $functionDC threaded in (#725).
    protected override FieldBuilder? GetFunctionDCField() => _builder.FunctionDCField;

    // Per-binding storage names for block-scoped let/const shadows (#766), shared with the analyzer via
    // the analysis. Empty for the common no-shadow case (and for analyses built without the renamer).
    // The block-scope-rename + function-DC variable overrides that consume this live in the shared
    // IteratorMoveNextEmitter base (#1124).
    private static readonly IReadOnlyDictionary<object, string> NoRenames = new Dictionary<object, string>();
    protected override IReadOnlyDictionary<object, string> BlockScopeRenames => _analysis.BlockScopeRenames ?? NoRenames;
}
