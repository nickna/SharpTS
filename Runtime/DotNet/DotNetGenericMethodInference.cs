using System.Reflection;

namespace SharpTS.Runtime.DotNet;

/// <summary>Closes generic CLR methods by unifying parameter shapes with known argument types.</summary>
internal static class DotNetGenericMethodInference
{
    internal static MethodInfo[] CloseCandidates(
        IEnumerable<MethodInfo> methods,
        IReadOnlyList<Type?> argumentTypes,
        IReadOnlyList<Type>? explicitTypeArguments = null,
        Type? expectedReturnType = null)
    {
        var closed = new List<MethodInfo>();
        foreach (var method in methods)
        {
            var candidate = TryClose(
                method, argumentTypes,
                explicitTypeArguments, expectedReturnType);
            if (candidate != null)
                closed.Add(candidate);
        }
        return closed.ToArray();
    }

    internal static MethodInfo? TryClose(
        MethodInfo method,
        IReadOnlyList<Type?> argumentTypes,
        IReadOnlyList<Type>? explicitTypeArguments = null,
        Type? expectedReturnType = null)
    {
        if (!method.IsGenericMethodDefinition)
        {
            return explicitTypeArguments is { Count: > 0 } || method.ContainsGenericParameters
                ? null
                : method;
        }

        var genericArguments = method.GetGenericArguments();
        if (explicitTypeArguments is { Count: > 0 })
        {
            if (explicitTypeArguments.Count != genericArguments.Length)
                return null;
            try
            {
                return method.MakeGenericMethod(explicitTypeArguments.ToArray());
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        var parameters = DotNetMethodResolver.GetInputParameters(method.GetParameters());
        bool hasParams = parameters.Length > 0 &&
                         parameters[^1].IsDefined(typeof(ParamArrayAttribute), false);
        int fixedCount = hasParams ? parameters.Length - 1 : parameters.Length;
        if ((!hasParams && argumentTypes.Count > parameters.Length) ||
            (hasParams && argumentTypes.Count < fixedCount))
            return null;

        var inferred = new Dictionary<Type, Type>();
        int regularCount = Math.Min(argumentTypes.Count, fixedCount);
        for (int i = 0; i < regularCount; i++)
        {
            Type? actual = argumentTypes[i];
            if (actual == null)
                continue;
            if (!TryUnify(DotNetMethodResolver.GetInputType(parameters[i]), actual, inferred))
                return null;
        }

        if (hasParams)
        {
            Type elementType = DotNetMethodResolver.GetInputType(parameters[^1]).GetElementType()!;
            for (int i = fixedCount; i < argumentTypes.Count; i++)
            {
                Type? actual = argumentTypes[i];
                if (actual != null && !TryUnify(elementType, actual, inferred))
                    return null;
            }
        }

        if (expectedReturnType != null &&
            !TryUnify(method.ReturnType, expectedReturnType, inferred))
        {
            return null;
        }

        if (genericArguments.Any(parameter => !inferred.ContainsKey(parameter)))
            return null;

        try
        {
            return method.MakeGenericMethod(
                genericArguments.Select(parameter => inferred[parameter]).ToArray());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryUnify(Type pattern, Type actual, Dictionary<Type, Type> inferred)
    {
        if (pattern.IsByRef)
            pattern = pattern.GetElementType()!;
        if (actual.IsByRef)
            actual = actual.GetElementType()!;

        if (pattern.IsGenericParameter)
        {
            if (!inferred.TryGetValue(pattern, out var existing))
            {
                inferred[pattern] = actual;
                return true;
            }
            return existing == actual ||
                   existing.IsAssignableFrom(actual) ||
                   actual.IsAssignableFrom(existing);
        }

        if (pattern.IsArray)
        {
            return actual.IsArray &&
                   TryUnify(pattern.GetElementType()!, actual.GetElementType()!, inferred);
        }

        if (typeof(Delegate).IsAssignableFrom(pattern) &&
            typeof(Delegate).IsAssignableFrom(actual) &&
            pattern.GetMethod("Invoke") is { } patternInvoke &&
            actual.GetMethod("Invoke") is { } actualInvoke)
        {
            var patternParameters = patternInvoke.GetParameters();
            var actualParameters = actualInvoke.GetParameters();
            if (patternParameters.Length != actualParameters.Length)
                return false;
            for (int i = 0; i < patternParameters.Length; i++)
            {
                if (!TryUnify(
                        patternParameters[i].ParameterType,
                        actualParameters[i].ParameterType,
                        inferred))
                    return false;
            }
            return TryUnify(
                patternInvoke.ReturnType, actualInvoke.ReturnType, inferred);
        }

        if (pattern.IsGenericType)
        {
            Type definition = pattern.GetGenericTypeDefinition();
            Type? matchingActual = FindConstructedType(actual, definition);
            if (matchingActual == null)
                return false;

            var patternArguments = pattern.GetGenericArguments();
            var actualArguments = matchingActual.GetGenericArguments();
            for (int i = 0; i < patternArguments.Length; i++)
            {
                if (!TryUnify(patternArguments[i], actualArguments[i], inferred))
                    return false;
            }
            return true;
        }

        return pattern.IsAssignableFrom(actual) || NumericTypesAreBridgeCompatible(pattern, actual);
    }

    private static Type? FindConstructedType(Type actual, Type genericDefinition)
    {
        if (actual.IsGenericType && actual.GetGenericTypeDefinition() == genericDefinition)
            return actual;

        foreach (var interfaceType in actual.GetInterfaces())
        {
            if (interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == genericDefinition)
            {
                return interfaceType;
            }
        }

        for (Type? current = actual.BaseType; current != null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == genericDefinition)
            {
                return current;
            }
        }

        return null;
    }

    private static bool NumericTypesAreBridgeCompatible(Type left, Type right) =>
        IsNumeric(left) && IsNumeric(right);

    private static bool IsNumeric(Type type) =>
        type == typeof(double) || type == typeof(float) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(decimal);
}
