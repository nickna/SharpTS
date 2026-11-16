using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ClosureAnalyzer
{
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
            if (DirectivePrologue.HasUseStrict(statements)) return call.PreparedEvalStatements;
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
        if (_currentFunction is Stmt.Function { IsAsync: true } or Stmt.Function { IsGenerator: true }
            or Expr.ArrowFunction { IsAsync: true } or Expr.ArrowFunction { IsGenerator: true }) return;
        var scanner = new BodyEvalBindingScanner();
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

    private void ReferenceEvalBindings(string name)
    {
        foreach (var scope in _scopeStack)
        {
            if (scope.Contains(name)) break;
            if (_evalScopeBindings.TryGetValue(scope, out var bindings)
                && bindings.TryGetValue(name, out var storage))
                ReferenceVariable(storage);
        }
    }

    private sealed class BodyEvalBindingScanner : AstVisitorBase
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
                    var statements = call.PreparedEvalStatements
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
