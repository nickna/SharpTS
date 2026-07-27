using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Configuration;

// =============================================================================
// CLI-only continuations of the tsconfig.json model. Deliberately NOT source-linked
// into SharpTS.Sdk.Tasks: the MSBuild task reads a frozen six-key subset and has no
// use for extends chains, strictness, or unknown-key capture — and keeping
// [JsonExtensionData] out of its source-generated serializer avoids a known
// System.Text.Json source-generator friction point.
// =============================================================================

internal sealed partial class TsConfigJson
{
    /// <summary>
    /// <c>extends</c>. A string or (tsc 5+) an array of strings, so it is captured raw and
    /// interpreted by the chain resolver.
    /// </summary>
    [JsonPropertyName("extends")]
    public JsonElement? Extends { get; set; }

    [JsonPropertyName("include")]
    public string[]? Include { get; set; }

    [JsonPropertyName("exclude")]
    public string[]? Exclude { get; set; }

    [JsonPropertyName("references")]
    public TsConfigProjectReference[]? References { get; set; }

    /// <summary>
    /// Top-level keys SharpTS did not bind. Drives the "unknown key / did you mean" warnings;
    /// keys keep the casing the user wrote so the message can echo it back.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

internal sealed partial class TsConfigCompilerOptions
{
    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }

    [JsonPropertyName("strictNullChecks")]
    public bool? StrictNullChecks { get; set; }

    [JsonPropertyName("strictFunctionTypes")]
    public bool? StrictFunctionTypes { get; set; }

    [JsonPropertyName("noImplicitAny")]
    public bool? NoImplicitAny { get; set; }

    [JsonPropertyName("checkJs")]
    public bool? CheckJs { get; set; }

    [JsonPropertyName("allowJs")]
    public bool? AllowJs { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("paths")]
    public Dictionary<string, string[]>? Paths { get; set; }

    [JsonPropertyName("moduleResolution")]
    public string? ModuleResolution { get; set; }

    [JsonPropertyName("lib")]
    public string[]? Lib { get; set; }

    [JsonPropertyName("types")]
    public string[]? Types { get; set; }

    [JsonPropertyName("typeRoots")]
    public string[]? TypeRoots { get; set; }

    [JsonPropertyName("incremental")]
    public bool? Incremental { get; set; }

    [JsonPropertyName("composite")]
    public bool? Composite { get; set; }

    [JsonPropertyName("tsBuildInfoFile")]
    public string? TsBuildInfoFile { get; set; }

    /// <summary>See <see cref="TsConfigJson.UnknownKeys"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }

    /// <summary>Projects the strictness keys onto the layer type the resolver folds.</summary>
    public StrictnessOptions ToStrictnessOptions() => new()
    {
        Strict = Strict,
        StrictNullChecks = StrictNullChecks,
        StrictFunctionTypes = StrictFunctionTypes,
        NoImplicitAny = NoImplicitAny,
    };
}

internal sealed class TsConfigProjectReference
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
