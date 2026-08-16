using System.Reflection;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>Callable bound to a CLR receiver and the current module's extension containers.</summary>
internal sealed class DotNetExtensionMethod(
    DotNetInstance receiver,
    IReadOnlyList<Type> containers,
    string memberName) : ISharpTSCallable
{
    public int Arity()
    {
        var methods = DotNetExtensionMethodResolver.GetReceiverClosedCandidates(
            containers, memberName, receiver.Type);
        int minimum = int.MaxValue;
        foreach (var method in methods)
        {
            int required = method.GetParameters()
                .Skip(1)
                .Count(p => !p.IsOptional &&
                            !p.IsOut &&
                            !p.IsDefined(typeof(ParamArrayAttribute), false));
            minimum = Math.Min(minimum, required);
        }
        return minimum == int.MaxValue ? 0 : minimum;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var allArguments = new List<object?>(arguments.Count + 1) { receiver };
        allArguments.AddRange(arguments);
        var argumentTypes = allArguments.Select(RuntimeArgumentType).ToArray();
        MethodInfo[] methods = DotNetExtensionMethodResolver.GetClosedCandidates(
            containers, memberName, argumentTypes);
        if (methods.Length == 0)
        {
            throw new InvalidOperationException(
                $"No compatible extension method '{memberName}' could infer its generic arguments.");
        }

        var candidate = DotNetMethodResolver.ResolveMethod(methods, allArguments);
        var method = (MethodInfo)candidate.Method;
        var invokeArgs = DotNetMethod.BuildInvokeArgs(
            method.GetParameters(), allArguments, candidate, interpreter);
        return DotNetInstance.InvokeWithMapping(() =>
        {
            object? value = method.Invoke(null, invokeArgs);
            return DotNetMethod.WrapInvocationResult(method, invokeArgs, value);
        });
    }

    private static Type? RuntimeArgumentType(object? value) => value switch
    {
        DotNetInstance instance => instance.Type,
        SharpTSUndefined or null => null,
        double => typeof(double),
        bool => typeof(bool),
        string => typeof(string),
        _ => value.GetType()
    };
}
