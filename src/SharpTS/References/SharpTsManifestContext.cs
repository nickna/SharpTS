using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.References;

/// <summary>
/// Source-generated serializer context for sharpts.json (#1324 Phase 1 — see
/// <see cref="Configuration.TsConfigJsonContext"/> for the rationale and the shared lenient
/// parse policy these options carry).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SharpTsManifest))]
internal sealed partial class SharpTsManifestContext : JsonSerializerContext;
