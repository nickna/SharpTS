using System.Diagnostics;
using System.Text.Json;
using SharpTS.ProcessTreeFixture;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public async Task Isolated_ProcessCapture_PreservesLargeUnterminatedPipesAndNonzeroExit()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var capture = Task.Run(() => ExecuteProcessIsolated(
            CaptureFixtureStart(directory.Path, "dual-output"), timeoutMs: 10000));
        var exception = await Assert.ThrowsAsync<Exception>(() => capture.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains("exited with code 7", exception.Message);
        using var diagnostic = ReadCaptureDiagnostic(exception);
        var before = diagnostic.RootElement.GetProperty("beforeCleanup");
        AssertObservedExit(before, 7);
        AssertCompletedPipe(before.GetProperty("stdout"),
            "stdout-begin:" + new string('o', 256 * 1024) + ":stdout-end");
        AssertCompletedPipe(before.GetProperty("stderr"),
            "stderr-begin:" + new string('e', 256 * 1024) + ":stderr-end");
    }

    [Fact]
    public async Task Isolated_ProcessCapture_ReportsPendingInheritedPipesBeforeBoundedCleanup()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var pidPath = directory.GetPath("owned-fixture-child.pid");
        var capture = Task.Run(() => ExecuteProcessIsolated(
            CaptureFixtureStart(directory.Path, "orphan-with-pid-file", pidPath), timeoutMs: 10000));
        try
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => capture.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Contains("exited, but its output pipes did not close", exception.Message);
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal(true, exception.Data["StandaloneCaptureStdoutStopped"]);
                Assert.Equal(true, exception.Data["StandaloneCaptureStderrStopped"]);
            }
            using var diagnostic = ReadCaptureDiagnostic(exception);
            var before = diagnostic.RootElement.GetProperty("beforeCleanup");
            int parentPid = AssertObservedExit(before, 0);
            Assert.True(File.Exists(pidPath), "The fixture must independently record its owned child for cleanup.");
            int childPid = int.Parse(File.ReadAllText(pidPath));
            Assert.NotEqual(parentPid, childPid);

            // Only this controlled fixture establishes inherited handles. These
            // assertions make no cause claim about intermittent guest failures.
            var stdout = before.GetProperty("stdout");
            var stderr = before.GetProperty("stderr");
            AssertPendingPipe(stdout);
            AssertPendingPipe(stderr);
            Assert.Contains($"CHILD:{childPid}", stdout.GetProperty("partialText").GetString());
            Assert.True(stdout.GetProperty("characterCount").GetInt64() > 0);
            AssertCaptureTimestamp(stdout.GetProperty("firstRead"));
            AssertCaptureTimestamp(stdout.GetProperty("lastRead"));
            Assert.Equal(0, stderr.GetProperty("characterCount").GetInt64());
        }
        finally
        {
            // The PID file keeps cleanup independent of the capture being tested.
            StopCaptureFixtureChild(pidPath);
            await Task.WhenAny(capture, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(capture.IsCompleted, "Capture did not complete after its owned fixture child was stopped.");
            _ = capture.Exception;
        }
    }

    [Fact]
    public async Task Isolated_ProcessCapture_ReadinessBudgetPrecedesExecutionBudget()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        // The fixture delays READY by 1,500 ms, longer than this execution
        // budget. Existing timeout/input tests cover expiry after readiness.
        var capture = Task.Run(() => ExecuteProcessIsolated(
            CaptureFixtureStart(directory.Path, "delayed-ready"), timeoutMs: 1000,
            timeoutStartsAfterOutput: "READY", readinessTimeoutMs: 15000,
            verifyStandardError: error => Assert.Empty(error)));
        Assert.Equal("READY\n", await capture.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public async Task Isolated_ProcessCapture_DrainsOnPoolCallerWithGlobalBacklog(int backlog)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        // Pool settings and artificial sleeping work belong only to the fixture
        // process. This invokes the real helper, not a copied capture algorithm.
        var start = CaptureFixtureStart(directory.Path, "capture-scheduling",
            typeof(StandaloneDllTests).Assembly.Location, directory.Path, backlog.ToString());
        var result = await TestProcess.RunAsync(start, TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var report = document.RootElement;
        Assert.Equal("helper-returned", report.GetProperty("outcome").GetString());
        Assert.True(report.GetProperty("callerIsThreadPoolThread").GetBoolean());
        Assert.Equal(backlog, report.GetProperty("backlogQueued").GetInt32());
        Assert.Equal(15000, report.GetProperty("overallBudgetMilliseconds").GetInt32());
        Assert.Equal(5000, report.GetProperty("drainBudgetMilliseconds").GetInt32());
        Assert.Null(report.GetProperty("error").GetString());
        Assert.Equal("child-start|child-end", report.GetProperty("output").GetString());
        Assert.Equal("child-stderr", report.GetProperty("standardError").GetString());
        var diagnostic = report.GetProperty("diagnostic");
        int childPid = AssertObservedExit(diagnostic, 0);
        string[] ownedIdentity = File.ReadAllText(directory.GetPath("owned-child.pid")).Split('|');
        Assert.Equal(childPid, int.Parse(ownedIdentity[0]));
        Assert.True(long.Parse(ownedIdentity[1]) > 0);
        Assert.Equal("completed", diagnostic.GetProperty("stage").GetString());
        // Completed diagnostics retain the exit snapshot; exact output above
        // confirms both readers subsequently reached EOF without losing data.
        Assert.True(report.GetProperty("cleanup").GetArrayLength() > 0);
    }

    private static ProcessStartInfo CaptureFixtureStart(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        start.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static JsonDocument ReadCaptureDiagnostic(Exception exception)
    {
        var path = Assert.IsType<string>(exception.Data["StandaloneProcessDiagnosticPath"]);
        Assert.True(File.Exists(path), $"The process diagnostic artifact is missing: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static int AssertObservedExit(JsonElement snapshot, int expectedCode)
    {
        var exit = snapshot.GetProperty("observedExit");
        int pid = exit.GetProperty("processId").GetInt32();
        Assert.True(pid > 0);
        Assert.Equal(expectedCode, exit.GetProperty("exitCode").GetInt32());
        AssertCaptureTimestamp(exit);
        return pid;
    }

    private static void AssertCompletedPipe(JsonElement pipe, string expected)
    {
        Assert.Equal("RanToCompletion", pipe.GetProperty("taskStatus").GetString());
        Assert.Equal(expected, pipe.GetProperty("partialText").GetString());
        Assert.Equal((long)expected.Length, pipe.GetProperty("characterCount").GetInt64());
        Assert.True(pipe.GetProperty("readCount").GetInt64() > 0);
        AssertCaptureTimestamp(pipe.GetProperty("readStarted"));
        AssertCaptureTimestamp(pipe.GetProperty("firstRead"));
        AssertCaptureTimestamp(pipe.GetProperty("lastRead"));
        AssertCaptureTimestamp(pipe.GetProperty("eof"));
        Assert.Equal(JsonValueKind.Null, pipe.GetProperty("fault").ValueKind);
        Assert.Equal(JsonValueKind.Null, pipe.GetProperty("canceled").ValueKind);
    }

    private static void AssertPendingPipe(JsonElement pipe)
    {
        Assert.DoesNotContain(pipe.GetProperty("taskStatus").GetString(),
            new[] { "RanToCompletion", "Faulted", "Canceled" });
        AssertCaptureTimestamp(pipe.GetProperty("readStarted"));
        Assert.Equal(JsonValueKind.Null, pipe.GetProperty("eof").ValueKind);
        Assert.Equal(JsonValueKind.Null, pipe.GetProperty("fault").ValueKind);
        Assert.Equal(JsonValueKind.Null, pipe.GetProperty("canceled").ValueKind);
    }

    private static void AssertCaptureTimestamp(JsonElement timestamp)
    {
        Assert.Equal(JsonValueKind.Object, timestamp.ValueKind);
        Assert.True(DateTimeOffset.TryParse(timestamp.GetProperty("utc").GetString(), out _));
        Assert.True(timestamp.GetProperty("elapsedMilliseconds").GetDouble() >= 0);
    }

    private static void StopCaptureFixtureChild(string pidPath)
    {
        if (!File.Exists(pidPath)) return;
        int pid = int.Parse(File.ReadAllText(pidPath));
        try
        {
            using var child = Process.GetProcessById(pid);
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(5000), $"Owned capture fixture child {pid} survived cleanup.");
        }
        catch (ArgumentException)
        {
            // Already reaped; the fixture's owned process is no longer present.
        }
    }
}
