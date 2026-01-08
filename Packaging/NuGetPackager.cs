using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace SharpTS.Packaging;

/// <summary>
/// Generates NuGet packages (.nupkg) from compiled SharpTS assemblies.
/// </summary>
public class NuGetPackager
{
    private readonly PackageJson _packageJson;
    private readonly string? _packageIdOverride;
    private readonly string? _versionOverride;

    /// <summary>
    /// Creates a new NuGet packager.
    /// </summary>
    /// <param name="packageJson">Package metadata from package.json.</param>
    /// <param name="packageIdOverride">Optional package ID override from CLI.</param>
    /// <param name="versionOverride">Optional version override from CLI.</param>
    public NuGetPackager(PackageJson packageJson, string? packageIdOverride = null, string? versionOverride = null)
    {
        _packageJson = packageJson;
        _packageIdOverride = packageIdOverride;
        _versionOverride = versionOverride;
    }

    /// <summary>
    /// Gets the effective package ID (CLI override or package.json name).
    /// </summary>
    public string PackageId => _packageIdOverride ?? _packageJson.Name ?? "UnnamedPackage";

    /// <summary>
    /// Gets the effective version (CLI override or package.json version).
    /// </summary>
    public string Version => _versionOverride ?? _packageJson.Version ?? "1.0.0";

    /// <summary>
    /// Creates a NuGet package from the compiled assembly.
    /// </summary>
    /// <param name="assemblyPath">Path to the compiled .dll file.</param>
    /// <param name="outputDirectory">Directory to write the .nupkg file.</param>
    /// <param name="readmePath">Optional path to README.md to include.</param>
    /// <returns>Path to the created .nupkg file.</returns>
    public string CreatePackage(string assemblyPath, string outputDirectory, string? readmePath = null)
    {
        var packageId = PackageId;
        var version = NuGetVersion.Parse(Version);

        var builder = new PackageBuilder
        {
            Id = packageId,
            Version = version,
            Description = _packageJson.Description ?? $"TypeScript library compiled with SharpTS"
        };

        // Add authors
        var author = _packageJson.Author;
        if (!string.IsNullOrEmpty(author))
        {
            builder.Authors.Add(author);
        }
        else
        {
            builder.Authors.Add("Unknown");
        }

        // Add tags/keywords
        if (_packageJson.Keywords?.Count > 0)
        {
            foreach (var keyword in _packageJson.Keywords)
            {
                builder.Tags.Add(keyword);
            }
        }

        // Add project URL
        if (!string.IsNullOrEmpty(_packageJson.ProjectUrl))
        {
            builder.ProjectUrl = new Uri(_packageJson.ProjectUrl);
        }

        // Add repository URL
        if (!string.IsNullOrEmpty(_packageJson.RepositoryUrl))
        {
            builder.Repository = new RepositoryMetadata
            {
                Type = "git",
                Url = _packageJson.RepositoryUrl
            };
        }

        // Add license
        if (!string.IsNullOrEmpty(_packageJson.License))
        {
            builder.LicenseMetadata = new LicenseMetadata(
                LicenseType.Expression,
                _packageJson.License,
                null,
                null,
                LicenseMetadata.CurrentVersion);
        }

        // Add the compiled DLL to lib/net10.0/
        var targetFramework = NuGetFramework.Parse("net10.0");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = assemblyPath,
            TargetPath = $"lib/{targetFramework.GetShortFolderName()}/{Path.GetFileName(assemblyPath)}"
        });

        // Add README if present
        if (!string.IsNullOrEmpty(readmePath) && File.Exists(readmePath))
        {
            builder.Readme = "README.md";
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = readmePath,
                TargetPath = "README.md"
            });
        }

        // Add runtime config if present
        var runtimeConfigPath = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        if (File.Exists(runtimeConfigPath))
        {
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = runtimeConfigPath,
                TargetPath = $"lib/{targetFramework.GetShortFolderName()}/{Path.GetFileName(runtimeConfigPath)}"
            });
        }

        // Build and save the package
        var packagePath = Path.Combine(outputDirectory, $"{packageId}.{version}.nupkg");
        using var stream = File.Create(packagePath);
        builder.Save(stream);

        return packagePath;
    }
}
