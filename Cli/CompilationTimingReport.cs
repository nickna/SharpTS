using System.Text.Json.Serialization;
using SharpTS.Diagnostics;

namespace SharpTS.Cli;

internal sealed record CompilationTimingReport(
    bool Success,
    double TotalDurationMs,
    ExecutionPhaseTiming[] Timings);

[JsonSerializable(typeof(CompilationTimingReport))]
internal sealed partial class CompilationTimingJsonContext : JsonSerializerContext;
