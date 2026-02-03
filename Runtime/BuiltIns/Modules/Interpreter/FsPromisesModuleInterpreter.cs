using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'fs/promises' module.
/// Provides promise-based async file system operations.
/// </summary>
/// <remarks>
/// All methods return Promises that resolve/reject based on the operation result.
/// Uses BuiltInAsyncMethod which automatically wraps results in SharpTSPromise.
/// </remarks>
public static class FsPromisesModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the fs/promises module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["readFile"] = new BuiltInAsyncMethod("readFile", 1, 2, ReadFile),
            ["writeFile"] = new BuiltInAsyncMethod("writeFile", 2, 3, WriteFile),
            ["appendFile"] = new BuiltInAsyncMethod("appendFile", 2, 3, AppendFile),
            ["stat"] = new BuiltInAsyncMethod("stat", 1, 2, Stat),
            ["lstat"] = new BuiltInAsyncMethod("lstat", 1, 2, Lstat),
            ["unlink"] = new BuiltInAsyncMethod("unlink", 1, 1, Unlink),
            ["mkdir"] = new BuiltInAsyncMethod("mkdir", 1, 2, Mkdir),
            ["rmdir"] = new BuiltInAsyncMethod("rmdir", 1, 2, Rmdir),
            ["rm"] = new BuiltInAsyncMethod("rm", 1, 2, Rm),
            ["readdir"] = new BuiltInAsyncMethod("readdir", 1, 2, Readdir),
            ["rename"] = new BuiltInAsyncMethod("rename", 2, 2, Rename),
            ["copyFile"] = new BuiltInAsyncMethod("copyFile", 2, 3, CopyFile),
            ["access"] = new BuiltInAsyncMethod("access", 1, 2, Access),
            ["chmod"] = new BuiltInAsyncMethod("chmod", 2, 2, Chmod),
            ["truncate"] = new BuiltInAsyncMethod("truncate", 1, 2, Truncate),
            ["utimes"] = new BuiltInAsyncMethod("utimes", 3, 3, Utimes),
            ["readlink"] = new BuiltInAsyncMethod("readlink", 1, 2, Readlink),
            ["realpath"] = new BuiltInAsyncMethod("realpath", 1, 2, Realpath),
            ["symlink"] = new BuiltInAsyncMethod("symlink", 2, 3, Symlink),
            ["link"] = new BuiltInAsyncMethod("link", 2, 2, Link),
            ["mkdtemp"] = new BuiltInAsyncMethod("mkdtemp", 1, 2, Mkdtemp),
            ["constants"] = FsModuleInterpreter.CreateConstants()
        };
    }

    /// <summary>
    /// Creates a namespace object containing all fs.promises methods.
    /// Used by fs.promises property.
    /// </summary>
    public static SharpTSObject CreatePromisesNamespace()
    {
        return new SharpTSObject(GetExports());
    }

    private static async Task<object?> ReadFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var options = args.Count >= 2 ? args[1] : null;

        // Extract encoding from options
        object? encoding = null;
        if (options is string s)
        {
            encoding = s;
        }
        else if (options is SharpTSObject opts)
        {
            encoding = opts.GetProperty("encoding");
        }

        try
        {
            return await FsAsyncHelpers.ReadFileAsync(path, encoding);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "open", path);
        }
    }

    private static async Task<object?> WriteFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var data = args[1];
        var options = args.Count >= 3 ? args[2] : null;

        try
        {
            await FsAsyncHelpers.WriteFileAsync(path, data, options);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "open", path);
        }
    }

    private static async Task<object?> AppendFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var data = args[1];
        var options = args.Count >= 3 ? args[2] : null;

        try
        {
            await FsAsyncHelpers.AppendFileAsync(path, data, options);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "open", path);
        }
    }

    private static async Task<object?> Stat(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        try
        {
            return await FsAsyncHelpers.StatAsync(path);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "stat", path);
        }
    }

    private static async Task<object?> Lstat(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        try
        {
            return await FsAsyncHelpers.LstatAsync(path);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "lstat", path);
        }
    }

    private static async Task<object?> Unlink(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        try
        {
            await FsAsyncHelpers.UnlinkAsync(path);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "unlink", path);
        }
    }

    private static async Task<object?> Mkdir(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var options = args.Count >= 2 ? args[1] : null;

        try
        {
            await FsAsyncHelpers.MkdirAsync(path, options);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "mkdir", path);
        }
    }

    private static async Task<object?> Rmdir(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var options = args.Count >= 2 ? args[1] : null;

        try
        {
            await FsAsyncHelpers.RmdirAsync(path, options);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "rmdir", path);
        }
    }

    private static async Task<object?> Rm(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var options = args.Count >= 2 ? args[1] : null;

        try
        {
            await FsAsyncHelpers.RmAsync(path, options);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "rm", path);
        }
    }

    private static async Task<object?> Readdir(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var options = args.Count >= 2 ? args[1] : null;

        try
        {
            return await FsAsyncHelpers.ReaddirAsync(path, options);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "readdir", path);
        }
    }

    private static async Task<object?> Rename(Interp interpreter, object? receiver, List<object?> args)
    {
        var oldPath = args[0]?.ToString() ?? "";
        var newPath = args[1]?.ToString() ?? "";

        try
        {
            await FsAsyncHelpers.RenameAsync(oldPath, newPath);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "rename", oldPath);
        }
    }

    private static async Task<object?> CopyFile(Interp interpreter, object? receiver, List<object?> args)
    {
        var src = args[0]?.ToString() ?? "";
        var dest = args[1]?.ToString() ?? "";
        var mode = args.Count >= 3 ? args[2] : null;

        try
        {
            await FsAsyncHelpers.CopyFileAsync(src, dest, mode);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "copyfile", src);
        }
    }

    private static async Task<object?> Access(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var mode = args.Count >= 2 ? args[1] : null;

        try
        {
            await FsAsyncHelpers.AccessAsync(path, mode);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "access", path);
        }
    }

    private static async Task<object?> Chmod(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var mode = args[1];

        try
        {
            await FsAsyncHelpers.ChmodAsync(path, mode);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "chmod", path);
        }
    }

    private static async Task<object?> Truncate(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var len = args.Count >= 2 ? args[1] : null;

        try
        {
            await FsAsyncHelpers.TruncateAsync(path, len);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "truncate", path);
        }
    }

    private static async Task<object?> Utimes(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";
        var atime = args[1];
        var mtime = args[2];

        try
        {
            await FsAsyncHelpers.UtimesAsync(path, atime, mtime);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "utimes", path);
        }
    }

    private static async Task<object?> Readlink(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        try
        {
            return await FsAsyncHelpers.ReadlinkAsync(path);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "readlink", path);
        }
    }

    private static async Task<object?> Realpath(Interp interpreter, object? receiver, List<object?> args)
    {
        var path = args[0]?.ToString() ?? "";

        try
        {
            return await FsAsyncHelpers.RealpathAsync(path);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "realpath", path);
        }
    }

    private static async Task<object?> Symlink(Interp interpreter, object? receiver, List<object?> args)
    {
        var target = args[0]?.ToString() ?? "";
        var path = args[1]?.ToString() ?? "";
        var type = args.Count >= 3 ? args[2] : null;

        try
        {
            await FsAsyncHelpers.SymlinkAsync(target, path, type);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "symlink", path);
        }
    }

    private static async Task<object?> Link(Interp interpreter, object? receiver, List<object?> args)
    {
        var existingPath = args[0]?.ToString() ?? "";
        var newPath = args[1]?.ToString() ?? "";

        try
        {
            await FsAsyncHelpers.LinkAsync(existingPath, newPath);
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "link", newPath);
        }
    }

    private static async Task<object?> Mkdtemp(Interp interpreter, object? receiver, List<object?> args)
    {
        var prefix = args[0]?.ToString() ?? "";

        try
        {
            return await FsAsyncHelpers.MkdtempAsync(prefix);
        }
        catch (Exception ex)
        {
            throw CreateNodeError(ex, "mkdtemp", null);
        }
    }

    /// <summary>
    /// Creates a NodeError from an exception with proper error code.
    /// </summary>
    private static NodeError CreateNodeError(Exception ex, string syscall, string? path)
    {
        if (ex is NodeError ne)
        {
            return ne;
        }

        var code = NodeErrorCodes.FromException(ex);
        return new NodeError(code, ex.Message, syscall, path);
    }
}
