using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed class StableCaptureParameter { }
    private readonly ConditionalWeakTable<BindingSymbol, StableCaptureParameter> _stableCaptureParameters = new();

    private void RecordStableCaptureParameters(IReadOnlyList<Stmt.Parameter> parameters,
        IReadOnlyList<Stmt>? body, Expr? expression = null)
    {
        Dictionary<string, BindingSymbol> candidates = new(StringComparer.Ordinal);
        foreach (var parameter in parameters)
            if (Bindings.GetSymbol(parameter.Name) is { } symbol)
                candidates[parameter.Name.Lexeme] = symbol;
        if (body is not null)
            foreach (var initializer in body.OfType<Stmt.Sequence>().Where(sequence => sequence.ParameterName is not null))
                foreach (var name in LexicalDeclarationBindings.Collect(initializer.Statements))
                    if (name.Start >= 0 && _environment.GetLocalValueBinding(name.Lexeme) is { } symbol)
                        candidates[name.Lexeme] = symbol;
        if (candidates.Count == 0) return;
        var writes = new ParameterCaptureWrites(this, candidates);
        if (body is not null) writes.VisitBody(body, ownsParameters: true);
        if (expression is not null) writes.Visit(expression);
        foreach (var symbol in candidates.Values)
        {
            _stableCaptureParameters.Remove(symbol);
            if (!writes.Assigned.Contains(symbol))
                _stableCaptureParameters.Add(symbol, new StableCaptureParameter());
        }
    }

    // This is a write proof for already-declared parameter identities, not a second binder.
    // Future tokens have not all been checked, so source-owned lexical shadows conservatively
    // exclude their writes; resolved tokens additionally have to name the exact candidate.
    private sealed class ParameterCaptureWrites(TypeChecker checker, IReadOnlyDictionary<string, BindingSymbol> candidates)
        : AstVisitorBase
    {
        internal HashSet<BindingSymbol> Assigned { get; } = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _shadows = new(StringComparer.Ordinal);

        private void Write(Token name)
        {
            if (_shadows.ContainsKey(name.Lexeme) || !candidates.TryGetValue(name.Lexeme, out var candidate)) return;
            var resolved = checker.Bindings.GetSymbol(name);
            if (resolved is null || ReferenceEquals(resolved, candidate)) Assigned.Add(candidate);
        }

        private void InScope(IEnumerable<string> names, Action visit)
        {
            string[] shadows = names.Distinct(StringComparer.Ordinal).ToArray();
            foreach (string name in shadows) _shadows[name] = _shadows.GetValueOrDefault(name) + 1;
            try { visit(); }
            finally
            {
                foreach (string name in shadows)
                    if (--_shadows[name] == 0) _shadows.Remove(name);
            }
        }

        private static IEnumerable<string> LexicalNames(IEnumerable<Stmt> body) =>
            LexicalDeclarationBindings.Collect(body).Select(name => name.Lexeme).Concat(body.SelectMany(statement => statement switch
            {
                Stmt.Function function => new[] { function.Name.Lexeme },
                Stmt.Class declaration => new[] { declaration.Name.Lexeme },
                Stmt.Enum declaration => new[] { declaration.Name.Lexeme },
                Stmt.Sequence sequence => LexicalNames(sequence.Statements),
                _ => []
            }));

        internal void VisitBody(IReadOnlyList<Stmt> body, bool ownsParameters = false)
        {
            var owned = ownsParameters
                ? LexicalDeclarationBindings.Collect(body).Where(name => candidates.TryGetValue(name.Lexeme, out var candidate) &&
                    ReferenceEquals(checker.Bindings.GetSymbol(name), candidate)).Select(name => name.Lexeme).ToHashSet(StringComparer.Ordinal)
                : [];
            InScope(LexicalNames(body).Where(name => !owned.Contains(name)),
                () => { foreach (var statement in body) Visit(statement); });
        }

        private void VisitCallable(IReadOnlyList<Stmt.Parameter> parameters, IReadOnlyList<Stmt>? body,
            Expr? expression, Token? name = null)
        {
            IEnumerable<string> names = parameters.Select(parameter => parameter.Name.Lexeme);
            if (name is not null) names = names.Append(name.Lexeme);
            InScope(names, () =>
            {
                foreach (var parameter in parameters)
                    if (parameter.DefaultValue is not null) Visit(parameter.DefaultValue);
                var variables = new FunctionVarNames();
                if (body is not null)
                    foreach (var statement in body) variables.Visit(statement);
                InScope(variables.Names, () =>
                {
                    if (body is not null) VisitBody(body);
                    if (expression is not null) Visit(expression);
                });
            });
        }

        protected override void VisitAssign(Expr.Assign expression) { Write(expression.Name); base.VisitAssign(expression); }
        protected override void VisitCompoundAssign(Expr.CompoundAssign expression) { Write(expression.Name); base.VisitCompoundAssign(expression); }
        protected override void VisitLogicalAssign(Expr.LogicalAssign expression) { Write(expression.Name); base.VisitLogicalAssign(expression); }
        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable) Write(variable.Name);
            base.VisitPrefixIncrement(expression);
        }
        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable) Write(variable.Name);
            base.VisitPostfixIncrement(expression);
        }
        protected override void VisitVar(Stmt.Var statement)
        {
            if (statement.IsVar && statement.Initializer is not null) Write(statement.Name);
            base.VisitVar(statement);
        }
        protected override void VisitBlock(Stmt.Block statement) => VisitBody(statement.Statements);
        protected override void VisitFunction(Stmt.Function statement) =>
            VisitCallable(statement.Parameters, statement.Body, null, statement.Name);
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            VisitCallable(expression.Parameters, expression.BlockBody, expression.ExpressionBody, expression.Name);
        protected override void VisitFor(Stmt.For statement) => InScope(
            statement.Initializer is null ? [] : LexicalNames([statement.Initializer]), () => base.VisitFor(statement));
        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (!statement.IsDeclaration || statement.IsVar) Write(statement.Variable);
            InScope(statement.IsDeclaration && !statement.IsVar ? [statement.Variable.Lexeme] : [], () => base.VisitForOf(statement));
        }
        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (!statement.IsDeclaration || statement.IsVar) Write(statement.Variable);
            InScope(statement.IsDeclaration && !statement.IsVar ? [statement.Variable.Lexeme] : [], () => base.VisitForIn(statement));
        }
        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            VisitBody(statement.TryBlock);
            if (statement.CatchBlock is not null)
                InScope(statement.CatchParam is null ? [] : [statement.CatchParam.Lexeme], () => VisitBody(statement.CatchBlock));
            if (statement.FinallyBlock is not null) VisitBody(statement.FinallyBlock);
        }
        protected override void VisitClass(Stmt.Class statement)
        {
            InScope([statement.Name.Lexeme], () =>
            {
                base.VisitClass(statement);
                foreach (var block in (statement.StaticInitializers ?? []).OfType<Stmt.StaticBlock>()) Visit(block);
            });
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression) =>
            InScope(expression.Name is null ? [] : [expression.Name.Lexeme], () =>
            {
                base.VisitClassExpr(expression);
                foreach (var block in (expression.StaticInitializers ?? []).OfType<Stmt.StaticBlock>()) Visit(block);
            });
        protected override void VisitSwitch(Stmt.Switch statement)
        {
            Visit(statement.Subject);
            var body = statement.Cases.SelectMany(@case => @case.Body).Concat(statement.DefaultBody ?? []).ToArray();
            InScope(LexicalNames(body), () =>
            {
                foreach (var @case in statement.Cases)
                {
                    Visit(@case.Value);
                    foreach (var child in @case.Body) Visit(child);
                }
                foreach (var child in statement.DefaultBody ?? []) Visit(child);
            });
        }
        protected override void VisitStaticBlock(Stmt.StaticBlock statement)
        {
            var variables = new FunctionVarNames();
            foreach (var child in statement.Body) variables.Visit(child);
            InScope(variables.Names, () => VisitBody(statement.Body));
        }
        protected override void VisitAccessor(Stmt.Accessor statement) => VisitCallable(
            statement.SetterParam is null ? [] : [statement.SetterParam], statement.Body, null);

        private sealed class FunctionVarNames : AstVisitorBase
        {
            internal HashSet<string> Names { get; } = new(StringComparer.Ordinal);
            protected override void VisitVar(Stmt.Var statement)
            {
                if (statement.IsVar) Names.Add(statement.Name.Lexeme);
                base.VisitVar(statement);
            }
            protected override void VisitFunction(Stmt.Function statement) { }
            protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
            protected override void VisitClass(Stmt.Class statement) { }
            protected override void VisitClassExpr(Expr.ClassExpr expression) { }
            protected override void VisitStaticBlock(Stmt.StaticBlock statement) { }
            protected override void VisitForOf(Stmt.ForOf statement)
            {
                if (statement.IsDeclaration && statement.IsVar) Names.Add(statement.Variable.Lexeme);
                base.VisitForOf(statement);
            }
            protected override void VisitForIn(Stmt.ForIn statement)
            {
                if (statement.IsDeclaration && statement.IsVar) Names.Add(statement.Variable.Lexeme);
                base.VisitForIn(statement);
            }
        }
    }
}
