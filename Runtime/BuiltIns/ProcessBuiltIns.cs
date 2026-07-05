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
public static partial class ProcessBuiltIns
{
    /// <summary>
    /// The Node.js version SharpTS emulates. Surfaced as <c>process.version</c>
    /// (with a leading "v") and <c>process.versions.node</c> so feature-detection
    /// code sees the compatibility target rather than the CLR version.
    /// </summary>
    public const string NodeVersion = "24.15.0";

    // Cache static methods to avoid allocation on every access
    private static readonly BuiltInMethod _cwd = new("cwd", 0, Cwd);
    private static readonly BuiltInMethod _chdir = new("chdir", 1, Chdir);
    private static readonly BuiltInMethod _exit = new("exit", 0, 1, Exit);
    private static readonly BuiltInMethod _hrtime = CreateHrtimeMethod();
    private static readonly BuiltInMethod _uptime = new("uptime", 0, Uptime);
    private static readonly BuiltInMethod _memoryUsage = CreateMemoryUsageMethod();
    private static readonly BuiltInMethod _nextTick = new("nextTick", 1, int.MaxValue, NextTick);

    // Lazily create env and argv objects
    private static SharpTSObject? _envObject;
    private static SharpTSArray? _argvArray;

    // Per-thread overrides for cluster workers (#1170): a worker thread's script sees
    // its own process.argv (cluster.settings.args) and process.env (fork(env) merged
    // over the parent environment). A worker's interpreter event loop is confined to
    // its thread, so [ThreadStatic] is safe here (same contract as ClusterContext).
    [ThreadStatic] internal static SharpTSArray? ThreadArgv;
    [ThreadStatic] internal static SharpTSObject? ThreadEnv;

    // Script arguments set by the caller (for interpreted mode)
    private static string? _scriptPath;
    private static string[]? _scriptArgs;

    // Monotonic process-start baseline for uptime()/hrtime(). Stopwatch is monotonic
    // (QueryPerformanceCounter on Windows, CLOCK_MONOTONIC on Unix); wall-clock
    // DateTime.UtcNow is NOT — an NTP slew between two reads could make uptime() run
    // backwards, which intermittently failed Process_Uptime_IncreasesOverTime.
    private static readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private static readonly double _tickFrequency = Stopwatch.Frequency;

    // Script start as a monotonic Stopwatch timestamp — reset each time a script
    // begins execution. When set, uptime() reports time since script start rather
    // than process start.
    //
    // [ThreadStatic] because the in-process test runner shares this one CLR process
    // across many concurrently-running scripts (xUnit parallelizes test collections):
    // SetScriptArguments/ClearScriptArguments toggle this baseline (a recent timestamp
    // ↔ null) on each run, and a *shared* field let one script's SetScriptArguments land
    // between another script's two uptime() reads — the first read using the null →
    // process-start baseline (large elapsed), the second the just-set recent baseline
    // (near-zero elapsed) — so uptime() ran backwards and intermittently failed
    // Process_Uptime_IncreasesOverTime (`up2 >= up1`). Per-thread isolation gives each
    // script a stable baseline (same precedent as ThreadArgv/ThreadEnv above); the
    // Stopwatch switch earlier fixed clock monotonicity but not this baseline race. In a
    // real single-script process SetScriptArguments and the synchronous run share one
    // thread, so script-relative uptime is preserved; an uptime() read on an async
    // continuation thread falls back to the (process-start) baseline, differing only by
    // the milliseconds between process and script start.
    [ThreadStatic] private static long? _scriptStartTimestamp;

    /// <summary>
    /// Gets a member of the process object by name. Resolves process-specific
    /// members first, then falls back to the EventEmitter surface of the
    /// <see cref="SharpTSProcess"/> singleton.
    /// </summary>
    public static object? GetMember(string name)
    {
        // Core EventEmitter methods resolve against the singleton's raw
        // EventEmitter surface (GetEventEmitterMember, NOT the virtual
        // GetMember — SharpTSProcess.GetMember delegates back here).
        return GetOwnMember(name)
            ?? SharpTSProcess.Instance.GetEventEmitterMember(name);
    }

    /// <summary>
    /// Resolves process-specific members (data props, methods, IPC), excluding
    /// the inherited EventEmitter surface. Null means "not a process member".
    /// </summary>
    internal static object? GetOwnMember(string name)
    {
        return name switch
        {
            // Properties
            "platform" => GetPlatform(),
            "arch" => GetArch(),
            "pid" => (double)Environment.ProcessId,
            "version" => "v" + NodeVersion,
            "env" => GetEnv(),
            "argv" => GetArgv(),
            "exitCode" => (double)Environment.ExitCode,

            // Stream objects. Inside a worker thread, process.stdin resolves to that worker's
            // isolated per-worker Readable (#1076) rather than the Console-reading singleton, so a
            // worker never consumes the host terminal; the override is null on the main thread.
            "stdin" => (object?)WorkerThreads.WorkerStdin ?? SharpTSStdin.Instance,
            "stdout" => SharpTSStdout.Instance,
            "stderr" => SharpTSStderr.Instance,

            // Methods
            "cwd" => _cwd,
            "chdir" => _chdir,
            "exit" => _exit,
            "hrtime" => _hrtime,
            "uptime" => _uptime,
            "memoryUsage" => _memoryUsage,
            "nextTick" => _nextTick,

            // Cluster worker IPC methods
            "send" when ClusterContext.IsWorker => BuiltInMethod.CreateV2("send", 1, static (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("process.send() requires at least one argument");
                ClusterContext.CurrentWorker?.PostMessageToPrimary(args[0].ToObject());
                return RuntimeValue.True;
            }),
            "disconnect" when ClusterContext.IsWorker => BuiltInMethod.CreateV2("disconnect", 0, static (_, _, _) =>
            {
                // Signal disconnect to primary
                try { ClusterContext.PrimaryToWorkerQueue?.CompleteAdding(); } catch { }
                return RuntimeValue.Null;
            }),
            "connected" when ClusterContext.IsWorker => !ClusterContext.CancellationToken.IsCancellationRequested,

            // Fork IPC methods (child side of child_process.fork())
            "send" when ForkIpcClient.IsForkedChild => BuiltInMethod.CreateV2("send", 1, static (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("process.send() requires at least one argument");
                return RuntimeValue.FromBoxed(ForkIpcClient.Instance!.Send(args[0].ToObject()));
            }),
            "disconnect" when ForkIpcClient.IsForkedChild => BuiltInMethod.CreateV2("disconnect", 0, static (_, _, _) =>
            {
                ForkIpcClient.Instance?.Disconnect();
                return RuntimeValue.Null;
            }),
            "connected" when ForkIpcClient.IsForkedChild => ForkIpcClient.Instance?.Connected ?? false,
            "channel" when ForkIpcClient.IsForkedChild || ClusterContext.IsWorker => GetIpcChannel(),

            // Identity / info properties (#1085)
            "ppid" => (double)GetParentPid(),
            "title" => GetTitle(),
            "versions" => GetVersions(),
            "execPath" => Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
            "execArgv" => GetExecArgv(),
            "argv0" => Environment.GetCommandLineArgs()[0],
            "config" => GetConfig(),
            "release" => GetRelease(),
            "features" => GetFeatures(),
            "debugPort" => 9229.0,
            "allowedNodeEnvironmentFlags" => GetAllowedNodeEnvironmentFlags(),

            // Diagnostics methods (#1082)
            "cpuUsage" => _cpuUsage,
            "resourceUsage" => _resourceUsage,
            "availableMemory" => _availableMemory,
            "constrainedMemory" => _constrainedMemory,
            "getActiveResourcesInfo" => _getActiveResourcesInfo,

            // Warnings / deprecation flags / abort / umask (#1083)
            "emitWarning" => _emitWarning,
            "abort" => _abort,
            "umask" => _umask,
            "throwDeprecation" => ThrowDeprecation,
            "traceDeprecation" => TraceDeprecation,
            "noDeprecation" => NoDeprecation,
            "sourceMapsEnabled" => SourceMapsEnabled,
            "setSourceMapsEnabled" => _setSourceMapsEnabled,

            // Signals / kill (#1081)
            "kill" => _kill,

            // Diagnostic report (#1084)
            "report" => GetReportObject(),

            // POSIX identity (#1086) — undefined on Windows, matching Node
            "getuid" when !OperatingSystem.IsWindows() => _getuid,
            "geteuid" when !OperatingSystem.IsWindows() => _geteuid,
            "getgid" when !OperatingSystem.IsWindows() => _getgid,
            "getegid" when !OperatingSystem.IsWindows() => _getegid,
            "getgroups" when !OperatingSystem.IsWindows() => _getgroups,
            "setuid" when !OperatingSystem.IsWindows() => _setuid,
            "setgid" when !OperatingSystem.IsWindows() => _setgid,

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
    /// Sets the script path and arguments for process.argv.
    /// Call this before interpreting a script to populate process.argv correctly.
    /// </summary>
    /// <param name="scriptPath">The absolute path to the script being run.</param>
    /// <param name="args">The user-provided arguments to pass to the script.</param>
    public static void SetScriptArguments(string scriptPath, string[] args)
    {
        _scriptPath = scriptPath;
        _scriptArgs = args;
        _argvArray = null; // Clear cache to rebuild with new arguments
        _scriptStartTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Clears the script arguments. Call this for REPL mode or tests to reset state.
    /// </summary>
    public static void ClearScriptArguments()
    {
        _scriptPath = null;
        _scriptArgs = null;
        _argvArray = null;
        _scriptStartTimestamp = null;
    }

    /// <summary>
    /// Records the current time as the script start time for uptime() calculations.
    /// Called automatically when the interpreter begins execution.
    /// </summary>
    public static void ResetScriptStartTime()
    {
        _scriptStartTimestamp ??= Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Returns the process.env object containing environment variables.
    /// </summary>
    public static SharpTSObject GetEnv()
    {
        if (ThreadEnv != null)
            return ThreadEnv;
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
    /// Mimics Node.js argv format: [runtime_path, script_path, ...args]
    /// </summary>
    public static SharpTSArray GetArgv()
    {
        if (ThreadArgv != null)
            return ThreadArgv;
        if (_argvArray != null)
            return _argvArray;

        var elements = new List<object?>();

        // Node.js format: [node_path, script_path, ...user_args]
        // argv[0] = executable path (runtime)
        // argv[1] = script path
        // argv[2+] = user arguments

        // Always use ProcessPath for argv[0] (the runtime)
        var cmdArgs = Environment.GetCommandLineArgs();
        elements.Add(Environment.ProcessPath ?? cmdArgs[0]);

        // If script arguments were explicitly set (interpreted mode), use them
        if (_scriptPath != null)
        {
            elements.Add(_scriptPath);
            if (_scriptArgs != null)
            {
                foreach (var arg in _scriptArgs)
                {
                    elements.Add(arg);
                }
            }
        }
        else
        {
            // Fall back to command line args (for compiled mode or when not explicitly set)
            // Skip args[0] (the DLL path) since it's not user-relevant
            for (int i = 1; i < cmdArgs.Length; i++)
            {
                elements.Add(cmdArgs[i]);
            }
        }

        _argvArray = new SharpTSArray(elements);
        return _argvArray;
    }

    private static object? Cwd(Interpreter i, object? r, List<object?> args)
    {
        return Directory.GetCurrentDirectory();
    }

    private static object? Chdir(Interpreter i, object? r, List<object?> args)
    {
        if (args.Count > 0 && args[0] is string dir)
        {
            Directory.SetCurrentDirectory(dir);
        }
        return null;
    }

    private static object? Exit(Interpreter i, object? r, List<object?> args)
    {
        // process.exit() with no (or non-numeric) argument uses process.exitCode.
        int exitCode = Environment.ExitCode;
        if (args.Count > 0 && args[0] is double d)
        {
            exitCode = (int)d;
        }

        // Publish the code first so 'exit' listeners reading process.exitCode
        // observe the final value, then emit synchronously (Node semantics).
        Environment.ExitCode = exitCode;
        EmitExitEvent(i, exitCode);

        Environment.Exit(exitCode);
        return null; // Never reached
    }

    /// <summary>
    /// Emits the 'exit' event on the process object.
    /// Called before process.exit() and at natural program end.
    /// In Node.js, the exit event callback receives the exit code as argument.
    /// </summary>
    public static void EmitExitEvent(Interpreter? interpreter, int exitCode)
    {
        try
        {
            var process = SharpTSProcess.Instance;
            if (interpreter != null)
            {
                // Use interpreter-based emit for interpreted mode
                var emit = process.GetMember("emit") as BuiltInMethod;
                emit?.Bind(process).Call(interpreter, ["exit", (double)exitCode]);
            }
            else
            {
                // Use direct emit for compiled mode (no interpreter available)
                process.EmitDirect("exit", (double)exitCode);
            }
        }
        catch
        {
            // Node.js ignores errors in exit handlers
        }
    }

    /// <summary>
    /// Dispatches an EventEmitter method call on the process singleton.
    /// Used by compiled code to call process.on(), process.emit(), etc.
    /// </summary>
    /// <param name="methodName">The EventEmitter method name (on, once, emit, etc.).</param>
    /// <param name="args">The arguments to pass to the method.</param>
    /// <returns>The result of the method call.</returns>
    public static object? EventEmitterCall(string methodName, object?[] args)
    {
        var process = SharpTSProcess.Instance;

        // For "on", "once", "addListener": register listener directly
        switch (methodName)
        {
            case "on":
            case "addListener":
                if (args.Length >= 2 && args[0] is string onEvent)
                {
                    process.AddListenerDirect(onEvent, args[1]!, once: false);
                    return process;
                }
                return process;

            case "once":
                if (args.Length >= 2 && args[0] is string onceEvent)
                {
                    process.AddListenerDirect(onceEvent, args[1]!, once: true);
                    return process;
                }
                return process;

            case "emit":
                if (args.Length >= 1 && args[0] is string emitEvent)
                {
                    var emitArgs = args.Length > 1 ? args[1..] : [];
                    return process.EmitDirect(emitEvent, emitArgs);
                }
                return false;

            case "off":
            case "removeListener":
                // Delegate to GetMember for complex operations
                var offMethod = process.GetMember(methodName) as BuiltInMethod;
                if (offMethod != null)
                {
                    return offMethod.Bind(process).Call(null!, args.ToList<object?>());
                }
                return process;

            case "removeAllListeners":
                var removeMethod = process.GetMember(methodName) as BuiltInMethod;
                if (removeMethod != null)
                {
                    return removeMethod.Bind(process).Call(null!, args.ToList<object?>());
                }
                return process;

            case "listenerCount":
                var lcMethod = process.GetMember(methodName) as BuiltInMethod;
                if (lcMethod != null)
                {
                    return lcMethod.Bind(process).Call(null!, args.ToList<object?>());
                }
                return 0.0;

            case "listeners":
            case "eventNames":
            case "setMaxListeners":
            case "getMaxListeners":
            case "prependListener":
            case "prependOnceListener":
                var method = process.GetMember(methodName) as BuiltInMethod;
                if (method != null)
                {
                    return method.Bind(process).Call(null!, args.ToList<object?>());
                }
                return process;

            default:
                return null;
        }
    }

    /// <summary>
    /// Handles uncaught exceptions by emitting 'uncaughtException' on process.
    /// Returns true if the exception was handled (a listener was registered).
    /// </summary>
    public static bool EmitUncaughtException(Interpreter? interpreter, Exception exception)
    {
        try
        {
            var process = SharpTSProcess.Instance;
            // Create an Error-like object for the exception
            var errorObj = new SharpTSObject(new Dictionary<string, object?>
            {
                ["message"] = exception.Message,
                ["stack"] = exception.StackTrace ?? "",
                ["name"] = exception.GetType().Name
            });

            if (interpreter != null)
            {
                var emit = process.GetMember("emit") as BuiltInMethod;
                var result = emit?.Bind(process).Call(interpreter, ["uncaughtException", errorObj]);
                return result is true;
            }
            else
            {
                return process.EmitDirect("uncaughtException", errorObj);
            }
        }
        catch
        {
            return false;
        }
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
        if (args.Count > 0 && args[0] is SharpTSArray prev && prev.Length >= 2)
        {
            var prevSeconds = Convert.ToDouble(prev[0]);
            var prevNanos = Convert.ToDouble(prev[1]);
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
        // Monotonic: derived from Stopwatch so two reads never go backwards.
        long start = _scriptStartTimestamp ?? _startTimestamp;
        return (Stopwatch.GetTimestamp() - start) / _tickFrequency;
    }

    /// <summary>
    /// Returns an object describing the memory usage of the process.
    /// </summary>
    private static object? MemoryUsage(Interpreter i, object? r, List<object?> args)
    {
        var process = Process.GetCurrentProcess();
        var heap = (double)GC.GetTotalMemory(false);

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["rss"] = (double)process.WorkingSet64,
            ["heapTotal"] = heap,
            ["heapUsed"] = heap,
            ["external"] = 0.0,
            ["arrayBuffers"] = 0.0
        });
    }

    /// <summary>
    /// Sets a member of the process object by name (exitCode, title, and the
    /// deprecation flags). Returns false for names that are not process-managed
    /// setters so callers can fall back to expando storage.
    /// </summary>
    public static bool SetMember(string name, object? value)
    {
        switch (name)
        {
            case "exitCode":
                // Node accepts numbers (also null/undefined, meaning "unset" → 0).
                Environment.ExitCode = value is double d ? (int)d : 0;
                return true;
            case "title":
                SetTitle(value?.ToString() ?? "");
                return true;
            case "throwDeprecation":
                ThrowDeprecation = value is true;
                return true;
            case "traceDeprecation":
                TraceDeprecation = value is true;
                return true;
            case "noDeprecation":
                NoDeprecation = value is true;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// process.nextTick(callback, ...args) - schedules callback to run after the current operation.
    /// </summary>
    /// <remarks>
    /// In Node.js, nextTick callbacks run before any I/O events. Since SharpTS uses a simplified
    /// event loop, we implement this as a timer with 0 delay (similar to setImmediate).
    /// </remarks>
    private static object? NextTick(Interpreter interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: process.nextTick requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: process.nextTick callback must be a function");

        var callbackArgs = args.Count > 1
            ? args.Skip(1).ToList()
            : new List<object?>();

        // Schedule as a timer with 0 delay (runs as soon as possible)
        TimerBuiltIns.SetTimeout(interpreter, callback, 0, callbackArgs);

        // nextTick returns undefined (void)
        return null;
    }
}
