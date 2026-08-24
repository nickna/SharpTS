using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Records deliberately narrow exact-instance method calls. A local candidate must
/// be a uniquely declared <c>const</c> initialized directly with <c>new C(...)</c> in
/// the same function/arrow scope as the call, and its static receiver type must still
/// be that same class. Nested functions get independent scopes, so captured aliases
/// and shadowed names stay on the public virtual method path.
/// </summary>
internal static class StableExactClassMethodCallAnalyzer
{
    public static void Analyze(
        List<Stmt> program,
        TypeMap? typeMap,
        RuntimeFeatureSet? features)
    {
        if (typeMap is null
            || features?.UsesDynamicPropertyDescriptors != false
            || features.UsesObjectIntegrityMutation
            || features.UsesClassPrototypeMutation)
            return;

        var visitor = new Visitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);

        foreach (var (key, candidateClassName) in visitor.Candidates)
        {
            if (visitor.DeclarationCounts.GetValueOrDefault(key) != 1
                || visitor.DisqualifiedCandidates.Contains(key)
                || !visitor.Calls.TryGetValue(key, out var calls))
                continue;

            foreach (var method in calls)
            {
                if (TryGetInstanceClassName(typeMap.Get(method.Object), out string? receiverClass)
                    && receiverClass == candidateClassName)
                    typeMap.MarkStableExactPrimitiveMethodCall(method);
            }
        }

        foreach (var method in visitor.ImmediateCalls)
            typeMap.MarkStableExactPrimitiveMethodCall(method);
    }

    private static bool TryGetInstanceClassName(TypeInfo? type, out string? className)
    {
        className = type is TypeInfo.Instance instance
            ? instance.ResolvedClassType switch
            {
                TypeInfo.Class c => c.Name,
                TypeInfo.MutableClass c => c.Name,
                _ => null
            }
            : null;
        return className is not null;
    }

    private sealed class Visitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private int _scope;
        private int _nextScope;
        private readonly Dictionary<int, int> _scopeParents = [];

        public Dictionary<(int Scope, string Name), string> Candidates { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public Dictionary<(int Scope, string Name), List<Expr.Get>> Calls { get; } = [];
        public HashSet<(int Scope, string Name)> DisqualifiedCandidates { get; } = [];
        public HashSet<Expr.Get> ImmediateCalls { get; } = new(ReferenceEqualityComparer.Instance);

        protected override void VisitFunction(Stmt.Function statement) =>
            InScope(() => base.VisitFunction(statement));

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InScope(() => base.VisitArrowFunction(expression));

        private void InScope(Action visit)
        {
            int saved = _scope;
            _scope = ++_nextScope;
            _scopeParents[_scope] = saved;
            visit();
            _scope = saved;
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            RecordDeclaration(statement.Name);
            if (statement.Initializer is not null)
                Visit(statement.Initializer);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            var key = RecordDeclaration(statement.Name);
            if (TryGetDirectNewClass(statement.Initializer, out string? className)
                && TryGetInstanceClassName(_typeMap.Get(statement.Initializer), out string? resolved)
                && resolved == className)
                Candidates[key] = className!;
            Visit(statement.Initializer);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            // A catch binding has block scope. Treating it as a same-function
            // redeclaration is deliberately more conservative: an outer exact
            // candidate with the same name is then never selected inside the catch.
            if (statement.CatchParam is not null)
                RecordDeclaration(statement.CatchParam);
            base.VisitTryCatch(statement);
        }

        private (int Scope, string Name) RecordDeclaration(Token name)
        {
            var key = (_scope, name.Lexeme);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;
            return key;
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional && expression.Callee is Expr.Get { Optional: false } method)
            {
                if (method.Object is Expr.Variable receiver)
                {
                    var key = (_scope, receiver.Name.Lexeme);
                    RecordAncestorUse(receiver.Name.Lexeme);
                    if (!Calls.TryGetValue(key, out var calls))
                        Calls[key] = calls = [];
                    calls.Add(method);
                    foreach (var argument in expression.Arguments)
                        Visit(argument);
                    return;
                }

                if (TryGetDirectNewClass(method.Object, out string? newClass)
                    && TryGetInstanceClassName(_typeMap.Get(method.Object), out string? receiverClass)
                    && receiverClass == newClass)
                    ImmediateCalls.Add(method);
            }

            base.VisitCall(expression);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            DisqualifiedCandidates.Add((_scope, expression.Name.Lexeme));
            RecordAncestorUse(expression.Name.Lexeme);
        }

        private void RecordAncestorUse(string name)
        {
            int scope = _scope;
            while (_scopeParents.TryGetValue(scope, out int parent))
            {
                DisqualifiedCandidates.Add((parent, name));
                scope = parent;
            }
        }

        private static bool TryGetDirectNewClass(Expr expression, out string? className)
        {
            expression = Unwrap(expression);
            className = expression is Expr.New { Callee: Expr.Variable variable }
                ? variable.Name.Lexeme
                : null;
            return className is not null;
        }

        private static Expr Unwrap(Expr expression)
        {
            while (true)
            {
                expression = expression switch
                {
                    Expr.Grouping grouping => grouping.Expression,
                    Expr.TypeAssertion assertion => assertion.Expression,
                    Expr.Satisfies satisfies => satisfies.Expression,
                    Expr.NonNullAssertion nonNull => nonNull.Expression,
                    _ => expression
                };
                if (expression is not (Expr.Grouping or Expr.TypeAssertion
                    or Expr.Satisfies or Expr.NonNullAssertion))
                    return expression;
            }
        }
    }
}
