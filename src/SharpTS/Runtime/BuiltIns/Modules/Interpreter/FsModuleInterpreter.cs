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
            ["existsSync"] = BuiltInMethod.CreateV2("existsSync", 1, 1, ExistsSync),
            ["readFileSync"] = BuiltInMethod.CreateV2("readFileSync", 1, 2, ReadFileSync),
            ["writeFileSync"] = BuiltInMethod.CreateV2("writeFileSync", 2, 3, WriteFileSync),
            ["appendFileSync"] = BuiltInMethod.CreateV2("appendFileSync", 2, 3, AppendFileSync),
            ["unlinkSync"] = BuiltInMethod.CreateV2("unlinkSync", 1, 1, UnlinkSync),
            ["mkdirSync"] = BuiltInMethod.CreateV2("mkdirSync", 1, 2, MkdirSync),
            ["rmdirSync"] = BuiltInMethod.CreateV2("rmdirSync", 1, 2, RmdirSync),
            ["readdirSync"] = BuiltInMethod.CreateV2("readdirSync", 1, 2, ReaddirSync),
            ["statSync"] = BuiltInMethod.CreateV2("statSync", 1, 1, StatSync),
            ["lstatSync"] = BuiltInMethod.CreateV2("lstatSync", 1, 1, LstatSync),
            // Raw stat records (#977) — the TS Stats class shapes these.
            ["statRaw"] = BuiltInMethod.CreateV2("statRaw", 1, 1, StatRaw),
            ["lstatRaw"] = BuiltInMethod.CreateV2("lstatRaw", 1, 1, LstatRaw),
            ["fstatRaw"] = BuiltInMethod.CreateV2("fstatRaw", 1, 1, FstatRaw),
            ["renameSync"] = BuiltInMethod.CreateV2("renameSync", 2, 2, RenameSync),
            ["copyFileSync"] = BuiltInMethod.CreateV2("copyFileSync", 2, 3, CopyFileSync),
            ["accessSync"] = BuiltInMethod.CreateV2("accessSync", 1, 2, AccessSync),
            ["chmodSync"] = BuiltInMethod.CreateV2("chmodSync", 2, 2, ChmodSync),
            ["chownSync"] = BuiltInMethod.CreateV2("chownSync", 3, 3, ChownSync),
            ["lchownSync"] = BuiltInMethod.CreateV2("lchownSync", 3, 3, LchownSync),
            ["truncateSync"] = BuiltInMethod.CreateV2("truncateSync", 1, 2, TruncateSync),
            ["symlinkSync"] = BuiltInMethod.CreateV2("symlinkSync", 2, 3, SymlinkSync),
            ["readlinkSync"] = BuiltInMethod.CreateV2("readlinkSync", 1, 1, ReadlinkSync),
            ["realpathSync"] = BuiltInMethod.CreateV2("realpathSync", 1, 1, RealpathSync),
            ["utimesSync"] = BuiltInMethod.CreateV2("utimesSync", 3, 3, UtimesSync),
            // File descriptor APIs
            ["openSync"] = BuiltInMethod.CreateV2("openSync", 2, 3, OpenSync),
            ["closeSync"] = BuiltInMethod.CreateV2("closeSync", 1, 1, CloseSync),
            ["readSync"] = BuiltInMethod.CreateV2("readSync", 5, 5, ReadSync),
            ["writeSync"] = BuiltInMethod.CreateV2("writeSync", 2, 5, WriteSync),
            ["fstatSync"] = BuiltInMethod.CreateV2("fstatSync", 1, 1, FstatSync),
            ["ftruncateSync"] = BuiltInMethod.CreateV2("ftruncateSync", 1, 2, FtruncateSync),
            // Long-tail fd primitives (#976) — the TS facade derives fsync/fdatasync,
            // fchmod/fchown/futimes (via fdPath), and statfs from these.
            ["fsyncSync"] = BuiltInMethod.CreateV2("fsyncSync", 1, 1, FsyncSync),
            ["fdPath"] = BuiltInMethod.CreateV2("fdPath", 1, 1, FdPath),
            ["statfsRaw"] = BuiltInMethod.CreateV2("statfsRaw", 1, 1, StatfsRaw),
            // Directory utilities
            ["mkdtempSync"] = BuiltInMethod.CreateV2("mkdtempSync", 1, 1, MkdtempSync),
            ["opendirSync"] = BuiltInMethod.CreateV2("opendirSync", 1, 1, OpendirSync),
            // Hard links
            ["linkSync"] = BuiltInMethod.CreateV2("linkSync", 2, 2, LinkSync),
            // Stream factory methods
            ["createReadStream"] = BuiltInMethod.CreateV2("createReadStream", 1, 2, CreateReadStream),
            ["createWriteStream"] = BuiltInMethod.CreateV2("createWriteStream", 1, 2, CreateWriteStream),
            ["constants"] = CreateConstants(),

            // Callback-based async methods
            ["readFile"] = BuiltInMethod.CreateV2("readFile", 2, 3, ReadFile),
            ["writeFile"] = BuiltInMethod.CreateV2("writeFile", 3, 4, WriteFile),
            ["appendFile"] = BuiltInMethod.CreateV2("appendFile", 3, 4, AppendFile),
            ["stat"] = BuiltInMethod.CreateV2("stat", 2, 3, Stat),
            ["lstat"] = BuiltInMethod.CreateV2("lstat", 2, 3, Lstat),
            ["unlink"] = BuiltInMethod.CreateV2("unlink", 2, 2, Unlink),
            ["mkdir"] = BuiltInMethod.CreateV2("mkdir", 2, 3, Mkdir),
            ["rmdir"] = BuiltInMethod.CreateV2("rmdir", 2, 3, Rmdir),
            ["readdir"] = BuiltInMethod.CreateV2("readdir", 2, 3, Readdir),
            ["rename"] = BuiltInMethod.CreateV2("rename", 3, 3, Rename),
            ["copyFile"] = BuiltInMethod.CreateV2("copyFile", 3, 4, CopyFile),
            ["access"] = BuiltInMethod.CreateV2("access", 2, 3, Access),
            ["chmod"] = BuiltInMethod.CreateV2("chmod", 3, 3, Chmod),
            ["truncate"] = BuiltInMethod.CreateV2("truncate", 2, 3, Truncate),
            ["utimes"] = BuiltInMethod.CreateV2("utimes", 4, 4, Utimes),
            ["readlink"] = BuiltInMethod.CreateV2("readlink", 2, 3, Readlink),
            ["realpath"] = BuiltInMethod.CreateV2("realpath", 2, 3, Realpath),
            ["symlink"] = BuiltInMethod.CreateV2("symlink", 3, 4, Symlink),
            ["link"] = BuiltInMethod.CreateV2("link", 3, 3, Link),
            ["mkdtemp"] = BuiltInMethod.CreateV2("mkdtemp", 2, 3, Mkdtemp),

            // File watching
            ["watch"] = BuiltInMethod.CreateV2("watch", 1, 3, Watch),
            ["watchFile"] = BuiltInMethod.CreateV2("watchFile", 2, 3, WatchFile),
            ["unwatchFile"] = BuiltInMethod.CreateV2("unwatchFile", 1, 2, UnwatchFile),

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

    private static RuntimeValue ExistsSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromBoolean(File.Exists(path) || Directory.Exists(path));
    }

    /// <summary>
    /// Extracts the encoding name from a string-or-options value (Node accepts both
    /// <c>'utf8'</c> and <c>{ encoding: 'utf8' }</c>). Returns null when no encoding
    /// is given — on read that means "return a Buffer".
    /// </summary>
    internal static string? EncodingName(object? options)
    {
        if (options is string s) return s;
        if (options is SharpTSObject opts && opts.GetProperty("encoding") is string es) return es;
        return null;
    }

    private static RuntimeValue ReadFileSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var encoding = EncodingName(args.Length >= 2 ? args[1].ToObject() : null);

        return RuntimeValue.FromBoxed(WrapFsOperation("open", path, () =>
        {
            var bytes = File.ReadAllBytes(path);
            // No encoding → Buffer; otherwise decode the raw bytes per the encoding.
            return encoding != null ? (object?)BufferEncoding.Decode(bytes, encoding) : new SharpTSBuffer(bytes);
        }));
    }

    private static RuntimeValue WriteFileSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        // Buffer/TypedArray data is written byte-exact; a string honors the encoding option.
        var bytes = BufferEncoding.ToBytes(args[1].ToObject(), EncodingName(args.Length >= 3 ? args[2].ToObject() : null));
        WrapFsOperation("open", path, () => File.WriteAllBytes(path, bytes));
        return RuntimeValue.Null;
    }

    private static RuntimeValue AppendFileSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var bytes = BufferEncoding.ToBytes(args[1].ToObject(), EncodingName(args.Length >= 3 ? args[2].ToObject() : null));
        WrapFsOperation("open", path, () => File.AppendAllBytes(path, bytes));
        return RuntimeValue.Null;
    }

    private static RuntimeValue UnlinkSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        WrapFsOperation("unlink", path, () =>
        {
            // File.Delete doesn't throw if file doesn't exist, but Node.js does
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
            File.Delete(path);
        });
        return RuntimeValue.Null;
    }

    private static RuntimeValue MkdirSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        WrapFsOperation("mkdir", path, () => Directory.CreateDirectory(path));
        return RuntimeValue.Null;
    }

    private static RuntimeValue RmdirSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var recursive = false;

        if (args.Length >= 2 && args[1].ToObject() is SharpTSObject options)
        {
            var recursiveValue = options.GetProperty("recursive");
            recursive = recursiveValue is true || (recursiveValue is double d && d != 0);
        }

        WrapFsOperation("rmdir", path, () => Directory.Delete(path, recursive));
        return RuntimeValue.Null;
    }

    private static RuntimeValue ReaddirSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var withFileTypes = false;
        var recursive = false;

        if (args.Length > 1 && args[1].ToObject() is SharpTSObject options)
        {
            var wft = options.GetProperty("withFileTypes");
            withFileTypes = wft is true || (wft is double d && d != 0);

            var rec = options.GetProperty("recursive");
            recursive = rec is true || (rec is double rd && rd != 0);
        }

        return RuntimeValue.FromBoxed(WrapFsOperation("readdir", path, () =>
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = Directory.GetFileSystemEntries(path, "*", searchOption);
            var list = new List<object?>();

            if (withFileTypes)
            {
                foreach (var entry in entries)
                {
                    list.Add(CreateDirent(entry, Path.GetDirectoryName(entry) ?? path));
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
        }));
    }

    /// <summary>
    /// Creates a Dirent-like object for readdirSync({ withFileTypes: true }).
    /// </summary>
    internal static SharpTSObject CreateDirent(string fullPath, string parentPath)
    {
        var name = Path.GetFileName(fullPath);
        var isFile = File.Exists(fullPath);
        var isDir = Directory.Exists(fullPath);
        var isSymlink = false;

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Exists || isDir)
        {
            isSymlink = fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        var fileOnly = isFile && !isDir;

        // is*() are methods (Node Dirent), uniform across sync/async/compiled (#977).
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["parentPath"] = parentPath,
            ["path"] = parentPath,
            ["isFile"] = BuiltInMethod.CreateV2("isFile", 0, 0, (_, _, _) => RuntimeValue.FromBoolean(fileOnly)),
            ["isDirectory"] = BuiltInMethod.CreateV2("isDirectory", 0, 0, (_, _, _) => RuntimeValue.FromBoolean(isDir)),
            ["isSymbolicLink"] = BuiltInMethod.CreateV2("isSymbolicLink", 0, 0, (_, _, _) => RuntimeValue.FromBoolean(isSymlink)),
            ["isBlockDevice"] = BuiltInMethod.CreateV2("isBlockDevice", 0, 0, (_, _, _) => RuntimeValue.False),
            ["isCharacterDevice"] = BuiltInMethod.CreateV2("isCharacterDevice", 0, 0, (_, _, _) => RuntimeValue.False),
            ["isFIFO"] = BuiltInMethod.CreateV2("isFIFO", 0, 0, (_, _, _) => RuntimeValue.False),
            ["isSocket"] = BuiltInMethod.CreateV2("isSocket", 0, 0, (_, _, _) => RuntimeValue.False),
        });
    }

    private static RuntimeValue StatSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";

        return RuntimeValue.FromBoxed(WrapFsOperation("stat", path, () =>
        {
            if (Directory.Exists(path))
            {
                return (object?)new SharpTSObject(new Dictionary<string, object?>
                {
                    ["isDirectory"] = BuiltInMethod.CreateV2("isDirectory", 0, 0, (_, _, _) => RuntimeValue.True),
                    ["isFile"] = BuiltInMethod.CreateV2("isFile", 0, 0, (_, _, _) => RuntimeValue.False),
                    ["size"] = 0.0
                });
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return new SharpTSObject(new Dictionary<string, object?>
                {
                    ["isDirectory"] = BuiltInMethod.CreateV2("isDirectory", 0, 0, (_, _, _) => RuntimeValue.False),
                    ["isFile"] = BuiltInMethod.CreateV2("isFile", 0, 0, (_, _, _) => RuntimeValue.True),
                    ["size"] = (double)fileInfo.Length
                });
            }
            else
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
        }));
    }

    private static RuntimeValue RenameSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var oldPath = args[0].ToObject()?.ToString() ?? "";
        var newPath = args[1].ToObject()?.ToString() ?? "";

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
        return RuntimeValue.Null;
    }

    private static RuntimeValue CopyFileSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var src = args[0].ToObject()?.ToString() ?? "";
        var dest = args[1].ToObject()?.ToString() ?? "";
        WrapFsOperation("copyfile", src, () => File.Copy(src, dest, overwrite: true));
        return RuntimeValue.Null;
    }

    private static RuntimeValue AccessSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var mode = args.Length >= 2 && args[1].ToObject() is double d ? (int)d : 0;

        WrapFsOperation("access", path, () => CheckAccess(path, mode));

        return RuntimeValue.Null;
    }

    /// <summary>
    /// Enforces an fs.access mode against a path. F_OK requires existence (ENOENT
    /// otherwise); W_OK requires the target to be writable — best-effort on Windows
    /// (a read-only attribute denies writes, EACCES). R_OK/X_OK are treated as
    /// granted when the path exists on the supported platforms. Shared by the sync
    /// and async access implementations.
    /// </summary>
    internal static void CheckAccess(string path, int mode)
    {
        var isFile = File.Exists(path);
        var isDir = Directory.Exists(path);
        if (!isFile && !isDir)
        {
            throw new FileNotFoundException("no such file or directory", path);
        }

        // W_OK (2): writability. A read-only file can't be written (Windows + Unix).
        if ((mode & 2) != 0 && isFile && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
        {
            throw new UnauthorizedAccessException($"permission denied, access '{path}'");
        }
    }

    private static RuntimeValue LstatSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";

        return RuntimeValue.FromBoxed(WrapFsOperation("lstat", path, () =>
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
                ["isDirectory"] = BuiltInMethod.CreateV2("isDirectory", 0, 0, (_, _, _) => RuntimeValue.FromBoolean(isDir)),
                ["isFile"] = BuiltInMethod.CreateV2("isFile", 0, 0, (_, _, _) => RuntimeValue.FromBoolean(isFile)),
                ["isSymbolicLink"] = BuiltInMethod.CreateV2("isSymbolicLink", 0, 0, (_, _, _) => RuntimeValue.FromBoolean(isSymlink)),
                ["size"] = size
            });
        }));
    }

    // ===== Stats raw record (#977) =====
    // statRaw/lstatRaw/fstatRaw return a flat numeric record; the TS Stats class
    // (stdlib/node/fs.ts) shapes it into the Node Stats object. The compiled twin
    // ($Runtime.FsStatRaw/FsLstatRaw/FsFstatRaw) mirrors this logic byte-for-byte.

    private static double StatTimeMs(DateTime dt) => new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeMilliseconds();

    /// <summary>
    /// Builds the flat numeric stat record. Perm bits are best-effort and
    /// platform-uniform (dir 0o777; file 0o666, or 0o444 when read-only); the
    /// device/inode/uid/gid fields are documented 0 fallbacks. Mirrored exactly by
    /// the compiled emitter so interpreter and compiled Stats are byte-identical.
    /// </summary>
    internal static SharpTSObject BuildStatRecord(bool isDir, bool isSymlink, bool isReadOnly, double size,
        DateTime atime, DateTime mtime, DateTime ctime, DateTime birthtime)
    {
        int typeBits = isSymlink ? 0xA000 : (isDir ? 0x4000 : 0x8000);
        int permBits = isDir ? 0x1FF : (isReadOnly ? 0x124 : 0x1B6);
        double mode = typeBits | permBits;
        double blocks = Math.Floor((size + 511.0) / 512.0);
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["mode"] = mode,
            ["size"] = size,
            ["atimeMs"] = StatTimeMs(atime),
            ["mtimeMs"] = StatTimeMs(mtime),
            ["ctimeMs"] = StatTimeMs(ctime),
            ["birthtimeMs"] = StatTimeMs(birthtime),
            ["dev"] = 0.0,
            ["ino"] = 0.0,
            ["nlink"] = 1.0,
            ["uid"] = 0.0,
            ["gid"] = 0.0,
            ["rdev"] = 0.0,
            ["blksize"] = 4096.0,
            ["blocks"] = blocks,
        });
    }

    private static RuntimeValue StatRaw(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromBoxed(WrapFsOperation("stat", path, () =>
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return (object?)BuildStatRecord(true, false, false, 0.0,
                    di.LastAccessTime, di.LastWriteTime, di.CreationTime, di.CreationTime);
            }
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                bool ro = fi.Attributes.HasFlag(FileAttributes.ReadOnly);
                return BuildStatRecord(false, false, ro, fi.Length,
                    fi.LastAccessTime, fi.LastWriteTime, fi.CreationTime, fi.CreationTime);
            }
            throw new FileNotFoundException("no such file or directory", path);
        }));
    }

    private static RuntimeValue LstatRaw(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromBoxed(WrapFsOperation("lstat", path, () =>
        {
            var fi = new FileInfo(path);
            var di = new DirectoryInfo(path);
            if (!fi.Exists && !di.Exists)
            {
                throw new FileNotFoundException("no such file or directory", path);
            }
            bool isSymlink = (fi.Exists ? fi.Attributes : di.Attributes).HasFlag(FileAttributes.ReparsePoint);
            bool isDir = di.Exists && !fi.Exists;
            double size = fi.Exists ? fi.Length : 0.0;
            bool ro = fi.Exists && fi.Attributes.HasFlag(FileAttributes.ReadOnly);
            DateTime at = isDir ? di.LastAccessTime : fi.LastAccessTime;
            DateTime mt = isDir ? di.LastWriteTime : fi.LastWriteTime;
            DateTime ct = isDir ? di.CreationTime : fi.CreationTime;
            return (object?)BuildStatRecord(isDir, isSymlink, ro, size, at, mt, ct, ct);
        }));
    }

    private static RuntimeValue FstatRaw(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());
        return RuntimeValue.FromBoxed(WrapFsOperation("fstat", null, () =>
        {
            var stream = _fdTable.Get(fd);
            double size = stream.Length;
            DateTime at = DateTime.UnixEpoch, mt = DateTime.UnixEpoch, ct = DateTime.UnixEpoch;
            try
            {
                var fi = new FileInfo(stream.Name);
                if (fi.Exists) { at = fi.LastAccessTime; mt = fi.LastWriteTime; ct = fi.CreationTime; }
            }
            catch { /* fd without a recoverable path → epoch fallback */ }
            return (object?)BuildStatRecord(false, false, false, size, at, mt, ct, ct);
        }));
    }

    private static RuntimeValue ChmodSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var mode = Convert.ToInt32(args[1].ToObject());

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

        return RuntimeValue.Null;
    }

    private static RuntimeValue ChownSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var uid = Convert.ToInt32(args[1].ToObject());
        var gid = Convert.ToInt32(args[2].ToObject());

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

        return RuntimeValue.Null;
    }

    private static RuntimeValue LchownSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var uid = Convert.ToInt32(args[1].ToObject());
        var gid = Convert.ToInt32(args[2].ToObject());

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

        return RuntimeValue.Null;
    }

    private static RuntimeValue TruncateSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var len = args.Length > 1 && !args[1].IsNull ? Convert.ToInt64(args[1].ToObject()) : 0L;

        WrapFsOperation("truncate", path, () =>
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
            fs.SetLength(len);
        });

        return RuntimeValue.Null;
    }

    private static RuntimeValue SymlinkSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var target = args[0].ToObject()?.ToString() ?? "";
        var linkPath = args[1].ToObject()?.ToString() ?? "";
        var type = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

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

        return RuntimeValue.Null;
    }

    private static RuntimeValue ReadlinkSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";

        return RuntimeValue.FromBoxed(WrapFsOperation("readlink", path, () =>
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
        }));
    }

    private static RuntimeValue RealpathSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";

        return RuntimeValue.FromBoxed(WrapFsOperation("realpath", path, () =>
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
        }));
    }

    private static RuntimeValue UtimesSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var atime = ParseTimestamp(args[1].ToObject());
        var mtime = ParseTimestamp(args[2].ToObject());

        WrapFsOperation("utimes", path, () =>
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("no such file or directory", path);
            }

            File.SetLastAccessTime(path, atime);
            File.SetLastWriteTime(path, mtime);
        });

        return RuntimeValue.Null;
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

    private static RuntimeValue OpenSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var flags = args[1].ToObject(); // string ('r', 'w', etc.) or number
        // mode parameter is ignored on Windows, used on Unix for permissions

        return RuntimeValue.FromNumber(WrapFsOperation("open", path, () =>
        {
            var (fileMode, fileAccess, fileShare) = FsFlags.Parse(flags);
            var fd = _fdTable.Open(path, fileMode, fileAccess, fileShare);
            return (double)fd;
        }));
    }

    private static RuntimeValue CloseSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());

        WrapFsOperation("close", null, () =>
        {
            _fdTable.Close(fd);
        });
        return RuntimeValue.Null;
    }

    private static RuntimeValue ReadSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());
        var buffer = args[1].ToObject() as SharpTSBuffer ?? throw new NodeError("ERR_INVALID_ARG_TYPE", "buffer must be a Buffer", "read", null);
        var offset = Convert.ToInt32(args[2].ToObject());
        var length = Convert.ToInt32(args[3].ToObject());
        var position = args[4].ToObject(); // null means use current position

        return RuntimeValue.FromNumber(WrapFsOperation("read", null, () =>
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
        }));
    }

    private static RuntimeValue WriteSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());
        var data = args[1].ToObject();

        // writeSync can be called with:
        // (fd, buffer, offset, length, position) - Buffer write
        // (fd, string, position, encoding) - String write
        if (data is SharpTSBuffer buffer)
        {
            var offset = args.Length > 2 && !args[2].IsNull ? Convert.ToInt32(args[2].ToObject()) : 0;
            var length = args.Length > 3 && !args[3].IsNull ? Convert.ToInt32(args[3].ToObject()) : buffer.Length;
            var position = args.Length > 4 ? args[4].ToObject() : null;

            return RuntimeValue.FromNumber(WrapFsOperation("write", null, () =>
            {
                var stream = _fdTable.Get(fd);

                if (position != null && position is not SharpTSUndefined)
                {
                    var pos = Convert.ToInt64(position);
                    stream.Seek(pos, SeekOrigin.Begin);
                }

                stream.Write(buffer.Data, offset, length);
                return (double)length;
            }));
        }
        else
        {
            // String write
            var str = data?.ToString() ?? "";
            var position = args.Length > 2 ? args[2].ToObject() : null;
            var bytes = System.Text.Encoding.UTF8.GetBytes(str);

            return RuntimeValue.FromNumber(WrapFsOperation("write", null, () =>
            {
                var stream = _fdTable.Get(fd);

                if (position != null && position is not SharpTSUndefined)
                {
                    var pos = Convert.ToInt64(position);
                    stream.Seek(pos, SeekOrigin.Begin);
                }

                stream.Write(bytes, 0, bytes.Length);
                return (double)bytes.Length;
            }));
        }
    }

    private static RuntimeValue FstatSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());

        return RuntimeValue.FromBoxed(WrapFsOperation("fstat", null, () =>
        {
            var stream = _fdTable.Get(fd);

            return (object?)new SharpTSObject(new Dictionary<string, object?>
            {
                ["isDirectory"] = BuiltInMethod.CreateV2("isDirectory", 0, 0, (_, _, _) => RuntimeValue.False), // File descriptors are always files
                ["isFile"] = BuiltInMethod.CreateV2("isFile", 0, 0, (_, _, _) => RuntimeValue.True),
                ["isSymbolicLink"] = BuiltInMethod.CreateV2("isSymbolicLink", 0, 0, (_, _, _) => RuntimeValue.False),
                ["size"] = (double)stream.Length
            });
        }));
    }

    private static RuntimeValue FtruncateSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());
        var len = args.Length > 1 && !args[1].IsNull ? Convert.ToInt64(args[1].ToObject()) : 0L;

        WrapFsOperation("ftruncate", null, () =>
        {
            var stream = _fdTable.Get(fd);
            stream.SetLength(len);
        });
        return RuntimeValue.Null;
    }

    // #976 long-tail fd primitives. The TS facade builds fsync/fdatasync and the
    // fd-variant metadata ops (fchmod/fchown/futimes) on top of these so both
    // execution modes share one Node-shape implementation.

    /// <summary>fsync/fdatasync: flush an open fd's buffered writes to disk.</summary>
    private static RuntimeValue FsyncSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());
        WrapFsOperation("fsync", null, () => _fdTable.Get(fd).Flush(true));
        return RuntimeValue.Null;
    }

    /// <summary>Resolves an open fd to its file path — lets the facade route the
    /// fd-variant metadata ops (fchmod/fchown/futimes) through the path ops.</summary>
    private static RuntimeValue FdPath(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fd = Convert.ToInt32(args[0].ToObject());
        return RuntimeValue.FromBoxed(WrapFsOperation<object?>("fstat", null, () => _fdTable.Get(fd).Name));
    }

    /// <summary>statfs: flat filesystem-stats record (type/bsize/blocks/bfree/
    /// bavail/files/ffree) the TS StatFs class shapes. Derived from DriveInfo.</summary>
    private static RuntimeValue StatfsRaw(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromBoxed(WrapFsOperation<object?>("statfs", path, () => BuildStatfsRecord(path)));
    }

    /// <summary>Builds the statfs record from DriveInfo (bsize fixed at 4096; the
    /// inode counts are unavailable through the BCL, reported as 0 like libuv on
    /// filesystems that don't expose them).</summary>
    internal static SharpTSObject BuildStatfsRecord(string path)
    {
        const double bsize = 4096.0;
        double blocks = 0, bfree = 0, bavail = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                blocks = Math.Floor(drive.TotalSize / bsize);
                bfree = Math.Floor(drive.TotalFreeSpace / bsize);
                bavail = Math.Floor(drive.AvailableFreeSpace / bsize);
            }
        }
        catch { /* unknown/virtual filesystem → zeros */ }
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["type"] = 0.0,
            ["bsize"] = bsize,
            ["blocks"] = blocks,
            ["bfree"] = bfree,
            ["bavail"] = bavail,
            ["files"] = 0.0,
            ["ffree"] = 0.0,
        });
    }

    #endregion

    #region Directory Utilities

    private static RuntimeValue MkdtempSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var prefix = args[0].ToObject()?.ToString() ?? "";

        return RuntimeValue.FromBoxed(WrapFsOperation("mkdtemp", null, () =>
        {
            // Generate a unique directory name
            var tempPath = Path.Combine(Path.GetTempPath(), prefix + Path.GetRandomFileName().Replace(".", ""));
            Directory.CreateDirectory(tempPath);
            return (object?)tempPath;
        }));
    }

    private static RuntimeValue OpendirSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";

        return RuntimeValue.FromBoxed(WrapFsOperation("opendir", path, () =>
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"no such file or directory, opendir '{path}'");
            }
            return (object?)new SharpTSDir(path);
        }));
    }

    #endregion

    #region Hard Links

    private static RuntimeValue LinkSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var existingPath = args[0].ToObject()?.ToString() ?? "";
        var newPath = args[1].ToObject()?.ToString() ?? "";

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
        return RuntimeValue.Null;
    }

    #endregion

    #region Stream Factory Methods

    private static RuntimeValue CreateReadStream(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? throw new Exception("Runtime Error: path is required");

        Dictionary<string, object?>? options = null;
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject opts)
        {
            options = new Dictionary<string, object?>();
            foreach (var key in new[] { "encoding", "start", "end", "highWaterMark", "flags", "autoClose", "fd", "mode", "emitClose", "signal" })
            {
                var val = opts.GetProperty(key);
                if (val != null)
                    options[key] = val;
            }
        }

        var stream = new SharpTSReadStream(path, options);
        // Defer reading to the next event-loop tick so listeners attached
        // synchronously after createReadStream() see open/ready/data/end/close
        // in Node order (#980). Ref/Unref keeps the loop alive across the tick (#205).
        interpreter.Ref();
        interpreter.ScheduleTimer(0, 0, () =>
        {
            try { stream.StartReading(interpreter); }
            finally { interpreter.Unref(); }
        }, isInterval: false);
        return RuntimeValue.FromObject(stream);
    }

    private static RuntimeValue CreateWriteStream(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? throw new Exception("Runtime Error: path is required");

        Dictionary<string, object?>? options = null;
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject opts)
        {
            options = new Dictionary<string, object?>();
            foreach (var key in new[] { "flags", "autoClose", "encoding", "start", "mode", "emitClose", "fd", "signal" })
            {
                var val = opts.GetProperty(key);
                if (val != null)
                    options[key] = val;
            }
        }

        var stream = new SharpTSWriteStream(path, options);
        stream.EmitOpen(interpreter);
        return RuntimeValue.FromObject(stream);
    }

    #endregion

    #region Callback-based Async Methods

    /// <summary>
    /// Extracts the callback function from the arguments.
    /// The callback is always the last argument.
    /// </summary>
    private static ISharpTSCallable GetCallback(ReadOnlySpan<RuntimeValue> args)
    {
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: callback is required");
        return callback;
    }

    /// <summary>
    /// Schedules an async callback on the interpreter's event loop.
    /// </summary>
    /// <remarks>
    /// Pairs with the <c>interpreter.Ref()</c> taken at each async dispatch
    /// site (#205): a pending fs callback counts as outstanding work, so the
    /// loop is released only after the callback has run (or failed). Every
    /// Task.Run body in this file calls ScheduleCallback exactly once — on
    /// the success path or the catch path — keeping the pairing balanced.
    /// </remarks>
    private static void ScheduleCallback(Interp interpreter, ISharpTSCallable callback, object? error, object? result)
    {
        interpreter.ScheduleTimer(0, 0, () =>
        {
            try
            {
                interpreter.InvokeGuestCallback(callback, [error, result]);
            }
            finally
            {
                interpreter.Unref();
            }
        }, isInterval: false);
    }

    /// <summary>
    /// Converts an exception to a Node.js-style error object for callbacks.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="FsPromisesModuleInterpreter"/>: the promise layer
    /// rejects with this same guest object (wrapped in
    /// <see cref="SharpTSPromiseRejectedException"/>) so the callback and promise
    /// error paths deliver an identical <c>{ code, syscall, path, message }</c>.
    /// </remarks>
    internal static SharpTSObject CreateErrorObject(Exception ex, string syscall, string? path)
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

    private static RuntimeValue ReadFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        // Extract encoding from middle argument if present
        object? encoding = null;
        if (args.Length == 3)
        {
            var options = args[1].ToObject();
            if (options is string s)
            {
                encoding = s;
            }
            else if (options is SharpTSObject opts)
            {
                encoding = opts.GetProperty("encoding");
            }
        }

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue WriteFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var data = args[1].ToObject();
        var callback = GetCallback(args);
        var options = args.Length == 4 ? args[2].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue AppendFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var data = args[1].ToObject();
        var callback = GetCallback(args);
        var options = args.Length == 4 ? args[2].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Stat(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Lstat(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Unlink(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Mkdir(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var options = args.Length == 3 ? args[1].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Rmdir(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var options = args.Length == 3 ? args[1].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Readdir(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var options = args.Length == 3 ? args[1].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Rename(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var oldPath = args[0].ToObject()?.ToString() ?? "";
        var newPath = args[1].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue CopyFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var src = args[0].ToObject()?.ToString() ?? "";
        var dest = args[1].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var mode = args.Length == 4 ? args[2].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Access(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var mode = args.Length == 3 ? args[1].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Chmod(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var mode = args[1].ToObject();
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Truncate(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var len = args.Length == 3 ? args[1].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Utimes(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var atime = args[1].ToObject();
        var mtime = args[2].ToObject();
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Readlink(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Realpath(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var path = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Symlink(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var target = args[0].ToObject()?.ToString() ?? "";
        var path = args[1].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);
        var type = args.Length == 4 ? args[2].ToObject() : null;

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Link(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var existingPath = args[0].ToObject()?.ToString() ?? "";
        var newPath = args[1].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue Mkdtemp(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var prefix = args[0].ToObject()?.ToString() ?? "";
        var callback = GetCallback(args);

        interpreter.Ref(); // released by ScheduleCallback after the callback runs (#205)
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

        return RuntimeValue.Undefined;
    }

    #endregion

    #region File Watching

    /// <summary>
    /// Tracks active StatWatchers for unwatchFile() support.
    /// </summary>
    private static readonly Dictionary<string, SharpTSStatWatcher> _statWatchers = [];

    private static RuntimeValue Watch(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var filename = args[0].ToObject()?.ToString() ?? throw new Exception("Runtime Error: watch requires a filename");

        Dictionary<string, object?>? options = null;
        ISharpTSCallable? listener = null;

        // Parse overloaded args: watch(filename, options?, listener?)
        if (args.Length > 1)
        {
            if (args[1].ToObject() is SharpTSObject optObj)
            {
                options = new Dictionary<string, object?>();
                foreach (var key in new[] { "recursive", "persistent", "encoding" })
                {
                    var val = optObj.GetProperty(key);
                    if (val != null && val is not SharpTSUndefined)
                        options[key] = val;
                }
            }
            else if (args[1].ToObject() is Dictionary<string, object?> dict)
            {
                options = dict;
            }
            else if (args[1].ToObject() is ISharpTSCallable cb)
            {
                listener = cb;
            }
        }

        if (args.Length > 2 && args[2].ToObject() is ISharpTSCallable listener2)
        {
            listener = listener2;
        }

        var watcher = new SharpTSFSWatcher(filename, options, interpreter);

        // If listener provided, register it for the 'change' event
        if (listener != null)
        {
            watcher.AddListenerDirect("change", listener);
        }

        return RuntimeValue.FromObject(watcher);
    }

    private static RuntimeValue WatchFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var filename = args[0].ToObject()?.ToString() ?? throw new Exception("Runtime Error: watchFile requires a filename");
        var fullPath = Path.GetFullPath(filename);

        Dictionary<string, object?>? options = null;
        ISharpTSCallable? listener = null;

        // Parse overloaded args: watchFile(filename, options?, listener)
        if (args.Length == 2)
        {
            if (args[1].ToObject() is ISharpTSCallable cb)
                listener = cb;
        }
        else if (args.Length >= 3)
        {
            if (args[1].ToObject() is SharpTSObject optObj)
            {
                options = new Dictionary<string, object?>();
                foreach (var key in new[] { "persistent", "interval" })
                {
                    var val = optObj.GetProperty(key);
                    if (val != null && val is not SharpTSUndefined)
                        options[key] = val;
                }
            }
            else if (args[1].ToObject() is Dictionary<string, object?> dict)
            {
                options = dict;
            }

            if (args[2].ToObject() is ISharpTSCallable cb2)
                listener = cb2;
        }

        if (listener == null)
            throw new Exception("Runtime Error: watchFile requires a listener callback");

        // Close existing watcher for this file if any
        if (_statWatchers.TryGetValue(fullPath, out var existing))
        {
            existing.CloseInternal();
        }

        var watcher = new SharpTSStatWatcher(filename, options, interpreter);
        watcher.AddListenerDirect("change", listener);
        _statWatchers[fullPath] = watcher;

        return RuntimeValue.FromObject(watcher);
    }

    private static RuntimeValue UnwatchFile(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var filename = args[0].ToObject()?.ToString() ?? throw new Exception("Runtime Error: unwatchFile requires a filename");
        var fullPath = Path.GetFullPath(filename);

        if (_statWatchers.TryGetValue(fullPath, out var watcher))
        {
            // If a specific listener was provided, remove just that listener
            if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable listener)
            {
                var offMethod = watcher.GetMember("off") as BuiltInMethod;
                offMethod?.Call(interpreter, ["change", listener]);
            }
            else
            {
                // Remove all listeners and close
                watcher.CloseInternal();
                _statWatchers.Remove(fullPath);
            }
        }

        return RuntimeValue.Null;
    }

    #endregion
}
