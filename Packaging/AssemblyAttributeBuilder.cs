using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Packaging;

/// <summary>
/// Builds CustomAttributeBuilder instances for assembly-level attributes.
/// </summary>
public static class AssemblyAttributeBuilder
{
    /// <summary>
    /// Creates all assembly-level attribute builders from metadata.
    /// </summary>
    public static List<CustomAttributeBuilder> BuildAll(AssemblyMetadata metadata)
    {
        var attributes = new List<CustomAttributeBuilder>();

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
    private static CustomAttributeBuilder BuildStringAttribute<TAttribute>(string value)
        where TAttribute : Attribute
    {
        var ctor = typeof(TAttribute).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException($"No string constructor found for {typeof(TAttribute).Name}");
        return new CustomAttributeBuilder(ctor, [value]);
    }
}
