using System.Reflection;
using SharpTS.Runtime.DotNet;

namespace SharpTS.Compilation;

/// <summary>Closes original metadata members using only the owning loader's types.</summary>
internal sealed class ExternalGenericMethodSelection(ExternalMetadataTypes types)
{
    internal MethodInfo[] CloseCandidates(IEnumerable<MethodInfo> methods, IReadOnlyList<Type?> arguments,
        IReadOnlyList<Type>? explicitArguments, Type? expectedReturn)
    {
        var result = new List<MethodInfo>();
        foreach (var method in methods)
        {
            if (!method.IsGenericMethodDefinition || !types.Owns(method.Module.Assembly))
            {
                if (DotNetGenericMethodInference.TryClose(method, arguments, explicitArguments, expectedReturn) is { } ordinary)
                    result.Add(ordinary);
                continue;
            }
            if (!TryTypes(arguments, out var inputs) ||
                (explicitArguments is not null && !TryTypes(explicitArguments, out _))) continue;
            Type[]? supplied = explicitArguments is null ? null : explicitArguments.Select(type => {
                types.TrySelectionType(type, out var selected); return selected;
            }).ToArray();
            Type? expected = null;
            if (expectedReturn is not null && !types.TrySelectionType(expectedReturn, out expected)) continue;
            try
            {
                var closed = DotNetGenericMethodInference.TryClose(method, inputs, supplied, expected, types.RuntimeAtom);
                if (closed is not null && SatisfiesConstraints(closed)) result.Add(closed);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or TypeLoadException)
            {
                // An unprovable metadata instantiation cannot authorize a saved call.
            }
        }
        return result.ToArray();
    }

    private bool TryTypes(IReadOnlyList<Type?> values, out Type?[] selected)
    {
        selected = new Type?[values.Count];
        for (int i = 0; i < values.Count; i++)
            if (values[i] is { } value && !types.TrySelectionType(value, out selected[i]!)) return false;
        return true;
    }

    // MetadataLoadContext.MakeGenericMethod checks context and arity, but does not
    // validate constraints. Keep runtime-style rejection before writing a MethodSpec.
    private bool SatisfiesConstraints(MethodInfo closed)
    {
        var definition = closed.GetGenericMethodDefinition();
        var parameters = definition.GetGenericArguments();
        var arguments = closed.GetGenericArguments();
        var substitutions = parameters.Zip(arguments).ToDictionary(pair => pair.First, pair => pair.Second);
        if (closed.DeclaringType is { IsConstructedGenericType: true } owner)
            foreach (var (parameter, argument) in owner.GetGenericTypeDefinition().GetGenericArguments().Zip(owner.GetGenericArguments()))
                substitutions.TryAdd(parameter, argument);
        for (int i = 0; i < parameters.Length; i++)
        {
            var actual = arguments[i];
            if (actual.ContainsGenericParameters || actual.IsPointer || actual.IsByRef || actual.IsByRefLike ||
                types.RuntimeAtom(actual) == typeof(void)) return false;
            var flags = parameters[i].GenericParameterAttributes;
            if ((flags & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && actual.IsValueType) return false;
            if ((flags & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 &&
                (!actual.IsValueType || types.IsNullable(actual))) return false;
            if ((flags & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !actual.IsValueType &&
                (actual.IsAbstract || ManagedDotNetInterop.GetConstructor(actual, Type.EmptyTypes) is null)) return false;
            foreach (var constraint in parameters[i].GetGenericParameterConstraints())
                if (Substitute(constraint, substitutions) is not { } bound || !bound.IsAssignableFrom(actual)) return false;
        }
        return true;
    }

    private Type? Substitute(Type value, Dictionary<Type, Type> substitutions)
    {
        if (!value.ContainsGenericParameters) return value;
        if (value.IsGenericParameter) return substitutions.GetValueOrDefault(value);
        if (value.HasElementType)
        {
            if (Substitute(value.GetElementType()!, substitutions) is not { } element) return null;
            if (value.IsByRef) return element.MakeByRefType();
            if (value.IsPointer) return element.MakePointerType();
            return types.ArrayShape(element, value.IsSZArray, value.GetArrayRank());
        }
        if (!value.IsConstructedGenericType) return null;
        var arguments = value.GetGenericArguments().Select(argument => Substitute(argument, substitutions)).ToArray();
        return arguments.Any(argument => argument is null) ? null :
            ManagedDotNetInterop.MakeGenericType(value.GetGenericTypeDefinition(), arguments!);
    }
}
