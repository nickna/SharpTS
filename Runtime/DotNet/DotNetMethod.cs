using System.Reflection;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Bound callable for a .NET instance or static method group.
/// Holds the overload set, a bound receiver (null for static), and an optional
/// overload hint from <c>@DotNetOverload</c>. Overload resolution happens on each
/// call against the actual runtime argument types.
/// </summary>
internal sealed class DotNetMethod : ISharpTSCallable
{
    private readonly MethodInfo[] _overloads;
    private readonly object? _receiver; // null for static methods
    private readonly string? _overloadHint;

    public DotNetMethod(MethodInfo[] overloads, object? receiver, string jsName, string? overloadHint)
    {
        _overloads = overloads;
        _receiver = receiver;
        _overloadHint = overloadHint;
    }

    public int Arity()
    {
        int min = int.MaxValue;
        foreach (var m in _overloads)
        {
            var ps = m.GetParameters();
            int required = ps.Count(p => !p.HasDefaultValue &&
                !p.IsDefined(typeof(ParamArrayAttribute), false));
            if (required < min) min = required;
        }
        return min == int.MaxValue ? 0 : min;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var candidate = DotNetMethodResolver.ResolveMethod(_overloads, arguments, _overloadHint);
        var method = (MethodInfo)candidate.Method;
        var parameters = method.GetParameters();

        object?[] invokeArgs = BuildInvokeArgs(parameters, arguments, candidate, interpreter);

        return DotNetInstance.InvokeWithMapping(() =>
        {
            var result = method.Invoke(_receiver, invokeArgs);
            return DotNetMarshaller.WrapReturn(result, method.ReturnType);
        });
    }

    /// <summary>
    /// Marshals TS arguments into a .NET argument array matching the resolved parameter list,
    /// honoring params-array semantics and default values. The interpreter reference is
    /// forwarded to the marshaller so TS callables can be wrapped in delegate shims.
    /// </summary>
    internal static object?[] BuildInvokeArgs(
        ParameterInfo[] parameters,
        IReadOnlyList<object?> arguments,
        RuntimeMethodCandidate candidate,
        Interpreter interpreter)
    {
        if (candidate.ParamsStartIndex < 0)
        {
            var result = new object?[parameters.Length];
            for (int i = 0; i < arguments.Count; i++)
            {
                result[i] = DotNetMarshaller.Convert(arguments[i], parameters[i].ParameterType, interpreter);
            }
            for (int i = arguments.Count; i < parameters.Length; i++)
            {
                result[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }
            return result;
        }

        // params-array case: pack trailing args into an array of the element type
        int fixedCount = candidate.ParamsStartIndex;
        var paramsParam = parameters[^1];
        var elementType = paramsParam.ParameterType.GetElementType()!;
        int variadicCount = arguments.Count - fixedCount;

        var result2 = new object?[parameters.Length];
        for (int i = 0; i < fixedCount; i++)
        {
            result2[i] = DotNetMarshaller.Convert(arguments[i], parameters[i].ParameterType, interpreter);
        }

        var variadic = Array.CreateInstance(elementType, Math.Max(0, variadicCount));
        for (int i = 0; i < variadicCount; i++)
        {
            variadic.SetValue(DotNetMarshaller.Convert(arguments[fixedCount + i], elementType, interpreter), i);
        }
        result2[^1] = variadic;
        return result2;
    }
}
