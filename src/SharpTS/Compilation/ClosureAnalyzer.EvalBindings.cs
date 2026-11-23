using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ClosureAnalyzer
{
    private readonly Dictionary<HashSet<string>, HashSet<string>> _evalLexicalNames = [];
    private readonly Dictionary<Expr.Call, string> _evalDeclarationConflicts = new(ReferenceEqualityComparer.Instance);

    internal string? GetEvalDeclarationConflict(Expr.Call call) => _evalDeclarationConflicts.GetValueOrDefault(call);

    private void RecordEvalDeclarationConflict(Expr.Call call, List<Stmt> statements)
    {
        // Only lexical declarations between the call and its variable environment
        // block sloppy eval vars. An outer function's lexicals and a simple catch
        // parameter are both legal to cross.
        foreach (var (name, _) in Runtime.ParameterBinder.BodyVarDeclarations(statements))
            foreach (var scope in _scopeStack)
            {
                if (!ReferenceEquals(_scopeOwners[scope], _currentFunction)) break;
                if (_evalLexicalNames.TryGetValue(scope, out var names) && names.Contains(name.Lexeme))
                {
                    _evalDeclarationConflicts[call] = name.Lexeme;
                    return;
                }
            }
    }

    private List<Stmt>? GetEvalProgram(Expr.Call call)
    {
        if (_evalPrograms is null) return call.PreparedEvalStatements;
        if (_evalPrograms.TryGetValue(call, out var prepared)) return prepared;
        Expr callee = call.Callee;
        while (callee is Expr.Grouping grouping) callee = grouping.Expression;
        if (callee is not Expr.Variable { Name.Lexeme: "eval" }
            || call.Arguments.FirstOrDefault() is not Expr.Literal { Value: string source })
            return call.PreparedEvalStatements;
        try
        {
            var statements = call.PreparedEvalStatements ?? WithStatementLowering.ApplyEvalEnvironments(
                new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(), call.EvalObjectEnvironments);
            statements = StaticEvalLowering.LowerFunctionDeclarations(statements, direct: true);
            _evalPrograms[call] = statements;
            return statements;
        }
        catch
        {
            // Runtime eval owns syntax errors on the executed path.
            return null;
        }
    }

    private readonly Dictionary<List<Stmt>, Dictionary<string, string>> _evalBodyBindings =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HashSet<string>, Dictionary<string, string>> _evalScopeBindings = [];
    private readonly Dictionary<string, List<string>> _evalBindingStorage = new(StringComparer.Ordinal);
    private int _nextEvalBindingId;

    internal IReadOnlyDictionary<string, string>? GetEvalBodyBindings(List<Stmt>? body) =>
        body is null ? null : _evalBodyBindings.GetValueOrDefault(body);

    internal IEnumerable<string> GetEvalBindingStorage(string name) =>
        _evalBindingStorage.TryGetValue(name, out var storage) ? storage.AsEnumerable().Reverse() : [];

    private void DeclareEvalBindings(List<Stmt> body)
    {
        // Static caller-scope eval is currently lowered by the synchronous
        // emitter. State machines retain their existing eval bridge boundary.
        if (_strictContext || _currentFunction is Stmt.Function { IsAsync: true } or Stmt.Function { IsGenerator: true }
            or Expr.ArrowFunction { IsAsync: true } or Expr.ArrowFunction { IsGenerator: true }) return;
        var scanner = new BodyEvalBindingScanner(GetEvalProgram);
        foreach (var statement in Runtime.ParameterBinder.BodyStatements(body)) scanner.Visit(statement);
        var scope = _scopeStack.Peek();
        foreach (string name in scanner.Names)
        {
            // Existing parameters and body declarations retain their ordinary storage.
            if (scope.Contains(name)) continue;
            string storage = $"<eval-binding:{_nextEvalBindingId++}:{name}>";
            if (!_evalBodyBindings.TryGetValue(body, out var bindings))
                _evalBodyBindings[body] = bindings = new(StringComparer.Ordinal);
            bindings[name] = storage;
            _evalScopeBindings[scope] = bindings;
            if (!_evalBindingStorage.TryGetValue(name, out var candidates))
                _evalBindingStorage[name] = candidates = [];
            candidates.Add(storage);
            DeclareVariable(storage);
        }
    }

    private readonly Dictionary<Expr.Variable, List<string>> _suspendedEvalReads =
        new(ReferenceEqualityComparer.Instance);

    internal IReadOnlyList<string>? GetSuspendedEvalRead(Expr.Variable reference) =>
        _suspendedEvalReads.GetValueOrDefault(reference);

    private readonly Dictionary<Expr.Variable, List<string>> _evalDeleteBindings = new(ReferenceEqualityComparer.Instance);
    internal IReadOnlyList<string>? GetEvalDeleteBindings(Expr.Variable reference) => _evalDeleteBindings.GetValueOrDefault(reference);

    protected override void VisitDelete(Expr.Delete expression)
    {
        Expr operand = expression.Operand;
        while (operand is Expr.Grouping grouping) operand = grouping.Expression;
        if (operand is Expr.Variable variable)
        {
            var candidates = VisibleEvalBindings(variable.Name.Lexeme).ToList();
            if (candidates.Count > 0) _evalDeleteBindings[variable] = candidates;
        }
        base.VisitDelete(expression);
    }

    private IEnumerable<string> VisibleEvalBindings(string name)
    {
        foreach (var scope in _scopeStack)
        {
            if (scope.Contains(name)) yield break;
            if (_evalScopeBindings.TryGetValue(scope, out var bindings)
                && bindings.TryGetValue(name, out var storage))
                yield return storage;
        }
    }

    private void ReferenceEvalBindings(string name, Expr.Variable? reference)
    {
        if (_evalScopeBindings.Count == 0) return;
        foreach (var storage in VisibleEvalBindings(name))
        {
            ReferenceVariable(storage);
            if (reference is not null && _currentFunction is Expr.ArrowFunction { IsAsync: true }
                or Expr.ArrowFunction { IsGenerator: true })
            {
                if (!_suspendedEvalReads.TryGetValue(reference, out var candidates))
                    _suspendedEvalReads[reference] = candidates = [];
                candidates.Add(storage);
            }
        }
    }

    private sealed class BodyEvalBindingScanner(Func<Expr.Call, List<Stmt>?> prepare) : AstVisitorBase
    {
        internal HashSet<string> Names { get; } = new(StringComparer.Ordinal);
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
        protected override void VisitCall(Expr.Call call)
        {
            if (call.Callee is Expr.Variable { Name.Lexeme: "eval" }
                && call.Arguments.FirstOrDefault() is Expr.Literal { Value: string source })
            {
                try
                {
                    var statements = prepare(call) ?? call.PreparedEvalStatements
                        ?? new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
                    if (!DirectivePrologue.HasUseStrict(statements))
                        foreach (var (name, _) in Runtime.ParameterBinder.BodyVarDeclarations(statements))
                            Names.Add(name.Lexeme);
                }
                catch
                {
                    // Runtime eval owns malformed-source diagnostics.
                }
            }
            base.VisitCall(call);
        }
    }
}
