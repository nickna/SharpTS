using System.Collections.Frozen;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

/// <summary>Options for an inspectable experimental native guest publish project.</summary>
public sealed record NativeAotProjectOptions(string OutputDirectory, string RuntimeIdentifier)
{
    /// <summary>The initial explicit NativeAOT project RID selection.</summary>
    public static IReadOnlySet<string> SupportedRuntimeIdentifiers { get; } = new[]
    {
        "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Host identity currently baked into generated process metadata.</summary>
    public static string? HostRuntimeIdentifier
    {
        get
        {
            string? platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "osx" : null;
            string? architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => null
            };
            return platform is null || architecture is null ? null : $"{platform}-{architecture}";
        }
    }

    /// <summary>The generated assembly path relative to the native project directory.</summary>
    public const string GuestAssemblyPath = "guest/Guest.dll";
}
