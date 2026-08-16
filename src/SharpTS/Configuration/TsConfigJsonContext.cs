using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Configuration;

/// <summary>
/// Source-generated serializer context for the CLI's tsconfig.json model (#1324 Phase 1):
/// reflection-based System.Text.Json hard-fails under Native AOT before a line of TS runs, so
/// every config/manifest loader binds through a context. Options carry the loaders' shared
/// lenient parse policy: case-insensitive keys, comments, and trailing commas allowed.
/// </summary>
/// <remarks>
/// Unlike the MSBuild task's <c>TsConfigSourceGenerationContext</c> (which links only the shared
/// compiler-option subset and deliberately excludes <c>[JsonExtensionData]</c>), this context
/// serves the full CLI model including extends/strictness/unknown-key capture. The known
/// source-generator friction with extension data is confined to fast-path serialization; the CLI
/// only ever deserializes tsconfig, which uses the metadata-based path where extension data works.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(TsConfigJson))]
internal sealed partial class TsConfigJsonContext : JsonSerializerContext;
