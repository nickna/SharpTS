using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation.Bundling;

/// <summary>
/// Detects whether the .NET SDK's Microsoft.NET.HostModel.dll is available
/// and provides access to the SDK bundler via reflection.
/// </summary>
public static class SdkBundlerDetector
{
    private static readonly Lazy<SdkDetectionResult> _detectionResult = new(DetectSdk, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Result of SDK detection containing availability status and assembly path.
    /// </summary>
    public record SdkDetectionResult(bool IsAvailable, string? HostModelPath, Assembly? HostModelAssembly, Type? BundlerType);

    /// <summary>
    /// Gets whether the .NET SDK bundler is available.
    /// </summary>
    public static bool IsSdkAvailable => _detectionResult.Value.IsAvailable;

    /// <summary>
    /// Gets the detection result with full details.
    /// </summary>
    public static SdkDetectionResult DetectionResult => _detectionResult.Value;

    /// <summary>
    /// Performs SDK detection (called once via Lazy).
    /// </summary>
    private static SdkDetectionResult DetectSdk()
    {
        var hostModelPath = FindHostModelDll();
        if (hostModelPath == null)
        {
            return new SdkDetectionResult(false, null, null, null);
        }

        try
        {
            var assembly = Assembly.LoadFrom(hostModelPath);
            var bundlerType = assembly.GetType("Microsoft.NET.HostModel.Bundle.Bundler");

            if (bundlerType == null)
            {
                return new SdkDetectionResult(false, hostModelPath, assembly, null);
            }

            return new SdkDetectionResult(true, hostModelPath, assembly, bundlerType);
        }
        catch
        {
            return new SdkDetectionResult(false, hostModelPath, null, null);
        }
    }

    /// <summary>
    /// Finds Microsoft.NET.HostModel.dll in the .NET SDK installation.
    /// </summary>
    private static string? FindHostModelDll()
    {
        var dotnetRoot = GetDotNetRoot();
        if (dotnetRoot == null)
        {
            return null;
        }

        var sdkDir = Path.Combine(dotnetRoot, "sdk");
        if (!Directory.Exists(sdkDir))
        {
            return null;
        }

        // Find the highest version SDK
        var sdkVersion = FindHighestSdkVersion(sdkDir);
        if (sdkVersion == null)
        {
            return null;
        }

        var hostModelPath = Path.Combine(sdkDir, sdkVersion, "Microsoft.NET.HostModel.dll");
        if (File.Exists(hostModelPath))
        {
            return hostModelPath;
        }

        return null;
    }

    /// <summary>
    /// Finds the highest version SDK directory.
    /// </summary>
    private static string? FindHighestSdkVersion(string sdkDir)
    {
        try
        {
            var directories = Directory.GetDirectories(sdkDir);
            Version? bestVersion = null;
            string? bestName = null;

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                var cleanVersion = CleanVersionString(name);

                if (Version.TryParse(cleanVersion, out var version))
                {
                    if (bestVersion == null || version > bestVersion)
                    {
                        bestVersion = version;
                        bestName = name;
                    }
                }
            }

            return bestName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes preview/rc suffixes from version strings.
    /// </summary>
    private static string CleanVersionString(string version)
    {
        var dashIndex = version.IndexOf('-');
        return dashIndex > 0 ? version[..dashIndex] : version;
    }

    /// <summary>
    /// Gets the .NET root directory.
    /// </summary>
    private static string? GetDotNetRoot()
    {
        // First try DOTNET_ROOT environment variable
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
        {
            return dotnetRoot;
        }

        // Try default locations based on platform
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var path = Path.Combine(programFiles, "dotnet");
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var paths = new[] { "/usr/local/share/dotnet", "/opt/homebrew/opt/dotnet/libexec" };
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    return path;
                }
            }
        }
        else // Linux
        {
            var paths = new[] { "/usr/share/dotnet", "/usr/lib/dotnet", "/opt/dotnet" };
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    return path;
                }
            }
        }

        // Try to derive from runtime location
        try
        {
            var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            if (!string.IsNullOrEmpty(runtimeDir))
            {
                // Navigate up: shared/Microsoft.NETCore.App/version -> dotnet root
                var dotnetRootFromRuntime = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
                if (Directory.Exists(dotnetRootFromRuntime))
                {
                    return dotnetRootFromRuntime;
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return null;
    }

    /// <summary>
    /// Resets the cached detection result (for testing purposes).
    /// </summary>
    internal static void ResetCache()
    {
        // Note: Lazy<T> cannot be reset, but this method exists for potential
        // future test infrastructure needs. In tests, you would typically
        // mock the detection at a higher level.
    }
}
