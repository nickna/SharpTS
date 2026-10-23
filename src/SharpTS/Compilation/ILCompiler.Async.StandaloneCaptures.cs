using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private IReadOnlySet<string> GetStandaloneStaticCaptureNames(
        Expr.ArrowFunction arrow, IEnumerable<string> captures)
    {
        string? modulePath = _arrowToModule.TryGetValue(arrow, out string? owner)
            ? NormalizeToEmissionPath(owner) : null;
        var staticFields = BuildModuleMemberTopLevelStaticVarsForModule(modulePath);
        var capturedGlobals = BuildCapturedTopLevelVarsForModule(modulePath);
        var liftedBlocks = BuildLiftedBlockScopedTopLevelVarsForModule(modulePath);
        HashSet<string> staticCaptures = [];
        foreach (string name in captures)
        {
            // Source provenance distinguishes an enclosing local from a same-named module
            // binding. Direct unlifted block captures still read their snapshot; other module
            // captures read shared static storage without taking creation-time snapshots.
            if (_closures.Analyzer.GetCaptureSource(arrow, name) is not null) continue;
            if (_closures.Analyzer.IsDirectTopLevelBlockScopedCapture(arrow, name)
                && liftedBlocks?.Contains(name) != true) continue;
            if (staticFields?.ContainsKey(name) == true || capturedGlobals?.Contains(name) == true)
                staticCaptures.Add(name);
        }
        return staticCaptures;
    }

    private Dictionary<string, FieldBuilder> GetStandaloneLiveCaptureFields(
        Expr.ArrowFunction arrow, IEnumerable<string> captures)
    {
        var result = new Dictionary<string, FieldBuilder>();
        foreach (var name in captures)
        {
            if (name == "this") continue;
            // Per-activation lexical cells must carry their own raw reference,
            // even when the function also has a same-spelled display-class slot.
            if (_closures.Analyzer.TryGetLoopBodyCellCapture(arrow, name, out _)) continue;
            var source = _closures.Analyzer.GetCaptureSource(arrow, name);
            if (source is null) continue;
            string storageName = _closures.ArrowFunctionDCFieldRenames.TryGetValue(arrow, out var renames)
                && renames.TryGetValue(name, out var renamed) ? renamed : name;
            if (source is Expr.ArrowFunction asyncOwner
                && _closures.AsyncArrowDCKeys.TryGetValue(asyncOwner, out var asyncKey)
                && _closures.FunctionDisplayClassFields.TryGetValue(asyncKey, out var asyncFields)
                && asyncFields.TryGetValue(storageName, out var asyncField))
            {
                result[name] = asyncField;
                continue;
            }
            if (_closures.ArrowScopeDisplayClassFields.TryGetValue(source, out var scopeFields)
                && scopeFields.TryGetValue(name, out var scopeField))
            {
                result[name] = scopeField;
                continue;
            }
            // Resolve the lexical owner by AST identity, including qualified
            // functions and methods; same-named sibling bindings stay distinct.
            foreach (var (key, callable) in _closures.FunctionAstNodes)
            {
                if (ReferenceEquals(callable, source)
                    && _closures.FunctionDisplayClassFields.TryGetValue(key, out var fields)
                    && fields.TryGetValue(name, out var field))
                {
                    result[name] = field;
                    break;
                }
            }
        }
        return result;
    }
}
