using System.Numerics;
using System.Reflection;
using SharpTS.Declaration;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Cost used to rank overload candidates at runtime. Values mirror
/// <c>SharpTS.Compilation.ConversionCost</c> so interpreter and compiled mode
/// agree on which overload is "best" for a given argument shape.
/// </summary>
internal enum RuntimeConversionCost
{
    Exact = 0,
    Lossless = 1,
    Narrowing = 5,
    ObjectFallback = 10,
    Incompatible = int.MaxValue
}

internal readonly record struct RuntimeMethodCandidate(
    MethodBase Method,
    int TotalCost,
    int ParamsStartIndex,
    bool UsesParamsExpanded
);

/// <summary>
/// Runtime overload resolution for .NET method calls from TypeScript.
/// </summary>
/// <remarks>
/// Ranks candidates by actual argument runtime types (not static TS types).
/// Supports <c>@DotNetOverload</c> hints to disambiguate when runtime types
/// alone aren't enough (e.g., a single overload must be forced among many
/// that all accept <c>double</c>).
/// </remarks>
internal static class DotNetMethodResolver
{
    public static RuntimeMethodCandidate ResolveMethod(
        MethodInfo[] candidates,
        IReadOnlyList<object?> arguments,
        string? overloadHint = null,
        IReadOnlyList<Type>? genericTypeArguments = null,
        IReadOnlyList<Type?>? argumentTypeHints = null,
        Type? expectedReturnType = null)
    {
        var argumentTypes = arguments.Select(GetRuntimeArgumentType).ToArray();
        if (argumentTypeHints != null)
        {
            for (int i = 0; i < argumentTypes.Length &&
                            i < argumentTypeHints.Count; i++)
            {
                argumentTypes[i] = argumentTypeHints[i] ?? argumentTypes[i];
            }
        }
        candidates = DotNetGenericMethodInference.CloseCandidates(
            candidates, argumentTypes,
            genericTypeArguments, expectedReturnType);
        return Resolve(candidates.Cast<MethodBase>().ToArray(), arguments, overloadHint, allowByRef: true);
    }

    public static RuntimeMethodCandidate ResolveConstructor(
        ConstructorInfo[] candidates,
        IReadOnlyList<object?> arguments,
        string? overloadHint = null)
    {
        return Resolve(candidates.Cast<MethodBase>().ToArray(), arguments, overloadHint, allowByRef: false);
    }

    private static RuntimeMethodCandidate Resolve(
        MethodBase[] candidates,
        IReadOnlyList<object?> arguments,
        string? overloadHint,
        bool allowByRef)
    {
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("No candidates provided to overload resolution.");
        }

        // Apply @DotNetOverload hint filter — narrow to matching signatures before scoring.
        IEnumerable<MethodBase> pool = candidates;
        if (!string.IsNullOrWhiteSpace(overloadHint))
        {
            pool = FilterByHint(candidates, overloadHint!);
        }

        var scored = new List<RuntimeMethodCandidate>();
        foreach (var method in pool)
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
            var inputParameters = GetInputParameters(parameters);
            bool hasParams = parameters.Length > 0 &&
                             parameters[^1].IsDefined(typeof(ParamArrayAttribute), false);

            if (hasParams)
            {
                var candidate = ScoreWithParams(method, inputParameters, arguments);
                if (candidate.TotalCost < (int)RuntimeConversionCost.Incompatible)
                    scored.Add(candidate);
            }
            else
            {
                if (arguments.Count > inputParameters.Length) continue;

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
                    if (!allOptional) continue;
                }

                var candidate = ScoreRegular(method, inputParameters, arguments);
                if (candidate.TotalCost < (int)RuntimeConversionCost.Incompatible)
                    scored.Add(candidate);
            }
        }

        if (scored.Count == 0)
        {
            throw new InvalidOperationException(
                $"No compatible overload found for '{candidates[0].Name}' with {arguments.Count} argument(s).");
        }

        scored.Sort(static (a, b) =>
        {
            int costCompare = a.TotalCost.CompareTo(b.TotalCost);
            if (costCompare != 0) return costCompare;

            // Prefer non-params over params
            if (a.ParamsStartIndex < 0 && b.ParamsStartIndex >= 0) return -1;
            if (a.ParamsStartIndex >= 0 && b.ParamsStartIndex < 0) return 1;

            // Prefer more specific (smaller) parameter types
            return GetMethodTypeSize(a.Method).CompareTo(GetMethodTypeSize(b.Method));
        });
        return scored[0];
    }

    private static RuntimeMethodCandidate ScoreRegular(
        MethodBase method, ParameterInfo[] parameters, IReadOnlyList<object?> arguments)
    {
        int totalCost = 0;
        for (int i = 0; i < arguments.Count; i++)
        {
            var cost = ScoreConversion(arguments[i], GetInputType(parameters[i]));
            if (cost == RuntimeConversionCost.Incompatible)
                return new RuntimeMethodCandidate(method, (int)RuntimeConversionCost.Incompatible, -1, false);
            totalCost += (int)cost;
        }
        return new RuntimeMethodCandidate(method, totalCost, -1, false);
    }

    private static RuntimeMethodCandidate ScoreWithParams(
        MethodBase method, ParameterInfo[] parameters, IReadOnlyList<object?> arguments)
    {
        int regularParamCount = parameters.Length - 1;
        var paramsParam = parameters[^1];

        if (arguments.Count < regularParamCount)
            return new RuntimeMethodCandidate(method, (int)RuntimeConversionCost.Incompatible, -1, false);

        int totalCost = 0;
        for (int i = 0; i < regularParamCount; i++)
        {
            var cost = ScoreConversion(arguments[i], GetInputType(parameters[i]));
            if (cost == RuntimeConversionCost.Incompatible)
                return new RuntimeMethodCandidate(method, (int)RuntimeConversionCost.Incompatible, -1, false);
            totalCost += (int)cost;
        }

        var elementType = paramsParam.ParameterType.GetElementType()!;
        for (int i = regularParamCount; i < arguments.Count; i++)
        {
            var cost = ScoreConversion(arguments[i], elementType);
            if (cost == RuntimeConversionCost.Incompatible)
                return new RuntimeMethodCandidate(method, (int)RuntimeConversionCost.Incompatible, -1, false);
            totalCost += (int)cost;
        }
        return new RuntimeMethodCandidate(method, totalCost, regularParamCount, true);
    }

    /// <summary>
    /// TypeScript-visible inputs for a CLR parameter list. Pure <c>out</c> slots are supplied
    /// by the bridge; <c>ref</c> and <c>in</c> slots consume ordinary input arguments.
    /// </summary>
    internal static ParameterInfo[] GetInputParameters(ParameterInfo[] parameters) =>
        parameters.Where(static p => !(p.ParameterType.IsByRef && p.IsOut)).ToArray();

    internal static Type GetInputType(ParameterInfo parameter) =>
        parameter.ParameterType.IsByRef
            ? parameter.ParameterType.GetElementType()!
            : parameter.ParameterType;

    private static Type? GetRuntimeArgumentType(object? argument)
    {
        if (argument is null or SharpTSUndefined)
            return null;
        if (argument is SharpTSArray array)
        {
            Type? elementType = null;
            foreach (var element in array)
            {
                Type? current = GetRuntimeArgumentType(element);
                if (current == null)
                    continue;
                if (elementType == null)
                {
                    elementType = current;
                }
                else if (elementType != current)
                {
                    return typeof(object[]);
                }
            }
            return (elementType ?? typeof(object)).MakeArrayType();
        }
        return argument is DotNetInstance instance
            ? instance.Type
            : argument.GetType();
    }

    private static RuntimeConversionCost ScoreConversion(object? arg, Type target)
    {
        if (arg is SharpTSUndefined) arg = null;

        // null → reference type / Nullable<T>
        if (arg == null)
        {
            if (!target.IsValueType) return RuntimeConversionCost.Exact;
            if (Nullable.GetUnderlyingType(target) != null) return RuntimeConversionCost.Exact;
            return RuntimeConversionCost.Incompatible;
        }

        // Object target accepts everything
        if (target == typeof(object)) return RuntimeConversionCost.ObjectFallback;

        var nonNullable = Nullable.GetUnderlyingType(target) ?? target;

        // Number (TS number is double at runtime)
        if (arg is double or int or long or float or short or byte or sbyte or ushort or uint or ulong)
        {
            if (nonNullable == typeof(double)) return RuntimeConversionCost.Exact;
            if (nonNullable == typeof(float)) return RuntimeConversionCost.Lossless;
            if (nonNullable == typeof(int) || nonNullable == typeof(long) ||
                nonNullable == typeof(short) || nonNullable == typeof(byte) ||
                nonNullable == typeof(sbyte) || nonNullable == typeof(ushort) ||
                nonNullable == typeof(uint) || nonNullable == typeof(ulong) ||
                nonNullable == typeof(char) || nonNullable == typeof(decimal))
            {
                return RuntimeConversionCost.Narrowing;
            }
            return RuntimeConversionCost.Incompatible;
        }

        if (arg is bool)
        {
            if (nonNullable == typeof(bool)) return RuntimeConversionCost.Exact;
            return RuntimeConversionCost.Incompatible;
        }

        if (arg is string)
        {
            if (nonNullable == typeof(string)) return RuntimeConversionCost.Exact;
            if (nonNullable == typeof(char)) return RuntimeConversionCost.Narrowing;
            return RuntimeConversionCost.Incompatible;
        }

        if (arg is BigInteger)
        {
            if (nonNullable == typeof(BigInteger)) return RuntimeConversionCost.Exact;
            if (nonNullable == typeof(long) || nonNullable == typeof(int) || nonNullable == typeof(double))
                return RuntimeConversionCost.Narrowing;
            return RuntimeConversionCost.Incompatible;
        }

        if (arg is DotNetInstance dni)
        {
            if (target.IsInstanceOfType(dni.Underlying)) return RuntimeConversionCost.Exact;
            return RuntimeConversionCost.Incompatible;
        }

        if (arg is SharpTSArray && target.IsArray)
            return RuntimeConversionCost.Lossless;

        // TS callable → .NET delegate: lossless (shim built at invoke time).
        // Ranks above `object` so an `Action` overload wins over an `object` overload.
        if (arg is ISharpTSCallable && typeof(Delegate).IsAssignableFrom(target))
        {
            return RuntimeConversionCost.Lossless;
        }

        // Anything else: accept via object fallback if assignable
        return target.IsInstanceOfType(arg)
            ? RuntimeConversionCost.Lossless
            : RuntimeConversionCost.ObjectFallback;
    }

    private static int GetMethodTypeSize(MethodBase method)
    {
        int total = 0;
        foreach (var p in method.GetParameters())
        {
            total += GetTypeSize(p.ParameterType);
        }
        return total;
    }

    private static int GetTypeSize(Type t)
    {
        if (t == typeof(byte) || t == typeof(sbyte)) return 1;
        if (t == typeof(short) || t == typeof(ushort) || t == typeof(char)) return 2;
        if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) return 4;
        if (t == typeof(long) || t == typeof(ulong) || t == typeof(double)) return 8;
        if (t == typeof(decimal)) return 16;
        if (t == typeof(string)) return 32;
        if (t == typeof(bool)) return 1;
        return 1000;
    }

    /// <summary>
    /// Filters a candidate method/constructor list by a <c>@DotNetOverload</c> hint.
    /// Shared between the interpreter (<see cref="DotNetMethodResolver"/>) and the
    /// compiler (<see cref="SharpTS.Compilation.ExternalMethodResolver"/>) so both
    /// honor the hint identically. Throws <see cref="InvalidOperationException"/>
    /// when the hint matches no candidate.
    /// </summary>
    internal static MethodBase[] FilterByHint(MethodBase[] candidates, string hint)
    {
        if (candidates.Length == 0) return candidates;
        var expected = ParseOverloadHint(hint);
        var filtered = candidates.Where(m => MatchesHint(m, expected)).ToArray();
        if (filtered.Length == 0)
        {
            throw new InvalidOperationException(
                $"@DotNetOverload hint '{hint}' did not match any candidate of '{candidates[0].Name}'.");
        }
        return filtered;
    }

    /// <summary>
    /// Parses a <c>@DotNetOverload</c> hint string (e.g. <c>"int, string"</c>) into
    /// a sequence of target .NET types. Accepts C# aliases and a few common System.* names.
    /// </summary>
    internal static Type[] ParseOverloadHint(string hint)
    {
        var parts = hint.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new Type[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            result[i] = ResolveHintType(parts[i]);
        }
        return result;
    }

    private static Type ResolveHintType(string name) => name.ToLowerInvariant() switch
    {
        "int" or "int32" or "system.int32" => typeof(int),
        "long" or "int64" or "system.int64" => typeof(long),
        "short" or "int16" or "system.int16" => typeof(short),
        "byte" or "system.byte" => typeof(byte),
        "sbyte" or "system.sbyte" => typeof(sbyte),
        "uint" or "uint32" or "system.uint32" => typeof(uint),
        "ulong" or "uint64" or "system.uint64" => typeof(ulong),
        "ushort" or "uint16" or "system.uint16" => typeof(ushort),
        "float" or "single" or "system.single" => typeof(float),
        "double" or "system.double" => typeof(double),
        "decimal" or "system.decimal" => typeof(decimal),
        "bool" or "boolean" or "system.boolean" => typeof(bool),
        "char" or "system.char" => typeof(char),
        "string" or "system.string" => typeof(string),
        "object" or "system.object" => typeof(object),
        _ => DotNetTypeRegistry.Resolve(name)
             ?? throw new InvalidOperationException($"Unknown type '{name}' in @DotNetOverload hint.")
    };

    private static bool MatchesHint(MethodBase method, Type[] expected)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != expected.Length) return false;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType != expected[i]) return false;
        }
        return true;
    }
}
