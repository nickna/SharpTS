using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'os' module.
/// </summary>
/// <remarks>
/// Provides runtime values for operating system information functions.
/// Wraps .NET's System.Runtime.InteropServices and Environment classes.
/// </remarks>
public static class OsModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the os module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Methods
            ["platform"] = new BuiltInMethod("platform", 0, 0, Platform),
            ["arch"] = new BuiltInMethod("arch", 0, 0, Arch),
            ["hostname"] = new BuiltInMethod("hostname", 0, 0, Hostname),
            ["homedir"] = new BuiltInMethod("homedir", 0, 0, Homedir),
            ["tmpdir"] = new BuiltInMethod("tmpdir", 0, 0, Tmpdir),
            ["type"] = new BuiltInMethod("type", 0, 0, Type),
            ["release"] = new BuiltInMethod("release", 0, 0, Release),
            ["cpus"] = new BuiltInMethod("cpus", 0, 0, Cpus),
            ["totalmem"] = new BuiltInMethod("totalmem", 0, 0, Totalmem),
            ["freemem"] = new BuiltInMethod("freemem", 0, 0, Freemem),
            ["userInfo"] = new BuiltInMethod("userInfo", 0, 0, UserInfo),

            // Properties
            ["EOL"] = Environment.NewLine
        };
    }

    private static object? Platform(Interp interpreter, object? receiver, List<object?> args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win32";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "darwin";
        return "unknown";
    }

    private static object? Arch(Interp interpreter, object? receiver, List<object?> args)
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

    private static object? Hostname(Interp interpreter, object? receiver, List<object?> args)
    {
        return Environment.MachineName;
    }

    private static object? Homedir(Interp interpreter, object? receiver, List<object?> args)
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static object? Tmpdir(Interp interpreter, object? receiver, List<object?> args)
    {
        return Path.GetTempPath();
    }

    private static object? Type(Interp interpreter, object? receiver, List<object?> args)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows_NT";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "Darwin";
        return "Unknown";
    }

    private static object? Release(Interp interpreter, object? receiver, List<object?> args)
    {
        return Environment.OSVersion.VersionString;
    }

    private static object? Cpus(Interp interpreter, object? receiver, List<object?> args)
    {
        var count = Environment.ProcessorCount;
        var list = new List<object?>();
        for (int i = 0; i < count; i++)
        {
            list.Add(new SharpTSObject(new Dictionary<string, object?>
            {
                ["model"] = "cpu",
                ["speed"] = 0.0
            }));
        }
        return new SharpTSArray(list);
    }

    private static object? Totalmem(Interp interpreter, object? receiver, List<object?> args)
    {
        var info = GC.GetGCMemoryInfo();
        return (double)info.TotalAvailableMemoryBytes;
    }

    private static object? Freemem(Interp interpreter, object? receiver, List<object?> args)
    {
        return (double)GetFreeMemoryBytes();
    }

    /// <summary>
    /// Gets the actual free system memory in bytes using platform-specific APIs.
    /// </summary>
    private static long GetFreeMemoryBytes()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsFreeMemory();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxFreeMemory();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacOSFreeMemory();
        }

        // Fallback: use GC info (less accurate)
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes - info.HeapSizeBytes;
    }

    private static long GetWindowsFreeMemory()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            return (long)memStatus.ullAvailPhys;
        }
        // Fallback
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes - info.HeapSizeBytes;
    }

    private static long GetLinuxFreeMemory()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long memAvailable = 0;
            long memFree = 0;
            long buffers = 0;
            long cached = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemAvailable:"))
                {
                    memAvailable = ParseMemInfoValue(line);
                }
                else if (line.StartsWith("MemFree:"))
                {
                    memFree = ParseMemInfoValue(line);
                }
                else if (line.StartsWith("Buffers:"))
                {
                    buffers = ParseMemInfoValue(line);
                }
                else if (line.StartsWith("Cached:"))
                {
                    cached = ParseMemInfoValue(line);
                }
            }

            // MemAvailable is the best metric if available (Linux 3.14+)
            if (memAvailable > 0)
                return memAvailable;

            // Fallback: MemFree + Buffers + Cached
            return memFree + buffers + cached;
        }
        catch
        {
            // Fallback
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes - info.HeapSizeBytes;
        }
    }

    private static long ParseMemInfoValue(string line)
    {
        // Format: "MemAvailable:    1234567 kB"
        var parts = line.Split(':', 2);
        if (parts.Length < 2) return 0;

        var valueStr = parts[1].Trim().Replace(" kB", "").Replace("kB", "").Trim();
        if (long.TryParse(valueStr, out var kb))
        {
            return kb * 1024; // Convert kB to bytes
        }
        return 0;
    }

    private static long GetMacOSFreeMemory()
    {
        // macOS: use vm_stat or fall back to GC info
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/vm_stat",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) throw new Exception("Failed to start vm_stat");

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Parse vm_stat output for free and inactive pages
            long pageSize = 4096; // Default page size
            long freePages = 0;
            long inactivePages = 0;

            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("Pages free:"))
                {
                    freePages = ParseVmStatValue(line);
                }
                else if (line.StartsWith("Pages inactive:"))
                {
                    inactivePages = ParseVmStatValue(line);
                }
            }

            return (freePages + inactivePages) * pageSize;
        }
        catch
        {
            // Fallback
            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes - info.HeapSizeBytes;
        }
    }

    private static long ParseVmStatValue(string line)
    {
        // Format: "Pages free:                             1234."
        var parts = line.Split(':');
        if (parts.Length < 2) return 0;

        var valueStr = parts[1].Trim().TrimEnd('.');
        if (long.TryParse(valueStr, out var value))
        {
            return value;
        }
        return 0;
    }

    // Windows P/Invoke for GlobalMemoryStatusEx
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static object? UserInfo(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["username"] = Environment.UserName,
            ["uid"] = -1.0,  // Not available on Windows
            ["gid"] = -1.0,  // Not available on Windows
            ["shell"] = null,  // Not available on Windows
            ["homedir"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        });
    }
}
