using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    // Mutable source functions need one binding shared by outer reads and class
    // captures. Stable functions retain their cached wrapper/direct-call path.
    private readonly Dictionary<Stmt.Function, FieldBuilder> _mutableFunctionFields =
        new(ReferenceEqualityComparer.Instance);

    private void EmitMutableFunctionInitialization(IEnumerable<Stmt> statements, ILEmitter emitter)
    {
        foreach (var declaration in statements.SelectMany(SourceDeclarations).OfType<Stmt.Function>())
            if (_mutableFunctionFields.TryGetValue(declaration, out var field))
                emitter.EmitHoistedModuleFunction(declaration, field);
    }

    private void RemoveMutableFunctionCaptures(object callable, HashSet<string> captures, string? path)
    {
        var fields = path is null ? _topLevelStaticVars
            : _moduleTopLevelStaticVars.GetValueOrDefault(path);
        if (fields is null) return;
        // These bindings already have shared storage. A closure must resolve that
        // field on each access rather than retaining its creation-time value.
        foreach (var name in captures.ToArray())
        {
            if (!fields.TryGetValue(name, out var field) || !_mutableFunctionFields.ContainsValue(field)
                || _closures.Analyzer.GetCaptureSource(callable, name) is not null
                || _closures.Analyzer.IsDirectTopLevelBlockScopedCapture(callable, name)
                || callable is Expr.ArrowFunction { Name: { } self } && self.Lexeme == name)
                continue;
            captures.Remove(name);
            if (!_liveModuleCaptureNames.TryGetValue(callable, out var removed))
                _liveModuleCaptureNames[callable] = removed = [];
            removed.Add(name);
        }
    }
}
