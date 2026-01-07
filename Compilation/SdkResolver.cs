using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

/// <summary>
/// Resolves .NET SDK reference assembly paths for cross-platform compilation.
/// </summary>
/// <remarks>
/// Reference assemblies are required for producing assemblies that can be
/// referenced at compile-time by other .NET projects. This resolver attempts
/// to auto-detect the SDK installation path and locate the appropriate
/// reference assemblies for the current .NET version.
/// </remarks>
public static class SdkResolver
{
    /// <summary>
    /// Attempts to find the path to .NET reference assemblies.
    /// </summary>
    /// <returns>Path to reference assemblies directory, or null if not found.</returns>
    public static string? FindReferenceAssembliesPath()
    {
        // Try multiple strategies in order of preference
        return TryFromRuntimeLocation()
            ?? TryFromDotnetRoot()
            ?? TryFromKnownPaths();
    }

    /// <summary>
    /// Attempts to find reference assemblies by deriving from the runtime location.
    /// </summary>
    private static string? TryFromRuntimeLocation()
    {
        try
        {
            // Get the runtime directory (e.g., /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/)
            var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            if (string.IsNullOrEmpty(runtimeDir))
                return null;

            // Navigate up to dotnet root: shared/Microsoft.NETCore.App/version -> dotnet root
            var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));

            return TryFindRefAssembliesInDotnetRoot(dotnetRoot);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to find reference assemblies from the DOTNET_ROOT environment variable.
    /// </summary>
    private static string? TryFromDotnetRoot()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrEmpty(dotnetRoot))
            return null;

        return TryFindRefAssembliesInDotnetRoot(dotnetRoot);
    }

    /// <summary>
    /// Attempts to find reference assemblies from known installation paths.
    /// </summary>
    private static string? TryFromKnownPaths()
    {
        var knownPaths = GetKnownDotnetPaths();

        foreach (var path in knownPaths)
        {
            if (Directory.Exists(path))
            {
                var result = TryFindRefAssembliesInDotnetRoot(path);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets known .NET installation paths based on the current platform.
    /// </summary>
    private static IEnumerable<string> GetKnownDotnetPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return @"C:\Program Files\dotnet";
            yield return @"C:\Program Files (x86)\dotnet";

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "Microsoft", "dotnet");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/share/dotnet";
            yield return "/opt/homebrew/opt/dotnet/libexec";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".dotnet");
        }
        else // Linux and others
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
            yield return "/opt/dotnet";

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".dotnet");
        }
    }

    /// <summary>
    /// Tries to find reference assemblies within a dotnet root directory.
    /// </summary>
    private static string? TryFindRefAssembliesInDotnetRoot(string dotnetRoot)
    {
        // Reference assemblies are in: dotnet/packs/Microsoft.NETCore.App.Ref/{version}/ref/net{major}.0/
        var packsPath = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");

        if (!Directory.Exists(packsPath))
            return null;

        // Find the best matching version
        var version = FindBestVersion(packsPath);
        if (version == null)
            return null;

        // Construct the full path
        var majorVersion = GetMajorVersion(version);
        var refPath = Path.Combine(packsPath, version, "ref", $"net{majorVersion}.0");

        if (Directory.Exists(refPath) && File.Exists(Path.Combine(refPath, "System.Runtime.dll")))
            return refPath;

        return null;
    }

    /// <summary>
    /// Finds the best matching SDK version, preferring the current runtime version.
    /// </summary>
    private static string? FindBestVersion(string packsPath)
    {
        var directories = Directory.GetDirectories(packsPath);
        if (directories.Length == 0)
            return null;

        // Get version numbers and sort descending
        var versions = directories
            .Select(Path.GetFileName)
            .Where(name => name != null && char.IsDigit(name[0]))
            .Select(name => (Name: name!, Version: TryParseVersion(name!)))
            .Where(v => v.Version != null)
            .OrderByDescending(v => v.Version)
            .ToList();

        if (versions.Count == 0)
            return null;

        // Try to match current runtime version
        var currentVersion = Environment.Version;
        var match = versions.FirstOrDefault(v =>
            v.Version!.Major == currentVersion.Major &&
            v.Version.Minor == currentVersion.Minor);

        if (match.Name != null)
            return match.Name;

        // Fall back to matching major version
        match = versions.FirstOrDefault(v => v.Version!.Major == currentVersion.Major);
        if (match.Name != null)
            return match.Name;

        // Fall back to latest version
        return versions[0].Name;
    }

    private static Version? TryParseVersion(string versionString)
    {
        // Handle versions like "10.0.0", "9.0.0-preview.1", etc.
        var dashIndex = versionString.IndexOf('-');
        if (dashIndex > 0)
            versionString = versionString.Substring(0, dashIndex);

        return Version.TryParse(versionString, out var version) ? version : null;
    }

    private static int GetMajorVersion(string versionString)
    {
        var dotIndex = versionString.IndexOf('.');
        if (dotIndex > 0)
        {
            if (int.TryParse(versionString.Substring(0, dotIndex), out var major))
                return major;
        }
        return Environment.Version.Major;
    }

    /// <summary>
    /// Gets detailed information about the SDK resolution process (for diagnostics).
    /// </summary>
    public static string GetDiagnosticInfo()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SDK Resolution Diagnostic Info:");
        sb.AppendLine($"  Runtime Directory: {RuntimeEnvironment.GetRuntimeDirectory()}");
        sb.AppendLine($"  DOTNET_ROOT: {Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "(not set)"}");
        sb.AppendLine($"  Current Runtime Version: {Environment.Version}");
        sb.AppendLine($"  OS Platform: {RuntimeInformation.OSDescription}");

        var foundPath = FindReferenceAssembliesPath();
        sb.AppendLine($"  Found Reference Assemblies: {foundPath ?? "(not found)"}");

        if (foundPath != null && Directory.Exists(foundPath))
        {
            var dllCount = Directory.GetFiles(foundPath, "*.dll").Length;
            sb.AppendLine($"  Reference Assembly Count: {dllCount}");
        }

        return sb.ToString();
    }
}
