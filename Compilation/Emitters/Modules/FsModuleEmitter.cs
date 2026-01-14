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
        "statSync", "lstatSync", "renameSync", "copyFileSync", "accessSync"
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
            "lstatSync" => EmitStatSync(emitter, arguments), // Same as statSync for now
            "renameSync" => EmitRenameSync(emitter, arguments),
            "copyFileSync" => EmitCopyFileSync(emitter, arguments),
            "accessSync" => EmitAccessSync(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // fs module has no properties
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

        // Call runtime helper: FsReaddirSync(object path) -> List<object>
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
}
