using System.Collections.Concurrent;

namespace SharpTS.Runtime.BuiltIns.Modules.Interop;

/// <summary>
/// Manages the mapping between integer file descriptors and .NET FileStream objects.
/// Node.js uses integer file descriptors (starting at 3, as 0-2 are reserved for
/// stdin, stdout, stderr). This class provides a thread-safe bridge between
/// Node.js-style fd operations and .NET's FileStream-based I/O.
/// </summary>
/// <remarks>
/// Used by both the interpreter and compiled code to manage file descriptor state.
/// File descriptors are allocated starting at 3 and increment atomically.
/// </remarks>
public sealed class FileDescriptorTable
{
    /// <summary>
    /// Global singleton instance for interpreter mode.
    /// </summary>
    public static readonly FileDescriptorTable Instance = new();

    /// <summary>
    /// The next file descriptor to allocate. Starts at 3 (0-2 are reserved).
    /// </summary>
    private int _nextFd = 3;

    /// <summary>
    /// Maps file descriptors to their associated FileStream objects.
    /// </summary>
    private readonly ConcurrentDictionary<int, FileStream> _streams = new();

    /// <summary>
    /// Opens a file and returns a file descriptor.
    /// </summary>
    /// <param name="path">The file path to open.</param>
    /// <param name="mode">The file mode (Create, Open, etc.).</param>
    /// <param name="access">The file access mode (Read, Write, etc.).</param>
    /// <param name="share">The file share mode.</param>
    /// <returns>A file descriptor (integer >= 3).</returns>
    public int Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var fd = Interlocked.Increment(ref _nextFd) - 1;
        var stream = new FileStream(path, mode, access, share);
        _streams[fd] = stream;
        return fd;
    }

    /// <summary>
    /// Gets the FileStream associated with a file descriptor.
    /// </summary>
    /// <param name="fd">The file descriptor.</param>
    /// <returns>The associated FileStream.</returns>
    /// <exception cref="NodeError">Thrown with EBADF if the fd is invalid.</exception>
    public FileStream Get(int fd)
    {
        if (_streams.TryGetValue(fd, out var stream))
        {
            return stream;
        }
        throw new NodeError("EBADF", "bad file descriptor", "fstat", null, 9);
    }

    /// <summary>
    /// Closes a file descriptor and disposes its associated stream.
    /// </summary>
    /// <param name="fd">The file descriptor to close.</param>
    /// <exception cref="NodeError">Thrown with EBADF if the fd is invalid.</exception>
    public void Close(int fd)
    {
        if (_streams.TryRemove(fd, out var stream))
        {
            stream.Dispose();
            return;
        }
        throw new NodeError("EBADF", "bad file descriptor", "close", null, 9);
    }

    /// <summary>
    /// Checks if a file descriptor is valid (open).
    /// </summary>
    /// <param name="fd">The file descriptor to check.</param>
    /// <returns>True if the fd is valid and open.</returns>
    public bool IsValid(int fd) => _streams.ContainsKey(fd);

    /// <summary>
    /// Gets the number of currently open file descriptors.
    /// </summary>
    public int OpenCount => _streams.Count;

    /// <summary>
    /// Closes all open file descriptors. Used for cleanup.
    /// </summary>
    public void CloseAll()
    {
        foreach (var kvp in _streams)
        {
            if (_streams.TryRemove(kvp.Key, out var stream))
            {
                stream.Dispose();
            }
        }
    }
}
