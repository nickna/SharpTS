using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Shared async implementations for fs module operations using .NET native async APIs.
/// Used by both callback-based methods and promise-based methods.
/// </summary>
public static class FsAsyncHelpers
{
    /// <summary>
    /// Test-only latency injection: when SHARPTS_TEST_FS_ASYNC_DELAY_MS is set, async fs
    /// operations stall that long before doing real work. Deterministically reproduces the
    /// slow-I/O timing of loaded CI machines (AV scans, cold disks) that exposed the
    /// event-loop quiescence give-up on in-flight native tasks. Read per call so a test can
    /// scope it with try/finally. Same env-var seam pattern as SHARPTS_DNS_SERVER (#225).
    /// </summary>
    private static Task InjectedTestLatency()
        => int.TryParse(Environment.GetEnvironmentVariable("SHARPTS_TEST_FS_ASYNC_DELAY_MS"), out var ms) && ms > 0
            ? Task.Delay(ms)
            : Task.CompletedTask;

    /// <summary>
    /// Asynchronously reads the entire contents of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="encoding">If provided, returns a string; otherwise returns a Buffer.</param>
    /// <returns>A string if encoding is provided, otherwise a SharpTSBuffer.</returns>
    public static async Task<object?> ReadFileAsync(string path, object? encoding)
    {
        await InjectedTestLatency();
        var encName = FsModuleInterpreter.EncodingName(encoding);
        var bytes = await File.ReadAllBytesAsync(path);
        // No encoding → Buffer; otherwise decode the raw bytes per the encoding.
        return encName != null ? (object?)BufferEncoding.Decode(bytes, encName) : new SharpTSBuffer(bytes);
    }

    /// <summary>
    /// Asynchronously writes data to a file, replacing the file if it exists.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="options">Optional options (encoding, mode, flag).</param>
    public static async Task WriteFileAsync(string path, object? data, object? options)
    {
        await InjectedTestLatency();
        // Buffer/TypedArray data is written byte-exact; a string honors the encoding option.
        var bytes = BufferEncoding.ToBytes(data, FsModuleInterpreter.EncodingName(options));
        await File.WriteAllBytesAsync(path, bytes);
    }

    /// <summary>
    /// Asynchronously appends data to a file, creating the file if it doesn't exist.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="data">The data to append.</param>
    /// <param name="options">Optional options (encoding, mode, flag).</param>
    public static async Task AppendFileAsync(string path, object? data, object? options)
    {
        await InjectedTestLatency();
        // Buffer/TypedArray data is appended byte-exact; a string honors the encoding option.
        var bytes = BufferEncoding.ToBytes(data, FsModuleInterpreter.EncodingName(options));
        await File.AppendAllBytesAsync(path, bytes);
    }

    /// <summary>
    /// Asynchronously retrieves file or directory statistics.
    /// </summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <returns>A Stats-like object with file information.</returns>
    public static async Task<SharpTSObject> StatAsync(string path)
    {
        // Returns the raw stat record (#977) — same shape/logic as the sync
        // FsModuleInterpreter.StatRaw, so statSync and await stat agree by
        // construction; the TS Stats class shapes it. stat follows symlinks.
        return await Task.Run(() =>
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return FsModuleInterpreter.BuildStatRecord(true, false, false, 0.0,
                    di.LastAccessTime, di.LastWriteTime, di.CreationTime, di.CreationTime);
            }
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                bool ro = fi.Attributes.HasFlag(FileAttributes.ReadOnly);
                return FsModuleInterpreter.BuildStatRecord(false, false, ro, fi.Length,
                    fi.LastAccessTime, fi.LastWriteTime, fi.CreationTime, fi.CreationTime);
            }
            throw new FileNotFoundException("no such file or directory", path);
        });
    }

    /// <summary>
    /// Asynchronously removes a file.
    /// </summary>
    /// <param name="path">The path to the file to remove.</param>
    public static async Task UnlinkAsync(string path)
    {
        await Task.Run(() =>
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
            File.Delete(path);
        });
    }

    /// <summary>
    /// Asynchronously creates a directory.
    /// </summary>
    /// <param name="path">The path of the directory to create.</param>
    /// <param name="options">Optional options (recursive, mode).</param>
    public static async Task MkdirAsync(string path, object? options)
    {
        await Task.Run(() =>
        {
            bool recursive = false;
            if (options is SharpTSObject opts)
            {
                var recursiveValue = opts.GetProperty("recursive");
                recursive = recursiveValue is true || (recursiveValue is double d && d != 0);
            }

            if (recursive)
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                // Non-recursive: check if parent exists
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    throw new DirectoryNotFoundException($"no such file or directory, mkdir '{path}'");
                }
                if (Directory.Exists(path))
                {
                    throw new IOException($"EEXIST: file already exists, mkdir '{path}'");
                }
                Directory.CreateDirectory(path);
            }
        });
    }

    /// <summary>
    /// Asynchronously reads the contents of a directory.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <param name="options">Optional options (encoding, withFileTypes, recursive).</param>
    /// <returns>An array of filenames or Dirent objects.</returns>
    public static async Task<SharpTSArray> ReaddirAsync(string path, object? options)
    {
        return await Task.Run(() =>
        {
            bool withFileTypes = false;
            bool recursive = false;

            if (options is SharpTSObject opts)
            {
                var wft = opts.GetProperty("withFileTypes");
                withFileTypes = wft is true || (wft is double d && d != 0);

                var rec = opts.GetProperty("recursive");
                recursive = rec is true || (rec is double rd && rd != 0);
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = Directory.GetFileSystemEntries(path, "*", searchOption);
            var list = new List<object?>();

            if (withFileTypes)
            {
                foreach (var entry in entries)
                {
                    // Reuse the one canonical Dirent builder (#977) for sync/async parity.
                    list.Add(FsModuleInterpreter.CreateDirent(entry, Path.GetDirectoryName(entry) ?? path));
                }
            }
            else
            {
                foreach (var entry in entries)
                {
                    if (recursive)
                    {
                        list.Add(Path.GetRelativePath(path, entry));
                    }
                    else
                    {
                        list.Add(Path.GetFileName(entry));
                    }
                }
            }

            return new SharpTSArray(list);
        });
    }

    /// <summary>
    /// Asynchronously renames a file or directory.
    /// </summary>
    /// <param name="oldPath">The current path.</param>
    /// <param name="newPath">The new path.</param>
    public static async Task RenameAsync(string oldPath, string newPath)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
            }
            else if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);
            }
            else
            {
                throw new FileNotFoundException("no such file or directory", oldPath);
            }
        });
    }

    /// <summary>
    /// Asynchronously copies a file.
    /// </summary>
    /// <param name="src">The source path.</param>
    /// <param name="dest">The destination path.</param>
    /// <param name="mode">Optional copy mode (e.g., COPYFILE_EXCL).</param>
    public static async Task CopyFileAsync(string src, string dest, object? mode)
    {
        bool overwrite = true;

        if (mode is double modeValue)
        {
            // COPYFILE_EXCL = 1 means fail if destination exists
            if ((int)modeValue == 1)
            {
                overwrite = false;
            }
        }

        // Use async file streams for true async I/O
        await using var sourceStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        await using var destStream = new FileStream(dest, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await sourceStream.CopyToAsync(destStream);
    }

    /// <summary>
    /// Asynchronously tests a user's permissions for a file or directory.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="mode">Optional mode specifying the accessibility checks (F_OK=0, R_OK=4, W_OK=2, X_OK=1).</param>
    public static async Task AccessAsync(string path, object? mode)
    {
        var modeInt = mode is double d ? (int)d : 0;
        // Shared with the sync path so callback/promise access enforce the same
        // F_OK/W_OK semantics as accessSync (and the compiled async path, which
        // calls the same FsAccessSync helper).
        await Task.Run(() => FsModuleInterpreter.CheckAccess(path, modeInt));
    }

    /// <summary>
    /// Asynchronously removes a directory.
    /// </summary>
    /// <param name="path">The path to the directory.</param>
    /// <param name="options">Optional options (recursive).</param>
    public static async Task RmdirAsync(string path, object? options)
    {
        await Task.Run(() =>
        {
            bool recursive = false;
            if (options is SharpTSObject opts)
            {
                var recursiveValue = opts.GetProperty("recursive");
                recursive = recursiveValue is true || (recursiveValue is double d && d != 0);
            }

            Directory.Delete(path, recursive);
        });
    }

    /// <summary>
    /// Asynchronously removes a file or directory (rm).
    /// </summary>
    /// <param name="path">The path to remove.</param>
    /// <param name="options">Optional options (recursive, force).</param>
    public static async Task RmAsync(string path, object? options)
    {
        await Task.Run(() =>
        {
            bool recursive = false;
            bool force = false;

            if (options is SharpTSObject opts)
            {
                var recursiveValue = opts.GetProperty("recursive");
                recursive = recursiveValue is true || (recursiveValue is double d && d != 0);

                var forceValue = opts.GetProperty("force");
                force = forceValue is true || (forceValue is double f && f != 0);
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (!force)
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
        });
    }

    /// <summary>
    /// Asynchronously retrieves file statistics without following symbolic links.
    /// </summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <returns>A Stats-like object with file information.</returns>
    public static async Task<SharpTSObject> LstatAsync(string path)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            var dirInfo = new DirectoryInfo(path);

            bool exists = fileInfo.Exists || dirInfo.Exists;
            if (!exists)
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            // Raw record matching the sync FsModuleInterpreter.LstatRaw (#977).
            bool isSymlink = (fileInfo.Exists ? fileInfo.Attributes : dirInfo.Attributes).HasFlag(FileAttributes.ReparsePoint);
            bool isDir = dirInfo.Exists && !fileInfo.Exists;
            double size = fileInfo.Exists ? fileInfo.Length : 0.0;
            bool ro = fileInfo.Exists && fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly);
            DateTime atime = isDir ? dirInfo.LastAccessTime : fileInfo.LastAccessTime;
            DateTime mtime = isDir ? dirInfo.LastWriteTime : fileInfo.LastWriteTime;
            DateTime ctime = isDir ? dirInfo.CreationTime : fileInfo.CreationTime;

            return FsModuleInterpreter.BuildStatRecord(isDir, isSymlink, ro, size, atime, mtime, ctime, ctime);
        });
    }

    /// <summary>
    /// Asynchronously changes file mode.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="mode">The file mode.</param>
    public static async Task ChmodAsync(string path, object? mode)
    {
        await Task.Run(() =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) ||
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                var modeValue = Convert.ToInt32(mode);
                File.SetUnixFileMode(path, (UnixFileMode)modeValue);
            }
            // Windows: No-op (permissions model is different)
        });
    }

    /// <summary>
    /// Asynchronously truncates a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="len">The new length (default 0).</param>
    public static async Task TruncateAsync(string path, object? len)
    {
        var length = len != null ? Convert.ToInt64(len) : 0L;
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, true);
        fs.SetLength(length);
    }

    /// <summary>
    /// Asynchronously changes file access and modification times.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="atime">The new access time.</param>
    /// <param name="mtime">The new modification time.</param>
    public static async Task UtimesAsync(string path, object? atime, object? mtime)
    {
        await Task.Run(() =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            var accessTime = ParseTimestamp(atime);
            var modTime = ParseTimestamp(mtime);

            File.SetLastAccessTime(path, accessTime);
            File.SetLastWriteTime(path, modTime);
        });
    }

    /// <summary>
    /// Parses a timestamp value (Unix seconds, milliseconds, string, or Date object).
    /// </summary>
    private static DateTime ParseTimestamp(object? value)
    {
        return value switch
        {
            double d => DateTimeOffset.FromUnixTimeSeconds((long)d).LocalDateTime,
            long l => DateTimeOffset.FromUnixTimeSeconds(l).LocalDateTime,
            int i => DateTimeOffset.FromUnixTimeSeconds(i).LocalDateTime,
            string s => DateTime.Parse(s),
            SharpTSObject obj when obj.HasProperty("getTime") => ParseDateObject(obj),
            _ => throw new ArgumentException("Invalid timestamp")
        };
    }

    private static DateTime ParseDateObject(SharpTSObject dateObj)
    {
        var getTime = dateObj.GetProperty("getTime");
        if (getTime is BuiltInMethod method)
        {
            var result = method.Bind(dateObj).Call(null!, []);
            if (result is double ms)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime;
            }
        }
        throw new ArgumentException("Invalid Date object");
    }

    /// <summary>
    /// Asynchronously reads the target of a symbolic link.
    /// </summary>
    /// <param name="path">The path to the symbolic link.</param>
    /// <returns>The target of the symbolic link.</returns>
    public static async Task<string> ReadlinkAsync(string path)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            var dirInfo = new DirectoryInfo(path);

            string? linkTarget = null;

            if (fileInfo.Exists)
            {
                linkTarget = fileInfo.LinkTarget;
            }
            else if (dirInfo.Exists)
            {
                linkTarget = dirInfo.LinkTarget;
            }
            else
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            if (linkTarget == null)
            {
                throw new NodeError("EINVAL", "invalid argument", "readlink", path);
            }

            return linkTarget;
        });
    }

    /// <summary>
    /// Asynchronously resolves a path to its absolute form.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolved absolute path.</returns>
    public static async Task<string> RealpathAsync(string path)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            var fullPath = Path.GetFullPath(path);

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Exists)
            {
                var resolved = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null)
                {
                    return resolved.FullName;
                }
            }

            var dirInfo = new DirectoryInfo(fullPath);
            if (dirInfo.Exists)
            {
                var resolved = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null)
                {
                    return resolved.FullName;
                }
            }

            return fullPath;
        });
    }

    /// <summary>
    /// Asynchronously creates a symbolic link.
    /// </summary>
    /// <param name="target">The link target.</param>
    /// <param name="path">The path of the symbolic link.</param>
    /// <param name="type">Optional type ('file', 'dir', 'junction').</param>
    public static async Task SymlinkAsync(string target, string path, object? type)
    {
        await Task.Run(() =>
        {
            var typeStr = type?.ToString();
            if (Directory.Exists(target) || typeStr == "dir" || typeStr == "junction")
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }
        });
    }

    /// <summary>
    /// Asynchronously creates a hard link.
    /// </summary>
    /// <param name="existingPath">The path of the existing file.</param>
    /// <param name="newPath">The path of the new hard link.</param>
    public static async Task LinkAsync(string existingPath, string newPath)
    {
        await Task.Run(() =>
        {
            if (!File.Exists(existingPath))
            {
                throw new FileNotFoundException("no such file or directory", existingPath);
            }
            if (File.Exists(newPath))
            {
                throw new IOException($"EEXIST: file already exists, link '{existingPath}' -> '{newPath}'");
            }

            // Use platform-specific hard link creation
            Interop.LibC.CreateHardLink(existingPath, newPath);
        });
    }

    /// <summary>
    /// Asynchronously creates a unique temporary directory.
    /// </summary>
    /// <param name="prefix">The prefix for the directory name.</param>
    /// <returns>The path of the created directory.</returns>
    public static async Task<string> MkdtempAsync(string prefix)
    {
        return await Task.Run(() =>
        {
            var tempPath = Path.Combine(Path.GetTempPath(), prefix + Path.GetRandomFileName().Replace(".", ""));
            Directory.CreateDirectory(tempPath);
            return tempPath;
        });
    }
}
