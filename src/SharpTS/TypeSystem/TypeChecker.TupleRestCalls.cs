using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record GenericTupleCallSlot(TypeInfo Type, bool Optional = false, bool Spread = false, Expr? Expression = null);

    private List<GenericTupleCallSlot> GenericTupleCallArguments(Expr.Call call, IReadOnlyList<TypeInfo> types)
    {
        List<GenericTupleCallSlot> result = [];
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (call.Arguments[i] is Expr.Spread spread) AppendSpread(spread.Expression, types[i]);
            else result.Add(new(types[i], Expression: call.Arguments[i]));
        }
        return result;

        void AppendSpread(Expr expression, TypeInfo type)
        {
            if (expression is Expr.Grouping grouping)
            {
                AppendSpread(grouping.Expression, _typeMap.Get(grouping.Expression) ?? type);
                return;
            }
            if (expression is Expr.ArrayLiteral literal)
            {
                foreach (var element in literal.Elements)
                {
                    if (element is Expr.Spread nested) AppendSpread(nested.Expression, _typeMap.Get(nested.Expression) ?? TypeInfo.Any.Shared);
                    else result.Add(new(_typeMap.Get(element) ?? TypeInfo.Any.Shared, Expression: element));
                }
                return;
            }
            if (type is TypeInfo.Tuple tuple)
            {
                tuple = (TypeInfo.Tuple)FlattenTupleSpreads(tuple);
                foreach (var element in tuple.Elements)
                    result.Add(new(element.IsSpread && element.Type is TypeInfo.Array array ? array.ElementType : element.Type,
                        element.IsOptional, element.IsSpread));
                if (tuple.RestElementType is { } rest) result.Add(new(rest, Spread: true));
                return;
            }
            if (!TryGetSpreadElementType(type, out var elementType))
                throw new TypeCheckException($"Spread argument must be an iterable type, got '{type}'.", call.Paren.Line, tsCode: "TS2488");
            result.Add(new(elementType, Spread: true));
        }
    }

    private TypeInfo.Tuple GenericTupleCallType(IEnumerable<GenericTupleCallSlot> slots, TypeInfo.Tuple? pattern = null)
    {
        var elements = slots.Select((slot, i) => new TypeInfo.TupleElement(
            slot.Spread ? new TypeInfo.Array(slot.Type) :
                pattern is not null && i < pattern.Elements.Count && pattern.Elements[i].Type is TypeInfo.Tuple &&
                slot.Expression is Expr.ArrayLiteral literal ? GenericTupleLiteralType(literal) : slot.Type,
            slot.Spread ? TupleElementKind.Spread : slot.Optional ? TupleElementKind.Optional : TupleElementKind.Required)).ToList();
        return new(elements, elements.Count(element => element.IsRequired));
    }

    private TypeInfo.Tuple GenericTupleLiteralType(Expr.ArrayLiteral literal) => new(
        literal.Elements.Select(element => new TypeInfo.TupleElement(
            element is Expr.ArrayLiteral nested ? GenericTupleLiteralType(nested) : _typeMap.Get(element) ?? TypeInfo.Any.Shared,
            TupleElementKind.Required)).ToList(), literal.Elements.Count);

    private void CheckGenericTupleRestCall(Expr.Call call, TypeInfo.Function signature, List<GenericTupleCallSlot> arguments,
        bool validate = true)
    {
        var tuple = (TypeInfo.Tuple)FlattenTupleSpreads(signature.ParamTypes[^1]);
        List<GenericTupleCallSlot> parameters = signature.ParamTypes.Take(signature.ParamTypes.Count - 1)
            .Select((type, i) => new GenericTupleCallSlot(type, Optional: i >= signature.MinArity)).ToList();
        parameters.AddRange(tuple.Elements.Select(element => new GenericTupleCallSlot(
            element.IsSpread && element.Type is TypeInfo.Array array ? array.ElementType : element.Type,
            element.IsOptional, element.IsSpread)));
        if (tuple.RestElementType is { } rest) parameters.Add(new(rest, Spread: true));
        int required = parameters.Count(parameter => !parameter.Optional && !parameter.Spread);
        if (!arguments.Any(argument => argument.Spread) &&
            (arguments.Count < required || !parameters.Any(parameter => parameter.Spread) && arguments.Count > parameters.Count))
            throw new TypeCheckException($"Expected {(parameters.Any(parameter => parameter.Spread) ? "at least " : "")}{required} arguments but got {arguments.Count}.",
                call.Paren.Line, tsCode: "TS2554");

        Dictionary<int, TypeInfo> contexts = [];
        if (!Match(0, 0, contextualize: true)) Fail();
        foreach (var (index, expected) in contexts)
            if (arguments[index].Expression is { } expression) ContextualizeNestedGenericCallback(expression, expected);
        if (!validate) return;
        if (!Match(0, 0, contextualize: false)) Fail();

        bool Match(int parameter, int argument, bool contextualize, bool optionalOpen = false)
        {
            if (parameter == parameters.Count)
                return argument == arguments.Count || optionalOpen && arguments.Skip(argument).All(slot => slot.Spread);
            var expected = parameters[parameter];
            if (expected.Spread)
            {
                if (Match(parameter + 1, argument, contextualize)) return true;
                if (argument == arguments.Count || !Compatible(expected, arguments[argument], contextualize)) return false;
                contexts[argument] = expected.Type;
                return Match(parameter, argument + 1, contextualize);
            }
            if (argument == arguments.Count)
                return expected.Optional && Match(parameter + 1, argument, contextualize);
            var actual = arguments[argument];
            if (actual.Spread && !expected.Optional) return false;
            if (!Compatible(expected, actual, contextualize)) return false;
            contexts[argument] = expected.Type;
            return Match(parameter + 1, actual.Spread ? argument : argument + 1, contextualize, actual.Spread);
        }

        bool Compatible(GenericTupleCallSlot expected, GenericTupleCallSlot actual, bool contextualize)
        {
            if (contextualize && actual.Expression is { } expression && ContainsGenericTupleCallback(expression)) return true;
            TypeInfo type = actual.Expression is null ? actual.Type : ActualType(actual.Expression, actual.Type);
            if (expected.Type is TypeInfo.Tuple && actual.Expression is Expr.ArrayLiteral literal)
                type = GenericTupleLiteralType(literal);
            if (actual.Optional) type = CreateUnion(type, TypeInfo.Undefined.Shared);
            return IsArgumentCompatible(expected.Type, type, expected.Optional);
        }

        TypeInfo ActualType(Expr expression, TypeInfo fallback) => expression is Expr.Grouping grouping
            ? ActualType(grouping.Expression, fallback) : _typeMap.Get(expression) ?? fallback;

        void Fail() => throw new TypeCheckException(
            $"Arguments '{GenericTupleCallType(arguments)}' are not assignable to tuple parameters '{tuple}'.",
            call.Paren.Line, tsCode: arguments.FindIndex(argument => argument.Spread) is var open && open >= 0 &&
                (open < parameters.TakeWhile(parameter => !parameter.Spread).Count(parameter => !parameter.Optional) ||
                 !parameters.Any(parameter => parameter.Spread) && open >= parameters.Count)
                    ? "TS2556" : "TS2345");
    }

    private static bool ContainsGenericTupleCallback(Expr expression) => expression switch
    {
        Expr.ArrowFunction => true,
        Expr.Grouping grouping => ContainsGenericTupleCallback(grouping.Expression),
        Expr.ObjectLiteral literal => literal.Properties.Any(property => ContainsGenericTupleCallback(property.Value)),
        Expr.ArrayLiteral literal => literal.Elements.Any(ContainsGenericTupleCallback),
        Expr.Spread spread => ContainsGenericTupleCallback(spread.Expression),
        _ => false
    };
}
