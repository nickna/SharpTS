using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record GenericTupleCallSlot(TypeInfo Type, bool Optional = false, bool Spread = false, Expr? Expression = null);
    private readonly record struct GenericTupleMatchState(int Parameter, int Argument);

    private static bool HasTupleRestParameter(TypeInfo.Function signature) =>
        signature.HasRestParam && signature.ParamTypes.LastOrDefault() is TypeInfo.Tuple;

    private bool TupleRestArgsSubtypeMatch(Expr.Call call, TypeInfo.Function signature, List<TypeInfo> argumentTypes)
    {
        try
        {
            CheckGenericTupleRestCall(call, signature, GenericTupleCallArguments(call, argumentTypes),
                contextualizeCallbacks: false, subtypeMatch: true, preferArgumentTypes: true);
            return true;
        }
        catch (TypeCheckException)
        {
            return false;
        }
    }

    private bool TryNormalizeCallableObject(TypeInfo callee, out TypeInfo callable,
        out IReadOnlyDictionary<TypeInfo, TypeInfo>? originalSignatures)
    {
        List<TypeInfo.CallSignature>? originals = callee switch
        {
            TypeInfo.Interface iface => CallableObjectSignatures(iface),
            TypeInfo.Record record => record.CallSignatures,
            TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface generic } => CallableObjectSignatures(generic),
            _ => null
        };
        callable = null!;
        originalSignatures = null;
        if (originals is not { Count: > 0 })
            return false;

        var signatures = callee is TypeInfo.InstantiatedGeneric instantiated
            ? originals.Select(signature =>
            {
                var substitutions = GenericInterfaceSubs((TypeInfo.GenericInterface)instantiated.GenericDefinition, instantiated.TypeArguments)
                    .Where(pair => signature.TypeParams is null || signature.TypeParams.All(parameter => parameter.Name != pair.Key)).ToDictionary();
                return CopyEditorSignatureMetadata(signature, signature with
                {
                    ParamTypes = signature.ParamTypes.Select(type => SubstitutePreservingSignatures(type, substitutions)).ToList(),
                    ReturnType = SubstitutePreservingSignatures(signature.ReturnType, substitutions)
                });
            }).ToList() : originals;
        List<TypeInfo> functions = [];
        Dictionary<TypeInfo, TypeInfo> sourceSignatures = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            if (signature.IsGeneric && callee is TypeInfo.InstantiatedGeneric instance)
            {
                var outer = GenericInterfaceSubs((TypeInfo.GenericInterface)instance.GenericDefinition, instance.TypeArguments)
                    .Where(pair => signature.TypeParams!.All(parameter => parameter.Name != pair.Key)).ToDictionary();
                signature = signature with
                {
                    TypeParams = signature.TypeParams!.Select(parameter => parameter with
                    {
                        Constraint = parameter.Constraint is null ? null : SubstitutePreservingSignatures(parameter.Constraint, outer),
                        Default = parameter.Default is null ? null : SubstitutePreservingSignatures(parameter.Default, outer)
                    }).ToList()
                };
            }
            TypeInfo function = signature.IsGeneric
                ? new TypeInfo.GenericFunction(signature.TypeParams!, signature.ParamTypes, signature.ReturnType,
                    signature.RequiredParams, signature.HasRestParam, ParamNames: signature.ParamNames)
                : new TypeInfo.Function(signature.ParamTypes, signature.ReturnType, signature.RequiredParams,
                    signature.HasRestParam, ParamNames: signature.ParamNames);
            CopyEditorSignatureMetadata(originals[i], function);
            RecordEditorInvocationInstantiation(originals[i], function);
            functions.Add(function);
            sourceSignatures[function] = originals[i];
        }
        originalSignatures = sourceSignatures;
        callable = functions.Count == 1 ? functions[0] : new TypeInfo.OverloadSet(functions);
        return true;
    }

    private void InvalidateTupleCallArgumentNarrowings(IReadOnlyList<Expr> arguments)
    {
        foreach (var argument in arguments)
        {
            if (_typeMap.Get(argument) is not { } type || !IsObjectType(type) || GetNarrowingPath(argument) is not { } path)
                continue;
            InvalidatePropertiesForFunctionArg(path);
            if (path is Narrowing.NarrowingPath.Variable variable) _escapeAnalyzer.MarkEscaped(variable.Name);
        }
    }

    private static bool GenericTupleCallArityCouldMatch(TypeInfo.Function signature, IReadOnlyList<GenericTupleCallSlot> arguments)
    {
        int minimum = signature.MinArity;
        int? maximum = signature.HasRestParam ? null : signature.ParamTypes.Count;
        if (signature.HasRestParam && signature.ParamTypes.LastOrDefault() is TypeInfo.Tuple tuple)
        {
            int prefix = signature.ParamTypes.Count - 1;
            minimum = tuple.RequiredCount > 0 ? prefix + tuple.RequiredCount : minimum;
            maximum = tuple.HasSpread || tuple.RestElementType is not null ? null : prefix + tuple.Elements.Count;
        }
        int fixedArguments = arguments.Count(argument => !argument.Spread);
        return (arguments.Any(argument => argument.Spread) || fixedArguments >= minimum) &&
            (maximum is null || fixedArguments <= maximum);
    }

    private List<TypeInfo> PrepareGenericTupleInferenceArguments(TypeInfo.GenericFunction generic,
        IReadOnlyList<GenericTupleCallSlot> slots) => slots.Select((slot, position) =>
            position < generic.ParamTypes.Count - 1 && slot.Expression is { } expression
                ? PrepareFreshCallArgumentForInference(generic.ParamTypes[position], expression, slot.Type)
                : slot.Type).ToList();

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
        {
            signature = FlattenInstantiatedInterface(instantiated)!.CallSignatures![0];
            var outer = GenericInterfaceSubs((TypeInfo.GenericInterface)instantiated.GenericDefinition, instantiated.TypeArguments)
                .Where(pair => signature.TypeParams!.All(parameter => parameter.Name != pair.Key)).ToDictionary();
            signature = signature with
            {
                TypeParams = signature.TypeParams!.Select(parameter => parameter with
                {
                    Constraint = parameter.Constraint is null ? null : SubstitutePreservingSignatures(parameter.Constraint, outer),
                    Default = parameter.Default is null ? null : SubstitutePreservingSignatures(parameter.Default, outer)
                }).ToList()
            };
        }
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
            CheckGenericTupleRestCall(call, signature, slots, validate: false,
                reportCallbackProperties: call.TypeArgs is { Count: > 0 });
            argumentTypes = RefreshContextualizedGenericArguments(call.Arguments, argumentTypes);
            slots = GenericTupleCallArguments(call, argumentTypes);
            try
            {
                signature = (TypeInfo.Function)InstantiateGenericFunction(generic, InferTypeArguments(generic,
                    PrepareGenericTupleInferenceArguments(generic, slots),
                    contextualResult, tupleRestArguments: GenericTupleCallType(
                        slots.Skip(generic.ParamTypes.Count - 1), (TypeInfo.Tuple)generic.ParamTypes[^1])));
            }
            catch (TypeCheckException error) when (error.Diagnostic.TsCode == "TS2344")
            {
                throw new TypeCheckException(error.Message, call.Paren.Line, tsCode: "TS2345");
            }
        }
        CheckGenericTupleRestCall(call, signature, slots, reportCallbackProperties: call.TypeArgs is { Count: > 0 });
        ContextualFreshCallArguments(call.Arguments,
            RefreshContextualizedGenericArguments(call.Arguments, argumentTypes), signature.ParamTypes, signature.HasRestParam, publish: true);
        return signature;
    }

    private List<GenericTupleCallSlot> GenericTupleCallArguments(Expr.Call call, IReadOnlyList<TypeInfo> types) =>
        GenericTupleArguments(call.Arguments, types, call.Paren.Line);

    private List<GenericTupleCallSlot> GenericTupleArguments(IReadOnlyList<Expr> arguments, IReadOnlyList<TypeInfo> types, int? line)
    {
        List<GenericTupleCallSlot> result = [];
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] is Expr.Spread spread) AppendSpread(spread.Expression, types[i]);
            else result.Add(new(types[i], Expression: arguments[i]));
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
                throw new TypeCheckException($"Spread argument must be an iterable type, got '{type}'.", line, tsCode: "TS2488");
            result.Add(new(elementType, Spread: true));
        }
    }

    private TypeInfo.Tuple GenericTupleCallType(IEnumerable<GenericTupleCallSlot> slots, TypeInfo.Tuple? pattern = null)
    {
        var elements = slots.Select((slot, i) => new TypeInfo.TupleElement(
            slot.Spread ? new TypeInfo.Array(slot.Type) :
                pattern is not null && i < pattern.Elements.Count && pattern.Elements[i].Type is TypeInfo.Tuple &&
                slot.Type is not TypeInfo.Tuple &&
                slot.Expression is { } expression && UngroupGenericTupleExpression(expression) is Expr.ArrayLiteral literal
                    ? GenericTupleLiteralType(literal) : slot.Type,
            slot.Spread ? TupleElementKind.Spread : slot.Optional ? TupleElementKind.Optional : TupleElementKind.Required)).ToList();
        return new(elements, elements.Count(element => element.IsRequired));
    }

    private static Expr UngroupGenericTupleExpression(Expr expression)
    {
        while (expression is Expr.Grouping grouping) expression = grouping.Expression;
        return expression;
    }

    private List<GenericTupleCallSlot> GenericTupleLiteralSlots(Expr.ArrayLiteral literal) =>
        GenericTupleArguments(literal.Elements, literal.Elements.Select(element =>
            _typeMap.Get(element is Expr.Spread spread ? spread.Expression : element) ?? TypeInfo.Any.Shared).ToList(),
            TryGetExprLine(literal));

    private TypeInfo.Tuple GenericTupleLiteralType(Expr.ArrayLiteral literal) => GenericTupleCallType(
        GenericTupleLiteralSlots(literal).Select(slot =>
            slot.Expression is { } expression && UngroupGenericTupleExpression(expression) is Expr.ArrayLiteral nested
                ? slot with { Type = GenericTupleLiteralType(nested) } : slot));

    private void CheckGenericTupleRestCall(Expr.Call call, TypeInfo.Function signature, List<GenericTupleCallSlot> arguments,
        bool validate = true, bool contextualizeCallbacks = true, bool subtypeMatch = false,
        bool reportCallbackProperties = true, bool preferArgumentTypes = false)
        => CheckTupleCallArguments(call.Paren.Line, signature, arguments, validate, contextualizeCallbacks,
            subtypeMatch, reportCallbackProperties, preferArgumentTypes);

    private void CheckTupleCallArguments(int? line, TypeInfo.Function signature, List<GenericTupleCallSlot> arguments,
        bool validate = true, bool contextualizeCallbacks = true, bool subtypeMatch = false,
        bool reportCallbackProperties = true, bool preferArgumentTypes = false)
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
                line, tsCode: "TS2554");

        if (!Match(contextualizeCallbacks, out var contexts))
        {
            if (reportCallbackProperties && Match(true, out var failedContexts)) CheckPropertyDiagnostics(failedContexts);
            Fail();
        }
        if (!contextualizeCallbacks) return;
        foreach (var (index, expected) in contexts)
            if (arguments[index].Expression is { } expression)
            {
                if (expected is TypeInfo.Tuple expectedTuple && UngroupGenericTupleExpression(expression) is Expr.ArrayLiteral literal)
                {
                    if (!literal.Elements.Any(element => element is Expr.Spread))
                        CheckArrayLiteralAgainstTuple(literal, expectedTuple, "argument");
                    else
                    {
                        try
                        {
                            CheckTupleCallArguments(line, new TypeInfo.Function([expectedTuple], TypeInfo.Void.Shared,
                                HasRestParam: true), GenericTupleLiteralSlots(literal), reportCallbackProperties: reportCallbackProperties);
                        }
                        catch (TypeCheckException error) when (error.Diagnostic.TsCode is "TS2554" or "TS2556")
                        {
                            throw new TypeCheckException(error.Message, line, tsCode: "TS2345");
                        }
                    }
                }
                else ContextualizeNestedGenericCallback(expression, expected);
                RefreshContextualizedGenericArgument(expression, arguments[index].Type);
            }
        if (!validate) return;
        if (!Match(false, out _))
        {
            if (reportCallbackProperties) CheckPropertyDiagnostics(contexts);
            Fail();
        }

        void CheckPropertyDiagnostics(Dictionary<int, TypeInfo> expectedTypes)
        {
            foreach (var (index, expected) in expectedTypes.OrderBy(pair => pair.Key))
                if (arguments[index].Expression is { } expression && GetNestedCallbackPropertyError(expression, expected) is { } error)
                    throw error;
        }

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
                    if (argument != arguments.Count) continue;
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
                Add(new(parameter + 1, argument + 1), state, argument, expected.Type);
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
            if (contextualize && expected.Type is TypeInfo.Tuple && actual.Expression is { } literalExpression &&
                UngroupGenericTupleExpression(literalExpression) is Expr.ArrayLiteral) return true;
            TypeInfo type = preferArgumentTypes || actual.Expression is null ? actual.Type : ActualType(actual.Expression, actual.Type);
            if (!(preferArgumentTypes && type is TypeInfo.Tuple) &&
                expected.Type is TypeInfo.Tuple && actual.Expression is { } arrayExpression &&
                UngroupGenericTupleExpression(arrayExpression) is Expr.ArrayLiteral literal)
                type = GenericTupleLiteralType(literal);
            if (actual.Optional) type = CreateUnion(type, TypeInfo.Undefined.Shared);
            if (subtypeMatch && type is TypeInfo.Any && expected.Type is not (TypeInfo.Any or TypeInfo.Unknown)) return false;
            return IsArgumentCompatible(expected.Type, type, expected.Optional);
        }

        TypeInfo ActualType(Expr expression, TypeInfo fallback) => expression is Expr.Grouping grouping
            ? ActualType(grouping.Expression, fallback) : _typeMap.Get(expression) ?? fallback;

        void Fail() => throw new TypeCheckException(
            $"Arguments '{GenericTupleCallType(arguments)}' are not assignable to tuple parameters '{tuple}'.",
            line, tsCode: arguments.FindIndex(argument => argument.Spread) is var open && open >= 0 &&
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
