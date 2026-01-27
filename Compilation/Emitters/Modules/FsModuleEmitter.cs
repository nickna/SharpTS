using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'fs' module (sync APIs only).
/// Methods wrap .NET file I/O with Node.js-compatible error handling.
/// </summary>
public sealed class FsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "fs";

    private static readonly string[] _exportedMembers =
    [
        "existsSync", "readFileSync", "writeFileSync", "appendFileSync",
        "unlinkSync", "mkdirSync", "rmdirSync", "readdirSync",
        "statSync", "lstatSync", "renameSync", "copyFileSync", "accessSync",
        "chmodSync", "chownSync", "lchownSync", "truncateSync",
        "symlinkSync", "readlinkSync", "realpathSync", "utimesSync",
        "constants"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "existsSync" => EmitExistsSync(emitter, arguments),
            "readFileSync" => EmitReadFileSync(emitter, arguments),
            "writeFileSync" => EmitWriteFileSync(emitter, arguments),
            "appendFileSync" => EmitAppendFileSync(emitter, arguments),
            "unlinkSync" => EmitUnlinkSync(emitter, arguments),
            "mkdirSync" => EmitMkdirSync(emitter, arguments),
            "rmdirSync" => EmitRmdirSync(emitter, arguments),
            "readdirSync" => EmitReaddirSync(emitter, arguments),
            "statSync" => EmitStatSync(emitter, arguments),
            "lstatSync" => EmitLstatSync(emitter, arguments),
            "renameSync" => EmitRenameSync(emitter, arguments),
            "copyFileSync" => EmitCopyFileSync(emitter, arguments),
            "accessSync" => EmitAccessSync(emitter, arguments),
            "chmodSync" => EmitChmodSync(emitter, arguments),
            "chownSync" => EmitChownSync(emitter, arguments),
            "lchownSync" => EmitLchownSync(emitter, arguments),
            "truncateSync" => EmitTruncateSync(emitter, arguments),
            "symlinkSync" => EmitSymlinkSync(emitter, arguments),
            "readlinkSync" => EmitReadlinkSync(emitter, arguments),
            "realpathSync" => EmitRealpathSync(emitter, arguments),
            "utimesSync" => EmitUtimesSync(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName == "constants")
        {
            var ctx = emitter.Context;
            var il = ctx.IL;
            il.Emit(OpCodes.Call, ctx.Runtime!.FsGetConstants);
            return true;
        }
        return false;
    }

    private static bool EmitExistsSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldc_I4_0); // false
            il.Emit(OpCodes.Box, ctx.Types.Boolean);
            return true;
        }

        // Emit path argument
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call runtime helper: FsExistsSync(object path) -> bool
        il.Emit(OpCodes.Call, ctx.Runtime!.FsExistsSync);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitReadFileSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // No path - throw error at runtime
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, ctx.Runtime!.FsReadFileSync);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit encoding (null if not provided)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper: FsReadFileSync(object path, object? encoding) -> object
        il.Emit(OpCodes.Call, ctx.Runtime!.FsReadFileSync);
        return true;
    }

    private static bool EmitWriteFileSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit data
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Call runtime helper: FsWriteFileSync(object path, object data)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsWriteFileSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitAppendFileSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit data
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Call runtime helper: FsAppendFileSync(object path, object data)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsAppendFileSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitUnlinkSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call runtime helper: FsUnlinkSync(object path)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsUnlinkSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitMkdirSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

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

        // Call runtime helper: FsMkdirSync(object path, object? options)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsMkdirSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitRmdirSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

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

        // Call runtime helper: FsRmdirSync(object path, object? options)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsRmdirSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitReaddirSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Return empty array
            il.Emit(OpCodes.Newobj, ctx.Types.GetConstructor(ctx.Types.ListOfObject));
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

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

        // Call runtime helper: FsReaddirSync(object path, object? options) -> List<object>
        il.Emit(OpCodes.Call, ctx.Runtime!.FsReaddirSync);
        return true;
    }

    private static bool EmitStatSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call runtime helper: FsStatSync(object path) -> object (Stats-like object)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsStatSync);
        return true;
    }

    private static bool EmitRenameSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit old path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit new path
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Call runtime helper: FsRenameSync(object oldPath, object newPath)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsRenameSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitCopyFileSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit source path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit destination path
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Call runtime helper: FsCopyFileSync(object src, object dest)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsCopyFileSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitAccessSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit mode (default to F_OK = 0)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, ctx.Types.Double);
        }

        // Call runtime helper: FsAccessSync(object path, object mode)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsAccessSync);
        il.Emit(OpCodes.Ldnull); // undefined return (throws if no access)
        return true;
    }

    private static bool EmitLstatSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call runtime helper: FsLstatSync(object path) -> object (Stats-like object)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsLstatSync);
        return true;
    }

    private static bool EmitChmodSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit mode
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Call runtime helper: FsChmodSync(object path, object mode)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsChmodSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitChownSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit uid
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Emit gid
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        // Call runtime helper: FsChownSync(object path, object uid, object gid)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsChownSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitLchownSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit uid
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Emit gid
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        // Call runtime helper: FsLchownSync(object path, object uid, object gid)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsLchownSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitTruncateSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit length (default to 0)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, ctx.Types.Double);
        }

        // Call runtime helper: FsTruncateSync(object path, object len)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsTruncateSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitSymlinkSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit target
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit path
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Emit type (null if not provided)
        if (arguments.Count >= 3)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper: FsSymlinkSync(object target, object path, object? type)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsSymlinkSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }

    private static bool EmitReadlinkSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call runtime helper: FsReadlinkSync(object path) -> string
        il.Emit(OpCodes.Call, ctx.Runtime!.FsReadlinkSync);
        return true;
    }

    private static bool EmitRealpathSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Call runtime helper: FsRealpathSync(object path) -> string
        il.Emit(OpCodes.Call, ctx.Runtime!.FsRealpathSync);
        return true;
    }

    private static bool EmitUtimesSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            il.Emit(OpCodes.Ldnull);
            return true;
        }

        // Emit path
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit atime
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Emit mtime
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);

        // Call runtime helper: FsUtimesSync(object path, object atime, object mtime)
        il.Emit(OpCodes.Call, ctx.Runtime!.FsUtimesSync);
        il.Emit(OpCodes.Ldnull); // undefined return
        return true;
    }
}
