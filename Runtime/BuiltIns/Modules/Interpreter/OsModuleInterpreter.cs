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
        var info = GC.GetGCMemoryInfo();
        return (double)(info.TotalAvailableMemoryBytes - info.HeapSizeBytes);
    }

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
