using System.Runtime.InteropServices;
using SharpTS.Runtime.BuiltIns.Modules.Interop;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'fs' module.
/// Provides synchronous, callback-based async, and promise-based APIs.
/// </summary>
/// <remarks>
/// Provides runtime values for synchronous file system operations.
/// Wraps .NET's System.IO classes with Node.js-compatible behavior.
/// Throws NodeError with proper error codes for fs operation failures.
/// </remarks>
public static class FsModuleInterpreter
{
    /// <summary>
    /// Wraps a file system operation with NodeError exception handling.
    /// </summary>
    private static T WrapFsOperation<T>(string syscall, string? path, Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (NodeError)
        {
            throw; // Already a NodeError, rethrow as-is
        }
        catch (Exception ex)
        {
            var code = NodeErrorCodes.FromException(ex);
            throw new NodeError(code, ex.Message, syscall, path);
        }
    }

    /// <summary>
    /// Wraps a void file system operation with NodeError exception handling.
    /// </summary>
    private static void WrapFsOperation(string syscall, string? path, Action operation)
    {
        try
        {
            operation();
        }
        catch (NodeError)
        {
            throw; // Already a NodeError, rethrow as-is
        }
        catch (Exception ex)
        {
            var code = NodeErrorCodes.FromException(ex);
            throw new NodeError(code, ex.Message, syscall, path);
        }
    }

    /// <summary>
    /// Gets all exported values for the fs module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["existsSync"] = new BuiltInMethod("existsSync", 1, 1, ExistsSync),
            ["readFileSync"] = new BuiltInMethod("readFileSync", 1, 2, ReadFileSync),
            ["writeFileSync"] = new BuiltInMethod("writeFileSync", 2, 3, WriteFileSync),
            ["appendFileSync"] = new BuiltInMethod("appendFileSync", 2, 3, AppendFileSync),
            ["unlinkSync"] = new BuiltInMethod("unlinkSync", 1, 1, UnlinkSync),
            ["mkdirSync"] = new BuiltInMethod("mkdirSync", 1, 2, MkdirSync),
            ["rmdirSync"] = new BuiltInMethod("rmdirSync", 1, 2, RmdirSync),
            ["readdirSync"] = new BuiltInMethod("readdirSync", 1, 2, ReaddirSync),
            ["statSync"] = new BuiltInMethod("statSync", 1, 1, StatSync),
            ["lstatSync"] = new BuiltInMethod("lstatSync", 1, 1, LstatSync),
            ["renameSync"] = new BuiltInMethod("renameSync", 2, 2, RenameSync),
            ["copyFileSync"] = new BuiltInMethod("copyFileSync", 2, 3, CopyFileSync),
            ["accessSync"] = new BuiltInMethod("accessSync", 1, 2, AccessSync),
            ["chmodSync"] = new BuiltInMethod("chmodSync", 2, 2, ChmodSync),
            ["chownSync"] = new BuiltInMethod("chownSync", 3, 3, ChownSync),
            ["lchownSync"] = new BuiltInMethod("lchownSync", 3, 3, LchownSync),
            ["truncateSync"] = new BuiltInMethod("truncateSync", 1, 2, TruncateSync),
            ["symlinkSync"] = new BuiltInMethod("symlinkSync", 2, 3, SymlinkSync),
            ["readlinkSync"] = new BuiltInMethod("readlinkSync", 1, 1, ReadlinkSync),
            ["realpathSync"] = new BuiltInMethod("realpathSync", 1, 1, RealpathSync),
            ["utimesSync"] = new BuiltInMethod("utimesSync", 3, 3, UtimesSync),
            // File descriptor APIs
            ["openSync"] = new BuiltInMethod("openSync", 2, 3, OpenSync),
            ["closeSync"] = new BuiltInMethod("closeSync", 1, 1, CloseSync),
            ["readSync"] = new BuiltInMethod("readSync", 5, 5, ReadSync),
            ["writeSync"] = new BuiltInMethod("writeSync", 2, 5, WriteSync),
            ["fstatSync"] = new BuiltInMethod("fstatSync", 1, 1, FstatSync),
            ["ftruncateSync"] = new BuiltInMethod("ftruncateSync", 1, 2, FtruncateSync),
            // Directory utilities
            ["mkdtempSync"] = new BuiltInMethod("mkdtempSync", 1, 1, MkdtempSync),
            ["opendirSync"] = new BuiltInMethod("opendirSync", 1, 1, OpendirSync),
            // Hard links
            ["linkSync"] = new BuiltInMethod("linkSync", 2, 2, LinkSync),
            ["constants"] = CreateConstants(),

            // Callback-based async methods
            ["readFile"] = new BuiltInMethod("readFile", 2, 3, ReadFile),
            ["writeFile"] = new BuiltInMethod("writeFile", 3, 4, WriteFile),
            ["appendFile"] = new BuiltInMethod("appendFile", 3, 4, AppendFile),
            ["stat"] = new BuiltInMethod("stat", 2, 3, Stat),
            ["lstat"] = new BuiltInMethod("lstat", 2, 3, Lstat),
            ["unlink"] = new BuiltInMethod("unlink", 2, 2, Unlink),
            ["mkdir"] = new BuiltInMethod("mkdir", 2, 3, Mkdir),
            ["rmdir"] = new BuiltInMethod("rmdir", 2, 3, Rmdir),
            ["readdir"] = new BuiltInMethod("readdir", 2, 3, Readdir),
            ["rename"] = new BuiltInMethod("rename", 3, 3, Rename),
            ["copyFile"] = new BuiltInMethod("copyFile", 3, 4, CopyFile),
            ["access"] = new BuiltInMethod("access", 2, 3, Access),
            ["chmod"] = new BuiltInMethod("chmod", 3, 3, Chmod),
            ["truncate"] = new BuiltInMethod("truncate", 2, 3, Truncate),
            ["utimes"] = new BuiltInMethod("utimes", 4, 4, Utimes),
            ["readlink"] = new BuiltInMethod("readlink", 2, 3, Readlink),
            ["realpath"] = new BuiltInMethod("realpath", 2, 3, Realpath),
            ["symlink"] = new BuiltInMethod("symlink", 3, 4, Symlink),
            ["link"] = new BuiltInMethod("link", 3, 3, Link),
            ["mkdtemp"] = new BuiltInMethod("mkdtemp", 2, 3, Mkdtemp),

            // Promise-based methods namespace
            ["promises"] = FsPromisesModuleInterpreter.CreatePromisesNamespace()
        };
    }

    /// <summary>
    /// Creates the fs.constants object with file system constants.
    /// </summary>
    internal static SharpTSObject CreateConstants()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            // File access constants (for accessSync)
            ["F_OK"] = 0.0,  // Existence
            ["R_OK"] = 4.0,  // Read
            ["W_OK"] = 2.0,  // Write
            ["X_OK"] = 1.0,  // Execute

            // File open constants (for future openSync)
            ["O_RDONLY"] = 0.0,
            ["O_WRONLY"] = 1.0,
            ["O_RDWR"] = 2.0,
            ["O_CREAT"] = 64.0,
            ["O_EXCL"] = 128.0,
            ["O_TRUNC"] = 512.0,
            ["O_APPEND"] = 1024.0,

            // Copy file constants
            ["COPYFILE_EXCL"] = 1.0,
            ["COPYFILE_FICLONE"] = 2.0,
            ["COPYFILE_FICLONE_FORCE"] = 4.0,

            // File type constants (for statSync mode)
            ["S_IFMT"] = 61440.0,   // File type mask
            ["S_IFREG"] = 32768.0,  // Regular file
            ["S_IFDIR"] = 16384.0,  // Directory
            ["S_IFCHR"] = 8192.0,   // Character device
            ["S_IFBLK"] = 24576.0,  // Block device
            ["S_IFIFO"] = 4096.0,   // FIFO/pipe
            ["S_IFLNK"] = 40960.0,  // Symbolic link
            ["S_IFSOCK"] = 49152.0, // Socket
        });
    }

    private static object? ExistsSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        return File.Exists(path) || Directory.Exists(path);
    }

    private static object? ReadFileSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var encoding = args.Count >= 2 ? args[1] : null;

        return WrapFsOperation("open", path, () =>
        {
            if (encoding != null)
            {
                // Return as string
                return (object?)File.ReadAllText(path);
            }
            else
            {
                // Return as Buffer
                var bytes = File.ReadAllBytes(path);
                return new SharpTSBuffer(bytes);
            }
        });
    }

    private static object? WriteFileSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var data = args[1]?.ToString() ?? "";
        WrapFsOperation("open", path, () => File.WriteAllText(path, data));
        return null;
    }

    private static object? AppendFileSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var data = args[1]?.ToString() ?? "";
        WrapFsOperation("open", path, () => File.AppendAllText(path, data));
        return null;
    }

    private static object? UnlinkSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        WrapFsOperation("unlink", path, () =>
        {
            // File.Delete doesn't throw if file doesn't exist, but Node.js does
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
            File.Delete(path);
        });
        return null;
    }

    private static object? MkdirSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        WrapFsOperation("mkdir", path, () => Directory.CreateDirectory(path));
        return null;
    }

    private static object? RmdirSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var recursive = false;

        if (args.Count >= 2 && args[1] is SharpTSObject options)
        {
            var recursiveValue = options.GetProperty("recursive");
            recursive = recursiveValue is true || (recursiveValue is double d && d != 0);
        }

        WrapFsOperation("rmdir", path, () => Directory.Delete(path, recursive));
        return null;
    }

    private static object? ReaddirSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var withFileTypes = false;
        var recursive = false;

        if (args.Count > 1 && args[1] is SharpTSObject options)
        {
            var wft = options.GetProperty("withFileTypes");
            withFileTypes = wft is true || (wft is double d && d != 0);

            var rec = options.GetProperty("recursive");
            recursive = rec is true || (rec is double rd && rd != 0);
        }

        return WrapFsOperation("readdir", path, () =>
        {
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
                    // For recursive, return relative paths; for non-recursive, just the filename
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

            return (object?)new SharpTSArray(list);
        });
    }

    /// <summary>
    /// Creates a Dirent-like object for readdirSync({ withFileTypes: true }).
    /// </summary>
    private static SharpTSObject CreateDirent(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        var isFile = File.Exists(fullPath);
        var isDir = Directory.Exists(fullPath);
        var isSymlink = false;

        // Check for symbolic link
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
            ["isSocket"] = new BuiltInMethod("isSocket", 0, 0, (_, _, _) => false),
        });
    }

    private static object? StatSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        return WrapFsOperation("stat", path, () =>
        {
            if (Directory.Exists(path))
            {
                return (object?)new SharpTSObject(new Dictionary<string, object?>
                {
                    ["isDirectory"] = new BuiltInMethod("isDirectory", 0, 0, (_, _, _) => true),
                    ["isFile"] = new BuiltInMethod("isFile", 0, 0, (_, _, _) => false),
                    ["size"] = 0.0
                });
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return new SharpTSObject(new Dictionary<string, object?>
                {
                    ["isDirectory"] = new BuiltInMethod("isDirectory", 0, 0, (_, _, _) => false),
                    ["isFile"] = new BuiltInMethod("isFile", 0, 0, (_, _, _) => true),
                    ["size"] = (double)fileInfo.Length
                });
            }
            else
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
        });
    }

    private static object? RenameSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var oldPath = args[0]?.ToString() ?? "";
        var newPath = args[1]?.ToString() ?? "";

        WrapFsOperation("rename", oldPath, () =>
        {
            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
            }
            else
            {
                File.Move(oldPath, newPath);
            }
        });
        return null;
    }

    private static object? CopyFileSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var src = args[0]?.ToString() ?? "";
        var dest = args[1]?.ToString() ?? "";
        WrapFsOperation("copyfile", src, () => File.Copy(src, dest, overwrite: true));
        return null;
    }

    private static object? AccessSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        WrapFsOperation("access", path, () =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
        });

        return null;
    }

    private static object? LstatSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        return WrapFsOperation("lstat", path, () =>
        {
            // Check for symbolic link first (lstat doesn't follow symlinks)
            var fileInfo = new FileInfo(path);
            var dirInfo = new DirectoryInfo(path);

            bool exists = fileInfo.Exists || dirInfo.Exists;
            if (!exists)
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            var isSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                           dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
            var isDir = dirInfo.Exists && !fileInfo.Exists;
            var size = fileInfo.Exists ? (double)fileInfo.Length : 0.0;

            var isFile = fileInfo.Exists && !isDir;
            return (object?)new SharpTSObject(new Dictionary<string, object?>
            {
                ["isDirectory"] = new BuiltInMethod("isDirectory", 0, 0, (_, _, _) => isDir),
                ["isFile"] = new BuiltInMethod("isFile", 0, 0, (_, _, _) => isFile),
                ["isSymbolicLink"] = new BuiltInMethod("isSymbolicLink", 0, 0, (_, _, _) => isSymlink),
                ["size"] = size
            });
        });
    }

    private static object? ChmodSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var mode = Convert.ToInt32(args[1]);

        WrapFsOperation("chmod", path, () =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                File.SetUnixFileMode(path, (UnixFileMode)mode);
            }
            // Windows: No-op (permissions model is different)
        });

        return null;
    }

    private static object? ChownSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var uid = Convert.ToInt32(args[1]);
        var gid = Convert.ToInt32(args[2]);

        WrapFsOperation("chown", path, () =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NodeError("ENOSYS", "function not implemented", "chown", path);
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            var result = LibC.chown(path, uid, gid);
            if (result != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new NodeError(
                    LibC.GetErrnoCode(errno),
                    LibC.GetErrnoMessage(errno),
                    "chown",
                    path,
                    errno
                );
            }
        });

        return null;
    }

    private static object? LchownSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var uid = Convert.ToInt32(args[1]);
        var gid = Convert.ToInt32(args[2]);

        WrapFsOperation("lchown", path, () =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NodeError("ENOSYS", "function not implemented", "lchown", path);
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            var result = LibC.lchown(path, uid, gid);
            if (result != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new NodeError(
                    LibC.GetErrnoCode(errno),
                    LibC.GetErrnoMessage(errno),
                    "lchown",
                    path,
                    errno
                );
            }
        });

        return null;
    }

    private static object? TruncateSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var len = args.Count > 1 && args[1] != null ? Convert.ToInt64(args[1]) : 0L;

        WrapFsOperation("truncate", path, () =>
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
            fs.SetLength(len);
        });

        return null;
    }

    private static object? SymlinkSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var target = args[0]?.ToString() ?? "";
        var linkPath = args[1]?.ToString() ?? "";
        var type = args.Count > 2 ? args[2]?.ToString() : null;

        WrapFsOperation("symlink", linkPath, () =>
        {
            if (Directory.Exists(target) || type == "dir" || type == "junction")
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, target);
            }
        });

        return null;
    }

    private static object? ReadlinkSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        return WrapFsOperation("readlink", path, () =>
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

            return (object?)linkTarget;
        });
    }

    private static object? RealpathSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        return WrapFsOperation("realpath", path, () =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            var fullPath = Path.GetFullPath(path);

            // Try to resolve symlinks
            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Exists)
            {
                var resolved = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null)
                {
                    return (object?)resolved.FullName;
                }
            }

            var dirInfo = new DirectoryInfo(fullPath);
            if (dirInfo.Exists)
            {
                var resolved = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (resolved != null)
                {
                    return (object?)resolved.FullName;
                }
            }

            return (object?)fullPath;
        });
    }

    private static object? UtimesSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var atime = ParseTimestamp(args[1]);
        var mtime = ParseTimestamp(args[2]);

        WrapFsOperation("utimes", path, () =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            File.SetLastAccessTime(path, atime);
            File.SetLastWriteTime(path, mtime);
        });

        return null;
    }

    /// <summary>
    /// Parses a timestamp value (Unix seconds, milliseconds, or Date object).
    /// </summary>
    private static DateTime ParseTimestamp(object? value)
    {
        return value switch
        {
            double d => DateTimeOffset.FromUnixTimeSeconds((long)d).LocalDateTime,
            long l => DateTimeOffset.FromUnixTimeSeconds(l).LocalDateTime,
            int i => DateTimeOffset.FromUnixTimeSeconds(i).LocalDateTime,
            SharpTSObject obj when obj.HasProperty("getTime") =>
                // Date object - call getTime() which returns milliseconds
                ParseDateObject(obj),
            _ => throw new ArgumentException("Invalid timestamp")
        };
    }

    private static DateTime ParseDateObject(SharpTSObject dateObj)
    {
        var getTime = dateObj.GetProperty("getTime");
        if (getTime is BuiltInMethod method)
        {
            // BuiltInMethod.Call needs an Interpreter but we pass null since getTime doesn't use it
            var result = method.Bind(dateObj).Call(null!, []);
            if (result is double ms)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime;
            }
        }
        throw new ArgumentException("Invalid Date object");
    }

    #region File Descriptor APIs

    /// <summary>
    /// Static file descriptor table for interpreter mode.
    /// </summary>
    private static readonly FileDescriptorTable _fdTable = FileDescriptorTable.Instance;

    private static object? OpenSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var flags = args[1]; // string ('r', 'w', etc.) or number
        // mode parameter is ignored on Windows, used on Unix for permissions

        return WrapFsOperation("open", path, () =>
        {
            var (fileMode, fileAccess, fileShare) = FsFlags.Parse(flags);
            var fd = _fdTable.Open(path, fileMode, fileAccess, fileShare);
            return (double)fd;
        });
    }

    private static object? CloseSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var fd = Convert.ToInt32(args[0]);

        WrapFsOperation("close", null, () =>
        {
            _fdTable.Close(fd);
        });
        return null;
    }

    private static object? ReadSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var fd = Convert.ToInt32(args[0]);
        var buffer = args[1] as SharpTSBuffer ?? throw new NodeError("ERR_INVALID_ARG_TYPE", "buffer must be a Buffer", "read", null);
        var offset = Convert.ToInt32(args[2]);
        var length = Convert.ToInt32(args[3]);
        var position = args[4]; // null means use current position

        return WrapFsOperation("read", null, () =>
        {
            var stream = _fdTable.Get(fd);

            // Handle position parameter
            if (position != null && position is not SharpTSUndefined)
            {
                var pos = Convert.ToInt64(position);
                stream.Seek(pos, SeekOrigin.Begin);
            }

            // Read into buffer
            var data = buffer.Data;
            var bytesRead = stream.Read(data, offset, length);
            return (double)bytesRead;
        });
    }

    private static object? WriteSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var fd = Convert.ToInt32(args[0]);
        var data = args[1];

        // writeSync can be called with:
        // (fd, buffer, offset, length, position) - Buffer write
        // (fd, string, position, encoding) - String write
        if (data is SharpTSBuffer buffer)
        {
            var offset = args.Count > 2 && args[2] != null ? Convert.ToInt32(args[2]) : 0;
            var length = args.Count > 3 && args[3] != null ? Convert.ToInt32(args[3]) : buffer.Length;
            var position = args.Count > 4 ? args[4] : null;

            return WrapFsOperation("write", null, () =>
            {
                var stream = _fdTable.Get(fd);

                if (position != null && position is not SharpTSUndefined)
                {
                    var pos = Convert.ToInt64(position);
                    stream.Seek(pos, SeekOrigin.Begin);
                }

                stream.Write(buffer.Data, offset, length);
                return (double)length;
            });
        }
        else
        {
            // String write
            var str = data?.ToString() ?? "";
            var position = args.Count > 2 ? args[2] : null;
            var bytes = System.Text.Encoding.UTF8.GetBytes(str);

            return WrapFsOperation("write", null, () =>
            {
                var stream = _fdTable.Get(fd);

                if (position != null && position is not SharpTSUndefined)
                {
                    var pos = Convert.ToInt64(position);
                    stream.Seek(pos, SeekOrigin.Begin);
                }

                stream.Write(bytes, 0, bytes.Length);
                return (double)bytes.Length;
            });
        }
    }

    private static object? FstatSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var fd = Convert.ToInt32(args[0]);

        return WrapFsOperation("fstat", null, () =>
        {
            var stream = _fdTable.Get(fd);

            return (object?)new SharpTSObject(new Dictionary<string, object?>
            {
                ["isDirectory"] = new BuiltInMethod("isDirectory", 0, 0, (_, _, _) => false), // File descriptors are always files
                ["isFile"] = new BuiltInMethod("isFile", 0, 0, (_, _, _) => true),
                ["isSymbolicLink"] = new BuiltInMethod("isSymbolicLink", 0, 0, (_, _, _) => false),
                ["size"] = (double)stream.Length
            });
        });
    }

    private static object? FtruncateSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var fd = Convert.ToInt32(args[0]);
        var len = args.Count > 1 && args[1] != null ? Convert.ToInt64(args[1]) : 0L;

        WrapFsOperation("ftruncate", null, () =>
        {
            var stream = _fdTable.Get(fd);
            stream.SetLength(len);
        });
        return null;
    }

    #endregion

    #region Directory Utilities

    private static object? MkdtempSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var prefix = args[0]?.ToString() ?? "";

        return WrapFsOperation("mkdtemp", null, () =>
        {
            // Generate a unique directory name
            var tempPath = Path.Combine(Path.GetTempPath(), prefix + Path.GetRandomFileName().Replace(".", ""));
            Directory.CreateDirectory(tempPath);
            return (object?)tempPath;
        });
    }

    private static object? OpendirSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        return WrapFsOperation("opendir", path, () =>
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"no such file or directory, opendir '{path}'");
            }
            return (object?)new SharpTSDir(path);
        });
    }

    #endregion

    #region Hard Links

    private static object? LinkSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var existingPath = args[0]?.ToString() ?? "";
        var newPath = args[1]?.ToString() ?? "";

        WrapFsOperation("link", newPath, () =>
        {
            if (!File.Exists(existingPath))
            {
                throw new FileNotFoundException("no such file or directory", existingPath);
            }
            if (File.Exists(newPath))
            {
                throw new IOException($"EEXIST: file already exists, link '{existingPath}' -> '{newPath}'");
            }

            LibC.CreateHardLink(existingPath, newPath);
        });
        return null;
    }

    #endregion

    #region Callback-based Async Methods

    /// <summary>
    /// Extracts the callback function from the arguments.
    /// The callback is always the last argument.
    /// </summary>
    private static ISharpTSCallable GetCallback(List<object?> args)
    {
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: callback is required");
        return callback;
    }

    /// <summary>
    /// Schedules an async callback on the interpreter's event loop.
    /// </summary>
    private static void ScheduleCallback(Interp interpreter, ISharpTSCallable callback, object? error, object? result)
    {
        interpreter.ScheduleTimer(0, 0, () =>
        {
            callback.Call(interpreter, [error, result]);
        }, isInterval: false);
    }

    /// <summary>
    /// Converts an exception to a Node.js-style error object for callbacks.
    /// </summary>
    private static SharpTSObject CreateErrorObject(Exception ex, string syscall, string? path)
    {
        var code = ex is NodeError ne ? ne.Code : NodeErrorCodes.FromException(ex);
        var message = ex.Message;

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["code"] = code,
            ["syscall"] = syscall,
            ["path"] = path,
            ["message"] = $"{code}: {message}, {syscall} '{path}'"
        });
    }

    private static object? ReadFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        // Extract encoding from middle argument if present
        object? encoding = null;
        if (args.Count == 3)
        {
            var options = args[1];
            if (options is string s)
            {
                encoding = s;
            }
            else if (options is SharpTSObject opts)
            {
                encoding = opts.GetProperty("encoding");
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.ReadFileAsync(path, encoding);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "open", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? WriteFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var data = args[1];
        var callback = GetCallback(args);
        var options = args.Count == 4 ? args[2] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.WriteFileAsync(path, data, options);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "open", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? AppendFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var data = args[1];
        var callback = GetCallback(args);
        var options = args.Count == 4 ? args[2] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.AppendFileAsync(path, data, options);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "open", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Stat(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.StatAsync(path);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "stat", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Lstat(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.LstatAsync(path);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "lstat", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Unlink(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.UnlinkAsync(path);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "unlink", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Mkdir(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);
        var options = args.Count == 3 ? args[1] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.MkdirAsync(path, options);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "mkdir", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Rmdir(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);
        var options = args.Count == 3 ? args[1] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.RmdirAsync(path, options);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "rmdir", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Readdir(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);
        var options = args.Count == 3 ? args[1] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.ReaddirAsync(path, options);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "readdir", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Rename(Interp interpreter, object? receiver, List<object?> args)
    {
        var oldPath = args[0]?.ToString() ?? "";
        var newPath = args[1]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.RenameAsync(oldPath, newPath);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "rename", oldPath);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? CopyFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var src = args[0]?.ToString() ?? "";
        var dest = args[1]?.ToString() ?? "";
        var callback = GetCallback(args);
        var mode = args.Count == 4 ? args[2] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.CopyFileAsync(src, dest, mode);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "copyfile", src);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Access(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);
        var mode = args.Count == 3 ? args[1] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.AccessAsync(path, mode);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "access", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Chmod(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var mode = args[1];
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.ChmodAsync(path, mode);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "chmod", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Truncate(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);
        var len = args.Count == 3 ? args[1] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.TruncateAsync(path, len);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "truncate", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Utimes(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var atime = args[1];
        var mtime = args[2];
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.UtimesAsync(path, atime, mtime);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "utimes", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Readlink(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.ReadlinkAsync(path);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "readlink", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Realpath(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.RealpathAsync(path);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "realpath", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Symlink(Interp interpreter, object? receiver, List<object?> args)
    {
        var target = args[0]?.ToString() ?? "";
        var path = args[1]?.ToString() ?? "";
        var callback = GetCallback(args);
        var type = args.Count == 4 ? args[2] : null;

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.SymlinkAsync(target, path, type);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "symlink", path);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Link(Interp interpreter, object? receiver, List<object?> args)
    {
        var existingPath = args[0]?.ToString() ?? "";
        var newPath = args[1]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                await FsAsyncHelpers.LinkAsync(existingPath, newPath);
                ScheduleCallback(interpreter, callback, null, null);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "link", newPath);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? Mkdtemp(Interp interpreter, object? receiver, List<object?> args)
    {
        var prefix = args[0]?.ToString() ?? "";
        var callback = GetCallback(args);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FsAsyncHelpers.MkdtempAsync(prefix);
                ScheduleCallback(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                var error = CreateErrorObject(ex, "mkdtemp", null);
                ScheduleCallback(interpreter, callback, error, null);
            }
        });

        return SharpTSUndefined.Instance;
    }

    #endregion
}
