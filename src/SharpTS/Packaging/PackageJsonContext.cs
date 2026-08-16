using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Packaging;

/// <summary>
/// Source-generated serializer context for package.json (#1324 Phase 1 — see
/// <see cref="Configuration.TsConfigJsonContext"/> for the rationale and the shared lenient
/// parse policy these options carry). The custom string-or-object converters on
/// <see cref="PackageJson"/> (author, repository) are attribute-declared and honored by the
/// generated metadata.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(PackageJson))]
internal sealed partial class PackageJsonContext : JsonSerializerContext;
