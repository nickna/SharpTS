using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // Overload candidates need independent contextual views. Rechecking the expressions here
    // would replay value flow and publish facts from signatures that might be rejected.
    private List<TypeInfo> ContextualFreshCallArguments(
        IReadOnlyList<Expr> arguments, List<TypeInfo> types,
        IReadOnlyList<TypeInfo> parameters, bool hasRest, bool publish = false)
    {
        var result = new List<TypeInfo>(types);
        for (int i = 0; i < arguments.Count && i < types.Count; i++)
        {
            if (arguments[i] is Expr.Spread) continue;
            var (context, isConst) = FreshCallParameterContext(parameters, hasRest, i);
            if (context is not null)
                result[i] = ContextualFreshCallArgument(arguments[i], types[i], context, isConst, publish);
        }
        return result;
    }

    private (TypeInfo? Context, bool IsConst) FreshCallParameterContext(IReadOnlyList<TypeInfo> parameters, bool hasRest, int index)
    {
        int regular = hasRest ? parameters.Count - 1 : parameters.Count;
        if (index < regular) return (parameters[index], parameters[index] is TypeInfo.TypeParameter { IsConst: true });
        if (!hasRest || parameters.Count == 0) return (null, false);
        TypeInfo rest = parameters[^1];
        bool isConst = rest is TypeInfo.TypeParameter { IsConst: true };
        rest = FreshCallConstraint(rest);
        TypeInfo? element = GetContextualElementType(rest, index - regular);
        // A const rest parameter also supplies a const context to each source argument.
        return (element, isConst || element is TypeInfo.TypeParameter { IsConst: true });
    }

    private static TypeInfo FreshCallConstraint(TypeInfo context)
    {
        var seen = new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance);
        while (context is TypeInfo.TypeParameter { Constraint: { } constraint } && seen.Add(context))
            context = constraint;
        return context;
    }

    private TypeInfo ContextualFreshCallArgument(
        Expr expression, TypeInfo stored, TypeInfo context, bool isConst, bool publish)
    {
        TypeInfo result = stored;
        TypeInfo shape = FreshCallConstraint(context);
        switch (expression)
        {
            case Expr.Grouping grouping:
                result = ContextualFreshCallArgument(grouping.Expression, stored, context, isConst, publish);
                break;
            case Expr.ObjectLiteral literal when stored is TypeInfo.Record record:
            {
                TypeInfo? selected = SelectObjectLiteralContext(literal, shape);
                var fields = record.Fields.ToDictionary();
                // Replay only the type-level member merge, in source order. In particular an
                // optional spread combines with an earlier property; a required one replaces it.
                foreach (var property in literal.Properties)
                {
                    if (property.IsSpread)
                    {
                        if (_typeMap.Get(property.Value) is { } spread)
                            TryMergeSpreadFields(spread, fields);
                        continue;
                    }
                    string? name = FreshCallPropertyName(property.Key);
                    if (name is null || !record.Fields.TryGetValue(name, out TypeInfo? field)) continue;
                    if (property.Kind != Expr.ObjectPropertyKind.Value)
                    {
                        // Accessor fields store values, while their source nodes store functions.
                        fields[name] = field;
                        continue;
                    }
                    TypeInfo? member = ObjectMemberContext(selected, property.Key);
                    TypeInfo normal = WidenMutableMemberValue(property.Value,
                        _typeMap.Get(property.Value) ?? field, member);
                    fields[name] = member is not null || isConst
                        ? ContextualFreshCallArgument(property.Value, normal,
                            member ?? TypeInfo.Unknown.Shared, isConst, publish) : normal;
                }
                result = record with { Fields = fields.ToFrozenDictionary() };
                break;
            }
            case Expr.Ternary ternary:
            {
                TypeInfo then = ContextualFreshCallArgument(ternary.ThenBranch,
                    _typeMap.Get(ternary.ThenBranch) ?? stored, context, isConst, publish);
                TypeInfo otherwise = ContextualFreshCallArgument(ternary.ElseBranch,
                    _typeMap.Get(ternary.ElseBranch) ?? stored, context, isConst, publish);
                result = IsCompatible(then, otherwise) ? then :
                    IsCompatible(otherwise, then) ? otherwise : CreateUnion(then, otherwise);
                break;
            }
            case Expr.ArrayLiteral array when stored is TypeInfo.Array or TypeInfo.Tuple &&
                !array.Elements.Any(element => element is Expr.Spread):
            {
                var arrayType = stored as TypeInfo.Array;
                var tupleType = stored as TypeInfo.Tuple;
                var elements = new List<TypeInfo>();
                for (int i = 0; i < array.Elements.Count; i++)
                {
                    Expr element = array.Elements[i];
                    TypeInfo? member = GetContextualElementType(shape, i);
                    TypeInfo storage = arrayType?.ElementType ?? GetContextualElementType(tupleType, i) ?? TypeInfo.Any.Shared;
                    TypeInfo elementType = _typeMap.Get(element) ?? storage;
                    // Callback contextualization owns the stored callable element view. Its
                    // source arrow node can still carry the earlier inference-pass signature.
                    TypeInfo source = elementType is TypeInfo.Function or TypeInfo.GenericFunction
                        ? storage : WidenMutableMemberValue(element, elementType, member);
                    elements.Add(member is not null || isConst
                        ? ContextualFreshCallArgument(element, source, member ?? TypeInfo.Unknown.Shared, isConst, publish)
                        : source);
                }
                if (shape is TypeInfo.Tuple || tupleType is not null)
                    result = TypeInfo.Tuple.FromTypes(elements, elements.Count, isReadonly: tupleType?.IsReadonly ?? false);
                else if (elements.Count > 0)
                {
                    TypeInfo common = elements[0];
                    foreach (TypeInfo next in elements.Skip(1))
                    {
                        if (IsCompatible(common, next)) continue;
                        common = IsCompatible(next, common) ? next : CreateUnion(common, next);
                    }
                    result = arrayType! with { ElementType = common };
                }
                break;
            }
            case Expr.TypeAssertion:
            case Expr.Satisfies:
                // Assertions and satisfies have already established the aggregate's domain.
                break;
            default:
                TypeInfo raw = _typeMap.Get(expression) ?? stored;
                if (FreshCallLiteral(raw) && (isConst || KeepsLiteralInContext(raw, context) ||
                    context is TypeInfo.TypeParameter && IsCompatible(shape, raw))) result = raw;
                break;
        }
        if (publish && UnwrapTransparentForWidening(expression) is Expr.ObjectLiteral or Expr.ArrayLiteral or Expr.Ternary)
        {
            _typeMap.Set(expression, result);
            RecordEditorExpressionType(expression, result);
        }
        return result;
    }

    private string? FreshCallPropertyName(Expr.PropertyKey? key) => key switch
    {
        Expr.IdentifierKey identifier => identifier.Name.Lexeme,
        Expr.LiteralKey literal => literal.Literal.Literal?.ToString(),
        Expr.ComputedKey computed => _typeMap.Get(computed.Expression) switch
        {
            TypeInfo.StringLiteral text => text.Value,
            TypeInfo.NumberLiteral number => number.Value.ToString(),
            _ => null,
        },
        _ => null,
    };

    private static bool FreshCallLiteral(TypeInfo type) => type switch
    {
        TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral => true,
        TypeInfo.Union union => union.FlattenedTypes.All(FreshCallLiteral),
        _ => false,
    };
}
