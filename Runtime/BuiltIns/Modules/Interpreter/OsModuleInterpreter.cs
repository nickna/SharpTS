using System.Net.NetworkInformation;
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
            ["loadavg"] = new BuiltInMethod("loadavg", 0, 0, Loadavg),
            ["networkInterfaces"] = new BuiltInMethod("networkInterfaces", 0, 0, NetworkInterfaces),

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

    /// <summary>
    /// Returns the system load averages for 1, 5, and 15 minutes.
    /// On Windows, returns [0, 0, 0] per Node.js specification.
    /// </summary>
    private static object? Loadavg(Interp interpreter, object? receiver, List<object?> args)
    {
        var loadavg = GetLoadAverage();
        return new SharpTSArray([loadavg[0], loadavg[1], loadavg[2]]);
    }

    /// <summary>
    /// Gets the load average as an array of 3 doubles (1, 5, 15 minute averages).
    /// </summary>
    private static double[] GetLoadAverage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows does not have load averages, return [0, 0, 0] per Node.js behavior
            return [0.0, 0.0, 0.0];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return GetLinuxLoadAverage();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return GetMacOSLoadAverage();
        }

        // Unknown platform, return zeros
        return [0.0, 0.0, 0.0];
    }

    private static double[] GetLinuxLoadAverage()
    {
        try
        {
            // Parse /proc/loadavg: "0.50 0.60 0.70 1/234 12345"
            var content = File.ReadAllText("/proc/loadavg");
            var parts = content.Split(' ');
            if (parts.Length >= 3)
            {
                return [
                    double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                ];
            }
        }
        catch
        {
            // Fall through to default
        }
        return [0.0, 0.0, 0.0];
    }

    private static double[] GetMacOSLoadAverage()
    {
        try
        {
            // Execute: sysctl -n vm.loadavg
            // Output: "{ 0.50 0.60 0.70 }"
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/sysctl",
                Arguments = "-n vm.loadavg",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return [0.0, 0.0, 0.0];

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Parse "{ 0.50 0.60 0.70 }"
            var trimmed = output.Trim().TrimStart('{').TrimEnd('}').Trim();
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                return [
                    double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                ];
            }
        }
        catch
        {
            // Fall through to default
        }
        return [0.0, 0.0, 0.0];
    }

    /// <summary>
    /// Returns network interface information as an object with interface names as keys.
    /// </summary>
    private static object? NetworkInterfaces(Interp interpreter, object? receiver, List<object?> args)
    {
        var result = new Dictionary<string, object?>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in interfaces)
            {
                var addressList = new List<object?>();
                var ipProps = nic.GetIPProperties();
                var mac = nic.GetPhysicalAddress();
                var macString = BitConverter.ToString(mac.GetAddressBytes()).Replace('-', ':').ToLowerInvariant();
                if (string.IsNullOrEmpty(macString)) macString = "00:00:00:00:00:00";

                var isInternal = nic.NetworkInterfaceType == NetworkInterfaceType.Loopback;

                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    var address = unicast.Address.ToString();
                    var family = unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        ? "IPv4"
                        : "IPv6";

                    // Calculate netmask from prefix length
                    var prefixLength = unicast.PrefixLength;
                    var netmask = GetNetmaskFromPrefixLength(prefixLength, family);
                    var cidr = $"{address}/{prefixLength}";

                    addressList.Add(new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["address"] = address,
                        ["netmask"] = netmask,
                        ["family"] = family,
                        ["mac"] = macString,
                        ["internal"] = isInternal,
                        ["cidr"] = cidr
                    }));
                }

                if (addressList.Count > 0)
                {
                    result[nic.Name] = new SharpTSArray(addressList);
                }
            }
        }
        catch
        {
            // Return empty object on error
        }

        return new SharpTSObject(result);
    }

    /// <summary>
    /// Converts a prefix length to a netmask string.
    /// </summary>
    private static string GetNetmaskFromPrefixLength(int prefixLength, string family)
    {
        if (family == "IPv4")
        {
            if (prefixLength < 0 || prefixLength > 32) return "255.255.255.255";
            uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
            return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
        }
        else
        {
            // IPv6 - return the prefix length representation
            // Full netmask would be complex; Node.js typically uses the hex representation
            if (prefixLength < 0 || prefixLength > 128) prefixLength = 128;

            // Create IPv6 netmask
            var bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                int bitsInThisByte = Math.Min(8, Math.Max(0, prefixLength - i * 8));
                bytes[i] = (byte)(0xFF << (8 - bitsInThisByte));
            }

            // Format as IPv6 address
            return string.Format("{0:x2}{1:x2}:{2:x2}{3:x2}:{4:x2}{5:x2}:{6:x2}{7:x2}:{8:x2}{9:x2}:{10:x2}{11:x2}:{12:x2}{13:x2}:{14:x2}{15:x2}",
                bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
                bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]);
        }
    }
}
