using SharpTS.Parsing;

namespace SharpTS.TypeSystem.ControlFlow;

// This is a syntax-only over-approximation. It never checks expressions or
// publishes source facts; unmodeled loop execution keeps a normal alternative.
internal static class StatementCompletionAnalysis
{
    private enum Kind { Normal, ValueReturn, EmptyReturn, Throw, Break, Continue }
    private readonly record struct Completion(Kind Kind, string? Label = null);
    private static readonly Completion Normal = new(Kind.Normal);

    internal static bool CanCompleteNormally(IEnumerable<Stmt> statements) => AnalyzeList(statements).Contains(Normal);

    internal static bool SwitchDefinitelyReturns(Stmt.Switch statement, bool allowEmptyReturn, bool isExhaustive = false)
    {
        var completions = AnalyzeSwitch(statement, isExhaustive);
        return completions.Count > 0 && completions.All(completion => completion.Kind is Kind.ValueReturn or Kind.Throw ||
            allowEmptyReturn && completion.Kind == Kind.EmptyReturn);
    }

    private static HashSet<Completion> AnalyzeList(IEnumerable<Stmt> statements)
    {
        HashSet<Completion> result = [Normal];
        foreach (var statement in statements)
        {
            if (!result.Remove(Normal)) break;
            result.UnionWith(Analyze(statement));
        }
        return result;
    }

    private static HashSet<Completion> Analyze(Stmt statement)
    {
        switch (statement)
        {
            case Stmt.Return returned: return [new(returned.Value is null ? Kind.EmptyReturn : Kind.ValueReturn)];
            case Stmt.Throw: return [new(Kind.Throw)];
            case Stmt.Break broken: return [new(Kind.Break, broken.Label?.Lexeme)];
            case Stmt.Continue continued: return [new(Kind.Continue, continued.Label?.Lexeme)];
            case Stmt.Block block: return AnalyzeList(block.Statements);
            case Stmt.Sequence sequence: return AnalyzeList(sequence.Statements);
            case Stmt.If conditional:
            {
                var result = Analyze(conditional.ThenBranch);
                result.UnionWith(conditional.ElseBranch is null ? [Normal] : Analyze(conditional.ElseBranch));
                return result;
            }
            case Stmt.Switch switched: return AnalyzeSwitch(switched, isExhaustive: false);
            case Stmt.TryCatch guarded:
            {
                var result = AnalyzeList(guarded.TryBlock);
                if (guarded.CatchBlock is { } caught)
                {
                    result.Remove(new(Kind.Throw));
                    // Expressions may throw even without a written throw statement.
                    result.UnionWith(AnalyzeList(caught));
                }
                if (guarded.FinallyBlock is { } finalized)
                {
                    var final = AnalyzeList(finalized);
                    if (!final.Remove(Normal)) result.Clear();
                    result.UnionWith(final);
                }
                return result;
            }
            case Stmt.LabeledStatement labeled:
            {
                var result = Analyze(labeled.Statement);
                if (result.Remove(new(Kind.Break, labeled.Label.Lexeme))) result.Add(Normal);
                Stmt target = labeled.Statement;
                while (target is Stmt.LabeledStatement nested) target = nested.Statement;
                if (target is Stmt.While or Stmt.DoWhile or Stmt.For or Stmt.ForOf or Stmt.ForIn)
                    result.Remove(new(Kind.Continue, labeled.Label.Lexeme));
                return result;
            }
            case Stmt.While loop: return AnalyzeLoop(loop.Body);
            case Stmt.DoWhile loop: return AnalyzeLoop(loop.Body);
            case Stmt.For loop: return AnalyzeLoop(loop.Body);
            case Stmt.ForOf loop: return AnalyzeLoop(loop.Body);
            case Stmt.ForIn loop: return AnalyzeLoop(loop.Body);
            default: return [Normal];
        }
    }

    private static HashSet<Completion> AnalyzeLoop(Stmt body)
    {
        var result = Analyze(body);
        result.Remove(new(Kind.Break));
        result.Remove(new(Kind.Continue));
        result.Add(Normal);
        return result;
    }

    private static HashSet<Completion> AnalyzeSwitch(Stmt.Switch statement, bool isExhaustive)
    {
        HashSet<Completion> result = [];
        HashSet<Completion> suffix = [Normal];
        bool hasDefault = false;
        for (int i = statement.Clauses.Count - 1; i >= 0; i--)
        {
            var clause = statement.Clauses[i];
            var body = AnalyzeList(clause.Body);
            if (body.Remove(Normal)) body.UnionWith(suffix);
            suffix = body;
            if (clause.IsDefault) hasDefault = true;
            if (!clause.IsDefault || !isExhaustive) result.UnionWith(body);
        }
        if (!hasDefault && !isExhaustive) result.Add(Normal);
        if (result.Remove(new(Kind.Break))) result.Add(Normal);
        return result;
    }
}
