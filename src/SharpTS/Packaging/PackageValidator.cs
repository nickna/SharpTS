using System.Reflection;
using System.Reflection.Metadata;

namespace SharpTS.Packaging;

/// <summary>
/// Validates assemblies and configuration before NuGet packaging.
/// </summary>
public static class PackageValidator
{
    /// <summary>
    /// Validation result with warnings and errors.
    /// </summary>
    public record ValidationResult(bool IsValid, List<string> Errors, List<string> Warnings);

    /// <summary>
    /// Validates the assembly and package configuration.
    /// </summary>
    /// <param name="assemblyPath">Path to the compiled assembly.</param>
    /// <param name="packageJson">Package metadata.</param>
    /// <param name="packageIdOverride">Optional package ID override.</param>
    /// <param name="versionOverride">Optional version override.</param>
    /// <returns>Validation result with errors and warnings.</returns>
    public static ValidationResult Validate(
        string assemblyPath,
        PackageJson? packageJson,
        string? packageIdOverride = null,
        string? versionOverride = null)
    {
        List<string> errors = [];
        List<string> warnings = [];

        // Check assembly exists
        if (!File.Exists(assemblyPath))
        {
            errors.Add($"Assembly not found: {assemblyPath}");
            return new ValidationResult(false, errors, warnings);
        }

        // Check for entry point (executable warning)
        if (HasEntryPoint(assemblyPath))
        {
            warnings.Add("Packaging an executable assembly. NuGet packages typically contain libraries.");
        }

        // Validate package ID
        var packageId = packageIdOverride ?? packageJson?.Name;
        if (string.IsNullOrWhiteSpace(packageId))
        {
            errors.Add("Package ID is required. Provide --package-id or add 'name' to package.json.");
        }
        else if (!IsValidPackageId(packageId))
        {
            errors.Add($"Invalid package ID '{packageId}'. Package IDs must contain only letters, numbers, dots, underscores, and hyphens.");
        }

        // Validate version
        var version = versionOverride ?? packageJson?.Version;
        if (string.IsNullOrWhiteSpace(version))
        {
            errors.Add("Version is required. Provide --version or add 'version' to package.json.");
        }
        else if (!NuGet.Versioning.NuGetVersion.TryParse(version, out _))
        {
            errors.Add($"Invalid version '{version}'. Use SemVer format (e.g., 1.0.0, 2.0.0-beta).");
        }

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>
    /// Checks if an assembly has an entry point (is an executable).
    /// </summary>
    private static bool HasEntryPoint(string assemblyPath)
    {
        try
        {
            // Use metadata-only loading to check for entry point
            using var fs = File.OpenRead(assemblyPath);
            using var peReader = new System.Reflection.PortableExecutable.PEReader(fs);

            if (!peReader.HasMetadata)
                return false;

            var metadataReader = peReader.GetMetadataReader();

            // Check PE header for entry point token
            var entryPointHandle = peReader.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
            return entryPointHandle != 0;
        }
        catch
        {
            // If we can't read the assembly, assume it's not an executable
            return false;
        }
    }

    /// <summary>
    /// Validates that a package ID contains only allowed characters.
    /// </summary>
    private static bool IsValidPackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        // NuGet package IDs can contain: letters, numbers, dots, underscores, hyphens
        // Cannot start or end with a dot
        // Cannot contain consecutive dots
        foreach (var c in packageId)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                return false;
        }

        if (packageId.StartsWith('.') || packageId.EndsWith('.'))
            return false;

        if (packageId.Contains(".."))
            return false;

        return true;
    }
}
