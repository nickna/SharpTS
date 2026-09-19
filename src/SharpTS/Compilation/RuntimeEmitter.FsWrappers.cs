using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits wrapper methods for fs module functions to support named imports.
    /// Each wrapper takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitFsModuleMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // existsSync(path) -> bool
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "existsSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().ExistsSync);
                il.Emit(OpCodes.Box, _types.Boolean);
            });

        // readFileSync(path, encoding?) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "readFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().ReadFileSync);
            });

        // writeFileSync(path, data) -> undefined (value-import fallback; encoding via
        // the direct-dispatch path, so this fixed-arity wrapper passes null).
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "writeFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().WriteFileSync);
                il.Emit(OpCodes.Ldnull);
            });

        // appendFileSync(path, data) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "appendFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().AppendFileSync);
                il.Emit(OpCodes.Ldnull);
            });

        // unlinkSync(path) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "unlinkSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().UnlinkSync);
                il.Emit(OpCodes.Ldnull);
            });

        // mkdirSync(path, options?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "mkdirSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().MkdirSync);
                il.Emit(OpCodes.Ldnull);
            });

        // rmdirSync(path, options?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "rmdirSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().RmdirSync);
                il.Emit(OpCodes.Ldnull);
            });

        // readdirSync(path, options?) -> List<object>
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "readdirSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().ReaddirSync);
            });

        // statSync(path) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "statSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().StatSync);
            });

        // lstatSync(path) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "lstatSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().LstatSync);
            });

        // renameSync(oldPath, newPath) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "renameSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().RenameSync);
                il.Emit(OpCodes.Ldnull);
            });

        // copyFileSync(src, dest) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "copyFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().CopyFileSync);
                il.Emit(OpCodes.Ldnull);
            });

        // accessSync(path, mode?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "accessSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().AccessSync);
                il.Emit(OpCodes.Ldnull);
            });

        // chmodSync(path, mode) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "chmodSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().ChmodSync);
                il.Emit(OpCodes.Ldnull);
            });

        // chownSync(path, uid, gid) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "chownSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().ChownSync);
                il.Emit(OpCodes.Ldnull);
            });

        // lchownSync(path, uid, gid) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "lchownSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().LchownSync);
                il.Emit(OpCodes.Ldnull);
            });

        // truncateSync(path, len?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "truncateSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().TruncateSync);
                il.Emit(OpCodes.Ldnull);
            });

        // symlinkSync(target, path, type?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "symlinkSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().SymlinkSync);
                il.Emit(OpCodes.Ldnull);
            });

        // readlinkSync(path) -> string
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "readlinkSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().ReadlinkSync);
            });

        // realpathSync(path) -> string
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "realpathSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().RealpathSync);
            });

        // utimesSync(path, atime, mtime) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "utimesSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.RequireFileSystem().UtimesSync);
                il.Emit(OpCodes.Ldnull);
            });

        // createReadStream(path, options?) -> $FsReadStream
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "createReadStream", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystemStreams().CreateReadStream);
            });

        // createWriteStream(path, options?) -> $FsWriteStream
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "createWriteStream", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.RequireFileSystemStreams().CreateWriteStream);
            });
    }

    /// <summary>
    /// Emits a wrapper method for a single fs module function.
    /// Takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitFsMethodWrapperSimple(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator> emitCall)
    {
        // Create parameter types - all object
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            $"Fs_{methodName}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();

        // Emit the actual method call
        emitCall(il);

        il.Emit(OpCodes.Ret);

        // Register the wrapper for named imports
        runtime.BuiltInModules.Register("fs", methodName, method);
    }
}
