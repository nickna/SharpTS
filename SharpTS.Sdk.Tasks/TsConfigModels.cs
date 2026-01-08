namespace SharpTS.Sdk.Tasks;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Represents a TypeScript tsconfig.json file structure.
/// </summary>
public sealed class TsConfigJson
{
    [JsonPropertyName("compilerOptions")]
    public CompilerOptions? CompilerOptions { get; set; }

    [JsonPropertyName("files")]
    public string[]? Files { get; set; }
}

/// <summary>
/// Represents TypeScript compiler options from tsconfig.json.
/// </summary>
public sealed class CompilerOptions
{
    [JsonPropertyName("preserveConstEnums")]
    public bool? PreserveConstEnums { get; set; }

    [JsonPropertyName("experimentalDecorators")]
    public bool? ExperimentalDecorators { get; set; }

    [JsonPropertyName("decorators")]
    public bool? Decorators { get; set; }

    [JsonPropertyName("emitDecoratorMetadata")]
    public bool? EmitDecoratorMetadata { get; set; }

    [JsonPropertyName("rootDir")]
    public string? RootDir { get; set; }

    [JsonPropertyName("outDir")]
    public string? OutDir { get; set; }
}

/// <summary>
/// JSON source generation context for tsconfig.json parsing.
/// Provides compile-time generated serialization code for optimal performance.
/// </summary>
[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TsConfigJson))]
internal partial class TsConfigSourceGenerationContext : JsonSerializerContext
{
}
