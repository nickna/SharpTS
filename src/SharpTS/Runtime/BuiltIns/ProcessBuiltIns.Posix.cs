using SharpTS.Execution;
using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// POSIX identity methods (#1086): getuid/geteuid/getgid/getegid/getgroups and
/// setuid/setgid. Exposed only on POSIX platforms — on Windows the properties
/// are undefined, exactly like Node.
/// </summary>
public static partial class ProcessBuiltIns
{
    [DllImport("libc", EntryPoint = "getuid")] private static extern uint LibcGetUid();
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint LibcGetEuid();
    [DllImport("libc", EntryPoint = "getgid")] private static extern uint LibcGetGid();
    [DllImport("libc", EntryPoint = "getegid")] private static extern uint LibcGetEgid();
    [DllImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static extern int LibcGetGroups(int size, uint[] list);
    [DllImport("libc", EntryPoint = "setuid", SetLastError = true)] private static extern int LibcSetUid(uint uid);
    [DllImport("libc", EntryPoint = "setgid", SetLastError = true)] private static extern int LibcSetGid(uint gid);

    private static readonly BuiltInMethod _getuid = new("getuid", 0, static (_, _, _) => (double)LibcGetUid());
    private static readonly BuiltInMethod _geteuid = new("geteuid", 0, static (_, _, _) => (double)LibcGetEuid());
    private static readonly BuiltInMethod _getgid = new("getgid", 0, static (_, _, _) => (double)LibcGetGid());
    private static readonly BuiltInMethod _getegid = new("getegid", 0, static (_, _, _) => (double)LibcGetEgid());
    private static readonly BuiltInMethod _getgroups = new("getgroups", 0, GetGroups);
    private static readonly BuiltInMethod _setuid = new("setuid", 1, SetUid);
    private static readonly BuiltInMethod _setgid = new("setgid", 1, SetGid);

    private static object? GetGroups(Interpreter i, object? r, List<object?> args)
    {
        var buffer = new uint[128];
        int count = LibcGetGroups(buffer.Length, buffer);
        if (count < 0)
            throw new Exceptions.ThrowException(new SharpTSError("getgroups EPERM") { Code = "EPERM" });
        var groups = new List<object?>(count);
        for (int n = 0; n < count; n++) groups.Add((double)buffer[n]);
        return new SharpTSArray(groups);
    }

    private static object? SetUid(Interpreter i, object? r, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not double uid)
            throw new Exceptions.ThrowException(
                new SharpTSTypeError("The \"id\" argument must be of type number."));
        if (LibcSetUid((uint)uid) != 0)
            throw new Exceptions.ThrowException(new SharpTSError("setuid EPERM") { Code = "EPERM" });
        return null;
    }

    private static object? SetGid(Interpreter i, object? r, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not double gid)
            throw new Exceptions.ThrowException(
                new SharpTSTypeError("The \"id\" argument must be of type number."));
        if (LibcSetGid((uint)gid) != 0)
            throw new Exceptions.ThrowException(new SharpTSError("setgid EPERM") { Code = "EPERM" });
        return null;
    }
}
