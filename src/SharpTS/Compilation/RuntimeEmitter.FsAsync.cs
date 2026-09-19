using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all async fs methods. Each builds its args and calls FsRunAsync, which
    /// runs the sync op on the thread pool (real backgrounding) with event-loop
    /// Ref/Unref so the loop stays alive until it drains (#971). No external
    /// FsAsyncHelpers dependency.
    /// </summary>
    private void EmitFsAsyncMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // #971: real-async infra (Ref + Task.Run + Unref). Must precede the ops.
        EmitFsRunAsyncInfra(typeBuilder, runtime.RequireFileSystemAsync(), runtime.EventLoop);

        // Emit all async fs methods with inline IL
        EmitFsReadFileAsync(typeBuilder, runtime);
        EmitFsWriteFileAsync(typeBuilder, runtime);
        EmitFsAppendFileAsync(typeBuilder, runtime);
        EmitFsStatAsync(typeBuilder, runtime);
        EmitFsLstatAsync(typeBuilder, runtime);
        EmitFsUnlinkAsync(typeBuilder, runtime);
        EmitFsMkdirAsync(typeBuilder, runtime);
        EmitFsRmdirAsync(typeBuilder, runtime);
        EmitFsRmAsync(typeBuilder, runtime);
        EmitFsReaddirAsync(typeBuilder, runtime);
        EmitFsRenameAsync(typeBuilder, runtime);
        EmitFsCopyFileAsync(typeBuilder, runtime);
        EmitFsAccessAsync(typeBuilder, runtime);
        EmitFsChmodAsync(typeBuilder, runtime);
        EmitFsTruncateAsync(typeBuilder, runtime);
        EmitFsUtimesAsync(typeBuilder, runtime);
        EmitFsReadlinkAsync(typeBuilder, runtime);
        EmitFsRealpathAsync(typeBuilder, runtime);
        EmitFsSymlinkAsync(typeBuilder, runtime);
        EmitFsLinkAsync(typeBuilder, runtime);
        EmitFsMkdtempAsync(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsReadFileAsync(object path, object? encoding) — runs
    /// FsReadFileSync on the thread pool via FsRunAsync (#971: real backgrounding +
    /// event-loop Ref/Unref so the loop stays alive until the op and its
    /// callback/await continuation drain).
    /// </summary>
    private void EmitFsReadFileAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadFileAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().ReadFile = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().ReadFileSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "readFile", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsWriteFileAsync(object path, object data, object? options)
    /// Dispatches FsWriteFileSync on the thread pool and returns its Task.
    /// </summary>
    private void EmitFsWriteFileAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsWriteFileAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().WriteFile = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().WriteFileSync, 3);

        runtime.BuiltInModules.Register("fs/promises", "writeFile", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsAppendFileAsync(object path, object data, object? options)
    /// Calls FsAppendFileSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsAppendFileAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsAppendFileAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().AppendFile = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().AppendFileSync, 3);

        runtime.BuiltInModules.Register("fs/promises", "appendFile", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsStatAsync(object path)
    /// Calls FsStatSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsStatAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsStatAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequireFileSystemAsync().Stat = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().StatRaw, 1);

        runtime.BuiltInModules.Register("fs/promises", "stat", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsLstatAsync(object path)
    /// Calls FsLstatSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsLstatAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsLstatAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequireFileSystemAsync().Lstat = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().LstatRaw, 1);

        runtime.BuiltInModules.Register("fs/promises", "lstat", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsUnlinkAsync(object path)
    /// Calls FsUnlinkSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsUnlinkAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsUnlinkAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequireFileSystemAsync().Unlink = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().UnlinkSync, 1);

        runtime.BuiltInModules.Register("fs/promises", "unlink", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsMkdirAsync(object path, object? options)
    /// Calls FsMkdirSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsMkdirAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsMkdirAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Mkdir = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().MkdirSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "mkdir", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsRmdirAsync(object path, object? options)
    /// Calls FsRmdirSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsRmdirAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRmdirAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Rmdir = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().RmdirSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "rmdir", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsRmAsync(object path, object? options)
    /// Implements rm with recursive/force options inline.
    /// </summary>
    private void EmitFsRmAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRmAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Rm = method;

        // rm has inline logic (recursive/force), so its sync body lives in
        // FsRmAsyncImpl and is run on the thread pool via FsRunAsync like the
        // other ops (#971).
        var impl = typeBuilder.DefineMethod("FsRmAsyncImpl",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object, _types.Object]);
        {
            var il = impl.GetILGenerator();

            // Convert path to string
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.StringCoercion.Stringify);
            var pathLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, pathLocal);

            var recursiveLocal = il.DeclareLocal(_types.Boolean);
            var forceLocal = il.DeclareLocal(_types.Boolean);
            var afterOptionsLabel = il.DefineLabel();
            var afterRecursiveLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, afterOptionsLabel);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "recursive");
            il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
            il.Emit(OpCodes.Stloc, recursiveLocal);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "force");
            il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
            il.Emit(OpCodes.Stloc, forceLocal);
            il.Emit(OpCodes.Br, afterRecursiveLabel);

            il.MarkLabel(afterOptionsLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, recursiveLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, forceLocal);

            il.MarkLabel(afterRecursiveLabel);

            var existsLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Path doesn't exist - if force, return; otherwise throw
            var throwLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, forceLocal);
            il.Emit(OpCodes.Brfalse, throwLabel);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(throwLabel);
            il.Emit(OpCodes.Ldstr, "ENOENT: no such file or directory");
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            var isFileLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, isFileLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, recursiveLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Delete", _types.String, _types.Boolean));
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(isFileLabel);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Delete", _types.String));

            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), method.GetILGenerator(), impl, 2);

        runtime.BuiltInModules.Register("fs/promises", "rm", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsReaddirAsync(object path, object? options)
    /// Calls FsReaddirSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsReaddirAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReaddirAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Readdir = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().ReaddirSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "readdir", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsRenameAsync(object oldPath, object newPath)
    /// Calls FsRenameSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsRenameAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRenameAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Rename = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().RenameSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "rename", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsCopyFileAsync(object src, object dest, object? mode)
    /// Calls FsCopyFileSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsCopyFileAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsCopyFileAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().CopyFile = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().CopyFileSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "copyFile", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsAccessAsync(object path, object? mode)
    /// Calls FsAccessSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsAccessAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsAccessAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Access = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().AccessSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "access", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsChmodAsync(object path, object mode)
    /// Calls FsChmodSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsChmodAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsChmodAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Chmod = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().ChmodSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "chmod", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsTruncateAsync(object path, object? len)
    /// Calls FsTruncateSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsTruncateAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsTruncateAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Truncate = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().TruncateSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "truncate", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsUtimesAsync(object path, object atime, object mtime)
    /// Calls FsUtimesSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsUtimesAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsUtimesAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Utimes = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().UtimesSync, 3);

        runtime.BuiltInModules.Register("fs/promises", "utimes", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsReadlinkAsync(object path)
    /// Calls FsReadlinkSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsReadlinkAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadlinkAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequireFileSystemAsync().Readlink = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().ReadlinkSync, 1);

        runtime.BuiltInModules.Register("fs/promises", "readlink", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsRealpathAsync(object path)
    /// Calls FsRealpathSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsRealpathAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRealpathAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequireFileSystemAsync().Realpath = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().RealpathSync, 1);

        runtime.BuiltInModules.Register("fs/promises", "realpath", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsSymlinkAsync(object target, object path, object? type)
    /// Calls FsSymlinkSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsSymlinkAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsSymlinkAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Symlink = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().SymlinkSync, 3);

        runtime.BuiltInModules.Register("fs/promises", "symlink", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsLinkAsync(object existingPath, object newPath)
    /// Calls FsLinkSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsLinkAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsLinkAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.RequireFileSystemAsync().Link = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().LinkSync, 2);

        runtime.BuiltInModules.Register("fs/promises", "link", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsMkdtempAsync(object prefix)
    /// Calls FsMkdtempSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsMkdtempAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsMkdtempAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequireFileSystemAsync().Mkdtemp = method;

        var il = method.GetILGenerator();
        EmitFsAsyncDispatch(runtime.RequireFileSystemAsync(), il, runtime.RequireFileSystem().MkdtempSync, 1);

        runtime.BuiltInModules.Register("fs/promises", "mkdtemp", method);
    }

    /// <summary>
    /// Emits: public static object FsGetPromisesNamespace()
    /// Returns a namespace object containing all fs.promises methods.
    /// Creates TSFunctions that wrap the async helper methods and return Promises.
    /// </summary>
    private void EmitFsGetPromisesNamespace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First, emit wrapper methods that call async helpers and wrap results in Promises
        EmitFsPromisesWrapperMethods(typeBuilder, runtime.RequireFileSystemAsync(), runtime.RequirePromise());

        var method = typeBuilder.DefineMethod(
            "FsGetPromisesNamespace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.RequireFileSystemAsync().GetPromisesNamespace = method;

        var il = method.GetILGenerator();

        // Create a new Dictionary<string, object?>
        var dictCtor = _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!;
        var addMethod = _types.GetMethod(_types.DictionaryStringObject, "Add", [typeof(string), typeof(object)])!;

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add each wrapper method as a TSFunction
        var fsPromisesWrappers = runtime.RequireFileSystemAsync().PromisesWrapperMethods;
        foreach (var (name, wrapper) in fsPromisesWrappers)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, name);

            // Create TSFunction: new $TSFunction(null, wrapperMethod)
            il.Emit(OpCodes.Ldnull); // target
            il.Emit(OpCodes.Ldtoken, wrapper);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(MethodBase), "GetMethodFromHandle", typeof(RuntimeMethodHandle)));
            il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            il.Emit(OpCodes.Newobj, runtime.FunctionConstruction.Constructor);

            il.Emit(OpCodes.Call, addMethod);
        }

        // Add constants
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "constants");
        il.Emit(OpCodes.Call, runtime.RequireFileSystem().GetConstants);
        il.Emit(OpCodes.Call, addMethod);

        // Create a SharpTSObject from the dictionary
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.ObjectStorage.Constructor);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits wrapper methods for fs.promises that call the async helpers and wrap results in Promises.
    /// Each wrapper takes individual object parameters for TSFunction compatibility.
    /// </summary>
    private void EmitFsPromisesWrapperMethods(TypeBuilder typeBuilder, EmittedFileSystemAsyncRuntime fsAsync, EmittedPromiseRuntime promise)
    {
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "readFile", fsAsync.ReadFile, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "writeFile", fsAsync.WriteFile, 3);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "appendFile", fsAsync.AppendFile, 3);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "stat", fsAsync.Stat, 1);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "lstat", fsAsync.Lstat, 1);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "unlink", fsAsync.Unlink, 1);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "mkdir", fsAsync.Mkdir, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "rmdir", fsAsync.Rmdir, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "rm", fsAsync.Rm, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "readdir", fsAsync.Readdir, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "rename", fsAsync.Rename, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "copyFile", fsAsync.CopyFile, 3);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "access", fsAsync.Access, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "chmod", fsAsync.Chmod, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "truncate", fsAsync.Truncate, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "utimes", fsAsync.Utimes, 3);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "readlink", fsAsync.Readlink, 1);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "realpath", fsAsync.Realpath, 1);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "symlink", fsAsync.Symlink, 3);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "link", fsAsync.Link, 2);
        EmitPromisesWrapper(typeBuilder, fsAsync, promise, "mkdtemp", fsAsync.Mkdtemp, 1);
    }

    /// <summary>
    /// Emits a single wrapper method for fs.promises.
    /// Signature: object MethodName(object arg0, object arg1, ...)
    /// Takes individual object parameters to work with TSFunction.Invoke reflection call.
    /// Calls the async helper, wraps the Task in a Promise, and returns it.
    /// </summary>
    private void EmitPromisesWrapper(
        TypeBuilder typeBuilder,
        EmittedFileSystemAsyncRuntime fsAsync, EmittedPromiseRuntime promise,
        string name,
        MethodBuilder asyncMethod,
        int argCount)
    {
        // Create parameter types - all object, matching the expected arg count
        var paramTypes = new Type[argCount];
        for (int i = 0; i < argCount; i++)
            paramTypes[i] = _types.Object;

        var wrapper = typeBuilder.DefineMethod(
            $"FsPromises_{name}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = wrapper.GetILGenerator();

        // Load each argument
        for (int i = 0; i < argCount; i++)
        {
            il.Emit(OpCodes.Ldarg, i);
        }

        // Call the async method
        il.Emit(OpCodes.Call, asyncMethod);

        // Wrap the Task in a Promise
        il.Emit(OpCodes.Call, promise.WrapTaskAsPromise);

        il.Emit(OpCodes.Ret);

        fsAsync.RegisterPromiseWrapper(name, wrapper);
    }
}
