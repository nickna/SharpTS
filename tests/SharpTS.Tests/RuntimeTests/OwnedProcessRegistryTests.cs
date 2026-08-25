using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

public sealed class OwnedProcessRegistryTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

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
        Process? child = null;

        try
        {
            string? line = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(int.TryParse(line, out int childPid), $"Parent did not report a child PID. Output: {line ?? "<null>"}");

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
        }
    }

    [Theory, ModeData]
    public void ExecutionTimeout_TerminatesSpawnedProcess(ExecutionMode mode)
    {
        string pidPath = Path.Combine(Path.GetTempPath(), $"sharpts-owned-process-{Guid.NewGuid():N}.pid");
        string sourcePath = pidPath.Replace("\\", "\\\\").Replace("'", "\\'");
        (string command, string arguments) = LongRunningGuestCommand();
        Process? child = null;

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
            Assert.Throws<TimeoutException>(() =>
                TestHarness.RunModules(files, "main.ts", mode, TimeSpan.FromSeconds(1)));

            Assert.True(File.Exists(pidPath), "The guest did not record the spawned child PID before timing out.");
            Assert.True(int.TryParse(File.ReadAllText(pidPath), out int childPid), "The guest recorded an invalid child PID.");

            child = TryGetProcess(childPid);
            Assert.True(child is null || WaitUntilExited(child), $"{mode} timeout left child PID {childPid} running.");
        }
        finally
        {
            ProcessTreeTermination.Terminate(child);
            child?.Dispose();
            try { File.Delete(pidPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Process StartLongRunningProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Start-Sleep -Seconds 120");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("sleep 120");
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start long-running test process.");
    }

    private static Process StartParentThatReportsChildPid()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "$child = Start-Process powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 120' " +
                "-PassThru -WindowStyle Hidden; [Console]::Out.WriteLine($child.Id); $child.WaitForExit()");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("sleep 120 & echo $!; wait");
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start process-tree test parent.");
    }

    private static (string Command, string Arguments) LongRunningGuestCommand()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("powershell.exe", "['-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 120']")
            : ("/bin/sh", "['-c', 'sleep 120']");
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

    private static Process? TryGetProcess(int processId)
    {
        try { return Process.GetProcessById(processId); }
        catch (ArgumentException) { return null; }
    }
}
