using System.Diagnostics;
using SharpTS.Execution;
using SharpTS.ProcessTreeFixture;
using SharpTS.Runtime;
using SharpTS.Tests.Infrastructure;
using Xunit;
using Xunit.Sdk;

namespace SharpTS.Tests.RuntimeTests;

[Collection("ExternalProcessTests")]
public sealed class OwnedProcessRegistryTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FixtureReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void InterpreterDispose_TerminatesOnlyItsOwnProcesses()
    {
        using var firstInterpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var secondInterpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        using Process firstProcess = StartLongRunningProcess();
        using Process secondProcess = StartLongRunningProcess();

        try
        {
            firstInterpreter.RegisterOwnedProcess(firstProcess);
            secondInterpreter.RegisterOwnedProcess(secondProcess);

            Assert.Equal(1, firstInterpreter.OwnedProcessCount);
            Assert.Equal(1, secondInterpreter.OwnedProcessCount);

            firstInterpreter.Dispose();

            Assert.True(WaitUntilExited(firstProcess), "The first interpreter did not terminate its child process.");
            Assert.False(secondProcess.HasExited);
            Assert.Equal(1, secondInterpreter.OwnedProcessCount);

            secondInterpreter.Dispose();
            Assert.True(WaitUntilExited(secondProcess), "The second interpreter did not terminate its child process.");
        }
        finally
        {
            ProcessTreeTermination.Terminate(firstProcess);
            ProcessTreeTermination.Terminate(secondProcess);
        }
    }

    [Fact]
    public async Task ProcessTreeTermination_TerminatesDescendants()
    {
        using Process parent = StartParentThatReportsChildPid();
        Task<string> stderrTask = parent.StandardError.ReadToEndAsync();
        var readiness = Stopwatch.StartNew();
        Process? child = null;

        try
        {
            string? line;
            try
            {
                line = await parent.StandardOutput.ReadLineAsync().WaitAsync(FixtureReadyTimeout);
            }
            catch (TimeoutException)
            {
                string diagnostics = StopFixtureAndDescribe(parent, stderrTask, readiness.Elapsed);
                throw new XunitException($"Timed out waiting for the fixture parent to report a child PID. {diagnostics}");
            }

            if (!int.TryParse(line, out int childPid))
            {
                string diagnostics = StopFixtureAndDescribe(parent, stderrTask, readiness.Elapsed);
                throw new XunitException(
                    $"Fixture parent did not report a valid child PID. Output: {line ?? "<null>"}. {diagnostics}");
            }

            child = Process.GetProcessById(childPid);
            Assert.False(child.HasExited);

            ProcessTreeTermination.Terminate(parent);

            Assert.True(WaitUntilExited(parent), "The parent process did not exit.");
            Assert.True(WaitUntilExited(child), "The descendant process survived process-tree termination.");
        }
        finally
        {
            ProcessTreeTermination.Terminate(parent);
            ProcessTreeTermination.Terminate(child);
            child?.Dispose();
            ObserveFixtureStderr(stderrTask);
        }
    }

    [Theory, ModeData]
    public async Task ExecutionTimeout_TerminatesSpawnedProcess(ExecutionMode mode)
    {
        string pidPath = Path.Combine(Path.GetTempPath(), $"sharpts-owned-process-{Guid.NewGuid():N}.pid");
        string sourcePath = pidPath.Replace("\\", "\\\\").Replace("'", "\\'");
        (string command, string arguments) = LongRunningGuestCommand();
        Process? child = null;
        Task<string>? execution = null;

        string source = $$"""
            import { spawn } from 'child_process';
            import { writeFileSync } from 'fs';

            const child = spawn('{{command}}', {{arguments}});
            writeFileSync('{{sourcePath}}', '' + child.pid);
            while (true) {}
            """;

        try
        {
            var files = new Dictionary<string, string> { ["main.ts"] = source };
            execution = Task.Run(() =>
                TestHarness.RunModules(files, "main.ts", mode, ExecutionTimeout));

            child = await CaptureGuestChildProcessAsync(pidPath, execution, FixtureReadyTimeout);
            int childPid = child.Id;

            Exception? executionFailure = null;
            try
            {
                await execution.WaitAsync(ExitTimeout);
            }
            catch (Exception exception)
            {
                executionFailure = exception;
            }

            Assert.True(execution.IsCompleted, $"{mode} execution did not honor its timeout.");
            Assert.IsType<TimeoutException>(executionFailure);

            Assert.True(WaitUntilExited(child), $"{mode} timeout left child PID {childPid} running.");
        }
        finally
        {
            ProcessTreeTermination.Terminate(child);
            child?.Dispose();
            if (execution is not null)
                await ObserveExecutionAsync(execution);
            try { File.Delete(pidPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Process StartLongRunningProcess()
    {
        return Process.Start(CreateFixtureStartInfo("child"))
            ?? throw new InvalidOperationException("Failed to start long-running test process.");
    }

    private static Process StartParentThatReportsChildPid()
    {
        ProcessStartInfo startInfo = CreateFixtureStartInfo("parent");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start process-tree test parent.");
    }

    private static (string Command, string Arguments) LongRunningGuestCommand()
    {
        string fixturePath = EscapeTypeScriptString(typeof(ProcessTreeFixtureMarker).Assembly.Location);
        return ("dotnet", $"['{fixturePath}', 'child']");
    }

    private static ProcessStartInfo CreateFixtureStartInfo(string mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);
        startInfo.ArgumentList.Add(mode);
        return startInfo;
    }

    private static string EscapeTypeScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private static string StopFixtureAndDescribe(
        Process parent,
        Task<string> stderrTask,
        TimeSpan elapsed)
    {
        ProcessTreeTermination.Terminate(parent);
        string stderr = ObserveFixtureStderr(stderrTask);

        string exitState;
        try
        {
            exitState = parent.HasExited ? $"exited with code {parent.ExitCode}" : "still running";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            exitState = $"exit state unavailable ({exception.GetType().Name})";
        }

        string stderrDescription = string.IsNullOrWhiteSpace(stderr) ? "<empty>" : stderr.Trim();
        return $"Parent PID: {parent.Id}; elapsed: {elapsed.TotalMilliseconds:F0} ms; parent {exitState}; stderr: {stderrDescription}";
    }

    private static string ObserveFixtureStderr(Task<string> stderrTask)
    {
        try
        {
            if (!stderrTask.Wait(ExitTimeout))
                return "<stderr did not close within the cleanup deadline>";

            return stderrTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            return $"<stderr read failed: {exception.GetType().Name}: {exception.Message}>";
        }
    }

    private static bool WaitUntilExited(Process process)
    {
        return SpinWait.SpinUntil(() =>
        {
            try { return process.HasExited; }
            catch (ObjectDisposedException) { return true; }
            catch (InvalidOperationException) { return true; }
        }, ExitTimeout);
    }

    private static async Task<Process> CaptureGuestChildProcessAsync(
        string pidPath,
        Task<string> execution,
        TimeSpan timeout)
    {
        var readiness = Stopwatch.StartNew();
        string lastPidFileState = "not created";

        while (readiness.Elapsed < timeout)
        {
            if (execution.IsCompleted)
            {
                throw new XunitException(
                    $"Guest execution {DescribeExecution(execution)} before its child process could be captured; " +
                    $"elapsed: {readiness.Elapsed.TotalMilliseconds:F0} ms; PID file: {lastPidFileState}.");
            }

            try
            {
                if (File.Exists(pidPath))
                {
                    string pidText = await File.ReadAllTextAsync(pidPath);
                    lastPidFileState = $"'{pidText}'";
                    if (int.TryParse(pidText, out int childPid))
                    {
                        Process candidate;
                        try
                        {
                            candidate = Process.GetProcessById(childPid);
                        }
                        catch (ArgumentException exception)
                        {
                            throw new XunitException(
                                $"Guest child PID {childPid} exited before its process handle could be captured; " +
                                $"elapsed: {readiness.Elapsed.TotalMilliseconds:F0} ms; " +
                                $"lookup failed with {exception.Message}");
                        }

                        try
                        {
                            if (candidate.HasExited)
                            {
                                throw new XunitException(
                                    $"Guest child PID {childPid} exited before its process handle could be captured; " +
                                    $"elapsed: {readiness.Elapsed.TotalMilliseconds:F0} ms.");
                            }

                            if (execution.IsCompleted)
                            {
                                throw new XunitException(
                                    $"Guest execution {DescribeExecution(execution)} while child PID {childPid} " +
                                    "was being captured.");
                            }

                            return candidate;
                        }
                        catch
                        {
                            candidate.Dispose();
                            throw;
                        }
                    }
                }
            }
            catch (IOException exception)
            {
                lastPidFileState = $"temporarily unreadable ({exception.Message})";
            }
            catch (UnauthorizedAccessException exception)
            {
                lastPidFileState = $"temporarily inaccessible ({exception.Message})";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new XunitException(
            $"Timed out after {readiness.Elapsed.TotalMilliseconds:F0} ms waiting to capture the guest child process; " +
            $"execution {DescribeExecution(execution)}; PID file: {lastPidFileState}.");
    }

    private static string DescribeExecution(Task<string> execution)
    {
        if (!execution.IsCompleted)
            return "is still running";
        if (execution.IsCanceled)
            return "was canceled";
        if (execution.IsFaulted)
        {
            Exception exception = execution.Exception!.GetBaseException();
            return $"failed with {exception.GetType().Name}: {exception.Message}";
        }

        return "completed successfully";
    }

    private static async Task ObserveExecutionAsync(Task<string> execution)
    {
        try
        {
            await execution.WaitAsync(ExitTimeout);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
        }
        catch
        {
            // The test asserts the expected timeout above; this await only observes cleanup failures.
        }
    }
}
