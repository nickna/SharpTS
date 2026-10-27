using System.Collections.Frozen;

namespace SharpTS.Compilation;

/// <summary>Options for an inspectable experimental native guest publish project.</summary>
public sealed record NativeAotProjectOptions(string OutputDirectory, string RuntimeIdentifier)
{
    /// <summary>The initial explicit NativeAOT project RID selection.</summary>
    public static IReadOnlySet<string> SupportedRuntimeIdentifiers { get; } = new[]
    {
        "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The generated assembly path relative to the native project directory.</summary>
    public const string GuestAssemblyPath = "guest/Guest.dll";
}
