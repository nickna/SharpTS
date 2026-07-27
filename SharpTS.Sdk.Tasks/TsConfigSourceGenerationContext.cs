namespace SharpTS.Sdk.Tasks;

using System.Text.Json;
using System.Text.Json.Serialization;
using SharpTS.Configuration;

/// <summary>
/// JSON source generation context for tsconfig.json parsing.
/// Provides compile-time generated serialization code for optimal performance.
/// </summary>
/// <remarks>
/// The model itself lives in <c>Configuration/TsConfigJson.cs</c> in the main SharpTS
/// project and is source-linked into this assembly (see SharpTS.Sdk.Tasks.csproj) so the
/// CLI and MSBuild agree on the tsconfig.json contract. Only the shared compiler-option subset is
/// linked; the CLI's extends/strictness/unknown-key members are not, so this generated
/// serializer never sees them.
/// </remarks>
[JsonSourceGenerationOptions(
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TsConfigJson))]
internal partial class TsConfigSourceGenerationContext : JsonSerializerContext
{
}
