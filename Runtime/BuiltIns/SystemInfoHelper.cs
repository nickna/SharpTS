using System.Runtime.InteropServices;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Static helper for cross-platform system information retrieval.
/// Used by both interpreter and compiled code paths.
/// </summary>
public static class SystemInfoHelper
{
    /// <summary>
    /// Gets the actual free system memory in bytes using platform-specific APIs.
    /// </summary>
    public static double GetFreeMemoryBytes()
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

    /// <summary>
    /// Gets the total system memory in bytes.
    /// </summary>
    public static double GetTotalMemoryBytes()
    {
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes;
    }

    private static double GetWindowsFreeMemory()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            return memStatus.ullAvailPhys;
        }
        // Fallback
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes - info.HeapSizeBytes;
    }

    private static double GetLinuxFreeMemory()
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
                else if (line.StartsWith("Cached:") && !line.StartsWith("Cached "))
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

    private static double GetMacOSFreeMemory()
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
}
