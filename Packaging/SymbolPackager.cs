using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace SharpTS.Packaging;

/// <summary>
/// Generates symbol packages (.snupkg) for debugging support.
/// </summary>
/// <param name="packageId">Package ID (must match the main package).</param>
/// <param name="version">Version (must match the main package).</param>
/// <param name="author">Author name.</param>
public class SymbolPackager(string packageId, string version, string? author = null)
{
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

        var parsedVersion = NuGetVersion.Parse(version);

        var builder = new PackageBuilder
        {
            Id = packageId,
            Version = parsedVersion,
            Description = $"Symbol package for {packageId}",
            PackageTypes = { PackageType.SymbolsPackage }
        };

        // Add author
        builder.Authors.Add(author ?? "Unknown");

        // Add the PDB file
        var targetFramework = NuGetFramework.Parse("net10.0");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = pdbPath,
            TargetPath = $"lib/{targetFramework.GetShortFolderName()}/{Path.GetFileName(pdbPath)}"
        });

        // Build and save the symbol package
        var snupkgPath = Path.Combine(outputDirectory, $"{packageId}.{parsedVersion}.snupkg");
        using var stream = File.Create(snupkgPath);
        builder.Save(stream);

        return snupkgPath;
    }
}