using System.Diagnostics;
using System.Text.Json;
using SharpTS.FrontendFixture;
using SharpTS.Runtime;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>Isolates malformed-input probes so hangs and stack overflows cannot poison testhost.</summary>
internal static class FrontendTestHarness
{
    public static FrontendResult Check(string source, int timeoutMs = 5000, string mode = "check")
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(FrontendFixtureMarker).Assembly.Location);
        start.ArgumentList.Add(mode);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start frontend fixture.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        // Writing can also block if a broken worker never reads its input.
        var input = Task.Run(async () =>
        {
            await process.StandardInput.WriteAsync(source);
            process.StandardInput.Close();
        });
        try
        {
            if (!process.WaitForExit(timeoutMs))
                throw new TimeoutException($"Frontend probe exceeded {timeoutMs}ms.");
            input.GetAwaiter().GetResult();
            string stdout = output.GetAwaiter().GetResult();
            string stderr = error.GetAwaiter().GetResult();
            Assert.True(process.ExitCode is 0 or 2,
                $"Frontend crashed (exit {process.ExitCode}):\n{stderr}\nSource:\n{source}");
            var result = JsonSerializer.Deserialize<FrontendResult>(stdout);
            Assert.NotNull(result);
            Assert.Equal(process.ExitCode == 0, result.IsSuccess);
            Assert.Equal(result.IsSuccess, result.Diagnostics.Length == 0);
            return result;
        }
        finally
        {
            if (!process.HasExited) ProcessTreeTermination.Terminate(process);
            if (!process.HasExited)
                throw new InvalidOperationException($"Frontend worker {process.Id} could not be terminated.");
            // Observe all redirected pipes, including on a crash or timeout. Bound cleanup
            // too, so a pipe failure cannot defeat the process timeout.
            try { Task.WhenAll(input, output, error).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (IOException) { }
        }
    }
}
