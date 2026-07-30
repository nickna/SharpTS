using SharpTS.Execution;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// process.report (#1084): getReport/writeReport plus the report config
/// properties. Populates the BCL-derivable sections; V8-specific sections
/// (native stack, GC internals) are documented ceilings and appear empty.
/// </summary>
public static partial class ProcessBuiltIns
{
    private static SharpTSObject? _reportObject;
    private static int _reportSequence;

    private static SharpTSObject GetReportObject()
    {
        return _reportObject ??= new SharpTSObject(new Dictionary<string, object?>
        {
            ["directory"] = "",
            ["filename"] = "",
            ["compact"] = false,
            ["signal"] = "SIGUSR2",
            ["reportOnFatalError"] = false,
            ["reportOnSignal"] = false,
            ["reportOnUncaughtException"] = false,
            ["excludeNetwork"] = false,
            ["getReport"] = new BuiltInMethod("getReport", 0, 1, GetReport),
            ["writeReport"] = new BuiltInMethod("writeReport", 0, 2, WriteReport),
        });
    }

    private static object? GetReport(Interpreter i, object? r, List<object?> args)
        => BuildReport("JavaScript API", args.Count > 0 ? args[0] : null);

    private static object? WriteReport(Interpreter i, object? r, List<object?> args)
    {
        // writeReport([filename][, err])
        string? filename = args.Count > 0 && args[0] is string f ? f : null;
        object? err = args.Count > 1 ? args[1] : (args.Count == 1 && args[0] is not string ? args[0] : null);

        var report = BuildReport("WriteReport", err);
        filename ??= DefaultReportFilename();

        var reportObj = GetReportObject();
        string directory = reportObj.GetProperty("directory") as string ?? "";
        string path = directory.Length > 0 ? Path.Combine(directory, filename) : filename;

        File.WriteAllText(path, StringifyReport(report));
        return filename;
    }

    private static string DefaultReportFilename()
    {
        // Node pattern: report.YYYYMMDD.HHMMSS.pid.seq.json
        var now = DateTime.Now;
        int seq = Interlocked.Increment(ref _reportSequence);
        return $"report.{now:yyyyMMdd}.{now:HHmmss}.{Environment.ProcessId}.{seq:000}.json";
    }

    private static string StringifyReport(SharpTSObject report)
    {
        var stringify = JSONBuiltIns.GetStaticMethod("stringify") as BuiltInMethod;
        return stringify?.Call(null!, [report, null, 2.0]) as string ?? "{}";
    }

    /// <summary>
    /// Builds the diagnostic report object with the sections .NET can supply.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000",
        Justification = "process.report intentionally omits dynamic and bundled assemblies whose Location is empty.")]
    private static SharpTSObject BuildReport(string trigger, object? error)
    {
        var proc = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();

        var header = new SharpTSObject(new Dictionary<string, object?>
        {
            ["reportVersion"] = 3.0,
            ["event"] = trigger,
            ["trigger"] = trigger,
            ["filename"] = "",
            ["dumpEventTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["processId"] = (double)Environment.ProcessId,
            ["threadId"] = (double)Environment.CurrentManagedThreadId,
            ["cwd"] = Directory.GetCurrentDirectory(),
            ["commandLine"] = new SharpTSArray(Environment.GetCommandLineArgs().Select(a => (object?)a).ToList()),
            ["nodejsVersion"] = "v" + NodeVersion,
            ["wordSize"] = (double)(IntPtr.Size * 8),
            ["arch"] = GetArch(),
            ["platform"] = GetPlatform(),
            ["osName"] = RuntimeInformation.OSDescription,
            ["osRelease"] = Environment.OSVersion.Version.ToString(),
            ["osVersion"] = Environment.OSVersion.VersionString,
            ["osMachine"] = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            ["host"] = Environment.MachineName,
        });

        string stackMessage = "";
        string? stack = null;
        if (error is SharpTSError tsError)
        {
            stackMessage = tsError.Message;
            stack = tsError.Stack;
        }
        var stackLines = (stack ?? Environment.StackTrace)
            .Split('\n')
            .Select(l => (object?)l.Trim())
            .Where(l => ((string)l!).Length > 0)
            .ToList();

        var javascriptStack = new SharpTSObject(new Dictionary<string, object?>
        {
            ["message"] = stackMessage,
            ["stack"] = new SharpTSArray(stackLines),
        });

        var javascriptHeap = new SharpTSObject(new Dictionary<string, object?>
        {
            ["totalMemory"] = (double)gc.HeapSizeBytes,
            ["executableMemory"] = 0.0,
            ["totalCommittedMemory"] = (double)gc.TotalCommittedBytes,
            ["availableMemory"] = (double)Math.Max(0, gc.TotalAvailableMemoryBytes - gc.MemoryLoadBytes),
            ["usedMemory"] = (double)GC.GetTotalMemory(false),
            ["memoryLimit"] = (double)gc.TotalAvailableMemoryBytes,
        });

        var resourceUsage = new SharpTSObject(new Dictionary<string, object?>
        {
            ["userCpuSeconds"] = proc.UserProcessorTime.TotalSeconds,
            ["kernelCpuSeconds"] = proc.PrivilegedProcessorTime.TotalSeconds,
            ["cpuConsumptionPercent"] = 0.0,
            ["maxRss"] = (double)proc.PeakWorkingSet64,
            ["rss"] = (double)proc.WorkingSet64,
        });

        var environmentVariables = new Dictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environmentVariables[entry.Key?.ToString() ?? ""] = entry.Value?.ToString();
        }

        var sharedObjects = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (object?)a.Location)
            .ToList();

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["header"] = header,
            ["javascriptStack"] = javascriptStack,
            ["nativeStack"] = new SharpTSArray(new List<object?>()),
            ["javascriptHeap"] = javascriptHeap,
            ["resourceUsage"] = resourceUsage,
            ["libuv"] = new SharpTSArray(new List<object?>()),
            ["workers"] = new SharpTSArray(new List<object?>()),
            ["environmentVariables"] = new SharpTSObject(environmentVariables),
            ["userLimits"] = new SharpTSObject(new Dictionary<string, object?>()),
            ["sharedObjects"] = new SharpTSArray(sharedObjects),
        });
    }
}
