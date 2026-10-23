using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>Representation facts for source primitive data fields, independent of annotations on uses.</summary>
internal static class PrimitiveFieldValueAnalysis
{
    internal static bool IsPrimitiveField(Expr.Get get, TypeMap? map)
    {
        if (map?.Get(get.Object) is not TypeInfo.Instance instance) return false;
        return IsPrimitiveField(instance.ResolvedClassType, get.Name.Lexeme);
    }

    private static bool IsPrimitiveField(TypeInfo? owner, string name)
    {
        while (owner is not null)
        {
            if (owner is TypeInfo.MutableClass mutable)
            {
                if (mutable.Getters.ContainsKey(name) || mutable.Setters.ContainsKey(name))
                    return false;
                if (mutable.FieldTypes.TryGetValue(name, out var field))
                    return IsPrimitive(field);
                owner = mutable.Superclass;
                continue;
            }
            var core = ClassInfoAccessor.Get(owner, c => c.Core, c => c.Core);
            if (core is null) return false;
            if (core.Getters.ContainsKey(name) || core.Setters.ContainsKey(name))
                return false;
            if (core.FieldTypes.TryGetValue(name, out var type))
                return IsPrimitive(type);
            owner = core.Superclass;
        }
        return false;
    }

    private static bool IsPrimitive(TypeInfo type) => type is TypeInfo.String or TypeInfo.StringLiteral
        or TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN };

    internal static List<Expr.Get> Reads(IReadOnlyList<Stmt> body, TypeMap? map)
    {
        var scanner = new Reader(map);
        foreach (var statement in body) scanner.Visit(statement);
        return scanner.Result;
    }

    internal static List<Expr.Get> Reads(Expr expression, TypeMap? map)
    {
        var scanner = new Reader(map);
        scanner.Visit(expression);
        return scanner.Result;
    }

    private sealed class Reader(TypeMap? map) : AstVisitorBase
    {
        internal List<Expr.Get> Result { get; } = [];
        protected override void VisitGet(Expr.Get expression)
        {
            if (IsPrimitiveField(expression, map)) Result.Add(expression);
            base.VisitGet(expression);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }

    internal static bool NativeMethodPreservesFields(Stmt.Function method, TypeMap? map)
    {
        HashSet<string> parameters = method.Parameters.Where(p =>
            p.DefaultValue is null && !p.IsOptional && !p.IsRest &&
            map?.IsUndefinedReachableNumericParam(p) != true &&
            p.Type is "number" or "boolean").Select(p => p.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
        bool Native(Expr expression) => expression switch
        {
            Expr.Literal { Value: double or bool } => true,
            Expr.Variable variable => parameters.Contains(variable.Name.Lexeme),
            Expr.Get { Object: Expr.This } get => IsPrimitiveField(get, map),
            Expr.Grouping grouping => Native(grouping.Expression),
            Expr.Binary binary => Native(binary.Left) && Native(binary.Right),
            Expr.Unary { Operator.Type: TokenType.PLUS or TokenType.MINUS or TokenType.BANG or TokenType.TILDE } unary => Native(unary.Right),
            _ => false
        };
        return method.Body is not null && method.Body.All(statement => statement switch
        {
            Stmt.Return { Value: not null } result => Native(result.Value),
            Stmt.Expression { Expr: Expr.Set { Object: Expr.This } set } =>
                map?.Get(set.Object) is TypeInfo.Instance instance &&
                IsPrimitiveField(instance.ResolvedClassType, set.Name.Lexeme) && Native(set.Value),
            _ => false
        });
    }

    internal static bool ReturnsRawField(Expr? expression, TypeMap? map) => expression switch
    {
        Expr.Get get => IsPrimitiveField(get, map),
        Expr.Grouping group => ReturnsRawField(group.Expression, map),
        Expr.TypeAssertion assertion => ReturnsRawField(assertion.Expression, map),
        Expr.NonNullAssertion assertion => ReturnsRawField(assertion.Expression, map),
        Expr.Satisfies satisfies => ReturnsRawField(satisfies.Expression, map),
        Expr.Binary { Operator.Type: TokenType.PLUS } plus =>
            ReturnsRawField(plus.Left, map) || ReturnsRawField(plus.Right, map),
        Expr.Ternary conditional => ReturnsRawField(conditional.ThenBranch, map) || ReturnsRawField(conditional.ElseBranch, map),
        Expr.Logical logical => ReturnsRawField(logical.Left, map) || ReturnsRawField(logical.Right, map),
        Expr.NullishCoalescing nullish => ReturnsRawField(nullish.Left, map) || ReturnsRawField(nullish.Right, map),
        _ => false
    };
}
