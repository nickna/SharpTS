using System.Reflection;
using SharpTS.Declaration;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.DotNet;
using SharpTS.TypeSystem;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Represents the cost of converting a TypeScript type to a .NET parameter type.
/// </summary>
public enum ConversionCost
{
    /// <summary>Exact match - no conversion needed (number to double, bool to bool, string to string).</summary>
    Exact = 0,

    /// <summary>Lossless widening conversion (number to float is lossless in range).</summary>
    Lossless = 1,

    /// <summary>Narrowing conversion that may lose precision (number to int, number to byte).</summary>
    Narrowing = 5,

    /// <summary>Boxing to object fallback (any to object).</summary>
    ObjectFallback = 10,

    /// <summary>Incompatible types - cannot convert.</summary>
    Incompatible = int.MaxValue
}

/// <summary>
/// Tracks a method candidate with its total conversion cost and parameter details.
/// </summary>
/// <param name="Method">The resolved method or constructor.</param>
/// <param name="TotalCost">Sum of all parameter conversion costs.</param>
/// <param name="ParamsStartIndex">Index where params array starts, or -1 if no params.</param>
/// <param name="UsesParamsExpanded">True if arguments are passed to params individually.</param>
public readonly record struct MethodCandidate(
    MethodBase Method,
    int TotalCost,
    int ParamsStartIndex,
    bool UsesParamsExpanded
);

/// <summary>
/// Resolves .NET method overloads based on TypeScript argument types.
/// Uses a cost-based scoring system to select the best matching overload.
/// </summary>
public class ExternalMethodResolver(TypeMap? typeMap, TypeProvider types)
{
    private readonly TypeMap? _typeMap = typeMap;
    private readonly TypeProvider _types = types;

    /// <summary>
    /// Resolves the best matching method overload for the given arguments.
    /// </summary>
    /// <param name="methods">Candidate methods to choose from.</param>
    /// <param name="arguments">TypeScript expressions being passed as arguments.</param>
    /// <param name="overloadHint">Optional <c>@DotNetOverload</c> signature string; when
    /// supplied, narrows candidates to those whose parameter types match the hint
    /// before cost-based scoring.</param>
    /// <returns>The best matching method candidate.</returns>
    /// <exception cref="Exception">When no compatible overload is found.</exception>
    public MethodCandidate ResolveMethod(MethodInfo[] methods, List<Expr> arguments, string? overloadHint = null)
    {
        return Resolve(methods.Cast<MethodBase>().ToArray(), arguments, overloadHint, allowByRef: true);
    }

    /// <summary>
    /// Resolves the best matching constructor overload for the given arguments.
    /// </summary>
    /// <param name="constructors">Candidate constructors to choose from.</param>
    /// <param name="arguments">TypeScript expressions being passed as arguments.</param>
    /// <param name="overloadHint">Optional <c>@DotNetOverload</c> signature string; see
    /// <see cref="ResolveMethod"/>.</param>
    /// <returns>The best matching constructor candidate.</returns>
    /// <exception cref="Exception">When no compatible overload is found.</exception>
    public MethodCandidate ResolveConstructor(ConstructorInfo[] constructors, List<Expr> arguments, string? overloadHint = null)
    {
        return Resolve(constructors.Cast<MethodBase>().ToArray(), arguments, overloadHint, allowByRef: false);
    }

    private MethodCandidate Resolve(
        MethodBase[] candidates, List<Expr> arguments, string? overloadHint, bool allowByRef)
    {
        // Apply @DotNetOverload hint filter — narrow to matching signatures before scoring.
        if (!string.IsNullOrWhiteSpace(overloadHint))
        {
            try
            {
                candidates = DotNetMethodResolver.FilterByHint(candidates, overloadHint!);
            }
            catch (InvalidOperationException ex)
            {
                throw new CompileException(ex.Message);
            }
        }

        var scored = new List<MethodCandidate>();

        foreach (var method in candidates)
        {
            if (method is MethodInfo methodInfo &&
                DotNetInteropClassifier.UnsupportedMethodReason(methodInfo) != null)
            {
                continue;
            }
            if (method is ConstructorInfo constructor &&
                DotNetInteropClassifier.UnsupportedConstructorReason(constructor) != null)
            {
                continue;
            }
            var parameters = method.GetParameters();
            if (!allowByRef && parameters.Any(p => p.ParameterType.IsByRef))
                continue;
            var inputParameters = DotNetMethodResolver.GetInputParameters(parameters);
            bool hasParams = parameters.Length > 0 &&
                             parameters[^1].IsDefined(typeof(ParamArrayAttribute), false);

            if (hasParams)
            {
                var candidate = ScoreWithParams(method, inputParameters, arguments);
                if (candidate.TotalCost < (int)ConversionCost.Incompatible)
                    scored.Add(candidate);
            }
            else
            {
                // Non-params: argument count must match exactly (or handle optional params)
                if (arguments.Count > inputParameters.Length)
                    continue;

                // Check if missing arguments have default values
                if (arguments.Count < inputParameters.Length)
                {
                    bool allOptional = true;
                    for (int i = arguments.Count; i < inputParameters.Length; i++)
                    {
                        if (!inputParameters[i].HasDefaultValue)
                        {
                            allOptional = false;
                            break;
                        }
                    }
                    if (!allOptional)
                        continue;
                }

                var candidate = ScoreRegular(method, inputParameters, arguments);
                if (candidate.TotalCost < (int)ConversionCost.Incompatible)
                    scored.Add(candidate);
            }
        }

        if (scored.Count == 0)
        {
            throw new CompileException($"No compatible overload found for {candidates[0].Name} with {arguments.Count} argument(s)");
        }

        // Sort by: total cost, then prefer non-params, then prefer smaller parameter types
        scored.Sort((a, b) =>
        {
            int costCompare = a.TotalCost.CompareTo(b.TotalCost);
            if (costCompare != 0) return costCompare;

            // Prefer non-params over params
            if (a.ParamsStartIndex < 0 && b.ParamsStartIndex >= 0) return -1;
            if (a.ParamsStartIndex >= 0 && b.ParamsStartIndex < 0) return 1;

            // Prefer smaller types (type specificity)
            return GetMethodTypeSize(a.Method).CompareTo(GetMethodTypeSize(b.Method));
        });

        return scored[0];
    }

    private MethodCandidate ScoreRegular(MethodBase method, ParameterInfo[] parameters, List<Expr> arguments)
    {
        int totalCost = 0;

        for (int i = 0; i < arguments.Count; i++)
        {
            var tsType = _typeMap?.TryGet(arguments[i], out var t) == true ? t : null;
            var cost = ScoreTypeConversion(tsType, DotNetMethodResolver.GetInputType(parameters[i]));

            if (cost == ConversionCost.Incompatible)
                return new MethodCandidate(method, (int)ConversionCost.Incompatible, -1, false);

            totalCost += (int)cost;
        }

        return new MethodCandidate(method, totalCost, -1, false);
    }

    private MethodCandidate ScoreWithParams(MethodBase method, ParameterInfo[] parameters, List<Expr> arguments)
    {
        int regularParamCount = parameters.Length - 1; // All except the params array
        var paramsParam = parameters[^1];

        // Must have at least the required non-params arguments
        if (arguments.Count < regularParamCount)
            return new MethodCandidate(method, (int)ConversionCost.Incompatible, -1, false);

        int totalCost = 0;

        // Score regular parameters
        for (int i = 0; i < regularParamCount; i++)
        {
            var tsType = _typeMap?.TryGet(arguments[i], out var t) == true ? t : null;
            var cost = ScoreTypeConversion(tsType, DotNetMethodResolver.GetInputType(parameters[i]));

            if (cost == ConversionCost.Incompatible)
                return new MethodCandidate(method, (int)ConversionCost.Incompatible, -1, false);

            totalCost += (int)cost;
        }

        // Score variadic arguments against the element type of the params array
        var elementType = paramsParam.ParameterType.GetElementType()!;
        for (int i = regularParamCount; i < arguments.Count; i++)
        {
            var tsType = _typeMap?.TryGet(arguments[i], out var t) == true ? t : null;
            var cost = ScoreTypeConversion(tsType, elementType);

            if (cost == ConversionCost.Incompatible)
                return new MethodCandidate(method, (int)ConversionCost.Incompatible, -1, false);

            totalCost += (int)cost;
        }

        return new MethodCandidate(method, totalCost, regularParamCount, true);
    }

    /// <summary>
    /// Calculates the conversion cost from a TypeScript type to a .NET parameter type.
    /// </summary>
    private ConversionCost ScoreTypeConversion(TSTypeInfo? tsType, Type targetType)
    {
        // Null/unknown TypeScript type - fallback behavior
        if (tsType == null)
        {
            // If target is object, it's always safe
            if (targetType == _types.Object || targetType == typeof(object))
                return ConversionCost.ObjectFallback;
            // Otherwise treat as compatible but costly
            return ConversionCost.Narrowing;
        }

        // Object target accepts everything
        if (targetType == _types.Object || targetType == typeof(object))
            return ConversionCost.ObjectFallback;

        // Non-null values convert against Nullable<T>'s underlying type. Keep the original
        // target around for the dedicated null branch below.
        Type effectiveTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // TypeScript number -> .NET numeric types
        if (IsNumberType(tsType))
        {
            if (effectiveTarget == _types.Double || effectiveTarget == typeof(double))
                return ConversionCost.Exact;

            if (effectiveTarget == typeof(float) || effectiveTarget.FullName == "System.Single")
                return ConversionCost.Lossless;

            // Integer types are narrowing conversions
            if (effectiveTarget == _types.Int32 || effectiveTarget == typeof(int) ||
                effectiveTarget == _types.Int64 || effectiveTarget == typeof(long) ||
                effectiveTarget == _types.Byte || effectiveTarget == typeof(byte) ||
                effectiveTarget == _types.Char || effectiveTarget == typeof(char) ||
                effectiveTarget.FullName == "System.Int16" ||
                effectiveTarget.FullName == "System.SByte" ||
                effectiveTarget.FullName == "System.UInt16" ||
                effectiveTarget.FullName == "System.UInt32" ||
                effectiveTarget.FullName == "System.UInt64" ||
                effectiveTarget.FullName == "System.Decimal")
            {
                return ConversionCost.Narrowing;
            }

            // Number to boolean/string is incompatible
            if (effectiveTarget == _types.Boolean || effectiveTarget == typeof(bool) ||
                effectiveTarget == _types.String || effectiveTarget == typeof(string))
            {
                return ConversionCost.Incompatible;
            }
        }

        // TypeScript boolean -> .NET bool
        if (IsBooleanType(tsType))
        {
            if (effectiveTarget == _types.Boolean || effectiveTarget == typeof(bool))
                return ConversionCost.Exact;

            // Boolean to number/string is incompatible
            if (IsNumericTarget(effectiveTarget) ||
                effectiveTarget == _types.String || effectiveTarget == typeof(string))
            {
                return ConversionCost.Incompatible;
            }
        }

        // TypeScript string -> .NET string
        if (IsStringType(tsType))
        {
            if (effectiveTarget == _types.String || effectiveTarget == typeof(string))
                return ConversionCost.Exact;

            // String to number/bool is incompatible
            if (IsNumericTarget(effectiveTarget) ||
                effectiveTarget == _types.Boolean || effectiveTarget == typeof(bool))
            {
                return ConversionCost.Incompatible;
            }

            // String to char - allowed as narrowing (first char)
            if (effectiveTarget == _types.Char || effectiveTarget == typeof(char))
                return ConversionCost.Narrowing;
        }

        // TypeScript function -> .NET delegate: built as a runtime shim via
        // DotNetDelegateShim.CreateForTSFunction. Lossless so delegate-typed overloads
        // beat `object` overloads for the same call.
        if (tsType is TSTypeInfo.Function && typeof(Delegate).IsAssignableFrom(targetType))
            return ConversionCost.Lossless;

        // Guest arrays are materialized element-by-element when crossing into a CLR
        // array parameter. Rank that path above object fallback.
        if (tsType is TSTypeInfo.Array or TSTypeInfo.Tuple && targetType.IsArray)
            return ConversionCost.Lossless;

        // TypeScript any/unknown -> object fallback
        if (tsType is TSTypeInfo.Any or TSTypeInfo.Unknown)
        {
            // any/unknown still need delegate routing when the target is Delegate — the
            // runtime value will be a $TSFunction, so the shim can bind it. Cost is
            // ObjectFallback to defer to more specific Function-typed candidates.
            if (typeof(Delegate).IsAssignableFrom(targetType))
                return ConversionCost.ObjectFallback;
            return ConversionCost.ObjectFallback;
        }

        // TypeScript null -> reference types or Nullable<T>
        if (tsType is TSTypeInfo.Null)
        {
            if (!targetType.IsValueType)
                return ConversionCost.Exact;

            // Handle Nullable<T> - null IS compatible
            if (PrimitiveTypeMappings.IsNullableValueType(targetType))
                return ConversionCost.Exact;

            // Non-nullable value type - truly incompatible
            return ConversionCost.Incompatible;
        }

        // Default: allow with object fallback cost
        return ConversionCost.ObjectFallback;
    }

    private static bool IsNumberType(TSTypeInfo? type) => type switch
    {
        TSTypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => true,
        TSTypeInfo.NumberLiteral => true,
        _ => false
    };

    private static bool IsBooleanType(TSTypeInfo? type) => type switch
    {
        TSTypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => true,
        TSTypeInfo.BooleanLiteral => true,
        _ => false
    };

    private static bool IsStringType(TSTypeInfo? type) => type switch
    {
        TSTypeInfo.String => true,        // `string` is always TypeInfo.String (#1108)
        TSTypeInfo.StringLiteral => true,
        _ => false
    };

    private bool IsNumericTarget(Type targetType) =>
        targetType == _types.Double || targetType == typeof(double) ||
        targetType == _types.Int32 || targetType == typeof(int) ||
        targetType == _types.Int64 || targetType == typeof(long) ||
        targetType == _types.Byte || targetType == typeof(byte) ||
        targetType.FullName == "System.Single" ||
        targetType.FullName == "System.Int16" ||
        targetType.FullName == "System.SByte" ||
        targetType.FullName == "System.UInt16" ||
        targetType.FullName == "System.UInt32" ||
        targetType.FullName == "System.UInt64" ||
        targetType.FullName == "System.Decimal";

    /// <summary>
    /// Gets the aggregate "type size" of a method's parameters for tie-breaking.
    /// Smaller total = more specific = preferred.
    /// </summary>
    private static int GetMethodTypeSize(MethodBase method)
    {
        int total = 0;
        foreach (var param in method.GetParameters())
        {
            total += GetTypeSize(param.ParameterType);
        }
        return total;
    }

    /// <summary>
    /// Gets the size ranking of a type for tie-breaking purposes.
    /// Smaller types are preferred (int over long, float over double).
    /// </summary>
    private static int GetTypeSize(Type t)
    {
        // Lower = more specific = preferred
        if (t == typeof(byte) || t == typeof(sbyte)) return 1;
        if (t == typeof(short) || t == typeof(ushort) || t == typeof(char)) return 2;
        if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) return 4;
        if (t == typeof(long) || t == typeof(ulong) || t == typeof(double)) return 8;
        if (t == typeof(decimal)) return 16;
        if (t == typeof(string)) return 32;  // String is specific
        if (t == typeof(bool)) return 1;     // Bool is specific
        return 1000; // object or other - least specific
    }
}
