using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

/// <summary>Diagnostic names for interface declarations inside namespaces.</summary>
internal static class QualifiedInterfaceNames
{
    private static readonly ConditionalWeakTable<TypeInfo, string> Names = new();

    internal static void Retain(TypeInfo type, string qualifiedName)
        => Names.AddOrUpdate(type, qualifiedName);

    internal static string? Get(TypeInfo type)
    {
        if (Names.TryGetValue(type, out var name)) return name;
        if (type is TypeInfo.InstantiatedGeneric generic &&
            Names.TryGetValue(generic.GenericDefinition, out var definitionName))
            return $"{definitionName}<{string.Join(", ", generic.TypeArguments.Select(argument => Display(argument, bareInterfaceName: true)))}>";
        return null;
    }

    internal static string Display(TypeInfo type, bool bareInterfaceName = false)
        => Get(type) ?? (bareInterfaceName && type is TypeInfo.Interface named ? named.Name : type.ToString());
}
