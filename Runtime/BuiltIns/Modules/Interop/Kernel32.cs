using System.Runtime.InteropServices;

namespace SharpTS.Runtime.BuiltIns.Modules.Interop;

/// <summary>
/// P/Invoke declarations for kernel32 functions used by the fs module.
/// These are only available on Windows.
/// </summary>
public static partial class Kernel32
{
    /// <summary>
    /// Creates a hard link (Windows only).
    /// </summary>
    /// <param name="lpFileName">The name of the new hard link.</param>
    /// <param name="lpExistingFileName">The name of the existing file.</param>
    /// <param name="lpSecurityAttributes">Reserved; must be NULL.</param>
    /// <returns>True if successful, false otherwise.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    /// <summary>
    /// Maps Windows error codes to Node.js error codes.
    /// </summary>
    public static string GetErrorCode(int errorCode) => errorCode switch
    {
        2 => "ENOENT",      // ERROR_FILE_NOT_FOUND
        3 => "ENOENT",      // ERROR_PATH_NOT_FOUND
        5 => "EACCES",      // ERROR_ACCESS_DENIED
        17 => "EXDEV",      // ERROR_NOT_SAME_DEVICE
        80 => "EEXIST",     // ERROR_FILE_EXISTS
        183 => "EEXIST",    // ERROR_ALREADY_EXISTS
        1142 => "EXDEV",    // ERROR_NOT_SAME_DEVICE (alternate)
        _ => "UNKNOWN"
    };

    /// <summary>
    /// Gets a human-readable message for a Windows error code.
    /// </summary>
    public static string GetErrorMessage(int errorCode) => errorCode switch
    {
        2 => "no such file or directory",
        3 => "no such file or directory",
        5 => "permission denied",
        17 => "cross-device link not permitted",
        80 => "file already exists",
        183 => "file already exists",
        1142 => "cross-device link not permitted",
        _ => $"unknown error ({errorCode})"
    };
}
