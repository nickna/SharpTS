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
                ["orphan"] => RunOrphan(),
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

    private static int RunOrphan()
    {
        using Process child = Process.Start(CreateSelfStartInfo(ChildMode))!;
        Console.WriteLine($"CHILD:{child.Id}");
        return 0; // Child deliberately retains our inherited output handles.
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
