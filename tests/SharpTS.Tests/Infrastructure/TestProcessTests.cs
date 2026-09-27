using System.Diagnostics;
using SharpTS.ProcessTreeFixture;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

[Collection("ExternalProcessTests")]
public sealed class TestProcessTests
{
    [Fact]
    public async Task DrainsBothPipesAndPreservesExitCodeAndUnterminatedOutput()
    {
        var result = await TestProcess.RunAsync(Start("output"), TimeSpan.FromSeconds(10));
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("stdout without a final newline", result.StandardOutput);
        Assert.Equal(new string('e', 256 * 1024), result.StandardError);
    }

    [Fact]
    public async Task TimeoutTerminatesTheOwnedTreeAndRetainsPartialOutput()
    {
        await using var process = new TestProcess(Start("parent"), TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => process.WaitForExitAsync());
        Assert.True(process.HasExited);
        Assert.Contains("cleanup completed", exception.Message);
        Assert.Contains("exited with code", exception.Message);
        int childId = int.Parse(process.StandardOutput.Trim());
        AssertExited(childId);
    }

    [Fact]
    public async Task ExitedParentWithInheritedPipesCannotHangOutputDrain()
    {
        await using var process = new TestProcess(Start("orphan"), TimeSpan.FromSeconds(10),
            cleanupTimeout: TimeSpan.FromMilliseconds(200));
        Process? child = null;
        try
        {
            await process.WaitForOutputAsync("CHILD:");
            int childId = int.Parse(process.StandardOutput.Trim().Split(':')[1]);
            child = Process.GetProcessById(childId);
            var clock = Stopwatch.StartNew();
            var exception = await Assert.ThrowsAsync<TimeoutException>(() => process.WaitForExitAsync());
            Assert.True(process.HasExited);
            Assert.Contains($"CHILD:{childId}", exception.Message);
            Assert.Contains("waiting for exit/output", exception.Message);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                Assert.True(child.WaitForExit(5000));
                child.Dispose();
            }
        }
    }

    private static ProcessStartInfo Start(string mode)
    {
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);
        start.ArgumentList.Add(mode);
        return start;
    }

    private static void AssertExited(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            Assert.True(process.WaitForExit(5000), $"Child {id} survived cleanup.");
        }
        catch (ArgumentException) { /* already reaped */ }
    }
}
