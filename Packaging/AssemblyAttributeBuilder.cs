using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SharpTS.Compilation;

namespace SharpTS.Packaging;

/// <summary>
/// Builds encoded assembly-level attributes (raw ECMA-335 blobs; CustomAttributeBuilder is unavailable under Native AOT, #1324).
/// </summary>
public static class AssemblyAttributeBuilder
{
    /// <summary>
    /// Creates all assembly-level attribute builders from metadata.
    /// </summary>
    public static List<EncodedCustomAttribute> BuildAll(AssemblyMetadata metadata)
    {
        List<EncodedCustomAttribute> attributes = [];

        if (!string.IsNullOrEmpty(metadata.Title))
            attributes.Add(BuildStringAttribute<AssemblyTitleAttribute>(metadata.Title));

        if (!string.IsNullOrEmpty(metadata.Description))
            attributes.Add(BuildStringAttribute<AssemblyDescriptionAttribute>(metadata.Description));

        if (!string.IsNullOrEmpty(metadata.Company))
            attributes.Add(BuildStringAttribute<AssemblyCompanyAttribute>(metadata.Company));

        if (!string.IsNullOrEmpty(metadata.Product))
            attributes.Add(BuildStringAttribute<AssemblyProductAttribute>(metadata.Product));

        if (!string.IsNullOrEmpty(metadata.Copyright))
            attributes.Add(BuildStringAttribute<AssemblyCopyrightAttribute>(metadata.Copyright));

        if (!string.IsNullOrEmpty(metadata.InformationalVersion))
            attributes.Add(BuildStringAttribute<AssemblyInformationalVersionAttribute>(metadata.InformationalVersion));

        // Always set file version to match assembly version
        if (metadata.Version != null)
            attributes.Add(BuildStringAttribute<AssemblyFileVersionAttribute>(metadata.Version.ToString()));

        return attributes;
    }

    /// <summary>
    /// Builds an attribute with a single string constructor parameter.
    /// </summary>
    private static EncodedCustomAttribute BuildStringAttribute<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAttribute>(
        string value)
        where TAttribute : Attribute
    {
        var ctor = typeof(TAttribute).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException($"No string constructor found for {typeof(TAttribute).Name}");
        return new EncodedCustomAttribute(ctor, CustomAttributeEncoder.Encode(ctor, value));
    }
}
