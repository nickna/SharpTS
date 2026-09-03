using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds uniquely named const object literals whose only observations are fixed
/// property reads or parser-generated object-destructuring reads.  These values
/// cannot have entered the compact carrier's materialization table, even when an
/// unrelated value with the same shape has, so their emitted field loads need no
/// per-instance weak-table probe.
/// </summary>
internal static class StableCompactRecordLocalAnalyzer
{
    internal static void Analyze(
        IReadOnlyList<Stmt> statements,
        TypeMap? typeMap,
        RuntimeFeatureSet features)
    {
        if (typeMap is null)
            return;

        var functions = new FunctionCollector();
        foreach (var statement in statements)
            functions.Visit(statement);

        foreach (var function in functions.Functions)
        {
            if (function.Body is null)
                continue;

            var collector = new CandidateCollector(typeMap, features, function);
            foreach (var statement in function.Body)
                collector.Visit(statement);
            if (collector.HasNestedCallable)
                continue;

            var usage = new UsageCollector(collector.CandidateFields);
            foreach (var statement in function.Body)
                usage.Visit(statement);

            foreach (var (name, literals) in collector.Candidates)
            {
                if (literals.Count == 1 &&
                    collector.DeclarationCounts.GetValueOrDefault(name) == 1 &&
                    !usage.Disqualified.Contains(name))
                {
                    features.CompactObjectRecordStableLocalLiterals.Add(literals[0]);
                }
            }
        }
    }

    private sealed class FunctionCollector : AstVisitorBase
    {
        public List<Stmt.Function> Functions { get; } = [];

        protected override void VisitFunction(Stmt.Function statement)
        {
            Functions.Add(statement);
            base.VisitFunction(statement);
        }
    }

    private sealed class CandidateCollector(
        TypeMap typeMap,
        RuntimeFeatureSet features,
        Stmt.Function function) : AstVisitorBase
    {
        public Dictionary<string, List<Expr.ObjectLiteral>> Candidates { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> CandidateFields { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, int> DeclarationCounts { get; } =
            function.Parameters
                .GroupBy(parameter => parameter.Name.Lexeme, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        public bool HasNestedCallable { get; private set; }

        protected override void VisitConst(Stmt.Const statement)
        {
            DeclarationCounts[statement.Name.Lexeme] =
                DeclarationCounts.GetValueOrDefault(statement.Name.Lexeme) + 1;
            if (statement.Initializer is Expr.ObjectLiteral literal &&
                JsonSerializationShapeAnalyzer.TryAnalyzeCompactObjectLiteral(
                    literal, typeMap, out var shape) &&
                features.CompactObjectRecordShapes.ContainsKey(
                    JsonSerializationShapeAnalyzer.Fingerprint(shape)))
            {
                if (!Candidates.TryGetValue(statement.Name.Lexeme, out var literals))
                {
                    literals = [];
                    Candidates.Add(statement.Name.Lexeme, literals);
                }
                literals.Add(literal);
                if (!CandidateFields.TryGetValue(statement.Name.Lexeme, out var fields))
                {
                    fields = new HashSet<string>(StringComparer.Ordinal);
                    CandidateFields.Add(statement.Name.Lexeme, fields);
                }
                foreach (var field in shape.Fields)
                    fields.Add(field.Key);
            }
            base.VisitConst(statement);
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            DeclarationCounts[statement.Name.Lexeme] =
                DeclarationCounts.GetValueOrDefault(statement.Name.Lexeme) + 1;
            base.VisitVar(statement);
        }

        protected override void VisitFunction(Stmt.Function statement) =>
            HasNestedCallable = true;

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            HasNestedCallable = true;
    }

    private sealed class UsageCollector(
        IReadOnlyDictionary<string, HashSet<string>> candidateFields) : AstVisitorBase
    {
        private readonly HashSet<string> _candidates =
            new(candidateFields.Keys, StringComparer.Ordinal);

        public HashSet<string> Disqualified { get; } = new(StringComparer.Ordinal);

        protected override void VisitConst(Stmt.Const statement) =>
            Visit(statement.Initializer);

        protected override void VisitVar(Stmt.Var statement)
        {
            if (statement.DestructuringSource == DestructuringSourceKind.Object &&
                statement.Initializer is Expr.Variable source &&
                _candidates.Contains(source.Name.Lexeme))
            {
                return;
            }
            base.VisitVar(statement);
        }

        protected override void VisitFunction(Stmt.Function statement)
        {
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (!expression.Optional && expression.Object is Expr.Variable receiver &&
                candidateFields.TryGetValue(receiver.Name.Lexeme, out var fields) &&
                fields.Contains(expression.Name.Lexeme))
            {
                return;
            }
            base.VisitGet(expression);
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (_candidates.Contains(expression.Name.Lexeme))
                Disqualified.Add(expression.Name.Lexeme);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (_candidates.Contains(expression.Name.Lexeme))
                Disqualified.Add(expression.Name.Lexeme);
            base.VisitAssign(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            DisqualifyPropertyMutation(expression.Operand);
            base.VisitDelete(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            DisqualifyPropertyMutation(expression.Operand);
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            DisqualifyPropertyMutation(expression.Operand);
            base.VisitPostfixIncrement(expression);
        }

        private void DisqualifyPropertyMutation(Expr expression)
        {
            if (expression is Expr.Get { Object: Expr.Variable receiver } &&
                _candidates.Contains(receiver.Name.Lexeme))
            {
                Disqualified.Add(receiver.Name.Lexeme);
            }
        }
    }
}
