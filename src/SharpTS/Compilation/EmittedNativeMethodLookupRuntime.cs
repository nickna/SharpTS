using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Declared exact-owner queries; their ILCompiler-owned catalog completes after source types.</summary>
internal sealed class EmittedNativeMethodLookupRuntime
{
    internal EmittedNativeMethodLookupRuntime(ModuleBuilder module, MethodBuilder select, MethodBuilder enumerate)
    {
        if (!ReferenceEquals(module, select.Module) || !ReferenceEquals(module, enumerate.Module) ||
            select.DeclaringType != enumerate.DeclaringType ||
            !HasShape(select, typeof(MethodInfo), [typeof(Type), typeof(string), typeof(BindingFlags)]) ||
            !HasShape(enumerate, typeof(MethodInfo[]), [typeof(Type), typeof(BindingFlags)]))
            throw new InvalidOperationException("Native method lookup requires exact same-output static query handles.");
        Select = select; Enumerate = enumerate;
    }

    internal MethodBuilder Select { get; }
    internal MethodBuilder Enumerate { get; }
    internal Module Module => Select.Module;

    private static bool HasShape(MethodBuilder method, Type result, Type[] parameters) =>
        method.IsStatic && !method.IsGenericMethod && method.ReturnType == result &&
        method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters);
}
