using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'process' module.
/// </summary>
/// <remarks>
/// Wraps the existing ProcessBuiltIns to make process available as an importable module.
/// This enables: import process from 'process' and import { argv, env } from 'process'
/// </remarks>
public static class ProcessModuleInterpreter
{
    // Cache method wrappers
    private static readonly BuiltInMethod _cwd = new("cwd", 0, Cwd);
    private static readonly BuiltInMethod _chdir = new("chdir", 1, Chdir);
    private static readonly BuiltInMethod _exit = new("exit", 0, 1, Exit);
    private static readonly BuiltInMethod _hrtime = new("hrtime", 0, 1, Hrtime);
    private static readonly BuiltInMethod _uptime = new("uptime", 0, Uptime);
    private static readonly BuiltInMethod _memoryUsage = new("memoryUsage", 0, MemoryUsage);
    private static readonly BuiltInMethod _nextTick = new("nextTick", 1, int.MaxValue, NextTick);

    /// <summary>
    /// Gets all exported values for the process module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Properties (delegate to ProcessBuiltIns)
            ["platform"] = ProcessBuiltIns.GetPlatform(),
            ["arch"] = ProcessBuiltIns.GetArch(),
            ["pid"] = (double)Environment.ProcessId,
            ["version"] = "v" + Environment.Version.ToString(),
            ["env"] = ProcessBuiltIns.GetEnv(),
            ["argv"] = ProcessBuiltIns.GetArgv(),
            ["exitCode"] = (double)Environment.ExitCode,

            // Stream objects
            ["stdin"] = SharpTSStdin.Instance,
            ["stdout"] = SharpTSStdout.Instance,
            ["stderr"] = SharpTSStderr.Instance,

            // Methods
            ["cwd"] = _cwd,
            ["chdir"] = _chdir,
            ["exit"] = _exit,
            ["hrtime"] = _hrtime,
            ["uptime"] = _uptime,
            ["memoryUsage"] = _memoryUsage,
            ["nextTick"] = _nextTick
        };
    }

    private static object? Cwd(Interp interpreter, object? receiver, List<object?> args)
    {
        return Directory.GetCurrentDirectory();
    }

    private static object? Chdir(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count > 0 && args[0] is string dir)
        {
            Directory.SetCurrentDirectory(dir);
        }
        return null;
    }

    private static object? Exit(Interp interpreter, object? receiver, List<object?> args)
    {
        int exitCode = 0;
        if (args.Count > 0 && args[0] is double d)
        {
            exitCode = (int)d;
        }
        Environment.Exit(exitCode);
        return null;
    }

    private static object? Hrtime(Interp interpreter, object? receiver, List<object?> args)
    {
        // Delegate to ProcessBuiltIns via GetMember to use cached implementation
        var method = ProcessBuiltIns.GetMember("hrtime") as BuiltInMethod;
        return method?.Call(interpreter, args);
    }

    private static object? Uptime(Interp interpreter, object? receiver, List<object?> args)
    {
        var method = ProcessBuiltIns.GetMember("uptime") as BuiltInMethod;
        return method?.Call(interpreter, args);
    }

    private static object? MemoryUsage(Interp interpreter, object? receiver, List<object?> args)
    {
        var method = ProcessBuiltIns.GetMember("memoryUsage") as BuiltInMethod;
        return method?.Call(interpreter, args);
    }

    /// <summary>
    /// process.nextTick(callback, ...args) - schedules callback to run after the current operation.
    /// </summary>
    /// <remarks>
    /// In Node.js, nextTick callbacks run before any I/O events. Since SharpTS uses a simplified
    /// event loop, we implement this as a timer with 0 delay (similar to setImmediate).
    /// </remarks>
    private static object? NextTick(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: process.nextTick requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: process.nextTick callback must be a function");

        var callbackArgs = args.Count > 1
            ? args.Skip(1).ToList()
            : new List<object?>();

        // Schedule as a timer with 0 delay (runs as soon as possible)
        // This matches Node.js behavior where nextTick runs after current operation
        TimerBuiltIns.SetTimeout(interpreter, callback, 0, callbackArgs);

        // nextTick returns undefined (void)
        return null;
    }
}
