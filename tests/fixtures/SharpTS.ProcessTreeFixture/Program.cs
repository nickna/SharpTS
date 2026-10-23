using System.Diagnostics;

namespace SharpTS.ProcessTreeFixture;

/// <summary>
/// Provides a stable assembly identity so the test project can locate the fixture output.
/// </summary>
public static class ProcessTreeFixtureMarker
{
}

internal static class Program
{
    private const string ParentMode = "parent";
    private const string ChildMode = "child";

    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                [ParentMode] => RunParent(),
                [ChildMode] => RunChild(),
                ["output"] => RunOutput(),
                ["dual-output"] => RunDualOutput(),
                ["utf8-output"] => RunUtf8Output(),
                ["orphan"] => RunOrphan(),
                ["orphan-with-pid-file", var pidPath] => RunOrphan(pidPath),
                ["delayed-ready"] => RunDelayedReady(),
                ["capture-scheduling", var tests, var directory, var backlog] =>
                    StandaloneCaptureScheduling.Run(tests, directory, int.Parse(backlog)),
                ["capture-scheduling-child", var pidPath] => StandaloneCaptureScheduling.RunChild(pidPath),
                _ => ReportUsage()
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunParent()
    {
        using Process child = Process.Start(CreateSelfStartInfo(ChildMode))
            ?? throw new InvalidOperationException("Failed to start the process-tree fixture child.");

        Console.Out.WriteLine(child.Id);
        Console.Out.Flush();

        child.WaitForExit();
        return child.ExitCode;
    }

    private static int RunChild()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static int RunOutput()
    {
        // More than an OS pipe buffer, with stdout held open until stderr drains.
        Console.Error.Write(new string('e', 256 * 1024));
        Console.Out.Write("stdout without a final newline");
        return 7;
    }

    private static int RunDualOutput()
    {
        // Both streams exceed a pipe buffer and deliberately omit final newlines.
        Console.Out.Write("stdout-begin:" + new string('o', 256 * 1024) + ":stdout-end");
        Console.Out.Flush();
        Console.Error.Write("stderr-begin:" + new string('e', 256 * 1024) + ":stderr-end");
        Console.Error.Flush();
        return 7;
    }

    private static int RunUtf8Output()
    {
        using var output = Console.OpenStandardOutput();
        output.Write(new byte[] { 0xEF, 0xBB, 0xBF });
        foreach (byte value in System.Text.Encoding.UTF8.GetBytes("Aé😀中|tail"))
            output.WriteByte(value);
        output.Flush();
        Console.Error.Write("utf8-stderr");
        return 0;
    }

    private static int RunOrphan(string? pidPath = null)
    {
        using Process child = Process.Start(CreateSelfStartInfo(ChildMode))!;
        // The test owns cleanup even if output capture itself fails.
        if (pidPath is not null) File.WriteAllText(pidPath, child.Id.ToString());
        Console.WriteLine($"CHILD:{child.Id}");
        return 0; // Child deliberately retains our inherited output handles.
    }

    private static int RunDelayedReady()
    {
        // Longer than the caller's execution budget, within its readiness budget.
        Thread.Sleep(1500);
        Console.WriteLine("READY");
        return 0;
    }

    private static ProcessStartInfo CreateSelfStartInfo(string mode)
    {
        string hostPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The fixture could not locate its .NET host process.");

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(hostPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);

        startInfo.ArgumentList.Add(mode);
        return startInfo;
    }

    private static int ReportUsage()
    {
        Console.Error.WriteLine($"Usage: SharpTS.ProcessTreeFixture <{ParentMode}|{ChildMode}>");
        return 64;
    }
}
