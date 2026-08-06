using System.Text.Json.Serialization;

namespace SharpTS.Execution;

/// <summary>
/// AOT-safe JSON metadata for the stable trusted-host execution protocol.
/// </summary>
[JsonSerializable(typeof(SourceExecutionResult))]
[JsonSerializable(typeof(ExecutionPhaseTiming))]
internal sealed partial class SourceExecutionJsonContext : JsonSerializerContext;
