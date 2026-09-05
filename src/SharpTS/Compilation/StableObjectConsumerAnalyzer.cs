using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Resolves small numeric record consumers at exact call sites. A summary may only read numeric
/// fields of its sole argument, write numeric expressions back to those fields, and return a
/// numeric expression. No calls, aliases, captures, control flow, or dynamic observations occur.
/// The caller must separately prove that the argument is a promoted record with those fields.
/// </summary>
internal static class StableObjectConsumerAnalyzer
{
    public static Dictionary<Expr.Call, ObjectConsumerInfo> Analyze(IReadOnlyList<Stmt> statements)
    {
        var results = new Dictionary<Expr.Call, ObjectConsumerInfo>(ReferenceEqualityComparer.Instance);
        var stable = new HashSet<Stmt.Function>(ReferenceEqualityComparer.Instance);
        StableFunctionBindingAnalyzer.Analyze(statements, stable);
        var hazards = new BindingHazards();
        foreach (var statement in statements) hazards.Visit(statement);
        if (hazards.DynamicBindings) return results;
        stable.RemoveWhere(function => hazards.ImportedNames.Contains(function.Name.Lexeme));

        // Reuse the lexical call resolver: same-named parameters/locals and nested callable
        // boundaries cannot inherit a top-level declaration's identity proof.
        var calls = NumericRestCallBindingAnalyzer.Analyze(statements, stable.ToList(), stable);
        foreach (var function in stable)
        {
            if (!TrySummarize(function, out var summary)) continue;
            foreach (var call in calls.GetValueOrDefault(function) ?? [])
                if (!call.Optional && call.Arguments is [Expr.Variable]
                    && call.Callee is Expr.Variable callee && callee.Name.Lexeme == function.Name.Lexeme)
                    results[call] = summary;
        }
        return results;
    }

    private static bool TrySummarize(Stmt.Function function, out ObjectConsumerInfo summary)
    {
        summary = null!;
        if (function.IsAsync || function.IsGenerator || function.IsDeclare
            || function.TypeParams is { Count: > 0 } || function.Decorators is { Count: > 0 }
            || function.Parameters is not [var parameter]
            || parameter.IsRest || parameter.IsOptional || parameter.DefaultValue is not null
            || parameter.DestructuredProperties is not null
            || function.Body is not { Count: > 0 and <= 8 } body
            || body[^1] is not Stmt.Return { Value: { } result })
            return false;

        var fields = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<Expr.Set>();
        int budget = 64;
        foreach (var statement in body.Take(body.Count - 1))
        {
            if (statement is not Stmt.Expression { Expr: Expr.Set write }
                || write.Object is not Expr.Variable receiver || receiver.Name.Lexeme != parameter.Name.Lexeme
                || !IsNumericExpression(write.Value, parameter.Name.Lexeme, fields, ref budget))
                return false;
            fields.Add(write.Name.Lexeme);
            writes.Add(write);
        }
        if (!IsNumericExpression(result, parameter.Name.Lexeme, fields, ref budget)) return false;
        summary = new(parameter.Name.Lexeme, writes, result, fields);
        return true;
    }

    private static bool IsNumericExpression(Expr expression, string parameter,
        HashSet<string> fields, ref int budget)
    {
        if (--budget < 0) return false;
        switch (expression)
        {
            case Expr.Literal { Value: double }:
                return true;
            case Expr.Get { Optional: false, Object: Expr.Variable receiver } read
                when receiver.Name.Lexeme == parameter:
                fields.Add(read.Name.Lexeme);
                return true;
            case Expr.Grouping grouping:
                return IsNumericExpression(grouping.Expression, parameter, fields, ref budget);
            case Expr.Unary unary when unary.Operator.Type is TokenType.PLUS or TokenType.MINUS:
                return IsNumericExpression(unary.Right, parameter, fields, ref budget);
            case Expr.Binary binary when binary.Operator.Type is TokenType.PLUS or TokenType.MINUS
                or TokenType.STAR or TokenType.SLASH or TokenType.PERCENT:
                return IsNumericExpression(binary.Left, parameter, fields, ref budget)
                    && IsNumericExpression(binary.Right, parameter, fields, ref budget);
            default:
                return false;
        }
    }

    private sealed class BindingHazards : AstVisitorBase
    {
        public bool DynamicBindings { get; private set; }
        public HashSet<string> ImportedNames { get; } = new(StringComparer.Ordinal);
        protected override void VisitVariable(Expr.Variable expression)
        {
            if (expression.Name.Lexeme is "eval" or "globalThis" or "Function") DynamicBindings = true;
        }
        protected override void VisitThis(Expr.This expression) => DynamicBindings = true;
        protected override void VisitImport(Stmt.Import statement)
        {
            if (statement.DefaultImport != null) ImportedNames.Add(statement.DefaultImport.Lexeme);
            if (statement.NamespaceImport != null) ImportedNames.Add(statement.NamespaceImport.Lexeme);
            foreach (var import in statement.NamedImports ?? [])
                ImportedNames.Add((import.LocalName ?? import.Imported).Lexeme);
        }
        // Import-equals constructs have separate binding rules; retain general calls.
        protected override void VisitImportAlias(Stmt.ImportAlias statement) => DynamicBindings = true;
        protected override void VisitImportRequire(Stmt.ImportRequire statement) => DynamicBindings = true;
    }
}
