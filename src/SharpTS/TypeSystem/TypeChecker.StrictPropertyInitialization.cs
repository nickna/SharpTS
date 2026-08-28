using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    /// <summary>
    /// Implements the constructor definite-assignment portion of
    /// strictPropertyInitialization. The analysis is deliberately syntactic,
    /// matching TypeScript's treatment of direct <c>this.field</c> writes.
    /// </summary>
    private void CheckStrictPropertyInitialization(
        Stmt.Class declaration,
        TypeInfo.MutableClass classType)
    {
        if (!_strictPropertyInitialization || !_strictNullChecks)
            return;

        var requiredFields = declaration.Fields
            .Where(f => !f.IsStatic && !f.IsDeclare && !f.IsLiteralName && !f.IsOptional &&
                        !f.HasDefiniteAssignmentAssertion && f.Initializer == null)
            .Where(f =>
            {
                string name = GetFieldMemberName(f);
                if (!classType.FieldTypes.TryGetValue(name, out var type))
                    return false;
                if (type is TypeInfo.Any && f.TypeAnnotationNode is IndexedAccessTypeNode indexed)
                    return !ContainsSelfIndexedAccess(
                        indexed, declaration.Name.Lexeme, requireLiteralIndex: true);
                TypeInfo currentType = type is TypeInfo.Any && f.TypeAnnotation != null
                    ? ResolveAnnotation(f.TypeAnnotation, f.TypeAnnotationNode) ?? type
                    : type;
                return !CanBeUndefined(currentType);
            })
            .ToList();

        if (requiredFields.Count == 0)
            return;

        var constructor = declaration.Methods
            .FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        HashSet<string> definitelyAssigned;
        if (constructor?.Body == null)
        {
            definitelyAssigned = [];
        }
        else
        {
            definitelyAssigned = constructor.Parameters
                .Where(p => p.IsParameterProperty)
                .Select(p => p.Name.Lexeme)
                .ToHashSet(StringComparer.Ordinal);

            var exits = new List<HashSet<string>>();
            var state = AnalyzeInitializationStatements(
                constructor.Body, definitelyAssigned, exits);
            if (state.Reachable)
                exits.Add(state.Assigned);

            // A constructor that cannot complete normally (for example, one that
            // always throws) has no uninitialized instance observable by callers.
            if (exits.Count > 0)
            {
                definitelyAssigned = new HashSet<string>(exits[0], StringComparer.Ordinal);
                foreach (var exit in exits.Skip(1))
                    definitelyAssigned.IntersectWith(exit);
            }
            else
            {
                definitelyAssigned = requiredFields
                    .Select(GetFieldMemberName)
                    .ToHashSet(StringComparer.Ordinal);
            }
        }

        foreach (var field in requiredFields)
        {
            string name = GetFieldMemberName(field);
            if (!definitelyAssigned.Contains(name))
            {
                RecordTypeError(new TypeCheckException(
                    $"Property '{name}' has no initializer and is not definitely assigned in the constructor.",
                    line: field.Name.Line,
                    tsCode: "TS2564"));
            }
        }
    }

    private static bool CanBeUndefined(TypeInfo type) => type switch
    {
        TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Undefined or TypeInfo.Void => true,
        TypeInfo.Union union => union.ContainsUndefined,
        _ => false,
    };

    private readonly record struct InitializationFlow(
        HashSet<string> Assigned,
        bool Reachable);

    private InitializationFlow AnalyzeInitializationStatements(
        IEnumerable<Stmt> statements,
        HashSet<string> incoming,
        List<HashSet<string>> exits)
    {
        var state = new InitializationFlow(
            new HashSet<string>(incoming, StringComparer.Ordinal), true);

        foreach (var statement in statements)
        {
            if (!state.Reachable)
                break;
            state = AnalyzeInitializationStatement(statement, state.Assigned, exits);
        }
        return state;
    }

    private InitializationFlow AnalyzeInitializationStatement(
        Stmt statement,
        HashSet<string> incoming,
        List<HashSet<string>> exits)
    {
        switch (statement)
        {
            case Stmt.Expression expression:
                return new InitializationFlow(
                    AnalyzeInitializationExpression(expression.Expr, incoming), true);

            case Stmt.Block block:
                return AnalyzeInitializationStatements(block.Statements, incoming, exits);

            case Stmt.Sequence sequence:
                return AnalyzeInitializationStatements(sequence.Statements, incoming, exits);

            case Stmt.If conditional:
            {
                var afterCondition = AnalyzeInitializationExpression(
                    conditional.Condition, incoming);
                var thenFlow = AnalyzeInitializationStatement(
                    conditional.ThenBranch, afterCondition, exits);
                var elseFlow = conditional.ElseBranch != null
                    ? AnalyzeInitializationStatement(
                        conditional.ElseBranch, afterCondition, exits)
                    : new InitializationFlow(
                        new HashSet<string>(afterCondition, StringComparer.Ordinal), true);

                if (!thenFlow.Reachable) return elseFlow;
                if (!elseFlow.Reachable) return thenFlow;
                thenFlow.Assigned.IntersectWith(elseFlow.Assigned);
                return new InitializationFlow(thenFlow.Assigned, true);
            }

            case Stmt.Return:
                exits.Add(new HashSet<string>(incoming, StringComparer.Ordinal));
                return new InitializationFlow(incoming, false);

            case Stmt.Throw:
                return new InitializationFlow(incoming, false);

            // Loops and switches may execute zero times or leave through multiple
            // control-flow edges. Keep only assignments known before entering them.
            // A do/while body does execute once, so its body can contribute.
            case Stmt.DoWhile doWhile:
            {
                var body = AnalyzeInitializationStatement(doWhile.Body, incoming, exits);
                return body.Reachable
                    ? new InitializationFlow(
                        AnalyzeInitializationExpression(doWhile.Condition, body.Assigned), true)
                    : body;
            }

            default:
                return new InitializationFlow(
                    new HashSet<string>(incoming, StringComparer.Ordinal), true);
        }
    }

    private HashSet<string> AnalyzeInitializationExpression(
        Expr expression,
        HashSet<string> incoming)
    {
        var assigned = new HashSet<string>(incoming, StringComparer.Ordinal);
        switch (expression)
        {
            case Expr.Set { Object: Expr.This, Name: var name, Value: var value }:
                assigned = AnalyzeInitializationExpression(value, assigned);
                assigned.Add(name.Lexeme);
                break;
            case Expr.SetPrivate { Object: Expr.This, Name: var name, Value: var value }:
                assigned = AnalyzeInitializationExpression(value, assigned);
                assigned.Add(name.Lexeme);
                break;
            case Expr.CompoundSet { Object: Expr.This, Name: var compoundName, Value: var compoundValue }:
                assigned = AnalyzeInitializationExpression(compoundValue, assigned);
                assigned.Add(compoundName.Lexeme);
                break;
            case Expr.LogicalSet { Object: Expr.This, Name: var logicalName, Value: var logicalValue }:
                assigned = AnalyzeInitializationExpression(logicalValue, assigned);
                assigned.Add(logicalName.Lexeme);
                break;
            case Expr.Comma comma:
                assigned = AnalyzeInitializationExpression(comma.Left, assigned);
                assigned = AnalyzeInitializationExpression(comma.Right, assigned);
                break;
            case Expr.Grouping grouping:
                assigned = AnalyzeInitializationExpression(grouping.Expression, assigned);
                break;
            case Expr.Ternary ternary:
            {
                var afterCondition = AnalyzeInitializationExpression(
                    ternary.Condition, assigned);
                var thenAssigned = AnalyzeInitializationExpression(
                    ternary.ThenBranch, afterCondition);
                var elseAssigned = AnalyzeInitializationExpression(
                    ternary.ElseBranch, afterCondition);
                thenAssigned.IntersectWith(elseAssigned);
                assigned = thenAssigned;
                break;
            }
        }
        return assigned;
    }
}
