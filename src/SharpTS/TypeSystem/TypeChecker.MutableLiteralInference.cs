using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private TypeInfo WidenFreshArrayTuple(Expr.ArrayLiteral literal, TypeInfo.Tuple tuple)
    {
        if (tuple.IsReadonly || literal.Elements.Any(element => element is Expr.Spread) ||
            literal.Elements.Count != tuple.Elements.Count) return tuple;
        // A destructuring pattern supplies a positional context to its synthetic RHS.
        // That outer tuple does not freeze fresh nested arrays or their element types.
        return tuple with
        {
            Elements = tuple.Elements.Select((element, index) => element with
            {
                Type = WidenClassFieldInitializerType(literal.Elements[index],
                    _typeMap.Get(literal.Elements[index]) ?? element.Type),
            }).ToList(),
        };
    }

    private TypeInfo? ContextualArrayElement(TypeInfo context) => context switch
    {
        TypeInfo.Array array => array.ElementType,
        TypeInfo.Union union => union.FlattenedTypes.Select(ContextualArrayElement).OfType<TypeInfo>().ToList() is { Count: > 0 } elements
            ? CollapseOrCreateUnion(elements) : null,
        _ => null,
    };

    private TypeInfo? SelectObjectLiteralContext(Expr.ObjectLiteral expression, TypeInfo? context)
    {
        if (context is not TypeInfo.Union union) return context;
        var candidates = union.FlattenedTypes.ToList();
        foreach (var property in expression.Properties)
        {
            if (property.IsSpread || property.Kind != Expr.ObjectPropertyKind.Value ||
                UnwrapTransparentForWidening(property.Value) is not Expr.Literal literal) continue;
            TypeInfo value = InferConstLiteralType(literal.Value);
            var matching = candidates.Where(candidate => ObjectMemberContext(candidate, property.Key) is not { } member ||
                IsCompatible(member, value)).ToList();
            if (matching.Count > 0) candidates = matching;
        }
        return candidates.Count == union.FlattenedTypes.Count ? context : CollapseOrCreateUnion(candidates);
    }

    private TypeInfo WidenMutableMemberValue(Expr expression, TypeInfo value, TypeInfo? context)
    {
        Expr source = UnwrapTransparentForWidening(expression);
        // These nodes have already completed their own contextual member/return inference.
        if (source is Expr.ObjectLiteral or Expr.ArrowFunction ||
            context is not null && source is Expr.ArrayLiteral || KeepsLiteralInContext(value, context))
            return value;
        return WidenClassFieldInitializerType(expression, value);
    }

    private TypeInfo? ObjectMemberContext(TypeInfo? context, Expr.PropertyKey? key)
    {
        if (context is null) return null;
        if (context is TypeInfo.Union union)
        {
            var members = union.FlattenedTypes.Select(member => ObjectMemberContext(member, key)).OfType<TypeInfo>().ToList();
            return members.Count == 0 ? null : CollapseOrCreateUnion(members);
        }
        string? name = key switch
        {
            Expr.IdentifierKey identifier => identifier.Name.Lexeme,
            Expr.LiteralKey literal => literal.Literal.Literal?.ToString(),
            Expr.ComputedKey { Expression: Expr.Literal { Value: string text } } => text,
            _ => null,
        };
        if (name is not null && GetPropertyType(context, name) is { } member) return member;
        return context switch
        {
            TypeInfo.Record record => record.StringIndexType ?? record.NumberIndexType,
            TypeInfo.Interface shape => shape.StringIndexType ?? shape.NumberIndexType,
            _ => null,
        };
    }

    private static bool KeepsLiteralInContext(TypeInfo value, TypeInfo? context)
    {
        if (value is TypeInfo.Union values && context is not null)
            return values.FlattenedTypes.All(member => KeepsLiteralInContext(member, context));
        if (context is TypeInfo.Union union) return union.FlattenedTypes.Any(member => KeepsLiteralInContext(value, member));
        if (context is TypeInfo.TypeParameter { Constraint: { } constraint }) return KeepsLiteralInContext(value, constraint);
        return (value, context) switch
        {
            (TypeInfo.NumberLiteral, TypeInfo.NumberLiteral) => true,
            (TypeInfo.StringLiteral, TypeInfo.StringLiteral) => true,
            (TypeInfo.BooleanLiteral, TypeInfo.BooleanLiteral or TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN }) => true,
            (TypeInfo.BigIntLiteral, TypeInfo.BigIntLiteral) => true,
            (TypeInfo.Null or TypeInfo.Undefined, _) => true,
            _ => false,
        };
    }

    private TypeInfo WidenFreshArrowReturn(Expr.ArrowFunction arrow, TypeInfo callable, TypeInfo? context)
    {
        TypeInfo result = callable switch
        {
            TypeInfo.Function function => function.ReturnType,
            TypeInfo.GenericFunction function => function.ReturnType,
            _ => TypeInfo.Inferred.Shared,
        };
        if (arrow.IsAsync) { result = ResolveAwaitedType(result); if (context is not null) context = ResolveAwaitedType(context); }
        // A union of literal alternatives remains an inferred literal union. The existing
        // source-owned freshness helper distinguishes a single fresh literal from an assertion.
        if (result is not (TypeInfo.NumberLiteral or TypeInfo.StringLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral) ||
            KeepsLiteralInContext(result, context)) return callable;
        return WidenClassFieldArrowReturn(arrow, callable);
    }
}
