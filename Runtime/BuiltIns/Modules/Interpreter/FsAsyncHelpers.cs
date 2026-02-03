using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Shared async implementations for fs module operations using .NET native async APIs.
/// Used by both callback-based methods and promise-based methods.
/// </summary>
public static class FsAsyncHelpers
{
    /// <summary>
    /// Asynchronously reads the entire contents of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="encoding">If provided, returns a string; otherwise returns a Buffer.</param>
    /// <returns>A string if encoding is provided, otherwise a SharpTSBuffer.</returns>
    public static async Task<object?> ReadFileAsync(string path, object? encoding)
    {
        if (encoding != null)
        {
            // Return as string
            return await File.ReadAllTextAsync(path);
        }
        else
        {
            // Return as Buffer
            var bytes = await File.ReadAllBytesAsync(path);
            return new SharpTSBuffer(bytes);
        }
    }

    /// <summary>
    /// Asynchronously writes data to a file, replacing the file if it exists.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="options">Optional options (encoding, mode, flag).</param>
    public static async Task WriteFileAsync(string path, object? data, object? options)
    {
        if (data is SharpTSBuffer buffer)
        {
            await File.WriteAllBytesAsync(path, buffer.Data);
        }
        else
        {
            var text = data?.ToString() ?? "";
            await File.WriteAllTextAsync(path, text);
        }
    }

    /// <summary>
    /// Asynchronously appends data to a file, creating the file if it doesn't exist.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="data">The data to append.</param>
    /// <param name="options">Optional options (encoding, mode, flag).</param>
    public static async Task AppendFileAsync(string path, object? data, object? options)
    {
        var text = data?.ToString() ?? "";
        await File.AppendAllTextAsync(path, text);
    }

    /// <summary>
    /// Asynchronously retrieves file or directory statistics.
    /// </summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <returns>A Stats-like object with file information.</returns>
    public static async Task<SharpTSObject> StatAsync(string path)
    {
        // Use Task.Run to offload the synchronous file info operations
        return await Task.Run(() =>
        {
            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                return CreateStatsObject(
                    isDirectory: true,
                    isFile: false,
                    isSymbolicLink: dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                    size: 0,
                    atime: dirInfo.LastAccessTime,
                    mtime: dirInfo.LastWriteTime,
                    ctime: dirInfo.CreationTime,
                    birthtime: dirInfo.CreationTime
                );
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return CreateStatsObject(
                    isDirectory: false,
                    isFile: true,
                    isSymbolicLink: fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint),
                    size: fileInfo.Length,
                    atime: fileInfo.LastAccessTime,
                    mtime: fileInfo.LastWriteTime,
                    ctime: fileInfo.CreationTime,
                    birthtime: fileInfo.CreationTime
                );
            }
            else
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
        });
    }

    /// <summary>
    /// Creates a Stats-like object for stat/lstat operations.
    /// </summary>
    private static SharpTSObject CreateStatsObject(
        bool isDirectory,
        bool isFile,
        bool isSymbolicLink,
        long size,
        DateTime atime,
        DateTime mtime,
        DateTime ctime,
        DateTime birthtime)
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["isDirectory"] = new BuiltInMethod("isDirectory", 0, 0, (_, _, _) => isDirectory),
            ["isFile"] = new BuiltInMethod("isFile", 0, 0, (_, _, _) => isFile),
            ["isSymbolicLink"] = new BuiltInMethod("isSymbolicLink", 0, 0, (_, _, _) => isSymbolicLink),
            ["isBlockDevice"] = new BuiltInMethod("isBlockDevice", 0, 0, (_, _, _) => false),
            ["isCharacterDevice"] = new BuiltInMethod("isCharacterDevice", 0, 0, (_, _, _) => false),
            ["isFIFO"] = new BuiltInMethod("isFIFO", 0, 0, (_, _, _) => false),
            ["isSocket"] = new BuiltInMethod("isSocket", 0, 0, (_, _, _) => false),
            ["size"] = (double)size,
            ["atime"] = CreateDateObject(atime),
            ["mtime"] = CreateDateObject(mtime),
            ["ctime"] = CreateDateObject(ctime),
            ["birthtime"] = CreateDateObject(birthtime),
            ["atimeMs"] = (double)new DateTimeOffset(atime).ToUnixTimeMilliseconds(),
            ["mtimeMs"] = (double)new DateTimeOffset(mtime).ToUnixTimeMilliseconds(),
            ["ctimeMs"] = (double)new DateTimeOffset(ctime).ToUnixTimeMilliseconds(),
            ["birthtimeMs"] = (double)new DateTimeOffset(birthtime).ToUnixTimeMilliseconds()
        });
    }

    /// <summary>
    /// Creates a Date-like object from a DateTime.
    /// </summary>
    private static SharpTSObject CreateDateObject(DateTime dt)
    {
        var timestamp = (double)new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["getTime"] = new BuiltInMethod("getTime", 0, 0, (_, _, _) => timestamp),
            ["toISOString"] = new BuiltInMethod("toISOString", 0, 0, (_, _, _) => dt.ToUniversalTime().ToString("o"))
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
                    list.Add(CreateDirent(entry));
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
    /// Creates a Dirent-like object for readdir with withFileTypes option.
    /// </summary>
    private static SharpTSObject CreateDirent(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        var isFile = File.Exists(fullPath);
        var isDir = Directory.Exists(fullPath);
        var isSymlink = false;

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Exists || Directory.Exists(fullPath))
        {
            isSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["isFile"] = new BuiltInMethod("isFile", 0, 0, (_, _, _) => isFile && !isDir),
            ["isDirectory"] = new BuiltInMethod("isDirectory", 0, 0, (_, _, _) => isDir),
            ["isSymbolicLink"] = new BuiltInMethod("isSymbolicLink", 0, 0, (_, _, _) => isSymlink),
            ["isBlockDevice"] = new BuiltInMethod("isBlockDevice", 0, 0, (_, _, _) => false),
            ["isCharacterDevice"] = new BuiltInMethod("isCharacterDevice", 0, 0, (_, _, _) => false),
            ["isFIFO"] = new BuiltInMethod("isFIFO", 0, 0, (_, _, _) => false),
            ["isSocket"] = new BuiltInMethod("isSocket", 0, 0, (_, _, _) => false)
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
        await Task.Run(() =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            // For now, just check existence
            // Full implementation would check actual permissions based on mode
        });
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

            bool isSymlink = false;
            bool isDir = dirInfo.Exists && !fileInfo.Exists;
            long size = fileInfo.Exists ? fileInfo.Length : 0;
            DateTime atime, mtime, ctime, birthtime;

            if (fileInfo.Exists)
            {
                isSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
                atime = fileInfo.LastAccessTime;
                mtime = fileInfo.LastWriteTime;
                ctime = fileInfo.CreationTime;
                birthtime = fileInfo.CreationTime;
            }
            else
            {
                isSymlink = dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
                atime = dirInfo.LastAccessTime;
                mtime = dirInfo.LastWriteTime;
                ctime = dirInfo.CreationTime;
                birthtime = dirInfo.CreationTime;
            }

            return CreateStatsObject(
                isDirectory: isDir,
                isFile: fileInfo.Exists && !isDir,
                isSymbolicLink: isSymlink,
                size: size,
                atime: atime,
                mtime: mtime,
                ctime: ctime,
                birthtime: birthtime
            );
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
