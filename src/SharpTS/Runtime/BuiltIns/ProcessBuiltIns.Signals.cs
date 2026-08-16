using SharpTS.Execution;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Signal events and process.kill (#1081). Signal listeners are registered
/// lazily: the first process.on('SIGINT', …) creates a BCL
/// <see cref="PosixSignalRegistration"/> which cancels the default action and
/// re-emits the signal as a process event on the interpreter's event loop.
/// Registrations do not keep the event loop alive (Node parity).
/// </summary>
public static partial class ProcessBuiltIns
{
    private static readonly BuiltInMethod _kill = new("kill", 1, 2, Kill);

    private static readonly ConcurrentDictionary<string, PosixSignalRegistration> _signalRegistrations = new();

    /// <summary>
    /// The interpreter whose event loop should receive process-level events
    /// delivered from foreign threads (signals, lifecycle). Set by the CLI /
    /// test harness alongside <see cref="Execution.Interpreter.EmitProcessLifecycleEvents"/>;
    /// null falls back to direct (interpreter-less) emission.
    /// </summary>
    internal static Execution.Interpreter? DispatchInterpreter { get; set; }

    /// <summary>
    /// Node signal name → conventional signal number (used for kill's numeric
    /// form and the 128+n default exit code).
    /// </summary>
    private static readonly Dictionary<string, int> _signalNumbers = new()
    {
        ["SIGHUP"] = 1, ["SIGINT"] = 2, ["SIGQUIT"] = 3, ["SIGABRT"] = 6,
        ["SIGKILL"] = 9, ["SIGUSR1"] = 10, ["SIGUSR2"] = 12, ["SIGTERM"] = 15,
        ["SIGBREAK"] = 21, ["SIGWINCH"] = 28,
    };

    /// <summary>
    /// Called by <see cref="SharpTSProcess.OnListenerAdded"/> — lazily installs
    /// the OS handler when the event name is a supported signal.
    /// </summary>
    internal static void OnProcessListenerAdded(string eventName)
    {
        if (!TryMapPosixSignal(eventName, out var posixSignal))
            return;

        _signalRegistrations.GetOrAdd(eventName, name =>
        {
            try
            {
                return PosixSignalRegistration.Create(posixSignal, ctx =>
                {
                    ctx.Cancel = true; // listener installed → default action suppressed
                    DispatchSignal(name);
                });
            }
            catch (Exception)
            {
                // PlatformNotSupported / IO errors: behave as if never trapped.
                return null!;
            }
        });
    }

    /// <summary>
    /// Maps a Node signal name to the BCL PosixSignal, honoring the Windows
    /// console mapping (SIGBREAK arrives as CTRL_BREAK, which .NET surfaces as
    /// SIGQUIT on Windows).
    /// </summary>
    private static bool TryMapPosixSignal(string name, out PosixSignal signal)
    {
        switch (name)
        {
            case "SIGINT": signal = PosixSignal.SIGINT; return true;
            case "SIGTERM": signal = PosixSignal.SIGTERM; return true;
            case "SIGHUP": signal = PosixSignal.SIGHUP; return true;
            case "SIGQUIT": signal = PosixSignal.SIGQUIT; return true;
            case "SIGBREAK" when OperatingSystem.IsWindows(): signal = PosixSignal.SIGQUIT; return true;
            case "SIGWINCH": signal = PosixSignal.SIGWINCH; return true;
            case "SIGCHLD": signal = PosixSignal.SIGCHLD; return true;
            case "SIGCONT": signal = PosixSignal.SIGCONT; return true;
            default: signal = default; return false;
        }
    }

    /// <summary>
    /// Emits a signal event on the process singleton, marshaled onto an
    /// interpreter's event loop. OS-delivered signals arrive on foreign
    /// threads and use <see cref="DispatchInterpreter"/>; process.kill passes
    /// its own calling interpreter so concurrent interpreters (test hosts)
    /// never cross-deliver.
    /// </summary>
    private static void DispatchSignal(string signalName, Execution.Interpreter? preferred = null)
    {
        var interp = preferred ?? DispatchInterpreter;
        if (interp == null)
        {
            SharpTSProcess.Instance.EmitDirect(signalName, signalName);
            return;
        }

        try
        {
            interp.EnqueueCallback(() =>
                SharpTSProcess.Instance.EmitWith(interp, signalName, signalName));
        }
        catch
        {
            // Loop already completed — deliver directly rather than dropping.
            SharpTSProcess.Instance.EmitDirect(signalName, signalName);
        }
    }

    /// <summary>
    /// process.kill(pid[, signal]) — signal 0 is an existence check; a signal
    /// sent to the current pid with listeners installed dispatches in-process;
    /// termination signals to other pids map to Process.Kill (documented
    /// divergence: the BCL cannot deliver arbitrary POSIX signals).
    /// </summary>
    private static object? Kill(Interpreter interp, object? r, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not double pidDouble)
            throw new Exceptions.ThrowException(
                new SharpTSTypeError("The \"pid\" argument must be of type number."));

        int pid = (int)pidDouble;

        // Sandboxed hosts can forbid cross-process signaling while retaining
        // ordinary self-signals/process.exit behavior inside the worker. The
        // AppContext switch is process-global host state and cannot be changed
        // through SharpTS's process.env surface.
        if (AppContext.TryGetSwitch("SharpTS.RestrictProcessControl", out bool restricted) &&
            restricted && pid != Environment.ProcessId)
        {
            throw new Exceptions.ThrowException(
                new SharpTSError("kill EPERM: cross-process signaling is disabled by the host")
                { Code = "EPERM" });
        }

        // Signal 0: existence check, no signal delivered.
        if (args.Count > 1 && args[1] is double dz && (int)dz == 0)
        {
            EnsureProcessExists(pid);
            return true;
        }

        string signalName = "SIGTERM";
        if (args.Count > 1 && args[1] != null && args[1] is not SharpTSUndefined)
        {
            signalName = args[1] switch
            {
                string s => s,
                double n => _signalNumbers.FirstOrDefault(kv => kv.Value == (int)n).Key
                    ?? throw new Exceptions.ThrowException(
                        new SharpTSError($"Unknown signal: {(int)n}") { Code = "ERR_UNKNOWN_SIGNAL" }),
                _ => throw new Exceptions.ThrowException(
                    new SharpTSTypeError("The \"signal\" argument must be of type string or number.")),
            };
        }

        if (!_signalNumbers.ContainsKey(signalName))
            throw new Exceptions.ThrowException(
                new SharpTSError($"Unknown signal: {signalName}") { Code = "ERR_UNKNOWN_SIGNAL" });

        if (pid == Environment.ProcessId)
        {
            // Self-signal: with listeners → dispatch the event (default action is
            // suppressed once a handler exists); without → Node's default action.
            if (SharpTSProcess.Instance.HasListenersInternal(signalName))
            {
                DispatchSignal(signalName, interp);
                return true;
            }
            if (signalName is "SIGINT" or "SIGTERM" or "SIGHUP" or "SIGQUIT" or "SIGBREAK" or "SIGKILL")
            {
                ProcessControl.Exit(128 + _signalNumbers[signalName]);
            }
            return true; // Untrappable-default-ignore signals (e.g. SIGWINCH)
        }

        // Cross-process: existence check + terminate for termination signals.
        var target = EnsureProcessExists(pid);
        if (signalName is "SIGKILL" or "SIGTERM" or "SIGINT" or "SIGQUIT" or "SIGHUP" or "SIGBREAK")
        {
            try { target.Kill(); }
            catch (Exception ex)
            {
                throw new Exceptions.ThrowException(
                    new SharpTSError($"kill EPERM: {ex.Message}") { Code = "EPERM" });
            }
        }
        return true;
    }

    private static Process EnsureProcessExists(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            throw new Exceptions.ThrowException(
                new SharpTSError($"kill ESRCH") { Code = "ESRCH" });
        }
    }

    /// <summary>
    /// Test-support reset: disposes signal registrations and clears process
    /// listeners/state so runs in a shared test process stay isolated.
    /// </summary>
    public static void ResetProcessState()
    {
        foreach (var key in _signalRegistrations.Keys.ToList())
        {
            if (_signalRegistrations.TryRemove(key, out var reg))
            {
                try { reg?.Dispose(); } catch { }
            }
        }
        SharpTSProcess.Instance.ClearAllListenersInternal();
        SharpTSProcess.Instance.ClearExpando();
        DispatchInterpreter = null;
        ThrowDeprecation = false;
        TraceDeprecation = false;
        NoDeprecation = false;
        SourceMapsEnabled = false;
        _title = null;
    }
}
