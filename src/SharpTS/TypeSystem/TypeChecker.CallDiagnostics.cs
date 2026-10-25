using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // Once the enclosing argument is known to be incompatible, prefer the assignment
    // diagnostic for a written callback property. Retained objects and direct callbacks
    // keep their argument-level diagnostic; this never supplies a contextual expected type
    // in place of the callback's checked, explicitly annotated type.
    private TypeCheckException? GetNestedCallbackPropertyError(Expr expression, TypeInfo expected)
    {
        expression = UngroupGenericTupleExpression(expression);
        if (expression is not Expr.ObjectLiteral literal || _typeMap.Get(expression) is not TypeInfo.Record actual)
            return null;
        foreach (var property in literal.Properties)
        {
            if (property.IsSpread || property.Key is null ||
                property.Kind is not (Expr.ObjectPropertyKind.Value or Expr.ObjectPropertyKind.Method)) continue;
            string name = GetPropertyKeyNameForTypeCheck(property.Key);
            TypeInfo? member = GetContextualMemberType(expected, name);
            if (member is null || !actual.Fields.TryGetValue(name, out var actualMember)) continue;
            bool method = actual.IsMethodMember(name) || (expected switch
            {
                TypeInfo.Record record => record.IsMethodMember(name),
                TypeInfo.Interface iface => iface.IsMethodMember(name),
                _ => false
            });
            if (IsMemberCompatible(member, actualMember, method)) continue;
            if (GetNestedCallbackPropertyError(property.Value, member) is { } nested) return nested;
            if (GetContextualFunctionType(member) is null || GetContextualFunctionType(actualMember) is null) continue;
            Token? token = property.Key switch
            {
                Expr.IdentifierKey key => key.Name,
                Expr.LiteralKey key => key.Literal,
                _ => null
            };
            var position = token is { Start: >= 0 } && CurrentSourceDocument is { } document
                ? document.Lines.ToPosition(token.Start) : ((int Line, int Column)?)null;
            return new TypeCheckException($"Type '{actualMember}' is not assignable to type '{member}'.",
                position?.Line ?? token?.Line ?? TryGetExprLine(property.Value), position?.Column, tsCode: "TS2322");
        }
        return null;
    }

    private TypeCheckException? GetCallCallbackPropertyError(Expr.Call call, TypeInfo.Function signature,
        IReadOnlyList<TypeInfo> argumentTypes)
    {
        int regular = signature.ParamTypes.Count - (signature.HasRestParam ? 1 : 0);
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            if (call.Arguments[i] is Expr.Spread) return null;
            TypeInfo? expected = i < regular ? signature.ParamTypes[i] :
                signature.HasRestParam && signature.ParamTypes.LastOrDefault() is TypeInfo.Array array ? array.ElementType : null;
            if (expected is null || IsArgumentCompatible(expected, argumentTypes[i], i < regular && i >= signature.MinArity)) continue;
            return GetNestedCallbackPropertyError(call.Arguments[i], expected);
        }
        return null;
    }
}
