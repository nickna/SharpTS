namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Node.js-compatible error with error code property.
/// Thrown by built-in modules to match Node.js error semantics.
/// </summary>
public class NodeError : Exception
{
    /// <summary>
    /// The Node.js error code (e.g., "ENOENT", "EACCES").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The system call that failed (e.g., "open", "stat").
    /// </summary>
    public string? Syscall { get; }

    /// <summary>
    /// The file path associated with the error, if applicable.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// The numeric error number (errno), if available.
    /// </summary>
    public int? Errno { get; }

    public NodeError(string code, string message, string? syscall = null, string? path = null, int? errno = null)
        : base(FormatMessage(code, message, syscall, path))
    {
        Code = code;
        Syscall = syscall;
        Path = path;
        Errno = errno;
    }

    private static string FormatMessage(string code, string message, string? syscall, string? path)
    {
        var parts = new List<string> { code };
        if (syscall != null) parts.Add(syscall);
        parts.Add(message);
        if (path != null) parts.Add($"'{path}'");
        return string.Join(": ", parts);
    }
}

/// <summary>
/// Common Node.js error codes used by built-in modules.
/// </summary>
public static class NodeErrorCodes
{
    /// <summary>No such file or directory.</summary>
    public const string ENOENT = "ENOENT";

    /// <summary>Permission denied.</summary>
    public const string EACCES = "EACCES";

    /// <summary>File already exists.</summary>
    public const string EEXIST = "EEXIST";

    /// <summary>Illegal operation on a directory.</summary>
    public const string EISDIR = "EISDIR";

    /// <summary>Not a directory.</summary>
    public const string ENOTDIR = "ENOTDIR";

    /// <summary>Directory not empty.</summary>
    public const string ENOTEMPTY = "ENOTEMPTY";

    /// <summary>Invalid argument.</summary>
    public const string EINVAL = "EINVAL";

    /// <summary>Too many open files.</summary>
    public const string EMFILE = "EMFILE";

    /// <summary>Bad file descriptor.</summary>
    public const string EBADF = "EBADF";

    /// <summary>Resource busy or locked.</summary>
    public const string EBUSY = "EBUSY";

    /// <summary>Operation not permitted.</summary>
    public const string EPERM = "EPERM";

    /// <summary>
    /// Converts a .NET exception to the appropriate Node.js error code.
    /// </summary>
    public static string FromException(Exception ex) => ex switch
    {
        FileNotFoundException => ENOENT,
        DirectoryNotFoundException => ENOENT,
        UnauthorizedAccessException => EACCES,
        IOException io when io.HResult == unchecked((int)0x80070050) => EEXIST, // ERROR_FILE_EXISTS
        IOException io when io.HResult == unchecked((int)0x80070005) => EACCES, // ERROR_ACCESS_DENIED
        IOException io when io.HResult == unchecked((int)0x80070091) => ENOTEMPTY, // ERROR_DIR_NOT_EMPTY
        IOException => EACCES,
        ArgumentException => EINVAL,
        _ => EINVAL
    };
}
