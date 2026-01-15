using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for Node.js process object members.
/// </summary>
/// <remarks>
/// Contains properties (platform, arch, pid, env, argv) and method implementations (cwd, exit)
/// that back the <c>process.x</c> syntax in TypeScript. Called by <see cref="Interpreter"/>
/// when resolving property access on <see cref="SharpTSProcess"/>. Methods are returned as
/// <see cref="BuiltInMethod"/> instances for uniform invocation.
/// </remarks>
/// <seealso cref="SharpTSProcess"/>
/// <seealso cref="BuiltInMethod"/>
public static class ProcessBuiltIns
{
    // Cache static methods to avoid allocation on every access
    private static readonly BuiltInMethod _cwd = new("cwd", 0, Cwd);
    private static readonly BuiltInMethod _exit = new("exit", 0, 1, Exit);
    private static readonly BuiltInMethod _hrtime = new("hrtime", 0, 1, Hrtime);
    private static readonly BuiltInMethod _uptime = new("uptime", 0, Uptime);
    private static readonly BuiltInMethod _memoryUsage = new("memoryUsage", 0, MemoryUsage);

    // Lazily create env and argv objects
    private static SharpTSObject? _envObject;
    private static SharpTSArray? _argvArray;

    // Process start time for uptime calculation
    private static readonly DateTime _processStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    // Stopwatch frequency for hrtime calculations
    private static readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private static readonly double _tickFrequency = Stopwatch.Frequency;

    /// <summary>
    /// Gets a member of the process object by name.
    /// </summary>
    public static object? GetMember(string name)
    {
        return name switch
        {
            // Properties
            "platform" => GetPlatform(),
            "arch" => GetArch(),
            "pid" => (double)Environment.ProcessId,
            "version" => "v" + Environment.Version.ToString(),
            "env" => GetEnv(),
            "argv" => GetArgv(),

            // Methods
            "cwd" => _cwd,
            "exit" => _exit,
            "hrtime" => _hrtime,
            "uptime" => _uptime,
            "memoryUsage" => _memoryUsage,

            _ => null
        };
    }

    /// <summary>
    /// Returns the operating system platform (win32, linux, darwin).
    /// </summary>
    public static string GetPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win32";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "darwin";
        return "unknown";
    }

    /// <summary>
    /// Returns the CPU architecture (x64, arm64, ia32, arm).
    /// </summary>
    public static string GetArch()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "ia32",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Returns the process.env object containing environment variables.
    /// </summary>
    public static SharpTSObject GetEnv()
    {
        if (_envObject != null)
            return _envObject;

        var fields = new Dictionary<string, object?>();
        var envVars = Environment.GetEnvironmentVariables();
        foreach (System.Collections.DictionaryEntry entry in envVars)
        {
            fields[entry.Key?.ToString() ?? ""] = entry.Value?.ToString();
        }
        _envObject = new SharpTSObject(fields);
        return _envObject;
    }

    /// <summary>
    /// Returns the process.argv array containing command line arguments.
    /// </summary>
    public static SharpTSArray GetArgv()
    {
        if (_argvArray != null)
            return _argvArray;

        var elements = new List<object?>();
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            elements.Add(arg);
        }
        _argvArray = new SharpTSArray(elements);
        return _argvArray;
    }

    private static object? Cwd(Interpreter i, object? r, List<object?> args)
    {
        return Directory.GetCurrentDirectory();
    }

    private static object? Exit(Interpreter i, object? r, List<object?> args)
    {
        int exitCode = 0;
        if (args.Count > 0 && args[0] is double d)
        {
            exitCode = (int)d;
        }
        Environment.Exit(exitCode);
        return null; // Never reached
    }

    /// <summary>
    /// Returns the current high-resolution real time in a [seconds, nanoseconds] tuple.
    /// If a previous hrtime result is passed, returns the difference.
    /// </summary>
    private static object? Hrtime(Interpreter i, object? r, List<object?> args)
    {
        long currentTicks = Stopwatch.GetTimestamp() - _startTimestamp;
        double totalNanoseconds = currentTicks * 1_000_000_000.0 / _tickFrequency;

        // If a previous time is provided, calculate the difference
        if (args.Count > 0 && args[0] is SharpTSArray prev && prev.Elements.Count >= 2)
        {
            var prevSeconds = Convert.ToDouble(prev.Elements[0]);
            var prevNanos = Convert.ToDouble(prev.Elements[1]);
            double prevTotalNanos = prevSeconds * 1_000_000_000.0 + prevNanos;
            totalNanoseconds -= prevTotalNanos;
        }

        double seconds = Math.Floor(totalNanoseconds / 1_000_000_000.0);
        double nanos = totalNanoseconds % 1_000_000_000.0;

        // Ensure non-negative values
        if (seconds < 0) seconds = 0;
        if (nanos < 0) nanos = 0;

        return new SharpTSArray([seconds, nanos]);
    }

    /// <summary>
    /// Returns the number of seconds the process has been running.
    /// </summary>
    private static object? Uptime(Interpreter i, object? r, List<object?> args)
    {
        return (DateTime.UtcNow - _processStartTime).TotalSeconds;
    }

    /// <summary>
    /// Returns an object describing the memory usage of the process.
    /// </summary>
    private static object? MemoryUsage(Interpreter i, object? r, List<object?> args)
    {
        var process = Process.GetCurrentProcess();

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["rss"] = (double)process.WorkingSet64,
            ["heapTotal"] = (double)GC.GetTotalMemory(false),
            ["heapUsed"] = (double)GC.GetTotalMemory(false),
            ["external"] = 0.0,
            ["arrayBuffers"] = 0.0
        });
    }
}
