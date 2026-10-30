using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Declared late constructor queries completed from actual Type-value producers.</summary>
internal sealed class EmittedNativeConstructorMetadataRuntime
{
    internal EmittedNativeConstructorMetadataRuntime(ModuleBuilder module, MethodBuilder close, MethodBuilder first)
    {
        if (!ReferenceEquals(close.Module, module) || !ReferenceEquals(first.Module, module) ||
            !ReferenceEquals(close.DeclaringType, first.DeclaringType) ||
            !Shape(close, typeof(Type), [typeof(Type), typeof(Type[]), typeof(Type)]) ||
            !Shape(first, typeof(ConstructorInfo), [typeof(Type)]))
            throw new InvalidOperationException("Native constructor metadata requires exact same-output query declarations.");
        CloseEvaluated = close; FirstPublicConstructor = first;
    }

    internal MethodBuilder CloseEvaluated { get; }
    internal MethodBuilder FirstPublicConstructor { get; }
    internal Module Module => CloseEvaluated.Module;

    private static bool Shape(MethodBuilder method, Type result, Type[] parameters) =>
        method.IsStatic && !method.IsGenericMethod && (method.CallingConvention & CallingConventions.VarArgs) == 0 &&
        method.ReturnType == result && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters);
}