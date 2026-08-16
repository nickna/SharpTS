using System.Runtime.InteropServices;

namespace SharpTS.Runtime.BuiltIns.Modules.Interop;

/// <summary>
/// P/Invoke declarations for libc functions used by the fs module.
/// These are only available on Unix-like systems (Linux, macOS).
/// </summary>
public static partial class LibC
{
    /// <summary>
    /// Changes the owner and group of a file.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="owner">New owner (uid). Pass -1 to leave unchanged.</param>
    /// <param name="group">New group (gid). Pass -1 to leave unchanged.</param>
    /// <returns>0 on success, -1 on error (check errno).</returns>
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int chown(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int owner,
        int group);

    /// <summary>
    /// Changes the owner and group of a symbolic link (does not follow symlinks).
    /// </summary>
    /// <param name="path">Path to the symbolic link.</param>
    /// <param name="owner">New owner (uid). Pass -1 to leave unchanged.</param>
    /// <param name="group">New group (gid). Pass -1 to leave unchanged.</param>
    /// <returns>0 on success, -1 on error (check errno).</returns>
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int lchown(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int owner,
        int group);

    /// <summary>
    /// Creates a hard link (Unix only).
    /// </summary>
    /// <param name="existingPath">Path to the existing file.</param>
    /// <param name="newPath">Path to the new hard link.</param>
    /// <returns>0 on success, -1 on error (check errno).</returns>
    [LibraryImport("libc", SetLastError = true)]
    internal static partial int link(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    /// <summary>
    /// Maps errno values to Node.js error codes.
    /// </summary>
    public static string GetErrnoCode(int errno) => errno switch
    {
        1 => "EPERM",      // Operation not permitted
        2 => "ENOENT",     // No such file or directory
        13 => "EACCES",    // Permission denied
        17 => "EEXIST",    // File exists
        20 => "ENOTDIR",   // Not a directory
        21 => "EISDIR",    // Is a directory
        22 => "EINVAL",    // Invalid argument
        40 => "ELOOP",     // Too many symbolic links
        _ => "UNKNOWN"
    };

    /// <summary>
    /// Gets a human-readable message for an errno value.
    /// </summary>
    public static string GetErrnoMessage(int errno) => errno switch
    {
        1 => "operation not permitted",
        2 => "no such file or directory",
        13 => "permission denied",
        17 => "file exists",
        20 => "not a directory",
        21 => "is a directory",
        22 => "invalid argument",
        40 => "too many symbolic links",
        _ => $"unknown error ({errno})"
    };

    /// <summary>
    /// Creates a hard link cross-platform.
    /// </summary>
    /// <param name="existingPath">Path to the existing file.</param>
    /// <param name="newPath">Path to the new hard link.</param>
    public static void CreateHardLink(string existingPath, string newPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Kernel32.CreateHardLink(newPath, existingPath, IntPtr.Zero))
            {
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new IOException($"{Kernel32.GetErrorCode(error)}: {Kernel32.GetErrorMessage(error)}, link '{existingPath}' -> '{newPath}'");
            }
        }
        else
        {
            var result = link(existingPath, newPath);
            if (result != 0)
            {
                var errno = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                throw new IOException($"{GetErrnoCode(errno)}: {GetErrnoMessage(errno)}, link '{existingPath}' -> '{newPath}'");
            }
        }
    }
}
