using System.Text.Json.Serialization;

namespace SharpTS.References;

/// <summary>
/// Model for the sharpts.json project manifest: assembly references and NuGet
/// packages that <c>dotnet:</c> imports and <c>@DotNetType</c> declarations may
/// resolve types from. See docs/dotnet-types.md.
/// </summary>
public class SharpTsManifest
{
    /// <summary>Optional application-host metadata used by the high-level CLI.</summary>
    [JsonPropertyName("application")]
    public SharpTsApplication? Application { get; set; }
    /// <summary>
    /// Local assembly references. Relative paths resolve against the manifest's
    /// directory (<see cref="ManifestDirectory"/>), not the current working directory.
    /// </summary>
    [JsonPropertyName("references")]
    public List<string>? References { get; set; }

    /// <summary>
    /// NuGet package references (id → version), restored on demand via
    /// <c>dotnet restore</c> into the global package cache.
    /// </summary>
    [JsonPropertyName("packages")]
    public Dictionary<string, string>? Packages { get; set; }

    /// <summary>Absolute path of the loaded manifest file (set by the loader).</summary>
    [JsonIgnore]
    public string ManifestPath { get; set; } = "";

    /// <summary>Directory containing the manifest; base for relative reference paths.</summary>
    [JsonIgnore]
    public string ManifestDirectory => Path.GetDirectoryName(ManifestPath) ?? "";
}

public sealed class SharpTsApplication
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("entry")]
    public string? Entry { get; set; }

    [JsonPropertyName("guiSdkVersion")]
    public string? GuiSdkVersion { get; set; }

    [JsonPropertyName("guiSdkSource")]
    public string? GuiSdkSource { get; set; }
}
