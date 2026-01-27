using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'fs' module (sync APIs only).
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
            ["readdirSync"] = new BuiltInMethod("readdirSync", 1, 1, ReaddirSync),
            ["statSync"] = new BuiltInMethod("statSync", 1, 1, StatSync),
            ["lstatSync"] = new BuiltInMethod("lstatSync", 1, 1, StatSync), // Same as statSync for now
            ["renameSync"] = new BuiltInMethod("renameSync", 2, 2, RenameSync),
            ["copyFileSync"] = new BuiltInMethod("copyFileSync", 2, 3, CopyFileSync),
            ["accessSync"] = new BuiltInMethod("accessSync", 1, 2, AccessSync)
        };
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
        return WrapFsOperation("readdir", path, () =>
        {
            var entries = Directory.GetFileSystemEntries(path);
            var list = new List<object?>();
            foreach (var entry in entries)
            {
                list.Add(Path.GetFileName(entry));
            }
            return (object?)new SharpTSArray(list);
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
                    ["isDirectory"] = true,
                    ["isFile"] = false,
                    ["size"] = 0.0
                });
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return new SharpTSObject(new Dictionary<string, object?>
                {
                    ["isDirectory"] = false,
                    ["isFile"] = true,
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
}
