using SharpTS.Execution;
using System.Diagnostics;
using System.Numerics;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Diagnostics methods for the process object (#1082): cpuUsage, resourceUsage,
/// hrtime.bigint, availableMemory, constrainedMemory, getActiveResourcesInfo,
/// and memoryUsage.rss.
/// </summary>
public static partial class ProcessBuiltIns
{
    private static readonly BuiltInMethod _cpuUsage = new("cpuUsage", 0, 1, CpuUsage);
    private static readonly BuiltInMethod _resourceUsage = new("resourceUsage", 0, ResourceUsage);
    private static readonly BuiltInMethod _availableMemory = new("availableMemory", 0, AvailableMemory);
    private static readonly BuiltInMethod _constrainedMemory = new("constrainedMemory", 0, ConstrainedMemory);
    private static readonly BuiltInMethod _getActiveResourcesInfo = new("getActiveResourcesInfo", 0, GetActiveResourcesInfo);

    /// <summary>
    /// Builds the hrtime method with its <c>bigint</c> own-property, mirroring
    /// Node's function-with-members shape (process.hrtime.bigint()).
    /// </summary>
    private static BuiltInMethod CreateHrtimeMethod()
    {
        return new BuiltInMethod("hrtime", 0, 1, Hrtime).WithOwnProperties(new Dictionary<string, object?>
        {
            ["bigint"] = new BuiltInMethod("bigint", 0, HrtimeBigint),
        });
    }

    /// <summary>
    /// Builds the memoryUsage method with its <c>rss</c> own-property
    /// (process.memoryUsage.rss()).
    /// </summary>
    private static BuiltInMethod CreateMemoryUsageMethod()
    {
        return new BuiltInMethod("memoryUsage", 0, MemoryUsage).WithOwnProperties(new Dictionary<string, object?>
        {
            ["rss"] = new BuiltInMethod("rss", 0, static (_, _, _) =>
                (double)Process.GetCurrentProcess().WorkingSet64),
        });
    }

    /// <summary>
    /// Monotonic nanoseconds since process start, as a BigInt (never negative,
    /// never runs backwards — Stopwatch-based like hrtime).
    /// </summary>
    public static BigInteger HrtimeBigintValue()
    {
        long ticks = Stopwatch.GetTimestamp() - _startTimestamp;
        // ticks * 1e9 / freq without double rounding for large values.
        return new BigInteger(ticks) * 1_000_000_000 / new BigInteger(Stopwatch.Frequency);
    }

    private static object? HrtimeBigint(Interpreter i, object? r, List<object?> args)
        => new SharpTSBigInt(HrtimeBigintValue());

    /// <summary>
    /// process.cpuUsage([previousValue]) → { user, system } in microseconds.
    /// </summary>
    private static object? CpuUsage(Interpreter i, object? r, List<object?> args)
    {
        var proc = Process.GetCurrentProcess();
        double user = proc.UserProcessorTime.Ticks / 10.0;        // 100ns ticks → µs
        double system = proc.PrivilegedProcessorTime.Ticks / 10.0;

        if (args.Count > 0 && args[0] is SharpTSObject prev)
        {
            if (prev.GetProperty("user") is double prevUser) user -= prevUser;
            if (prev.GetProperty("system") is double prevSystem) system -= prevSystem;
        }

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["user"] = Math.Max(0, user),
            ["system"] = Math.Max(0, system),
        });
    }

    /// <summary>
    /// process.resourceUsage() — the Node shape, populated where .NET exposes a
    /// value (CPU times, maxRSS); libuv-specific counters report 0.
    /// </summary>
    private static object? ResourceUsage(Interpreter i, object? r, List<object?> args)
    {
        var proc = Process.GetCurrentProcess();
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["userCPUTime"] = proc.UserProcessorTime.Ticks / 10.0,
            ["systemCPUTime"] = proc.PrivilegedProcessorTime.Ticks / 10.0,
            ["maxRSS"] = proc.PeakWorkingSet64 / 1024.0, // kilobytes, like Node
            ["sharedMemorySize"] = 0.0,
            ["unsharedDataSize"] = 0.0,
            ["unsharedStackSize"] = 0.0,
            ["minorPageFault"] = 0.0,
            ["majorPageFault"] = 0.0,
            ["swappedOut"] = 0.0,
            ["fsRead"] = 0.0,
            ["fsWrite"] = 0.0,
            ["ipcSent"] = 0.0,
            ["ipcReceived"] = 0.0,
            ["signalsCount"] = 0.0,
            ["voluntaryContextSwitches"] = 0.0,
            ["involuntaryContextSwitches"] = 0.0,
        });
    }

    /// <summary>
    /// process.availableMemory() — free memory available to the process, from
    /// the GC's view of the machine/container limit minus the committed load.
    /// </summary>
    private static object? AvailableMemory(Interpreter i, object? r, List<object?> args)
    {
        var info = GC.GetGCMemoryInfo();
        double available = info.TotalAvailableMemoryBytes - info.MemoryLoadBytes;
        return Math.Max(0, available);
    }

    /// <summary>
    /// process.constrainedMemory() — the memory limit imposed on the process,
    /// or 0 when none is known (Node returns 0 in the unconstrained case too).
    /// </summary>
    private static object? ConstrainedMemory(Interpreter i, object? r, List<object?> args)
    {
        // GCMemoryInfo reflects cgroup/job limits in TotalAvailableMemoryBytes,
        // but is indistinguishable from physical RAM when unconstrained; report
        // 0 (no known constraint) — documented ceiling.
        return 0.0;
    }

    /// <summary>
    /// process.getActiveResourcesInfo() — approximation: one "Timeout" entry per
    /// active event-loop handle (timers, servers, and sockets all count).
    /// </summary>
    private static object? GetActiveResourcesInfo(Interpreter i, object? r, List<object?> args)
    {
        int count = i?.ActiveHandleCount ?? 0;
        var entries = new List<object?>(count);
        for (int n = 0; n < count; n++) entries.Add("Timeout");
        return new SharpTSArray(entries);
    }
}
