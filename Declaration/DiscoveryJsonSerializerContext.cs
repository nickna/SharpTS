using System.Text.Json.Serialization;

namespace SharpTS.Declaration;

/// <summary>
/// Source-generated metadata for the <c>--gen-decl --json</c> report.
/// </summary>
[JsonSerializable(typeof(DiscoveryReport))]
internal partial class DiscoveryJsonSerializerContext : JsonSerializerContext;
