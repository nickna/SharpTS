using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the <c>primitive:process</c> module.
/// </summary>
/// <remarks>
/// Every export delegates to <see cref="ProcessBuiltIns"/> so the module facade
/// (stdlib/node/process.ts) and the bare global expose the same behavior from a
/// single source of truth. <c>processObject</c> is the live
/// <see cref="SharpTSProcess"/> singleton — the facade re-exports it as the
/// module's default export, making <c>import process from 'process'</c>
/// identical (same object) to the global <c>process</c> (#1079).
/// </remarks>
public static class ProcessModuleInterpreter
{
    /// <summary>
    /// Names re-exported from the shared process surface. POSIX-only members
    /// (getuid etc.) resolve to null on Windows and surface as undefined —
    /// matching Node, where process.getuid does not exist on Windows.
    /// </summary>
    private static readonly string[] _exportedNames =
    [
        // Data properties
        "platform", "arch", "pid", "ppid", "version", "versions", "env",
        "argv", "argv0", "execPath", "execArgv", "exitCode", "title",
        "config", "release", "features", "debugPort", "allowedNodeEnvironmentFlags",
        "stdin", "stdout", "stderr", "report",
        "throwDeprecation", "traceDeprecation", "noDeprecation", "sourceMapsEnabled",
        // IPC (forked child / cluster worker only)
        "connected", "channel", "send", "disconnect",
        // POSIX identity
        "getuid", "geteuid", "getgid", "getegid", "getgroups", "setuid", "setgid",
        // Methods
        "cwd", "chdir", "exit", "hrtime", "uptime", "memoryUsage", "nextTick",
        "kill", "abort", "umask", "cpuUsage", "resourceUsage",
        "availableMemory", "constrainedMemory", "getActiveResourcesInfo",
        "emitWarning", "setSourceMapsEnabled",
        // EventEmitter surface
        "on", "addListener", "once", "off", "removeListener", "emit",
        "removeAllListeners", "listeners", "rawListeners", "listenerCount",
        "eventNames", "prependListener", "prependOnceListener",
        "setMaxListeners", "getMaxListeners",
    ];

    /// <summary>
    /// Gets all exported values for the process module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        var exports = new Dictionary<string, object?>
        {
            // The live process object (module default export === the global).
            ["processObject"] = SharpTSProcess.Instance,
        };
        foreach (var name in _exportedNames)
        {
            exports[name] = ProcessBuiltIns.GetMember(name);
        }
        return exports;
    }
}
