namespace SharpTS.Packaging;

/// <summary>
/// Holds assembly-level metadata for IL compilation.
/// This metadata is embedded into the compiled assembly as assembly attributes.
/// </summary>
/// <param name="Version">Assembly version (e.g., 1.0.0.0)</param>
/// <param name="Title">Assembly title attribute</param>
/// <param name="Description">Assembly description attribute</param>
/// <param name="Company">Company attribute</param>
/// <param name="Product">Product name attribute</param>
/// <param name="Copyright">Copyright notice</param>
/// <param name="InformationalVersion">Informational version (can include pre-release tags)</param>
public record AssemblyMetadata(
    Version? Version = null,
    string? Title = null,
    string? Description = null,
    string? Company = null,
    string? Product = null,
    string? Copyright = null,
    string? InformationalVersion = null
)
{
    /// <summary>
    /// Creates AssemblyMetadata from a PackageJson configuration.
    /// </summary>
    public static AssemblyMetadata FromPackageJson(PackageJson package)
    {
        // Parse version, handling pre-release versions
        Version? version = null;
        if (!string.IsNullOrEmpty(package.Version))
        {
            // Try to parse as a standard version (strip pre-release suffix)
            var versionPart = package.Version.Split('-')[0];
            if (Version.TryParse(versionPart, out var parsedVersion))
            {
                version = parsedVersion;
            }
        }

        return new AssemblyMetadata(
            Version: version,
            Title: package.Name,
            Description: package.Description,
            Company: package.Author,
            Product: package.Name,
            Copyright: package.License != null ? $"Licensed under {package.License}" : null,
            InformationalVersion: package.Version
        );
    }

    /// <summary>
    /// Gets the version or a default of 1.0.0.0.
    /// </summary>
    public Version EffectiveVersion => Version ?? new Version(1, 0, 0, 0);
}
