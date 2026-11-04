using SharpTS.Testing;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class SharpPaintFailureArtifactTests(ITestOutputHelper testOutput)
{
    [Fact]
    public async Task MissingTraceStillRetainsObservedFailureWithoutCopyingOtherStageFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpts-gui-failure-retention-{Guid.NewGuid():N}");
        string stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(stage);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(stage, "settings.json"), "unrelated data");
            var failure = new InvalidOperationException("trace not yet created");
            var process = new TestProcessResult(1, "partial stdout", "partial stderr", "partial timeline");
            string retained = SharpPaintFailureArtifacts.Retain(root, stage, "compiled", "editing", "headless.tests.tsx", failure, process);
            Directory.Delete(stage, recursive: true);
            Assert.Single(Directory.GetFiles(retained));
            using JsonDocument evidence = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(retained, "failure.json")));
            JsonElement data = evidence.RootElement;
            Assert.False(data.GetProperty("TracePresent").GetBoolean());
            Assert.Equal("compiled", data.GetProperty("Mode").GetString());
            Assert.Equal("editing", data.GetProperty("Scenario").GetString());
            Assert.Equal("headless.tests.tsx", data.GetProperty("EntryPoint").GetString());
            Assert.Contains(failure.Message, data.GetProperty("Failure").GetString());
            Assert.Equal(1, data.GetProperty("ExitCode").GetInt32());
            Assert.Equal(process.StandardOutput, data.GetProperty("StandardOutput").GetString());
            Assert.Equal(process.StandardError, data.GetProperty("StandardError").GetString());
            Assert.Equal(process.Timeline, data.GetProperty("Timeline").GetString());
            Assert.False(File.Exists(Path.Combine(retained, "settings.json")));
        }
        finally
        {
            await TestDirectory.TryDeleteAsync(root, testOutput.WriteLine);
        }
    }
}
