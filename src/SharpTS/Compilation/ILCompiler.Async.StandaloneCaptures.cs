using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private IReadOnlySet<string>? GetDebugStaticCaptureNames(
        Expr.ArrowFunction arrow, IEnumerable<string> captures)
    {
        if (!EmitDebugSymbols) return null;
        string? modulePath = _arrowToModule.TryGetValue(arrow, out string? owner)
            ? NormalizeToEmissionPath(owner) : null;
        var staticFields = BuildModuleMemberTopLevelStaticVarsForModule(modulePath);
        var capturedGlobals = BuildCapturedTopLevelVarsForModule(modulePath);
        var liftedBlocks = BuildLiftedBlockScopedTopLevelVarsForModule(modulePath);
        HashSet<string> hidden = [];
        foreach (string name in captures)
        {
            // Source provenance distinguishes an enclosing local from a same-named module
            // binding. Direct unlifted block captures still read their snapshot; other module
            // captures read static storage, so their unused creation snapshots must stay hidden.
            if (_closures.Analyzer.GetCaptureSource(arrow, name) is not null) continue;
            if (_closures.Analyzer.IsDirectTopLevelBlockScopedCapture(arrow, name)
                && liftedBlocks?.Contains(name) != true) continue;
            if (staticFields?.ContainsKey(name) == true || capturedGlobals?.Contains(name) == true)
                hidden.Add(name);
        }
        return hidden;
    }

    private Dictionary<string, FieldBuilder> GetStandaloneLiveCaptureFields(
        Expr.ArrowFunction arrow, IEnumerable<string> captures)
    {
        var result = new Dictionary<string, FieldBuilder>();
        foreach (var name in captures)
        {
            if (name == "this") continue;
            var source = _closures.Analyzer.GetCaptureSource(arrow, name);
            if (source is null) continue;
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
