using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'fs/promises' module.
/// Promise-based async file system operations.
/// </summary>
public sealed class FsPromisesModuleEmitter : IBuiltInModuleEmitter
{
    // Registered under the primitive:fs/promises key — the user-facing 'fs/promises'
    // specifier is served by stdlib/node/fs/promises.ts, which imports from this primitive.
    public string ModuleName => "primitive:fs/promises";

    private static readonly IReadOnlyList<string> _exportedMembers = (ImmutableArray<string>)
    [
        "readFile", "writeFile", "appendFile",
        "stat", "lstat", "unlink", "mkdir", "rmdir", "rm",
        "readdir", "rename", "copyFile", "access",
        "chmod", "truncate", "utimes",
        "readlink", "realpath", "symlink", "link", "mkdtemp",
        "constants"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "readFile" => EmitReadFile(emitter, arguments),
            "writeFile" => EmitWriteFile(emitter, arguments),
            "appendFile" => EmitAppendFile(emitter, arguments),
            "stat" => EmitStat(emitter, arguments),
            "lstat" => EmitLstat(emitter, arguments),
            "unlink" => EmitUnlink(emitter, arguments),
            "mkdir" => EmitMkdir(emitter, arguments),
            "rmdir" => EmitRmdir(emitter, arguments),
            "rm" => EmitRm(emitter, arguments),
            "readdir" => EmitReaddir(emitter, arguments),
            "rename" => EmitRename(emitter, arguments),
            "copyFile" => EmitCopyFile(emitter, arguments),
            "access" => EmitAccess(emitter, arguments),
            "chmod" => EmitChmod(emitter, arguments),
            "truncate" => EmitTruncate(emitter, arguments),
            "utimes" => EmitUtimes(emitter, arguments),
            "readlink" => EmitReadlink(emitter, arguments),
            "realpath" => EmitRealpath(emitter, arguments),
            "symlink" => EmitSymlink(emitter, arguments),
            "link" => EmitLink(emitter, arguments),
            "mkdtemp" => EmitMkdtemp(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName == "constants")
        {
            var ctx = emitter.Context;
            var il = ctx.IL;
            il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystem().GetConstants);
            return true;
        }
        return false;
    }

    private static bool EmitReadFile(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit path
        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        // Emit options (null if not provided)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper: FsReadFileAsync(object path, object? options) -> Task<object>
        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().ReadFile);

        // Wrap Task in Promise
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitWriteFile(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            // Return rejected promise for invalid args
            il.Emit(OpCodes.Ldstr, "path and data are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit data
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Emit options (null if not provided)
        if (arguments.Count >= 3)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().WriteFile);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitAppendFile(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "path and data are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        if (arguments.Count >= 3)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().AppendFile);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitStat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Stat);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitLstat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Lstat);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitUnlink(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Unlink);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitMkdir(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Mkdir);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitRmdir(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Rmdir);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitRm(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Rm);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitReaddir(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Readdir);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitRename(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "oldPath and newPath are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Rename);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitCopyFile(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "src and dest are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        if (arguments.Count >= 3)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().CopyFile);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitAccess(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Access);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitChmod(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "path and mode are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Chmod);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitTruncate(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Truncate);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitUtimes(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            il.Emit(OpCodes.Ldstr, "path, atime, and mtime are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Utimes);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitReadlink(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Readlink);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitRealpath(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "path is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Realpath);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitSymlink(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "target and path are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        if (arguments.Count >= 3)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Symlink);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitLink(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "existingPath and newPath are required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Link);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }

    private static bool EmitMkdtemp(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "prefix is required");
            il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().TypeReject);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireFileSystemAsync().Mkdtemp);
        il.Emit(OpCodes.Call, ctx.Runtime!.RequirePromise().WrapTaskAsPromise);

        return true;
    }
}
