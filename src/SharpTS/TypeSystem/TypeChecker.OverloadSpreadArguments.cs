using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private List<GenericTupleCallSlot>? FixedOverloadSpreadArguments(Expr.Call call,
        IReadOnlyList<TypeInfo> types, IReadOnlyList<TypeInfo> parameters, bool hasRest, bool publish = false)
    {
        if (!call.Arguments.Any(argument => argument is Expr.Spread)) return null;
        var slots = GenericTupleCallArguments(call, types);
        // Open array/variadic spreads retain the existing matching path. Only
        // completed fixed slots provide positional evidence for this path.
        if (slots.Any(slot => slot.Spread)) return null;
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].Expression is not { } expression) continue;
            var (context, isConst) = FreshCallParameterContext(parameters, hasRest, index);
            if (context is not null)
                slots[index] = slots[index] with
                {
                    Type = ContextualFreshCallArgument(expression, slots[index].Type, context, isConst, publish)
                };
        }
        return slots;
    }

    private bool? MatchFixedOverloadSpread(Expr.Call call, TypeInfo.Function signature,
        List<TypeInfo> types, bool subtype = false)
    {
        if (HasTupleRestParameter(signature)) return null;
        var slots = FixedOverloadSpreadArguments(call, types, signature.ParamTypes, signature.HasRestParam);
        if (slots is null) return null;
        var parameters = signature.ParamTypes.Select((type, index) => new TypeInfo.TupleElement(type,
            signature.HasRestParam && index == signature.ParamTypes.Count - 1 ? TupleElementKind.Spread :
            index >= signature.MinArity ? TupleElementKind.Optional : TupleElementKind.Required)).ToList();
        var tuple = new TypeInfo.Tuple(parameters, parameters.Count(parameter => parameter.IsRequired));
        var positional = new TypeInfo.Function([tuple], signature.ReturnType, 0, true);
        try
        {
            CheckGenericTupleRestCall(call, positional, slots, contextualizeCallbacks: false,
                subtypeMatch: subtype, reportCallbackProperties: false, preferArgumentTypes: true);
            return true;
        }
        catch (TypeCheckException)
        {
            return false;
        }
    }

    private List<TypeInfo> PrepareOverloadInferenceArguments(Expr.Call call,
        TypeInfo.GenericFunction signature, List<TypeInfo> types)
    {
        var slots = FixedOverloadSpreadArguments(call, types, signature.ParamTypes, signature.HasRestParam);
        if (slots is null) return PrepareFreshCallArgumentsForInference(signature, call.Arguments, types);
        int regular = signature.HasRestParam ? signature.ParamTypes.Count - 1 : signature.ParamTypes.Count;
        bool widenRest = signature.HasRestParam && signature.ParamTypes.LastOrDefault() is
            TypeInfo.TypeParameter { IsConst: false, Constraint: TypeInfo.Array { ElementType: TypeInfo.Any or TypeInfo.Unknown } };
        return slots.Select((slot, index) =>
        {
            TypeInfo type = slot.Optional ? CreateUnion(slot.Type, TypeInfo.Undefined.Shared) : slot.Type;
            if (slot.Expression is not { } expression) return type;
            if (widenRest && index >= regular) return WidenClassFieldInitializerType(expression, type);
            TypeInfo? parameter = index < regular ? signature.ParamTypes[index] :
                signature.HasRestParam && signature.ParamTypes.Count > 0 ? signature.ParamTypes[^1] : null;
            return parameter is null ? WidenLiteralType(type) :
                PrepareFreshCallArgumentForInference(parameter, expression, type);
        }).ToList();
    }

    private bool PublishFixedOverloadSpread(Expr.Call call, TypeInfo.Function signature, List<TypeInfo> types)
    {
        if (HasTupleRestParameter(signature)) return false;
        var slots = FixedOverloadSpreadArguments(call, types, signature.ParamTypes, signature.HasRestParam, publish: true);
        if (slots is null) return false;
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].Expression is not { } expression) continue;
            var (context, _) = FreshCallParameterContext(signature.ParamTypes, signature.HasRestParam, index);
            if (context is null) continue;
            ContextualizeNestedGenericCallback(expression, context);
            RefreshContextualizedGenericArgument(expression, slots[index].Type);
            if (expression is Expr.ObjectLiteral && slots[index].Type is TypeInfo.Record fresh && HasNamedSymbolMembers(context))
                CheckExcessProperties(fresh, context, expression);
        }
        return true;
    }
}
