using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // A diagnostic scope receipt, not a resolved-type cache. Plain blocks and function
    // bodies do not pre-register their local types, so retain their forward-name policy.
    private readonly HashSet<TypeEnvironment> _completedTypeDeclarationScopes =
        new(ReferenceEqualityComparer.Instance);

    // Exact syntax whose already-performed resolution recognizes a type (including
    // intrinsic generics with a legitimate Any result). Used only to avoid false diagnostics.
    private readonly HashSet<NamedTypeNode> _resolvedAnnotationTypeNames =
        new(ReferenceEqualityComparer.Instance);

    private void ReportUnknownTypeArgument(TypeNode? annotation)
    {
        if (annotation is null || _suppressDiagnostics != 0 ||
            _currentFunctionReturnType is not null ||
            !_completedTypeDeclarationScopes.Contains(_environment))
            return;
        if (FindUnknownTypeArgument(annotation, [], insideArgument: false) is { } unknown)
            throw new TypeCheckException($" Cannot find name '{unknown.Name}'.", unknown.Line, tsCode: "TS2304");
    }

    private NamedTypeNode? FindUnknownTypeArgument(TypeNode node, HashSet<string> bound, bool insideArgument)
    {
        // Conditional/infer ownership is handled by the existing conditional checker.
        if (node is ConditionalTypeNode or InferTypeNode) return null;
        if (node is GenericFunctionTypeNode function)
            return FindUnknownSignatureArgument(function.TypeParameters, function.Body, bound, insideArgument);
        if (node is GenericConstructorTypeNode constructor)
            return FindUnknownSignatureArgument(constructor.TypeParameters, constructor.Body, bound, insideArgument);
        if (node is MappedTypeNode mapped)
        {
            if (FindUnknownTypeArgument(mapped.Constraint, bound, insideArgument) is { } constraint)
                return constraint;
            var mappedBound = new HashSet<string>(bound, StringComparer.Ordinal) { mapped.ParamName };
            return FindUnknownTypeArgument(mapped.ValueType, mappedBound, insideArgument) ??
                (mapped.AsClause is { } remap ? FindUnknownTypeArgument(remap, mappedBound, insideArgument) : null);
        }
        if (node is NamedTypeNode named)
        {
            if ((insideArgument || named.TypeArguments is { Count: > 0 }) &&
                !_resolvedAnnotationTypeNames.Contains(named) &&
                IsSimpleIdentifier(named.Name) && named.Name != "this" &&
                FindUnknownTypeReference(named with { TypeArguments = null }, bound) is not null)
                return named;
            if (named.TypeArguments is { } arguments)
                foreach (var argument in arguments)
                    if (FindUnknownTypeArgument(argument, bound, insideArgument: true) is { } unknown)
                        return unknown;
            return null;
        }
        foreach (var child in TypeNodeChildren(node))
            if (FindUnknownTypeArgument(child, bound, insideArgument) is { } unknown)
                return unknown;
        return null;
    }

    private NamedTypeNode? FindUnknownSignatureArgument(List<TypeParam> parameters, TypeNode body,
        HashSet<string> outerBound, bool insideArgument)
    {
        var bound = new HashSet<string>(outerBound, StringComparer.Ordinal);
        foreach (var parameter in parameters) bound.Add(parameter.Name.Lexeme);
        foreach (var parameter in parameters)
        {
            if (parameter.ConstraintNode is { } constraint &&
                FindUnknownTypeArgument(constraint, bound, insideArgument) is { } unknownConstraint)
                return unknownConstraint;
            if (parameter.DefaultNode is { } defaultType &&
                FindUnknownTypeArgument(defaultType, bound, insideArgument) is { } unknownDefault)
                return unknownDefault;
        }
        return FindUnknownTypeArgument(body, bound, insideArgument);
    }
}
