using SharpTS.Testing;
using System.Text.Json;

namespace SharpTS.Gui.Conformance.Tests;

internal static class SharpPaintFailureArtifacts
{
    internal static string Retain(
        string repositoryRoot,
        string stage,
        string mode,
        string scenario,
        string entryPoint,
        Exception failure,
        TestProcessResult? process,
        TestProcess? observedProcess = null)
    {
        string destination = Path.Combine(repositoryRoot, "artifacts", "gui-conformance-failures", Path.GetFileName(stage));
        Directory.CreateDirectory(destination);
        string trace = Path.Combine(stage, $"{mode}.json");
        bool tracePresent = File.Exists(trace);
        if (tracePresent) File.Copy(trace, Path.Combine(destination, "trace.json"));
        File.WriteAllText(Path.Combine(destination, "failure.json"), JsonSerializer.Serialize(new
        {
            Mode = mode,
            Scenario = scenario,
            EntryPoint = entryPoint,
            TracePresent = tracePresent,
            Failure = failure.ToString(),
            ExitCode = process?.ExitCode,
            StandardOutput = process?.StandardOutput ?? observedProcess?.StandardOutput,
            StandardError = process?.StandardError ?? observedProcess?.StandardError,
            Timeline = process?.Timeline ?? observedProcess?.Timeline
        }, new JsonSerializerOptions { WriteIndented = true }));
        return destination;
    }
}
