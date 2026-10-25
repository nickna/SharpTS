using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record GenericTupleCallSlot(TypeInfo Type, bool Optional = false, bool Spread = false, Expr? Expression = null);
    private readonly record struct GenericTupleMatchState(int Parameter, int Argument, bool OptionalOpen = false);

    private bool TryGetGenericTupleCallSignature(TypeInfo callee, out TypeInfo.CallSignature original,
        out TypeInfo.GenericFunction generic)
    {
        TypeInfo.CallSignature? signature = callee switch
        {
            TypeInfo.Interface { CallSignatures: [var single] } => single,
            TypeInfo.Record { CallSignatures: [var single] } => single,
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface { CallSignatures: [var single] } } => single,
            _ => null
        };
        original = signature!;
        generic = null!;
        if (signature is not { TypeParams.Count: > 0, HasRestParam: true } || signature.ParamTypes.LastOrDefault() is not TypeInfo.Tuple)
            return false;
        if (callee is TypeInfo.InstantiatedGeneric instantiated)
            signature = FlattenInstantiatedInterface(instantiated)!.CallSignatures![0];
        generic = CopyEditorSignatureMetadata(original, new TypeInfo.GenericFunction(signature.TypeParams!,
            signature.ParamTypes, signature.ReturnType, signature.RequiredParams, signature.HasRestParam, ParamNames: signature.ParamNames));
        return true;
    }

    private TypeInfo.Function CompleteGenericTupleRestCall(Expr.Call call, TypeInfo.GenericFunction generic,
        TypeInfo.Function signature, List<TypeInfo> argumentTypes, TypeInfo? contextualResult = null)
    {
        var slots = GenericTupleCallArguments(call, argumentTypes);
        if (call.TypeArgs is not { Count: > 0 } && call.Arguments.Any(ContainsGenericTupleCallback))
        {
            CheckGenericTupleRestCall(call, signature, slots, validate: false);
            argumentTypes = RefreshContextualizedGenericArguments(call.Arguments, argumentTypes);
            slots = GenericTupleCallArguments(call, argumentTypes);
            try
            {
                signature = (TypeInfo.Function)InstantiateGenericFunction(generic, InferTypeArguments(generic,
                    PrepareFreshCallArgumentsForInference(generic.ParamTypes, call.Arguments, argumentTypes),
                    contextualResult, tupleRestArguments: GenericTupleCallType(
                        slots.Skip(generic.ParamTypes.Count - 1), (TypeInfo.Tuple)generic.ParamTypes[^1])));
            }
            catch (TypeCheckException error) when (error.Diagnostic.TsCode == "TS2344")
            {
                throw new TypeCheckException(error.Message, call.Paren.Line, tsCode: "TS2345");
            }
        }
        CheckGenericTupleRestCall(call, signature, slots);
        return signature;
    }

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
        bool validate = true, bool contextualizeCallbacks = true)
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

        if (!Match(contextualizeCallbacks, out var contexts)) Fail();
        if (!contextualizeCallbacks) return;
        foreach (var (index, expected) in contexts)
            if (arguments[index].Expression is { } expression)
            {
                if (expected is TypeInfo.Tuple expectedTuple && expression is Expr.ArrayLiteral literal)
                    CheckArrayLiteralAgainstTuple(literal, expectedTuple, "argument");
                else ContextualizeNestedGenericCallback(expression, expected);
            }
        if (!validate) return;
        if (!Match(false, out _)) Fail();

        bool Match(bool contextualize, out Dictionary<int, TypeInfo> winningContexts)
        {
            // A finite state graph bounds variadic alternatives without recursive backtracking.
            // Contexts come only from the successful path, never from rejected alternatives.
            GenericTupleMatchState initial = new(0, 0);
            Queue<GenericTupleMatchState> pending = new();
            Dictionary<GenericTupleMatchState, (GenericTupleMatchState Parent, int Argument, TypeInfo? Type)> paths =
                new() { [initial] = (initial, -1, null) };
            pending.Enqueue(initial);
            while (pending.TryDequeue(out var state))
            {
                int parameter = state.Parameter, argument = state.Argument;
                if (parameter == parameters.Count)
                {
                    if (argument != arguments.Count &&
                        !(state.OptionalOpen && arguments.Skip(argument).All(slot => slot.Spread))) continue;
                    winningContexts = [];
                    for (var current = state; current != initial; current = paths[current].Parent)
                        if (paths[current] is { Argument: >= 0, Type: { } type } step)
                            winningContexts[step.Argument] = type;
                    return true;
                }
                var expected = parameters[parameter];
                if (expected.Spread)
                {
                    Add(new(parameter + 1, argument), state);
                    if (argument < arguments.Count && Compatible(expected, arguments[argument], contextualize))
                        Add(new(parameter, argument + 1), state, argument, expected.Type);
                    continue;
                }
                if (argument == arguments.Count)
                {
                    if (expected.Optional) Add(new(parameter + 1, argument), state);
                    continue;
                }
                var actual = arguments[argument];
                if (actual.Spread && !expected.Optional || !Compatible(expected, actual, contextualize)) continue;
                Add(new(parameter + 1, actual.Spread ? argument : argument + 1, actual.Spread), state, argument, expected.Type);
            }
            winningContexts = [];
            return false;

            void Add(GenericTupleMatchState next, GenericTupleMatchState parent, int argument = -1, TypeInfo? type = null)
            {
                if (!paths.TryAdd(next, (parent, argument, type))) return;
                pending.Enqueue(next);
            }
        }

        bool Compatible(GenericTupleCallSlot expected, GenericTupleCallSlot actual, bool contextualize)
        {
            if (contextualize && actual.Expression is { } expression && ContainsGenericTupleCallback(expression)) return true;
            if (contextualize && expected.Type is TypeInfo.Tuple && actual.Expression is Expr.ArrayLiteral) return true;
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
