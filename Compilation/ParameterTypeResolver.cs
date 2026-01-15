using SharpTS.Parsing;
using SharpTS.TypeSystem;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Centralized parameter and return type resolution from TypeMap.
/// Provides typed .NET types instead of defaulting to object.
/// </summary>
public static class ParameterTypeResolver
{
    /// <summary>
    /// Resolves parameter types from TypeMap function type info.
    /// Falls back to object if type info is not available.
    /// </summary>
    /// <param name="parameters">Parameters from AST</param>
    /// <param name="typeMapper">TypeMapper for converting TypeInfo to .NET Type</param>
    /// <param name="funcType">Function type from TypeMap (may be null)</param>
    /// <returns>Array of .NET types for each parameter</returns>
    public static Type[] ResolveParameters(
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TSTypeInfo.Function? funcType)
    {
        if (funcType == null || funcType.ParamTypes.Count != parameters.Count)
        {
            // Fallback: try to resolve from parameter type annotations
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();
        }

        // Map each parameter type, but use 'object' for:
        // 1. Optional parameters without explicit defaults (preserves null-checking)
        // 2. BigInteger parameters (operations expect boxed values)
        return funcType.ParamTypes
            .Select((pt, i) =>
            {
                var mappedType = typeMapper.MapTypeInfoStrict(pt);

                // BigInteger parameters need to stay as object because BigInt operations
                // in the emitter expect boxed values
                if (mappedType == typeof(System.Numerics.BigInteger))
                {
                    return typeof(object);
                }

                // If parameter is optional (no explicit default) and would be a value type,
                // use object instead so null can be passed
                if (i < parameters.Count &&
                    parameters[i].DefaultValue == null &&
                    parameters[i].IsOptional)
                {
                    if (mappedType.IsValueType)
                    {
                        return typeof(object);
                    }
                }

                return mappedType;
            })
            .ToArray();
    }

    /// <summary>
    /// Resolves a single parameter's type from its annotation or defaults to object.
    /// </summary>
    private static Type ResolveParameterType(Stmt.Parameter param, TypeMapper typeMapper)
    {
        if (param.Type == null)
            return typeof(object);

        // Parse the type annotation and map to .NET type
        var typeInfo = ParseTypeAnnotation(param.Type);
        return typeMapper.MapTypeInfoStrict(typeInfo);
    }

    /// <summary>
    /// Resolves the return type for a function.
    /// </summary>
    /// <param name="returnTypeInfo">Return type from TypeMap (may be null)</param>
    /// <param name="isAsync">Whether this is an async function</param>
    /// <param name="typeMapper">TypeMapper for conversion</param>
    /// <returns>.NET type for the return value</returns>
    public static Type ResolveReturnType(
        TSTypeInfo? returnTypeInfo,
        bool isAsync,
        TypeMapper typeMapper)
    {
        Type baseType;

        if (returnTypeInfo == null)
        {
            baseType = typeof(object);
        }
        else
        {
            baseType = typeMapper.MapTypeInfoStrict(returnTypeInfo);
        }

        // Wrap async return types in Task<T>
        if (isAsync)
        {
            if (baseType == typeof(void))
                return typeof(Task);
            return typeof(Task<>).MakeGenericType(baseType);
        }

        return baseType;
    }

    /// <summary>
    /// Resolves parameter types for a class method.
    /// </summary>
    public static Type[] ResolveMethodParameters(
        string className,
        string methodName,
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        if (typeMap == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        var classType = typeMap.GetClassType(className);
        if (classType == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        TSTypeInfo.Function? funcType = null;

        // Check instance methods
        if (classType.Methods.TryGetValue(methodName, out var methodType))
        {
            funcType = ExtractFunctionType(methodType);
        }
        // Check static methods
        else if (classType.StaticMethods.TryGetValue(methodName, out var staticMethodType))
        {
            funcType = ExtractFunctionType(staticMethodType);
        }

        if (funcType == null || funcType.ParamTypes.Count != parameters.Count)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        // Map each parameter type, handling optional parameters and BigInteger
        return funcType.ParamTypes
            .Select((pt, i) =>
            {
                Type mappedType;
                try
                {
                    mappedType = typeMapper.MapTypeInfoStrict(pt);
                }
                catch
                {
                    // Union types may throw during early method definition phase
                    // when TypeBuilder isn't finalized yet. Fall back to object.
                    return typeof(object);
                }

                // BigInteger parameters need to stay as object because BigInt operations
                // in the emitter expect boxed values
                if (mappedType == typeof(System.Numerics.BigInteger))
                {
                    return typeof(object);
                }

                // If parameter is optional (no explicit default) and would be a value type,
                // use object instead so null can be passed
                if (i < parameters.Count &&
                    parameters[i].DefaultValue == null &&
                    parameters[i].IsOptional)
                {
                    if (mappedType.IsValueType)
                    {
                        return typeof(object);
                    }
                }

                return mappedType;
            })
            .ToArray();
    }

    /// <summary>
    /// Resolves return type for a class method.
    /// </summary>
    public static Type ResolveMethodReturnType(
        string className,
        string methodName,
        bool isAsync,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        if (typeMap == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        var classType = typeMap.GetClassType(className);
        if (classType == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        TSTypeInfo.Function? funcType = null;

        // Check instance methods
        if (classType.Methods.TryGetValue(methodName, out var methodType))
        {
            funcType = ExtractFunctionType(methodType);
        }
        // Check static methods
        else if (classType.StaticMethods.TryGetValue(methodName, out var staticMethodType))
        {
            funcType = ExtractFunctionType(staticMethodType);
        }

        if (funcType == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        return ResolveReturnType(funcType.ReturnType, isAsync, typeMapper);
    }

    /// <summary>
    /// Resolves constructor parameter types for a class.
    /// </summary>
    public static Type[] ResolveConstructorParameters(
        string className,
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        if (typeMap == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        var classType = typeMap.GetClassType(className);
        if (classType == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        if (!classType.Methods.TryGetValue("constructor", out var ctorTypeInfo))
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        var funcType = ExtractFunctionType(ctorTypeInfo);
        if (funcType == null || funcType.ParamTypes.Count != parameters.Count)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        // Map each parameter type, handling optional parameters and BigInteger
        return funcType.ParamTypes
            .Select((pt, i) =>
            {
                Type mappedType;
                try
                {
                    mappedType = typeMapper.MapTypeInfoStrict(pt);
                }
                catch (NotSupportedException)
                {
                    // Union types may throw during early definition phase
                    return typeof(object);
                }

                // BigInteger parameters need to stay as object
                if (mappedType == typeof(System.Numerics.BigInteger))
                {
                    return typeof(object);
                }

                // Optional parameters without defaults should use object for null-checking
                if (i < parameters.Count &&
                    parameters[i].DefaultValue == null &&
                    parameters[i].IsOptional)
                {
                    if (mappedType.IsValueType)
                    {
                        return typeof(object);
                    }
                }

                return mappedType;
            })
            .ToArray();
    }

    /// <summary>
    /// Extracts parameter types from a method TypeInfo.
    /// </summary>
    private static Type[] ExtractParameterTypes(TSTypeInfo methodType, int paramCount, TypeMapper typeMapper)
    {
        var funcType = ExtractFunctionType(methodType);
        if (funcType == null || funcType.ParamTypes.Count != paramCount)
            return Enumerable.Repeat(typeof(object), paramCount).ToArray();

        return funcType.ParamTypes
            .Select(pt => typeMapper.MapTypeInfoStrict(pt))
            .ToArray();
    }

    /// <summary>
    /// Extracts Function type from a method TypeInfo (handles overloads).
    /// </summary>
    private static TSTypeInfo.Function? ExtractFunctionType(TSTypeInfo methodType)
    {
        return methodType switch
        {
            TSTypeInfo.Function f => f,
            TSTypeInfo.OverloadedFunction of => of.Implementation,
            _ => null
        };
    }

    /// <summary>
    /// Parses a type annotation string into TypeInfo.
    /// Delegates to centralized PrimitiveTypeMappings for consistency.
    /// </summary>
    private static TSTypeInfo ParseTypeAnnotation(string typeAnnotation) =>
        PrimitiveTypeMappings.ParseAnnotation(typeAnnotation);
}
