using System.Xml.Linq;

namespace SharpTS.LspBridge.Project;

/// <summary>
/// Parses .csproj files to extract assembly references.
/// </summary>
public static class CsprojParser
{
    /// <summary>
    /// Parses a .csproj file and returns all resolved assembly paths.
    /// </summary>
    public static List<string> Parse(string csprojPath)
    {
        if (!File.Exists(csprojPath))
            throw new FileNotFoundException($"Project file not found: {csprojPath}");

        var doc = XDocument.Load(csprojPath);
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath)) ?? ".";
        var references = new List<string>();

        // Parse <Reference> elements (direct assembly references)
        foreach (var refElement in doc.Descendants("Reference"))
        {
            var hintPath = refElement.Element("HintPath")?.Value;
            if (!string.IsNullOrEmpty(hintPath))
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, hintPath));
                if (File.Exists(fullPath))
                    references.Add(fullPath);
            }
        }

        // Parse <PackageReference> elements (NuGet packages)
        var packageReferences = new List<(string Id, string Version)>();
        foreach (var pkgElement in doc.Descendants("PackageReference"))
        {
            var id = pkgElement.Attribute("Include")?.Value;
            var version = pkgElement.Attribute("Version")?.Value
                ?? pkgElement.Element("Version")?.Value;

            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                packageReferences.Add((id, version));
            }
        }

        // Resolve NuGet packages to actual DLLs
        var nugetPath = GetNuGetPackagesPath();
        foreach (var (id, version) in packageReferences)
        {
            var packagePath = Path.Combine(nugetPath, id.ToLowerInvariant(), version);
            if (Directory.Exists(packagePath))
            {
                var libPath = FindBestTfmLib(packagePath);
                if (libPath != null)
                {
                    foreach (var dll in Directory.GetFiles(libPath, "*.dll"))
                    {
                        references.Add(dll);
                    }
                }
            }
        }

        return references;
    }

    private static string GetNuGetPackagesPath()
    {
        // Check NUGET_PACKAGES environment variable first
        var envPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            return envPath;

        // Default to user profile location
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".nuget", "packages");
    }

    private static string? FindBestTfmLib(string packagePath)
    {
        var libPath = Path.Combine(packagePath, "lib");
        if (!Directory.Exists(libPath))
            return null;

        // Prefer newer .NET versions, then netstandard
        var tfmPreferences = new[]
        {
            "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
            "netcoreapp3.1", "netcoreapp3.0",
            "netstandard2.1", "netstandard2.0", "netstandard1.6"
        };

        foreach (var tfm in tfmPreferences)
        {
            var tfmPath = Path.Combine(libPath, tfm);
            if (Directory.Exists(tfmPath))
                return tfmPath;
        }

        // Fall back to first available
        var dirs = Directory.GetDirectories(libPath);
        return dirs.Length > 0 ? dirs[0] : null;
    }
}
