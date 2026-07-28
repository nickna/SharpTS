using System.Reflection;
using System.Runtime.CompilerServices;
using SharpTS.Declaration;

namespace SharpTS.Runtime.DotNet;

/// <summary>Discovers and closes imported CLR extension methods.</summary>
internal static class DotNetExtensionMethodResolver
{
    internal static MethodInfo[] GetClosedCandidates(
        IEnumerable<Type> containers,
        string memberName,
        IReadOnlyList<Type?> argumentTypes)
    {
        var methods = new List<MethodInfo>();
        foreach (var container in containers)
        {
            foreach (var method in container.GetMethods(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.IsDefined(typeof(ExtensionAttribute), inherit: false) ||
                    !string.Equals(
                        DotNetTypeMapper.ToTypeScriptMethodName(method.Name),
                        memberName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (method.GetParameters().Length != argumentTypes.Count)
                    continue;
                var closed = DotNetGenericMethodInference.TryClose(method, argumentTypes);
                if (closed != null)
                    methods.Add(closed);
            }
        }
        return methods.ToArray();
    }

    internal static MethodInfo[] GetReceiverClosedCandidates(
        IEnumerable<Type> containers,
        string memberName,
        Type receiverType)
    {
        var methods = new List<MethodInfo>();
        foreach (var container in containers)
        {
            foreach (var method in container.GetMethods(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (!method.IsDefined(typeof(ExtensionAttribute), inherit: false) ||
                    !string.Equals(
                        DotNetTypeMapper.ToTypeScriptMethodName(method.Name),
                        memberName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var argumentTypes = new Type?[method.GetParameters().Length];
                argumentTypes[0] = receiverType;
                var closed = DotNetGenericMethodInference.TryClose(method, argumentTypes);
                if (closed != null)
                    methods.Add(closed);
            }
        }
        return methods.ToArray();
    }
}
