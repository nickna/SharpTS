using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace SharpTS.Packaging;

/// <summary>
/// Generates symbol packages (.snupkg) for debugging support.
/// </summary>
public class SymbolPackager
{
    private readonly string _packageId;
    private readonly string _version;
    private readonly string? _author;

    /// <summary>
    /// Creates a new symbol packager.
    /// </summary>
    /// <param name="packageId">Package ID (must match the main package).</param>
    /// <param name="version">Version (must match the main package).</param>
    /// <param name="author">Author name.</param>
    public SymbolPackager(string packageId, string version, string? author = null)
    {
        _packageId = packageId;
        _version = version;
        _author = author;
    }

    /// <summary>
    /// Creates a symbol package (.snupkg) from PDB files.
    /// </summary>
    /// <param name="assemblyPath">Path to the compiled .dll file (used to find .pdb).</param>
    /// <param name="outputDirectory">Directory to write the .snupkg file.</param>
    /// <returns>Path to the created .snupkg file, or null if no PDB found.</returns>
    public string? CreateSymbolPackage(string assemblyPath, string outputDirectory)
    {
        // Look for the PDB file
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(pdbPath))
        {
            // Check for embedded PDB (assembly has DebugType=embedded)
            // In this case, no separate .snupkg is needed
            return null;
        }

        var version = NuGetVersion.Parse(_version);

        var builder = new PackageBuilder
        {
            Id = _packageId,
            Version = version,
            Description = $"Symbol package for {_packageId}",
            PackageTypes = { PackageType.SymbolsPackage }
        };

        // Add author
        builder.Authors.Add(_author ?? "Unknown");

        // Add the PDB file
        var targetFramework = NuGetFramework.Parse("net10.0");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = pdbPath,
            TargetPath = $"lib/{targetFramework.GetShortFolderName()}/{Path.GetFileName(pdbPath)}"
        });

        // Build and save the symbol package
        var snupkgPath = Path.Combine(outputDirectory, $"{_packageId}.{version}.snupkg");
        using var stream = File.Create(snupkgPath);
        builder.Save(stream);

        return snupkgPath;
    }
}
