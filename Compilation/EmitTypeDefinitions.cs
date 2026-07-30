using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Centralizes type relationships that are serialized into persisted Reflection.Emit metadata.
/// </summary>
internal static class EmitTypeDefinitions
{
    private const string EmitMetadataJustification =
        "The type relationship is recorded in a persisted generated assembly, not inspected or instantiated by the native host. Native builds generate complete compiler metadata, and native compile smoke tests exercise this seam.";

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = EmitMetadataJustification)]
    internal static TypeBuilder DefineType(
        ModuleBuilder module,
        string name,
        TypeAttributes attributes,
        Type? parent)
    {
        return module.DefineType(name, attributes, parent);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = EmitMetadataJustification)]
    internal static TypeBuilder DefineType(
        ModuleBuilder module,
        string name,
        TypeAttributes attributes,
        Type? parent,
        Type[]? interfaces)
    {
        return module.DefineType(name, attributes, parent, interfaces);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = EmitMetadataJustification)]
    internal static void SetParent(TypeBuilder typeBuilder, Type? parent)
    {
        typeBuilder.SetParent(parent);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = EmitMetadataJustification)]
    internal static void AddInterfaceImplementation(TypeBuilder typeBuilder, Type interfaceType)
    {
        typeBuilder.AddInterfaceImplementation(interfaceType);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = EmitMetadataJustification)]
    internal static void SetBaseTypeConstraint(
        GenericTypeParameterBuilder parameter,
        Type? baseTypeConstraint)
    {
        parameter.SetBaseTypeConstraint(baseTypeConstraint);
    }
}
