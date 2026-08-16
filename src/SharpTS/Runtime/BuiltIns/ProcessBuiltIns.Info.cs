using SharpTS.Execution;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Identity / info properties for the process object (#1085): ppid, title,
/// versions, execPath/execArgv/argv0, config, release, features, debugPort,
/// allowedNodeEnvironmentFlags, plus the IPC channel object (#1086).
/// </summary>
public static partial class ProcessBuiltIns
{
    private static string? _title;
    private static SharpTSObject? _versionsObject;
    private static SharpTSArray? _execArgvArray;
    private static SharpTSObject? _configObject;
    private static SharpTSObject? _releaseObject;
    private static SharpTSObject? _featuresObject;
    private static SharpTSSet? _allowedFlagsSet;
    private static SharpTSObject? _ipcChannelObject;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength,
        out int returnLength);

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int PosixGetPpid();

    /// <summary>
    /// Returns the parent process id. Windows: NtQueryInformationProcess;
    /// POSIX: getppid(2). Returns 0 when the platform query fails.
    /// </summary>
    public static int GetParentPid()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new ProcessBasicInformation();
                int status = NtQueryInformationProcess(
                    Process.GetCurrentProcess().Handle, 0, ref info,
                    Marshal.SizeOf<ProcessBasicInformation>(), out _);
                return status == 0 ? (int)info.InheritedFromUniqueProcessId : 0;
            }
            return PosixGetPpid();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// process.title getter — an explicitly assigned title wins; otherwise the
    /// console title (Windows) or the executable name.
    /// </summary>
    public static string GetTitle()
    {
        if (_title != null) return _title;
        try
        {
            if (OperatingSystem.IsWindows() && Console.Title is { Length: > 0 } t)
                return t;
        }
        catch { /* no console attached */ }
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "sharpts"; }
    }

    /// <summary>
    /// process.title setter — stores the title and best-effort syncs the console
    /// window title (Windows only; setting is silently ignored elsewhere, like Node).
    /// </summary>
    public static void SetTitle(string title)
    {
        _title = title;
        try
        {
            if (OperatingSystem.IsWindows())
                Console.Title = title;
        }
        catch { /* no console attached */ }
    }

    /// <summary>
    /// process.versions — the emulated Node version plus SharpTS/.NET identity.
    /// </summary>
    private static SharpTSObject GetVersions()
    {
        return _versionsObject ??= new SharpTSObject(new Dictionary<string, object?>
        {
            ["node"] = NodeVersion,
            ["sharpts"] = typeof(ProcessBuiltIns).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            ["dotnet"] = Environment.Version.ToString(),
        });
    }

    private static SharpTSArray GetExecArgv()
    {
        // SharpTS has no runtime flags today; Node returns [] when launched plain.
        return _execArgvArray ??= new SharpTSArray(new List<object?>());
    }

    private static SharpTSObject GetConfig()
    {
        return _configObject ??= new SharpTSObject(new Dictionary<string, object?>
        {
            ["target_defaults"] = new SharpTSObject(new Dictionary<string, object?>()),
            ["variables"] = new SharpTSObject(new Dictionary<string, object?>
            {
                ["host_arch"] = GetArch(),
                ["target_arch"] = GetArch(),
                ["node_module_version"] = 0.0,
            }),
        });
    }

    private static SharpTSObject GetRelease()
    {
        // name is 'node' for ecosystem compatibility (libraries gate on it);
        // SharpTS identifies itself via process.versions.sharpts.
        return _releaseObject ??= new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = "node",
            ["sourceUrl"] = "",
            ["headersUrl"] = "",
        });
    }

    private static SharpTSObject GetFeatures()
    {
        return _featuresObject ??= new SharpTSObject(new Dictionary<string, object?>
        {
            ["inspector"] = false,
            ["debug"] = false,
            ["uv"] = true,
            ["ipv6"] = true,
            ["tls"] = true,
            ["tls_alpn"] = true,
            ["tls_sni"] = true,
            ["tls_ocsp"] = false,
            ["cached_builtins"] = true,
            ["typescript"] = "strip",
        });
    }

    private static SharpTSSet GetAllowedNodeEnvironmentFlags()
    {
        // SharpTS honors no NODE_OPTIONS flags, so the (Set-like) collection is
        // genuinely empty — has() correctly answers false for every flag.
        return _allowedFlagsSet ??= new SharpTSSet();
    }

    /// <summary>
    /// process.channel — a minimal control object exposed while an IPC channel
    /// (child_process.fork or a cluster worker) is open. ref()/unref() are
    /// accepted no-ops; the channel's lifetime is managed by the IPC client.
    /// </summary>
    private static SharpTSObject GetIpcChannel()
    {
        return _ipcChannelObject ??= new SharpTSObject(new Dictionary<string, object?>
        {
            ["ref"] = BuiltInMethod.CreateV2("ref", 0, static (_, _, _) => RuntimeValue.Undefined),
            ["unref"] = BuiltInMethod.CreateV2("unref", 0, static (_, _, _) => RuntimeValue.Undefined),
        });
    }
}
