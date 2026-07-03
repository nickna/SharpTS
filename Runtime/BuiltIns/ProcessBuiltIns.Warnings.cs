using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// process.emitWarning, the deprecation flags, process.abort and process.umask
/// (#1083), plus the sourceMapsEnabled flag pair.
/// </summary>
public static partial class ProcessBuiltIns
{
    /// <summary>Honored by emitWarning for DeprecationWarning: throw instead of warn.</summary>
    public static bool ThrowDeprecation { get; set; }

    /// <summary>Honored by emitWarning for DeprecationWarning: print a stack trace.</summary>
    public static bool TraceDeprecation { get; set; }

    /// <summary>Honored by emitWarning for DeprecationWarning: suppress entirely.</summary>
    public static bool NoDeprecation { get; set; }

    /// <summary>process.sourceMapsEnabled — SharpTS has no V8 source-map support; settable no-op.</summary>
    public static bool SourceMapsEnabled { get; set; }

    private static readonly BuiltInMethod _emitWarning = new("emitWarning", 1, 4, EmitWarning);
    private static readonly BuiltInMethod _abort = new("abort", 0, Abort);
    private static readonly BuiltInMethod _umask = new("umask", 0, 1, Umask);
    private static readonly BuiltInMethod _setSourceMapsEnabled = new("setSourceMapsEnabled", 1,
        static (_, _, args) => { SourceMapsEnabled = args.Count > 0 && args[0] is true; return null; });

    // Windows has no umask; keep a process-local value (default 0o22) so
    // get/set round-trips consistently. POSIX uses the real umask(2).
    private static int _storedUmask = 0x12; // 0o22

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "umask")]
    private static extern uint PosixUmask(uint mask);

    /// <summary>
    /// process.emitWarning(warning[, options | type[, code[, ctor]]]) —
    /// constructs the warning, emits 'warning' on the process (next tick), and
    /// prints the Node-style default line to stderr. DeprecationWarning honors
    /// noDeprecation (suppress) and throwDeprecation (throw).
    /// </summary>
    private static object? EmitWarning(Interpreter interp, object? r, List<object?> args)
    {
        object? warning = args.Count > 0 ? args[0] : null;
        string type = "Warning";
        string? code = null;

        // Parse options: (warning, { type, code }) or (warning, type[, code[, ctor]])
        if (args.Count > 1)
        {
            if (args[1] is SharpTSObject options && args[1] is not null)
            {
                if (options.GetProperty("type") is string t) type = t;
                if (options.GetProperty("code") is string c) code = c;
            }
            else if (args[1] is string typeArg)
            {
                type = typeArg;
                if (args.Count > 2 && args[2] is string codeArg) code = codeArg;
            }
        }

        SharpTSObject warningObject;
        if (warning is SharpTSError err)
        {
            warningObject = new SharpTSObject(new Dictionary<string, object?>
            {
                ["name"] = string.IsNullOrEmpty(err.Name) ? "Warning" : err.Name,
                ["message"] = err.Message,
                ["stack"] = err.Stack ?? "",
            });
            if (code != null) warningObject.SetProperty("code", code);
        }
        else if (warning is SharpTSObject warnObj)
        {
            warningObject = warnObj;
            if (code != null && warningObject.GetProperty("code") == null)
                warningObject.SetProperty("code", code);
        }
        else
        {
            warningObject = new SharpTSObject(new Dictionary<string, object?>
            {
                ["name"] = type,
                ["message"] = warning?.ToString() ?? "",
                ["stack"] = $"{type}: {warning}",
            });
            if (code != null) warningObject.SetProperty("code", code);
        }

        string name = warningObject.GetProperty("name") as string ?? type;
        string message = warningObject.GetProperty("message") as string ?? "";

        bool isDeprecation = name == "DeprecationWarning";
        if (isDeprecation && NoDeprecation)
            return null;
        if (isDeprecation && ThrowDeprecation)
            throw new Exceptions.ThrowException(warningObject);

        // Default handler prints to stderr regardless of listeners (Node prints
        // unless --no-warnings, which SharpTS doesn't model).
        string codePart = warningObject.GetProperty("code") is string wc ? $"[{wc}] " : "";
        interp.Error.WriteLine($"(node:{Environment.ProcessId}) {codePart}{name}: {message}");
        if (isDeprecation && TraceDeprecation && warningObject.GetProperty("stack") is string stack)
            interp.Error.WriteLine(stack);

        // Emit 'warning' asynchronously (Node emits on the next tick).
        interp.EnqueueCallback(() =>
            SharpTSProcess.Instance.EmitWith(interp, "warning", warningObject));

        return null;
    }

    private static object? Abort(Interpreter i, object? r, List<object?> args)
    {
        // Abnormal termination — no 'exit' event, nonzero exit, like SIGABRT.
        Environment.FailFast("process.abort() called");
        return null; // Never reached
    }

    /// <summary>
    /// process.umask([mask]) — get, or set and return the previous mask.
    /// Real umask(2) on POSIX; process-local stored value on Windows.
    /// </summary>
    private static object? Umask(Interpreter i, object? r, List<object?> args)
    {
        bool set = args.Count > 0 && args[0] != null && args[0] is not SharpTSUndefined;
        int newMask = 0;
        if (set)
        {
            newMask = args[0] switch
            {
                double d => (int)d,
                string s => ParseMaskString(s),
                _ => throw new Exceptions.ThrowException(
                    new SharpTSTypeError("The \"mask\" argument must be of type number or string.")),
            };
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                if (set) return (double)PosixUmask((uint)newMask);
                // Read-only query: set 0, then restore (the same trick libuv uses).
                uint current = PosixUmask(0);
                PosixUmask(current);
                return (double)current;
            }
            catch
            {
                // libc unavailable (unlikely) — fall through to the stored value.
            }
        }

        int previous = _storedUmask;
        if (set) _storedUmask = newMask;
        return (double)previous;
    }

    private static int ParseMaskString(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0o") || s.StartsWith("0O")) s = s[2..];
        return Convert.ToInt32(s.Length == 0 ? "0" : s, 8);
    }
}
