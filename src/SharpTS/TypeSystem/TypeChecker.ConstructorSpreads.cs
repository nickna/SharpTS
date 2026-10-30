using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private List<TypeInfo> CheckConstructorInferenceArguments(IReadOnlyList<Expr> arguments)
    {
        if (!arguments.Any(argument => argument is Expr.Spread))
            return arguments.Select(CheckExpr).ToList();
        var types = CheckGenericInferenceArguments(arguments);
        var slots = GenericTupleArguments(arguments, types, null);
        // Finite tuple spreads supply separate inference sites. Retain the
        // existing aggregate inference policy for optional/open-ended spreads.
        return slots.Any(slot => slot.Optional || slot.Spread) ? types : slots.Select(slot => slot.Type).ToList();
    }

    private void ValidateSpreadConstructorCall(TypeInfo? constructor, Expr.New expression, string name,
        Dictionary<string, TypeInfo>? substitutions)
    {
        var types = CheckGenericInferenceArguments(expression.Arguments);
        int? line = TryGetExprLine(expression.Callee);
        var arguments = GenericTupleArguments(expression.Arguments, types, line);
        if (constructor is null)
        {
            CheckTupleCallArguments(line, ConstructorTupleSignature([], 0, false), arguments);
            _editorInvocationAttempt?.SelectImplicitConstructor();
            return;
        }
        var candidates = constructor is TypeInfo.OverloadedFunction overload
            ? overload.Signatures : [(TypeInfo.Function)constructor];
        foreach (var candidate in candidates)
        {
            var parameters = candidate.ParamTypes.Select(type => ResolveConstructorParameter(
                substitutions is null ? type : SubstituteConstructorMember(type, substitutions))).ToList();
            var tupleSignature = ConstructorTupleSignature(parameters, candidate.MinArity, candidate.HasRestParam);
            RecordEditorConstructorInstantiation(candidate, parameters, candidate.MinArity, candidate.HasRestParam,
                candidate.ThisType, candidate.ParamNames);
            if (constructor is TypeInfo.OverloadedFunction)
            {
                try { CheckTupleCallArguments(line, tupleSignature, arguments, contextualizeCallbacks: false); }
                catch (TypeCheckException) { continue; }
            }
            // Contextual callback facts belong only to the selected signature.
            CheckTupleCallArguments(line, tupleSignature, arguments);
            RecordEditorInvocationSelection(candidate);
            return;
        }
        throw new TypeCheckException($"No constructor overload matches the call for '{name}'.", line, tsCode: "TS2769");
    }

    private static TypeInfo.Function ConstructorTupleSignature(List<TypeInfo> parameters, int minArity, bool hasRest)
    {
        if (hasRest && parameters.LastOrDefault() is TypeInfo.Tuple)
            return new(parameters, TypeInfo.Void.Shared, minArity, HasRestParam: true);
        var elements = parameters.Select((type, index) => new TypeInfo.TupleElement(type,
            hasRest && index == parameters.Count - 1 ? TupleElementKind.Spread :
            index >= minArity ? TupleElementKind.Optional : TupleElementKind.Required)).ToList();
        return new([new TypeInfo.Tuple(elements, minArity)], TypeInfo.Void.Shared, HasRestParam: true);
    }
}
