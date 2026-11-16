using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ClosureAnalyzer
{
    internal sealed record EvalTemplateCache(string Storage, int Count, object? Owner);
    internal sealed record EvalTemplateSite(EvalTemplateCache Cache, int Index);

    private readonly Dictionary<List<Stmt>, EvalTemplateCache> _evalTemplateCaches =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<List<string>, EvalTemplateSite> _evalTemplateSites =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _evalTemplateStorageNames = new(StringComparer.Ordinal);

    internal bool IsEvalTemplateCacheStorage(string name) => _evalTemplateStorageNames.Contains(name);

    internal EvalTemplateCache? GetEvalTemplateCache(List<Stmt> program) =>
        _evalTemplateCaches.GetValueOrDefault(program);

    internal EvalTemplateSite? GetEvalTemplateSite(List<string> rawStrings) =>
        _evalTemplateSites.GetValueOrDefault(rawStrings);

    private void PrepareEvalTemplateCache(List<Stmt> program)
    {
        if (_evalTemplateCaches.ContainsKey(program)) return;
        var scanner = new EvalTemplateScanner();
        foreach (var statement in program) scanner.Visit(statement);
        if (scanner.Sites.Count == 0) return;
        var cache = new EvalTemplateCache($"<eval-templates:{_evalTemplateCaches.Count}>",
            scanner.Sites.Count, _currentFunction);
        _evalTemplateCaches.Add(program, cache);
        _evalTemplateStorageNames.Add(cache.Storage);
        for (int i = 0; i < scanner.Sites.Count; i++)
            _evalTemplateSites[scanner.Sites[i]] = new(cache, i);
    }

    protected override void VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expression)
    {
        if (_evalTemplateSites.TryGetValue(expression.RawStrings, out var site))
        {
            // This immutable reference is an activation snapshot, not a shared
            // caller variable. Forward it through every intervening closure,
            // including closures which only create another function. Keeping it
            // out of caller display-class locals prevents a later loop iteration
            // executing the same eval from replacing an older closure's cache.
            foreach (var frame in _functionStack)
            {
                if (ReferenceEquals(frame, site.Cache.Owner)) break;
                if (frame is not null) _captures[frame].Add(site.Cache.Storage);
            }
            foreach (var (classExpression, enclosingFunctions) in _classCaptureOwners)
                if (site.Cache.Owner is null || enclosingFunctions.Contains(site.Cache.Owner))
                    _captures[classExpression].Add(site.Cache.Storage);
            _allCapturedVariables.Add(site.Cache.Storage);
        }
        base.VisitTaggedTemplateLiteral(expression);
    }

    private sealed class EvalTemplateScanner : AstVisitorBase
    {
        internal List<List<string>> Sites { get; } = [];
        protected override void VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expression)
        {
            Sites.Add(expression.RawStrings);
            base.VisitTaggedTemplateLiteral(expression);
        }
    }
}
