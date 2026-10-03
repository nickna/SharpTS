using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
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
