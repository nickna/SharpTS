using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation.Bundling;

/// <summary>
/// Creates single-file executables using manual byte-patching of the apphost template.
/// This bundler works without requiring the .NET SDK to be installed.
/// </summary>
public class ManualBundler : IBundler
{
    // Bundle header placeholder (40 bytes total):
    // - First 8 bytes: header offset (zeros for non-bundle, patched with actual offset)
    // - Next 32 bytes: SHA-256 signature of ".net core bundle"
    private static readonly byte[] BundleHeaderPlaceholder = [
        // 8 bytes for header offset (initially zeros)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // 32 bytes signature
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];

    // DLL path placeholder (SHA-256 of "foobar") - used to locate where to write the DLL name
    private static readonly byte[] DllPathPlaceholder =
        Encoding.UTF8.GetBytes("c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");

    /// <inheritdoc/>
    public BundleTechnique Technique => BundleTechnique.ManualBundler;

    /// <inheritdoc/>
    public BundleResult CreateSingleFileExecutable(string dllPath, string exePath, string assemblyName)
    {
        // Find the apphost template and SDK version
        var (apphostPath, sdkVersion) = FindAppHostTemplateWithVersion();
        if (apphostPath == null || sdkVersion == null)
        {
            throw new CompileException(
                "Could not find apphost template. Ensure the .NET SDK is installed.");
        }

        // Read apphost template
        var apphostBytes = File.ReadAllBytes(apphostPath);

        // 1. Patch the DLL path placeholder
        var dllPathIndex = FindSequence(apphostBytes, DllPathPlaceholder);
        if (dllPathIndex < 0)
        {
            throw new CompileException(
                "Could not find DLL path placeholder in apphost template.");
        }

        var dllName = $"{assemblyName}.dll";
        var dllNameBytes = Encoding.UTF8.GetBytes(dllName);
        Array.Clear(apphostBytes, dllPathIndex, 1024);  // Clear 1024-byte placeholder area
        Array.Copy(dllNameBytes, 0, apphostBytes, dllPathIndex, dllNameBytes.Length);

        // 2. Find the full 40-byte bundle header placeholder
        var headerOffsetIndex = FindSequence(apphostBytes, BundleHeaderPlaceholder);
        if (headerOffsetIndex < 0)
        {
            throw new CompileException(
                "Could not find bundle header placeholder in apphost template.");
        }

        // Read input files
        var dllBytes = File.ReadAllBytes(dllPath);
        var runtimeConfig = GenerateRuntimeConfigJson(sdkVersion);
        var runtimeConfigBytes = Encoding.UTF8.GetBytes(runtimeConfig);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Build the bundle in memory
        // Correct format per .NET source: [apphost] [file data...] [manifest]
        // Offsets in manifest are ABSOLUTE positions in the bundle
        using var bundleStream = new MemoryStream();

        // Write the patched apphost first
        bundleStream.Write(apphostBytes);

        // Write file 1: Main assembly DLL (with alignment for assemblies)
        // Assemblies should be aligned to 4KB for memory-mapping
        const int AssemblyAlignment = 4096;
        var misalignment = bundleStream.Position % AssemblyAlignment;
        if (misalignment != 0)
        {
            var padding = AssemblyAlignment - misalignment;
            for (int i = 0; i < padding; i++)
                bundleStream.WriteByte(0);
        }

        var dllOffset = bundleStream.Position;  // Absolute offset
        bundleStream.Write(dllBytes);

        // Write file 2: runtimeconfig.json (no special alignment needed)
        var configOffset = bundleStream.Position;  // Absolute offset
        bundleStream.Write(runtimeConfigBytes);

        // Now write the manifest at the end
        var manifestOffset = bundleStream.Position;

        var dllNameForEntry = dllName;
        var configNameForEntry = $"{assemblyName}.runtimeconfig.json";

        // Generate a deterministic bundle ID (12 chars, must be path-safe)
        var bundleId = GenerateBundleId(dllBytes, runtimeConfigBytes);

        using (var writer = new BinaryWriter(bundleStream, Encoding.UTF8, leaveOpen: true))
        {
            // Bundle header format (version 6 for .NET 6+):
            writer.Write((uint)6);  // Major version
            writer.Write((uint)0);  // Minor version
            writer.Write(2);        // Number of embedded files

            // Bundle ID (BinaryWriter.Write(string) uses 7-bit length prefix + UTF8)
            writer.Write(bundleId);

            // deps.json location (absolute offset, size) - none
            writer.Write((long)0);
            writer.Write((long)0);

            // runtimeconfig.json location (absolute offset, size)
            writer.Write(configOffset);
            writer.Write((long)runtimeConfigBytes.Length);

            // Flags (0 = none)
            writer.Write((ulong)0);

            // File entry 1: Main assembly DLL
            writer.Write(dllOffset);                          // Absolute offset
            writer.Write((long)dllBytes.Length);              // Size
            writer.Write((long)0);                            // Compressed size (0 = uncompressed)
            writer.Write((byte)1);                            // FileType: Assembly
            writer.Write(dllNameForEntry);                    // Path (BinaryWriter handles length prefix)

            // File entry 2: runtimeconfig.json
            writer.Write(configOffset);                       // Absolute offset
            writer.Write((long)runtimeConfigBytes.Length);    // Size
            writer.Write((long)0);                            // Compressed size (0 = uncompressed)
            writer.Write((byte)4);                            // FileType: RuntimeConfigJson
            writer.Write(configNameForEntry);                 // Path
        }

        // Now patch the header offset in the apphost portion of our bundle
        var bundleBytes = bundleStream.ToArray();
        var offsetBytes = BitConverter.GetBytes(manifestOffset);
        Array.Copy(offsetBytes, 0, bundleBytes, headerOffsetIndex, 8);

        // Write the final bundle to disk
        File.WriteAllBytes(exePath, bundleBytes);

        // Set execute permissions on Unix
        SetExecutePermission(exePath);

        return new BundleResult(exePath, BundleTechnique.ManualBundler);
    }

    /// <summary>
    /// Generate a 12-character bundle ID by hashing the embedded file contents.
    /// </summary>
    private static string GenerateBundleId(byte[] dllBytes, byte[] configBytes)
    {
        using var sha = SHA256.Create();

        // Hash the DLL content
        var dllHash = sha.ComputeHash(dllBytes);
        sha.TransformBlock(dllHash, 0, dllHash.Length, dllHash, 0);

        // Hash the config content
        var configHash = SHA256.HashData(configBytes);
        sha.TransformFinalBlock(configHash, 0, configHash.Length);

        // Convert to Base64Url and take first 12 chars
        var base64 = Convert.ToBase64String(sha.Hash!);
        // Make URL-safe: replace + with -, / with _, remove padding
        var urlSafe = base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return urlSafe.Substring(0, 12);
    }

    private static int FindSequence(byte[] array, byte[] sequence)
    {
        for (int i = 0; i <= array.Length - sequence.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < sequence.Length; j++)
            {
                if (array[i + j] != sequence[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }
        return -1;
    }

    private static string GenerateRuntimeConfigJson(Version sdkVersion)
    {
        return $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{sdkVersion.Major}}.{{sdkVersion.Minor}}",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{sdkVersion.Major}}.{{sdkVersion.Minor}}.{{sdkVersion.Build}}"
                }
              }
            }
            """;
    }

    /// <summary>
    /// Finds the apphost template and returns both the path and the SDK version.
    /// </summary>
    internal static (string? Path, Version? Version) FindAppHostTemplateWithVersion()
    {
        var dotnetRoot = GetDotNetRoot();
        if (dotnetRoot == null) return (null, null);

        var rid = GetCurrentRuntimeIdentifier();
        var packsDir = Path.Combine(dotnetRoot, "packs");
        var hostPackPattern = $"Microsoft.NETCore.App.Host.{rid}";

        if (!Directory.Exists(packsDir)) return (null, null);

        var hostPackDirs = Directory.GetDirectories(packsDir)
            .Where(d => Path.GetFileName(d).StartsWith(hostPackPattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (hostPackDirs.Count == 0) return (null, null);

        // Find highest available version (prefer newer SDKs)
        string? bestPath = null;
        Version? bestVersion = null;

        foreach (var packDir in hostPackDirs)
        {
            foreach (var versionDir in Directory.GetDirectories(packDir))
            {
                var versionStr = Path.GetFileName(versionDir);
                var dashIndex = versionStr.IndexOf('-');
                var cleanVersion = dashIndex > 0 ? versionStr[..dashIndex] : versionStr;

                if (Version.TryParse(cleanVersion, out var version))
                {
                    if (bestVersion == null || version > bestVersion)
                    {
                        var exeName = OperatingSystem.IsWindows() ? "apphost.exe" : "apphost";
                        var apphostPath = Path.Combine(versionDir, "runtimes", rid, "native", exeName);
                        if (File.Exists(apphostPath))
                        {
                            bestVersion = version;
                            bestPath = apphostPath;
                        }
                    }
                }
            }
        }

        return (bestPath, bestVersion);
    }

    private static string? GetDotNetRoot()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
        {
            return dotnetRoot;
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var path = Path.Combine(programFiles, "dotnet");
            if (Directory.Exists(path)) return path;
        }
        else
        {
            var homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
            var paths = new[] { "/usr/share/dotnet", "/usr/local/share/dotnet", "/opt/dotnet", homeDotnet };
            foreach (var path in paths)
            {
                if (Directory.Exists(path)) return path;
            }
        }

        return null;
    }

    private static string GetCurrentRuntimeIdentifier()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsLinux()) return $"linux-{arch}";
        if (OperatingSystem.IsMacOS()) return $"osx-{arch}";

        return $"win-{arch}";
    }

    /// <summary>
    /// Sets execute permission on the file for Unix systems.
    /// On Windows, this is a no-op.
    /// </summary>
    internal static void SetExecutePermission(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // On Unix, set the execute bit (owner, group, and others can execute)
        // Using UnixFileMode which is available on .NET 6+
        var currentMode = File.GetUnixFileMode(filePath);
        var newMode = currentMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(filePath, newMode);
    }
}
