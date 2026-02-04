using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SharpTS.Runtime.BuiltIns.Modules;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits fs module helper methods.
    /// </summary>
    private void EmitFsModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitFsExistsSync(typeBuilder, runtime);
        EmitFsReadFileSync(typeBuilder, runtime);
        EmitFsWriteFileSync(typeBuilder, runtime);
        EmitFsAppendFileSync(typeBuilder, runtime);
        EmitFsUnlinkSync(typeBuilder, runtime);
        EmitFsMkdirSync(typeBuilder, runtime);
        EmitFsRmdirSync(typeBuilder, runtime);
        EmitFsCreateDirent(typeBuilder, runtime); // Must be before ReaddirSync which uses it
        EmitFsReaddirSync(typeBuilder, runtime);
        EmitFsStatSync(typeBuilder, runtime);
        EmitFsLstatSync(typeBuilder, runtime);
        EmitFsRenameSync(typeBuilder, runtime);
        EmitFsCopyFileSync(typeBuilder, runtime);
        EmitFsAccessSync(typeBuilder, runtime);
        EmitFsChmodSync(typeBuilder, runtime);
        EmitFsChownSync(typeBuilder, runtime);
        EmitFsLchownSync(typeBuilder, runtime);
        EmitFsTruncateSync(typeBuilder, runtime);
        EmitFsSymlinkSync(typeBuilder, runtime);
        EmitFsReadlinkSync(typeBuilder, runtime);
        EmitFsRealpathSync(typeBuilder, runtime);
        EmitFsUtimesSync(typeBuilder, runtime);
        EmitFsGetConstants(typeBuilder, runtime);

        // File descriptor APIs
        EmitFsOpenSync(typeBuilder, runtime);
        EmitFsCloseSync(typeBuilder, runtime);
        EmitFsReadSync(typeBuilder, runtime);
        EmitFsWriteSync(typeBuilder, runtime);
        EmitFsFstatSync(typeBuilder, runtime);
        EmitFsFtruncateSync(typeBuilder, runtime);

        // Directory utilities
        EmitFsMkdtempSync(typeBuilder, runtime);
        EmitFsOpendirSync(typeBuilder, runtime);

        // Hard links
        EmitFsLinkSync(typeBuilder, runtime);

        // Async fs methods (fs.promises and fs/promises)
        EmitFsAsyncMethods(typeBuilder, runtime);
        EmitFsGetPromisesNamespace(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits a try-catch block that converts exceptions to Node.js-style errors.
    /// The emitTryBody action receives the afterTry label for the Leave instruction.
    /// </summary>
    private void EmitWithFsErrorHandling(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder pathLocal,
        string syscall,
        Action<Label> emitTryBody)
    {
        var caughtExLocal = il.DeclareLocal(_types.Exception);
        var afterTry = il.DefineLabel();

        il.BeginExceptionBlock();
        emitTryBody(afterTry);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, caughtExLocal);
        il.Emit(OpCodes.Ldloc, caughtExLocal);
        il.Emit(OpCodes.Ldstr, syscall);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, runtime.ThrowNodeError);
        il.Emit(OpCodes.Rethrow);

        il.EndExceptionBlock();
        il.MarkLabel(afterTry);
    }

    /// <summary>
    /// Emits: public static bool FsExistsSync(object path)
    /// Returns true if file or directory exists.
    /// </summary>
    private void EmitFsExistsSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsExistsSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.FsExistsSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Check File.Exists || Directory.Exists
        var fileExists = _types.GetMethod(_types.File, "Exists", _types.String);
        var dirExists = _types.GetMethod(_types.Directory, "Exists", _types.String);

        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, fileExists);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, dirExists);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsReadFileSync(object path, object? encoding)
    /// Returns string if encoding specified, $Buffer otherwise.
    /// </summary>
    private void EmitFsReadFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.FsReadFileSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // Check if encoding is provided (not null)
            var readBytesLabel = il.DefineLabel();
            var afterReadLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, readBytesLabel);

            // Read as text: File.ReadAllText(path)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "ReadAllText", _types.String));
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, afterReadLabel);

            // Read as bytes: File.ReadAllBytes(path) - wrap in $Buffer
            il.MarkLabel(readBytesLabel);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "ReadAllBytes", _types.String));

            // Create $Buffer from byte[]
            il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
            il.Emit(OpCodes.Stloc, resultLocal);

            il.MarkLabel(afterReadLabel);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsWriteFileSync(object path, object data)
    /// </summary>
    private void EmitFsWriteFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsWriteFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsWriteFileSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert data to string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var dataLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, dataLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // File.WriteAllText(path, data)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "WriteAllText", _types.String, _types.String));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsAppendFileSync(object path, object data)
    /// </summary>
    private void EmitFsAppendFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsAppendFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsAppendFileSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert data to string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var dataLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, dataLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // File.AppendAllText(path, data)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "AppendAllText", _types.String, _types.String));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsUnlinkSync(object path)
    /// </summary>
    private void EmitFsUnlinkSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsUnlinkSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.FsUnlinkSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "unlink", afterTry =>
        {
            // Check if file exists first (File.Delete is a no-op if file doesn't exist)
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // File doesn't exist - throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            // File exists - delete it
            il.MarkLabel(existsLabel);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Delete", _types.String));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsMkdirSync(object path, object? options)
    /// </summary>
    private void EmitFsMkdirSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsMkdirSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsMkdirSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "mkdir", afterTry =>
        {
            // Directory.CreateDirectory(path)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "CreateDirectory", _types.String));
            il.Emit(OpCodes.Pop); // CreateDirectory returns DirectoryInfo
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsRmdirSync(object path, object? options)
    /// </summary>
    private void EmitFsRmdirSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRmdirSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsRmdirSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "rmdir", afterTry =>
        {
            // Check if options.recursive is set
            var nonRecursive = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, nonRecursive);

            // Get "recursive" property if options is provided
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "recursive");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brfalse, nonRecursive);

            // Directory.Delete(path, true)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Delete", _types.String, _types.Boolean));
            il.Emit(OpCodes.Leave, afterTry);

            // Directory.Delete(path, false)
            il.MarkLabel(nonRecursive);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Delete", _types.String, _types.Boolean));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static List&lt;object&gt; FsReaddirSync(object path)
    /// </summary>
    private void EmitFsReaddirSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReaddirSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.Object]
        );
        runtime.FsReaddirSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Check if withFileTypes option is set
        var withFileTypesLocal = il.DeclareLocal(_types.Boolean);
        var recursiveLocal = il.DeclareLocal(_types.Boolean);
        var afterOptionsLabel = il.DefineLabel();
        var checkRecursiveLabel = il.DefineLabel();
        var afterRecursiveLabel = il.DefineLabel();

        // If options is null, withFileTypes = false, recursive = false
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, afterOptionsLabel);

        // Check if options has withFileTypes property
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "withFileTypes");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        var wftValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, wftValueLocal);

        // Check if value is truthy
        il.Emit(OpCodes.Ldloc, wftValueLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, withFileTypesLocal);

        // Check if options has recursive property
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "recursive");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        var recValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, recValueLocal);

        // Check if value is truthy
        il.Emit(OpCodes.Ldloc, recValueLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, recursiveLocal);
        il.Emit(OpCodes.Br, afterRecursiveLabel);

        il.MarkLabel(afterOptionsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, withFileTypesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, recursiveLocal);

        il.MarkLabel(afterRecursiveLabel);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "readdir", afterTry =>
        {
            // Get entries based on recursive option
            // Directory.GetFileSystemEntries(path, "*", SearchOption)
            var searchOptionType = typeof(SearchOption);
            var getEntriesMethod = _types.Directory.GetMethod("GetFileSystemEntries", [_types.String, _types.String, searchOptionType])!;
            var entriesLocal = il.DeclareLocal(_types.StringArray);

            var nonRecursiveLabel = il.DefineLabel();
            var afterGetEntriesLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, recursiveLocal);
            il.Emit(OpCodes.Brfalse, nonRecursiveLabel);

            // Recursive: use SearchOption.AllDirectories
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldstr, "*");
            il.Emit(OpCodes.Ldc_I4_1); // SearchOption.AllDirectories = 1
            il.Emit(OpCodes.Call, getEntriesMethod);
            il.Emit(OpCodes.Stloc, entriesLocal);
            il.Emit(OpCodes.Br, afterGetEntriesLabel);

            // Non-recursive: use SearchOption.TopDirectoryOnly
            il.MarkLabel(nonRecursiveLabel);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldstr, "*");
            il.Emit(OpCodes.Ldc_I4_0); // SearchOption.TopDirectoryOnly = 0
            il.Emit(OpCodes.Call, getEntriesMethod);
            il.Emit(OpCodes.Stloc, entriesLocal);

            il.MarkLabel(afterGetEntriesLabel);

            // Create result list
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject));
            var listLocal = il.DeclareLocal(_types.ListOfObject);
            il.Emit(OpCodes.Stloc, listLocal);

            // Loop through entries
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();
            var indexLocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, indexLocal);

            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldloc, entriesLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bge, loopEnd);

            // Get current entry path
            il.Emit(OpCodes.Ldloc, entriesLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldelem_Ref);
            var entryPathLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, entryPathLocal);

            // Check if withFileTypes
            var addFilenameLabel = il.DefineLabel();
            var afterAddLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, withFileTypesLocal);
            il.Emit(OpCodes.Brfalse, addFilenameLabel);

            // withFileTypes = true: add Dirent object
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldloc, entryPathLocal);
            il.Emit(OpCodes.Call, runtime.FsCreateDirent);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, afterAddLabel);

            // withFileTypes = false: add filename or relative path
            il.MarkLabel(addFilenameLabel);
            il.Emit(OpCodes.Ldloc, listLocal);

            // Check if recursive - if so, use relative path; otherwise just filename
            var useFilenameLabel = il.DefineLabel();
            var afterPathLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, recursiveLocal);
            il.Emit(OpCodes.Brfalse, useFilenameLabel);

            // Recursive: use Path.GetRelativePath(basePath, entryPath)
            var getRelativePathMethod = _types.Path.GetMethod("GetRelativePath", [_types.String, _types.String])!;
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, entryPathLocal);
            il.Emit(OpCodes.Call, getRelativePathMethod);
            il.Emit(OpCodes.Br, afterPathLabel);

            // Non-recursive: use Path.GetFileName
            il.MarkLabel(useFilenameLabel);
            il.Emit(OpCodes.Ldloc, entryPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));

            il.MarkLabel(afterPathLabel);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            il.MarkLabel(afterAddLabel);

            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, indexLocal);
            il.Emit(OpCodes.Br, loopStart);

            il.MarkLabel(loopEnd);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsStatSync(object path)
    /// Returns a Stats-like object with file/directory info.
    /// </summary>
    private void EmitFsStatSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsStatSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsStatSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "stat", afterTry =>
        {
            // Locals for $Stats constructor arguments
            var isFileLocal = il.DeclareLocal(_types.Boolean);
            var isDirLocal = il.DeclareLocal(_types.Boolean);
            var isSymlinkLocal = il.DeclareLocal(_types.Boolean);
            var sizeLocal = il.DeclareLocal(_types.Double);
            var fileInfoLocal = il.DeclareLocal(_types.FileInfo);

            // Define all labels within try block scope
            var isDirLabel = il.DefineLabel();
            var isFileLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            // Check if directory exists first
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, isDirLabel);

            // Check if file exists
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, isFileLabel);

            // Neither exists - throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            // It's a directory
            il.MarkLabel(isDirLabel);
            il.Emit(OpCodes.Ldc_I4_0); // isFile = false
            il.Emit(OpCodes.Stloc, isFileLocal);
            il.Emit(OpCodes.Ldc_I4_1); // isDirectory = true
            il.Emit(OpCodes.Stloc, isDirLocal);
            il.Emit(OpCodes.Ldc_I4_0); // isSymbolicLink = false (for now)
            il.Emit(OpCodes.Stloc, isSymlinkLocal);
            il.Emit(OpCodes.Ldc_R8, 0.0); // size = 0 for directories
            il.Emit(OpCodes.Stloc, sizeLocal);
            il.Emit(OpCodes.Br, doneLabel);

            // It's a file
            il.MarkLabel(isFileLabel);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
            il.Emit(OpCodes.Stloc, fileInfoLocal);

            il.Emit(OpCodes.Ldc_I4_1); // isFile = true
            il.Emit(OpCodes.Stloc, isFileLocal);
            il.Emit(OpCodes.Ldc_I4_0); // isDirectory = false
            il.Emit(OpCodes.Stloc, isDirLocal);
            il.Emit(OpCodes.Ldc_I4_0); // isSymbolicLink = false (for now)
            il.Emit(OpCodes.Stloc, isSymlinkLocal);
            il.Emit(OpCodes.Ldloc, fileInfoLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Length").GetMethod!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, sizeLocal);

            il.MarkLabel(doneLabel);

            // Create $Stats instance:
            // new $Stats(isFile, isDirectory, isSymbolicLink, size, mode, atimeMs, mtimeMs, ctimeMs, birthtimeMs)
            il.Emit(OpCodes.Ldloc, isFileLocal);      // arg1: isFile
            il.Emit(OpCodes.Ldloc, isDirLocal);       // arg2: isDirectory
            il.Emit(OpCodes.Ldloc, isSymlinkLocal);   // arg3: isSymbolicLink
            il.Emit(OpCodes.Ldloc, sizeLocal);        // arg4: size
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg5: mode (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg6: atimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg7: mtimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg8: ctimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg9: birthtimeMs (placeholder)
            il.Emit(OpCodes.Newobj, runtime.StatsCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsRenameSync(object oldPath, object newPath)
    /// </summary>
    private void EmitFsRenameSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRenameSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsRenameSync = method;

        var il = method.GetILGenerator();

        // Convert paths to strings
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var oldPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, oldPathLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var newPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, newPathLocal);

        EmitWithFsErrorHandling(il, runtime, oldPathLocal, "rename", afterTry =>
        {
            // Check if it's a directory
            var isFileLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, oldPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, isFileLabel);

            // Directory.Move
            il.Emit(OpCodes.Ldloc, oldPathLocal);
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Move", _types.String, _types.String));
            il.Emit(OpCodes.Leave, afterTry);

            // File.Move
            il.MarkLabel(isFileLabel);
            il.Emit(OpCodes.Ldloc, oldPathLocal);
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Move", _types.String, _types.String));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsCopyFileSync(object src, object dest)
    /// </summary>
    private void EmitFsCopyFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsCopyFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsCopyFileSync = method;

        var il = method.GetILGenerator();

        // Convert paths to strings
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var srcLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, srcLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var destLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, destLocal);

        EmitWithFsErrorHandling(il, runtime, srcLocal, "copyfile", afterTry =>
        {
            // File.Copy(src, dest, overwrite: true)
            il.Emit(OpCodes.Ldloc, srcLocal);
            il.Emit(OpCodes.Ldloc, destLocal);
            il.Emit(OpCodes.Ldc_I4_1); // overwrite = true
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Copy", _types.String, _types.String, _types.Boolean));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsAccessSync(object path, object mode)
    /// Throws if file doesn't exist or is not accessible.
    /// </summary>
    private void EmitFsAccessSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsAccessSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsAccessSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "access", afterTry =>
        {
            // Check if exists (File.Exists || Directory.Exists)
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsLstatSync(object path)
    /// Returns a Stats-like object with file/directory info (doesn't follow symlinks).
    /// </summary>
    private void EmitFsLstatSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsLstatSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsLstatSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "lstat", afterTry =>
        {
            // Locals for $Stats constructor arguments
            var isFileLocal = il.DeclareLocal(_types.Boolean);
            var isDirLocal = il.DeclareLocal(_types.Boolean);
            var isSymlinkLocal = il.DeclareLocal(_types.Boolean);
            var sizeLocal = il.DeclareLocal(_types.Double);

            // Create FileInfo to check attributes
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
            var fileInfoLocal = il.DeclareLocal(_types.FileInfo);
            il.Emit(OpCodes.Stloc, fileInfoLocal);

            // Check if exists (file or directory)
            var existsLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, fileInfoLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Exists").GetMethod!);
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Neither exists - throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Check if it's a directory
            var isFileLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, isFileLabel);

            // It's a directory
            il.Emit(OpCodes.Ldc_I4_0); // isFile = false
            il.Emit(OpCodes.Stloc, isFileLocal);
            il.Emit(OpCodes.Ldc_I4_1); // isDirectory = true
            il.Emit(OpCodes.Stloc, isDirLocal);

            // Check isSymbolicLink via ReparsePoint attribute on DirectoryInfo
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DirectoryInfo, _types.String));
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DirectoryInfo, "Attributes").GetMethod!);
            il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Stloc, isSymlinkLocal);

            il.Emit(OpCodes.Ldc_R8, 0.0); // size = 0 for directories
            il.Emit(OpCodes.Stloc, sizeLocal);
            il.Emit(OpCodes.Br, doneLabel);

            // It's a file
            il.MarkLabel(isFileLabel);
            il.Emit(OpCodes.Ldc_I4_1); // isFile = true
            il.Emit(OpCodes.Stloc, isFileLocal);
            il.Emit(OpCodes.Ldc_I4_0); // isDirectory = false
            il.Emit(OpCodes.Stloc, isDirLocal);

            // Check isSymbolicLink via ReparsePoint attribute on FileInfo
            il.Emit(OpCodes.Ldloc, fileInfoLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Attributes").GetMethod!);
            il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Stloc, isSymlinkLocal);

            // size = fileInfo.Length
            il.Emit(OpCodes.Ldloc, fileInfoLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Length").GetMethod!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, sizeLocal);

            il.MarkLabel(doneLabel);

            // Create $Stats instance:
            // new $Stats(isFile, isDirectory, isSymbolicLink, size, mode, atimeMs, mtimeMs, ctimeMs, birthtimeMs)
            il.Emit(OpCodes.Ldloc, isFileLocal);      // arg1: isFile
            il.Emit(OpCodes.Ldloc, isDirLocal);       // arg2: isDirectory
            il.Emit(OpCodes.Ldloc, isSymlinkLocal);   // arg3: isSymbolicLink
            il.Emit(OpCodes.Ldloc, sizeLocal);        // arg4: size
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg5: mode (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg6: atimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg7: mtimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg8: ctimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);             // arg9: birthtimeMs (placeholder)
            il.Emit(OpCodes.Newobj, runtime.StatsCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsChmodSync(object path, object mode)
    /// Changes file permissions (Unix only, no-op on Windows).
    /// </summary>
    private void EmitFsChmodSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsChmodSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsChmodSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert mode to int32
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var modeLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, modeLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "chmod", afterTry =>
        {
            // Check if file/directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Check if on Unix (Linux or OSX)
            var windowsLabel = il.DefineLabel();
            var runtimeInfoType = typeof(RuntimeInformation);
            var osPlatformType = typeof(OSPlatform);

            // RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            var isOsPlatformMethod = runtimeInfoType.GetMethod("IsOSPlatform", [osPlatformType])!;
            var linuxField = osPlatformType.GetProperty("Linux")!.GetMethod!;
            var osxField = osPlatformType.GetProperty("OSX")!.GetMethod!;

            var unixLabel = il.DefineLabel();
            il.Emit(OpCodes.Call, linuxField);
            il.Emit(OpCodes.Call, isOsPlatformMethod);
            il.Emit(OpCodes.Brtrue, unixLabel);

            il.Emit(OpCodes.Call, osxField);
            il.Emit(OpCodes.Call, isOsPlatformMethod);
            il.Emit(OpCodes.Brfalse, windowsLabel);

            il.MarkLabel(unixLabel);

            // File.SetUnixFileMode(path, (UnixFileMode)mode)
            var setUnixFileMode = _types.File.GetMethod("SetUnixFileMode", [typeof(string), typeof(UnixFileMode)])!;
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, modeLocal);
            il.Emit(OpCodes.Call, setUnixFileMode);

            il.MarkLabel(windowsLabel);
            // Windows: no-op
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsChownSync(object path, object uid, object gid)
    /// Changes file ownership (Unix only, throws ENOSYS on Windows).
    /// </summary>
    private void EmitFsChownSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsChownSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsChownSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert uid to int32
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var uidLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, uidLocal);

        // Convert gid to int32
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var gidLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, gidLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "chown", afterTry =>
        {
            // Check if on Windows - throw ENOSYS
            var runtimeInfoType = typeof(RuntimeInformation);
            var osPlatformType = typeof(OSPlatform);
            var isOsPlatformMethod = runtimeInfoType.GetMethod("IsOSPlatform", [osPlatformType])!;
            var windowsField = osPlatformType.GetProperty("Windows")!.GetMethod!;

            var notWindowsLabel = il.DefineLabel();
            il.Emit(OpCodes.Call, windowsField);
            il.Emit(OpCodes.Call, isOsPlatformMethod);
            il.Emit(OpCodes.Brfalse, notWindowsLabel);

            // Throw ENOSYS on Windows
            var nodeErrorCtor = typeof(NodeError).GetConstructor([
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(int?)
            ])!;
            il.Emit(OpCodes.Ldstr, "ENOSYS");
            il.Emit(OpCodes.Ldstr, "function not implemented");
            il.Emit(OpCodes.Ldstr, "chown");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Newobj, nodeErrorCtor);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(notWindowsLabel);

            // Check if file/directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Call LibC.chown via reflection (we can't directly call P/Invoke from emitted code)
            // For compiled code, we need to use a helper method that wraps the P/Invoke
            // For now, just leave as no-op on Unix too (P/Invoke is complex from IL)
            // The interpreter handles this correctly with real P/Invoke

            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsLchownSync(object path, object uid, object gid)
    /// Changes symlink ownership (Unix only, throws ENOSYS on Windows).
    /// </summary>
    private void EmitFsLchownSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsLchownSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsLchownSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert uid to int32
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var uidLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, uidLocal);

        // Convert gid to int32
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var gidLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, gidLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "lchown", afterTry =>
        {
            // Check if on Windows - throw ENOSYS
            var runtimeInfoType = typeof(RuntimeInformation);
            var osPlatformType = typeof(OSPlatform);
            var isOsPlatformMethod = runtimeInfoType.GetMethod("IsOSPlatform", [osPlatformType])!;
            var windowsField = osPlatformType.GetProperty("Windows")!.GetMethod!;

            var notWindowsLabel = il.DefineLabel();
            il.Emit(OpCodes.Call, windowsField);
            il.Emit(OpCodes.Call, isOsPlatformMethod);
            il.Emit(OpCodes.Brfalse, notWindowsLabel);

            // Throw ENOSYS on Windows
            var nodeErrorCtor = typeof(NodeError).GetConstructor([
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(int?)
            ])!;
            il.Emit(OpCodes.Ldstr, "ENOSYS");
            il.Emit(OpCodes.Ldstr, "function not implemented");
            il.Emit(OpCodes.Ldstr, "lchown");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Newobj, nodeErrorCtor);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(notWindowsLabel);

            // Check if file/directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // For now, just leave as no-op on Unix (P/Invoke from IL is complex)
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsTruncateSync(object path, object len)
    /// Truncates file to specified length.
    /// </summary>
    private void EmitFsTruncateSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsTruncateSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsTruncateSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert len to int64 (default 0 if null)
        var lenLocal = il.DeclareLocal(_types.Int64);
        var hasLenLabel = il.DefineLabel();
        var afterLenLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, afterLenLabel); // null -> use default 0

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, hasLenLabel);

        il.MarkLabel(afterLenLabel);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Stloc, lenLocal);

        il.MarkLabel(hasLenLabel);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "truncate", afterTry =>
        {
            // using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
            // fs.SetLength(len);
            var fileStreamType = typeof(FileStream);
            var fileStreamCtor = fileStreamType.GetConstructor([typeof(string), typeof(FileMode), typeof(FileAccess)])!;
            var setLengthMethod = fileStreamType.GetMethod("SetLength", [typeof(long)])!;
            var disposeMethod = _types.DisposableDispose;

            var fsLocal = il.DeclareLocal(fileStreamType);
            var innerAfterTry = il.DefineLabel();

            il.BeginExceptionBlock();

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldc_I4, (int)FileMode.Open);
            il.Emit(OpCodes.Ldc_I4, (int)FileAccess.Write);
            il.Emit(OpCodes.Newobj, fileStreamCtor);
            il.Emit(OpCodes.Stloc, fsLocal);

            il.Emit(OpCodes.Ldloc, fsLocal);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Callvirt, setLengthMethod);

            il.Emit(OpCodes.Leave, innerAfterTry);

            il.BeginFinallyBlock();
            var skipDisposeLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, fsLocal);
            il.Emit(OpCodes.Brfalse, skipDisposeLabel);
            il.Emit(OpCodes.Ldloc, fsLocal);
            il.Emit(OpCodes.Callvirt, disposeMethod);
            il.MarkLabel(skipDisposeLabel);
            il.EndExceptionBlock();

            il.MarkLabel(innerAfterTry);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsSymlinkSync(object target, object path, object? type)
    /// Creates a symbolic link.
    /// </summary>
    private void EmitFsSymlinkSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsSymlinkSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsSymlinkSync = method;

        var il = method.GetILGenerator();

        // Convert target to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var targetLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, targetLocal);

        // Convert linkPath to string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var linkPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, linkPathLocal);

        // Convert type to string (may be null)
        var typeLocal = il.DeclareLocal(_types.String);
        var typeNullLabel = il.DefineLabel();
        var afterTypeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, typeNullLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, typeLocal);
        il.Emit(OpCodes.Br, afterTypeLabel);

        il.MarkLabel(typeNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, typeLocal);

        il.MarkLabel(afterTypeLabel);

        EmitWithFsErrorHandling(il, runtime, linkPathLocal, "symlink", afterTry =>
        {
            // Check if target is a directory or type is "dir" or "junction"
            var createFileSymlink = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            // Check if target is a directory
            il.Emit(OpCodes.Ldloc, targetLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, doneLabel); // Create directory symlink

            // Check if type == "dir"
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Brfalse, createFileSymlink);
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldstr, "dir");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, doneLabel);

            // Check if type == "junction"
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldstr, "junction");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, doneLabel);

            // Create file symlink
            il.MarkLabel(createFileSymlink);
            var createFileSymlinkMethod = _types.File.GetMethod("CreateSymbolicLink", [typeof(string), typeof(string)])!;
            il.Emit(OpCodes.Ldloc, linkPathLocal);
            il.Emit(OpCodes.Ldloc, targetLocal);
            il.Emit(OpCodes.Call, createFileSymlinkMethod);
            il.Emit(OpCodes.Pop); // Discard FileSystemInfo result
            il.Emit(OpCodes.Leave, afterTry);

            // Create directory symlink
            il.MarkLabel(doneLabel);
            var createDirSymlinkMethod = _types.Directory.GetMethod("CreateSymbolicLink", [typeof(string), typeof(string)])!;
            il.Emit(OpCodes.Ldloc, linkPathLocal);
            il.Emit(OpCodes.Ldloc, targetLocal);
            il.Emit(OpCodes.Call, createDirSymlinkMethod);
            il.Emit(OpCodes.Pop); // Discard FileSystemInfo result
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsReadlinkSync(object path)
    /// Reads the target of a symbolic link.
    /// </summary>
    private void EmitFsReadlinkSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadlinkSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsReadlinkSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "readlink", afterTry =>
        {
            var linkTargetLocal = il.DeclareLocal(_types.String);
            var checkDirLabel = il.DefineLabel();
            var notFoundLabel = il.DefineLabel();
            var checkTargetLabel = il.DefineLabel();

            // Check if file exists
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, checkDirLabel);

            // Get file LinkTarget
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "LinkTarget").GetMethod!);
            il.Emit(OpCodes.Stloc, linkTargetLocal);
            il.Emit(OpCodes.Br, checkTargetLabel);

            // Check if directory exists
            il.MarkLabel(checkDirLabel);
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, notFoundLabel);

            // Get directory LinkTarget
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DirectoryInfo, _types.String));
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DirectoryInfo, "LinkTarget").GetMethod!);
            il.Emit(OpCodes.Stloc, linkTargetLocal);
            il.Emit(OpCodes.Br, checkTargetLabel);

            // Not found
            il.MarkLabel(notFoundLabel);
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            // Check if linkTarget is null (not a symlink)
            il.MarkLabel(checkTargetLabel);
            var validLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, linkTargetLocal);
            il.Emit(OpCodes.Brtrue, validLabel);

            // Not a symlink - throw EINVAL
            var nodeErrorCtor = typeof(NodeError).GetConstructor([
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(int?)
            ])!;
            il.Emit(OpCodes.Ldstr, "EINVAL");
            il.Emit(OpCodes.Ldstr, "invalid argument");
            il.Emit(OpCodes.Ldstr, "readlink");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Newobj, nodeErrorCtor);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(validLabel);
            il.Emit(OpCodes.Ldloc, linkTargetLocal);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsRealpathSync(object path)
    /// Returns the canonical absolute pathname, resolving symlinks.
    /// </summary>
    private void EmitFsRealpathSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsRealpathSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsRealpathSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "realpath", afterTry =>
        {
            // Check if file/directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Get full path
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFullPath", _types.String));
            var fullPathLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, fullPathLocal);

            // Try to resolve symlinks
            var doneLabel = il.DefineLabel();
            var tryDirLabel = il.DefineLabel();

            // Try file first
            il.Emit(OpCodes.Ldloc, fullPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, tryDirLabel);

            // FileInfo.ResolveLinkTarget
            il.Emit(OpCodes.Ldloc, fullPathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
            il.Emit(OpCodes.Ldc_I4_1); // returnFinalTarget: true
            var resolveLinkTarget = _types.FileInfo.GetMethod("ResolveLinkTarget", [typeof(bool)])!;
            il.Emit(OpCodes.Callvirt, resolveLinkTarget);
            var resolvedLocal = il.DeclareLocal(typeof(FileSystemInfo));
            il.Emit(OpCodes.Stloc, resolvedLocal);

            // If resolved is not null, use its FullName
            var useFullPathLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, resolvedLocal);
            il.Emit(OpCodes.Brfalse, useFullPathLabel);
            il.Emit(OpCodes.Ldloc, resolvedLocal);
            il.Emit(OpCodes.Callvirt, typeof(FileSystemInfo).GetProperty("FullName")!.GetMethod!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);

            // Try directory
            il.MarkLabel(tryDirLabel);
            il.Emit(OpCodes.Ldloc, fullPathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DirectoryInfo, _types.String));
            il.Emit(OpCodes.Ldc_I4_1); // returnFinalTarget: true
            var dirResolveLinkTarget = _types.DirectoryInfo.GetMethod("ResolveLinkTarget", [typeof(bool)])!;
            il.Emit(OpCodes.Callvirt, dirResolveLinkTarget);
            il.Emit(OpCodes.Stloc, resolvedLocal);

            il.Emit(OpCodes.Ldloc, resolvedLocal);
            il.Emit(OpCodes.Brfalse, useFullPathLabel);
            il.Emit(OpCodes.Ldloc, resolvedLocal);
            il.Emit(OpCodes.Callvirt, typeof(FileSystemInfo).GetProperty("FullName")!.GetMethod!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);

            il.MarkLabel(useFullPathLabel);
            il.Emit(OpCodes.Ldloc, fullPathLocal);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsUtimesSync(object path, object atime, object mtime)
    /// Sets file access and modification times.
    /// </summary>
    private void EmitFsUtimesSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsUtimesSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsUtimesSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Convert atime to DateTime (Unix timestamp in seconds)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I8);
        var atimeSecondsLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, atimeSecondsLocal);

        var dateTimeOffsetType = typeof(DateTimeOffset);
        var fromUnixTimeSeconds = dateTimeOffsetType.GetMethod("FromUnixTimeSeconds", [typeof(long)])!;
        var localDateTimeGetter = dateTimeOffsetType.GetProperty("LocalDateTime")!.GetMethod!;

        il.Emit(OpCodes.Ldloc, atimeSecondsLocal);
        il.Emit(OpCodes.Call, fromUnixTimeSeconds);
        var dtoLocal = il.DeclareLocal(dateTimeOffsetType);
        il.Emit(OpCodes.Stloc, dtoLocal);
        il.Emit(OpCodes.Ldloca, dtoLocal);
        il.Emit(OpCodes.Call, localDateTimeGetter);
        var atimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Stloc, atimeLocal);

        // Convert mtime to DateTime (Unix timestamp in seconds)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I8);
        var mtimeSecondsLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, mtimeSecondsLocal);

        il.Emit(OpCodes.Ldloc, mtimeSecondsLocal);
        il.Emit(OpCodes.Call, fromUnixTimeSeconds);
        il.Emit(OpCodes.Stloc, dtoLocal);
        il.Emit(OpCodes.Ldloca, dtoLocal);
        il.Emit(OpCodes.Call, localDateTimeGetter);
        var mtimeLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Stloc, mtimeLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "utimes", afterTry =>
        {
            // Check if file/directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // File.SetLastAccessTime(path, atime)
            var setLastAccessTime = _types.File.GetMethod("SetLastAccessTime", [typeof(string), typeof(DateTime)])!;
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, atimeLocal);
            il.Emit(OpCodes.Call, setLastAccessTime);

            // File.SetLastWriteTime(path, mtime)
            var setLastWriteTime = _types.File.GetMethod("SetLastWriteTime", [typeof(string), typeof(DateTime)])!;
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, mtimeLocal);
            il.Emit(OpCodes.Call, setLastWriteTime);

            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsGetConstants()
    /// Returns the fs.constants object.
    /// </summary>
    private void EmitFsGetConstants(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsGetConstants",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.FsGetConstants = method;

        var il = method.GetILGenerator();

        // Create dictionary for constants
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add file access constants
        void AddConstant(string name, double value)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_R8, value);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, addMethod);
        }

        // File access constants
        AddConstant("F_OK", 0.0);
        AddConstant("R_OK", 4.0);
        AddConstant("W_OK", 2.0);
        AddConstant("X_OK", 1.0);

        // File open constants
        AddConstant("O_RDONLY", 0.0);
        AddConstant("O_WRONLY", 1.0);
        AddConstant("O_RDWR", 2.0);
        AddConstant("O_CREAT", 64.0);
        AddConstant("O_EXCL", 128.0);
        AddConstant("O_TRUNC", 512.0);
        AddConstant("O_APPEND", 1024.0);

        // Copy file constants
        AddConstant("COPYFILE_EXCL", 1.0);
        AddConstant("COPYFILE_FICLONE", 2.0);
        AddConstant("COPYFILE_FICLONE_FORCE", 4.0);

        // File type constants
        AddConstant("S_IFMT", 61440.0);
        AddConstant("S_IFREG", 32768.0);
        AddConstant("S_IFDIR", 16384.0);
        AddConstant("S_IFCHR", 8192.0);
        AddConstant("S_IFBLK", 24576.0);
        AddConstant("S_IFIFO", 4096.0);
        AddConstant("S_IFLNK", 40960.0);
        AddConstant("S_IFSOCK", 49152.0);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsCreateDirent(string fullPath)
    /// Creates a Dirent-like object for readdirSync withFileTypes.
    /// </summary>
    private void EmitFsCreateDirent(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsCreateDirent",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]
        );
        runtime.FsCreateDirent = method;

        var il = method.GetILGenerator();

        // Get filename from path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));
        var nameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, nameLocal);

        // Check isFile
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
        var isFileLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Stloc, isFileLocal);

        // Check isDir
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
        var isDirLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Stloc, isDirLocal);

        // Check isSymlink (via FileAttributes)
        var isSymlinkLocal = il.DeclareLocal(_types.Boolean);
        var afterSymlinkCheck = il.DefineLabel();
        var checkFileSymlink = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, isFileLocal);
        il.Emit(OpCodes.Brfalse, checkFileSymlink);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Attributes").GetMethod!);
        il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Stloc, isSymlinkLocal);
        il.Emit(OpCodes.Br, afterSymlinkCheck);

        il.MarkLabel(checkFileSymlink);
        il.Emit(OpCodes.Ldloc, isDirLocal);
        il.Emit(OpCodes.Brfalse, afterSymlinkCheck);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DirectoryInfo, _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DirectoryInfo, "Attributes").GetMethod!);
        il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Stloc, isSymlinkLocal);

        il.MarkLabel(afterSymlinkCheck);

        // Create dictionary
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add name
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, addMethod);

        // Add isFile method (returns isFile && !isDir)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isFile");
        il.Emit(OpCodes.Ldloc, isFileLocal);
        il.Emit(OpCodes.Ldloc, isDirLocal);
        il.Emit(OpCodes.Not);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Add isDirectory
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isDirectory");
        il.Emit(OpCodes.Ldloc, isDirLocal);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Add isSymbolicLink
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isSymbolicLink");
        il.Emit(OpCodes.Ldloc, isSymlinkLocal);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Add isBlockDevice: false
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isBlockDevice");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Add isCharacterDevice: false
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isCharacterDevice");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Add isFIFO: false
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isFIFO");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Add isSocket: false
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isSocket");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }

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
                il.Emit(OpCodes.Call, runtime.FsExistsSync);
                il.Emit(OpCodes.Box, _types.Boolean);
            });

        // readFileSync(path, encoding?) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "readFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsReadFileSync);
            });

        // writeFileSync(path, data) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "writeFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsWriteFileSync);
                il.Emit(OpCodes.Ldnull);
            });

        // appendFileSync(path, data) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "appendFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsAppendFileSync);
                il.Emit(OpCodes.Ldnull);
            });

        // unlinkSync(path) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "unlinkSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.FsUnlinkSync);
                il.Emit(OpCodes.Ldnull);
            });

        // mkdirSync(path, options?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "mkdirSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsMkdirSync);
                il.Emit(OpCodes.Ldnull);
            });

        // rmdirSync(path, options?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "rmdirSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsRmdirSync);
                il.Emit(OpCodes.Ldnull);
            });

        // readdirSync(path, options?) -> List<object>
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "readdirSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsReaddirSync);
            });

        // statSync(path) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "statSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.FsStatSync);
            });

        // lstatSync(path) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "lstatSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.FsLstatSync);
            });

        // renameSync(oldPath, newPath) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "renameSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsRenameSync);
                il.Emit(OpCodes.Ldnull);
            });

        // copyFileSync(src, dest) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "copyFileSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsCopyFileSync);
                il.Emit(OpCodes.Ldnull);
            });

        // accessSync(path, mode?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "accessSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsAccessSync);
                il.Emit(OpCodes.Ldnull);
            });

        // lstatSync(path) -> object
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "lstatSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.FsLstatSync);
            });

        // chmodSync(path, mode) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "chmodSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsChmodSync);
                il.Emit(OpCodes.Ldnull);
            });

        // chownSync(path, uid, gid) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "chownSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.FsChownSync);
                il.Emit(OpCodes.Ldnull);
            });

        // lchownSync(path, uid, gid) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "lchownSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.FsLchownSync);
                il.Emit(OpCodes.Ldnull);
            });

        // truncateSync(path, len?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "truncateSync", 2,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.FsTruncateSync);
                il.Emit(OpCodes.Ldnull);
            });

        // symlinkSync(target, path, type?) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "symlinkSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.FsSymlinkSync);
                il.Emit(OpCodes.Ldnull);
            });

        // readlinkSync(path) -> string
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "readlinkSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.FsReadlinkSync);
            });

        // realpathSync(path) -> string
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "realpathSync", 1,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.FsRealpathSync);
            });

        // utimesSync(path, atime, mtime) -> undefined
        EmitFsMethodWrapperSimple(typeBuilder, runtime, "utimesSync", 3,
            il =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, runtime.FsUtimesSync);
                il.Emit(OpCodes.Ldnull);
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
        runtime.RegisterBuiltInModuleMethod("fs", methodName, method);
    }

    #region File Descriptor APIs

    /// <summary>
    /// Emits: public static double FsOpenSync(object path, object flags, object mode)
    /// Opens a file and returns a file descriptor.
    /// </summary>
    private void EmitFsOpenSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsOpenSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsOpenSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        var resultLocal = il.DeclareLocal(_types.Double);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // Parse flags using FsFlags.Parse
            var fsFlagsType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FsFlags);
            var parseMethod = fsFlagsType.GetMethod("Parse", [typeof(object)])!;
            var fdTableType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FileDescriptorTable);
            var instanceField = fdTableType.GetField("Instance")!;
            var openMethod = fdTableType.GetMethod("Open", [typeof(string), typeof(FileMode), typeof(FileAccess), typeof(FileShare)])!;

            // FsFlags.Parse(flags)
            il.Emit(OpCodes.Ldarg_1); // flags
            il.Emit(OpCodes.Call, parseMethod);

            // The tuple is on the stack, decompose it
            var tupleType = typeof(ValueTuple<FileMode, FileAccess, FileShare>);
            var tupleLocal = il.DeclareLocal(tupleType);
            il.Emit(OpCodes.Stloc, tupleLocal);

            // Call FileDescriptorTable.Instance.Open(path, mode, access, share)
            il.Emit(OpCodes.Ldsfld, instanceField); // Get instance
            il.Emit(OpCodes.Ldloc, pathLocal); // path
            il.Emit(OpCodes.Ldloca, tupleLocal);
            il.Emit(OpCodes.Ldfld, tupleType.GetField("Item1")!); // FileMode
            il.Emit(OpCodes.Ldloca, tupleLocal);
            il.Emit(OpCodes.Ldfld, tupleType.GetField("Item2")!); // FileAccess
            il.Emit(OpCodes.Ldloca, tupleLocal);
            il.Emit(OpCodes.Ldfld, tupleType.GetField("Item3")!); // FileShare
            il.Emit(OpCodes.Callvirt, openMethod);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsCloseSync(object fd)
    /// Closes a file descriptor.
    /// </summary>
    private void EmitFsCloseSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsCloseSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.FsCloseSync = method;

        var il = method.GetILGenerator();

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Create a null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "close", afterTry =>
        {
            var fdTableType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FileDescriptorTable);
            var instanceField = fdTableType.GetField("Instance")!;
            var closeMethod = fdTableType.GetMethod("Close")!;

            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, closeMethod);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static double FsReadSync(object fd, object buffer, object offset, object length, object position)
    /// Reads from a file descriptor into a buffer.
    /// </summary>
    private void EmitFsReadSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.FsReadSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Convert offset to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert length to int
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "read", afterTry =>
        {
            var fdTableType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FileDescriptorTable);
            var instanceField = fdTableType.GetField("Instance")!;
            var getMethod = fdTableType.GetMethod("Get")!;
            var fileStreamReadMethod = typeof(FileStream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!;
            var fileStreamSeekMethod = typeof(FileStream).GetMethod("Seek")!;

            // Get FileStream from fd table
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, getMethod);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // Handle position (if not null, seek to it)
            var skipSeekLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Brfalse, skipSeekLabel);
            // Check for undefined
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Isinst, typeof(SharpTS.Runtime.Types.SharpTSUndefined));
            il.Emit(OpCodes.Brtrue, skipSeekLabel);

            // Seek to position
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldc_I4_0); // SeekOrigin.Begin
            il.Emit(OpCodes.Callvirt, fileStreamSeekMethod);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(skipSeekLabel);

            // Get buffer data - cast to compiled $Buffer type and call GetData()
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            var dataLocal = il.DeclareLocal(typeof(byte[]));
            il.Emit(OpCodes.Stloc, dataLocal);

            // Read from stream
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldloc, offsetLocal);
            il.Emit(OpCodes.Ldloc, lengthLocal);
            il.Emit(OpCodes.Callvirt, fileStreamReadMethod);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static double FsWriteSync(object fd, object data, object offset, object length, object position)
    /// Writes to a file descriptor from a buffer or string.
    /// </summary>
    private void EmitFsWriteSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsWriteSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.FsWriteSyncBuffer = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "write", afterTry =>
        {
            var fdTableType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FileDescriptorTable);
            var instanceField = fdTableType.GetField("Instance")!;
            var getMethod = fdTableType.GetMethod("Get")!;
            var fileStreamWriteMethod = typeof(FileStream).GetMethod("Write", [typeof(byte[]), typeof(int), typeof(int)])!;
            var fileStreamSeekMethod = typeof(FileStream).GetMethod("Seek")!;

            // Get FileStream from fd table
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, getMethod);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // Handle position (if not null and not undefined, seek to it)
            var skipSeekLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Brfalse, skipSeekLabel);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Isinst, typeof(SharpTS.Runtime.Types.SharpTSUndefined));
            il.Emit(OpCodes.Brtrue, skipSeekLabel);

            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, fileStreamSeekMethod);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(skipSeekLabel);

            // Check if data is a buffer or string
            var isBufferLabel = il.DefineLabel();
            var afterDataLabel = il.DefineLabel();
            var dataLocal = il.DeclareLocal(typeof(byte[]));
            var offsetLocal = il.DeclareLocal(_types.Int32);
            var lengthLocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, isBufferLabel);

            // String case - convert to UTF8 bytes
            // Stack: [] -> [Encoding] -> [Encoding, string] -> [byte[]]
            il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetMethod!);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Callvirt, _types.EncodingGetBytesFromString);
            il.Emit(OpCodes.Stloc, dataLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, offsetLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, lengthLocal);
            il.Emit(OpCodes.Br, afterDataLabel);

            // Buffer case - use compiled $Buffer type
            il.MarkLabel(isBufferLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            il.Emit(OpCodes.Stloc, dataLocal);

            // Offset: use arg2 if provided, else 0
            var useDefaultOffsetLabel = il.DefineLabel();
            var afterOffsetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brfalse, useDefaultOffsetLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, offsetLocal);
            il.Emit(OpCodes.Br, afterOffsetLabel);
            il.MarkLabel(useDefaultOffsetLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, offsetLocal);
            il.MarkLabel(afterOffsetLabel);

            // Length: use arg3 if provided, else buffer.Length (via data array length)
            var useDefaultLengthLabel = il.DefineLabel();
            var afterLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, useDefaultLengthLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, lengthLocal);
            il.Emit(OpCodes.Br, afterLengthLabel);
            il.MarkLabel(useDefaultLengthLabel);
            // Use data array length instead of calling buffer.Length
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, lengthLocal);
            il.MarkLabel(afterLengthLabel);

            il.MarkLabel(afterDataLabel);

            // Write to stream
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldloc, dataLocal);
            il.Emit(OpCodes.Ldloc, offsetLocal);
            il.Emit(OpCodes.Ldloc, lengthLocal);
            il.Emit(OpCodes.Callvirt, fileStreamWriteMethod);
            il.Emit(OpCodes.Ldloc, lengthLocal);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsFstatSync(object fd)
    /// Returns stats for an open file descriptor.
    /// </summary>
    private void EmitFsFstatSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsFstatSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsFstatSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "fstat", afterTry =>
        {
            var fdTableType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FileDescriptorTable);
            var instanceField = fdTableType.GetField("Instance")!;
            var getMethod = fdTableType.GetMethod("Get")!;
            var lengthGetter = typeof(FileStream).GetProperty("Length")!.GetMethod!;

            // Get FileStream from fd table
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, getMethod);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // Get file size from stream
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Callvirt, lengthGetter);
            il.Emit(OpCodes.Conv_R8);
            var sizeLocal = il.DeclareLocal(_types.Double);
            il.Emit(OpCodes.Stloc, sizeLocal);

            // Create $Stats instance:
            // new $Stats(isFile, isDirectory, isSymbolicLink, size, mode, atimeMs, mtimeMs, ctimeMs, birthtimeMs)
            il.Emit(OpCodes.Ldc_I4_1);        // arg1: isFile = true (fds are always files)
            il.Emit(OpCodes.Ldc_I4_0);        // arg2: isDirectory = false
            il.Emit(OpCodes.Ldc_I4_0);        // arg3: isSymbolicLink = false
            il.Emit(OpCodes.Ldloc, sizeLocal); // arg4: size
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg5: mode (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg6: atimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg7: mtimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg8: ctimeMs (placeholder)
            il.Emit(OpCodes.Ldc_R8, 0.0);     // arg9: birthtimeMs (placeholder)
            il.Emit(OpCodes.Newobj, runtime.StatsCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsFtruncateSync(object fd, object len)
    /// Truncates an open file descriptor to the specified length.
    /// </summary>
    private void EmitFsFtruncateSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsFtruncateSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsFtruncateSync = method;

        var il = method.GetILGenerator();

        // Convert fd to int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        var fdLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, fdLocal);

        // Convert len to long (default 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I8);
        var lenLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Stloc, lenLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "ftruncate", afterTry =>
        {
            var fdTableType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.FileDescriptorTable);
            var instanceField = fdTableType.GetField("Instance")!;
            var getMethod = fdTableType.GetMethod("Get")!;
            var setLengthMethod = typeof(FileStream).GetMethod("SetLength")!;

            // Get FileStream from fd table
            il.Emit(OpCodes.Ldsfld, instanceField);
            il.Emit(OpCodes.Ldloc, fdLocal);
            il.Emit(OpCodes.Callvirt, getMethod);
            var streamLocal = il.DeclareLocal(typeof(FileStream));
            il.Emit(OpCodes.Stloc, streamLocal);

            // SetLength
            il.Emit(OpCodes.Ldloc, streamLocal);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Callvirt, setLengthMethod);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Directory Utilities

    /// <summary>
    /// Emits: public static object FsMkdtempSync(object prefix)
    /// Creates a unique temporary directory.
    /// </summary>
    private void EmitFsMkdtempSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsMkdtempSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsMkdtempSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);

        // Convert prefix to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var prefixLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, prefixLocal);

        // Create null path local for error handling
        il.Emit(OpCodes.Ldnull);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "mkdtemp", afterTry =>
        {
            // Path.GetTempPath()
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetTempPath"));

            // Path.GetRandomFileName()
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetRandomFileName"));

            // Remove the dot from the random filename
            il.Emit(OpCodes.Ldstr, ".");
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Replace", [typeof(string), typeof(string)])!);

            // Combine prefix + randomFileName
            il.Emit(OpCodes.Ldloc, prefixLocal);
            var tempFileNameLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, tempFileNameLocal);

            // string.Concat(tempPath, prefix + randomFileName)
            var tempPathLocal = il.DeclareLocal(_types.String);
            // We need to re-emit GetTempPath
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetTempPath"));
            il.Emit(OpCodes.Stloc, tempPathLocal);

            il.Emit(OpCodes.Ldloc, prefixLocal);
            il.Emit(OpCodes.Ldloc, tempFileNameLocal);
            il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string)])!);
            var suffixLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, suffixLocal);

            // Path.Combine(tempPath, prefix + random)
            il.Emit(OpCodes.Ldloc, tempPathLocal);
            il.Emit(OpCodes.Ldloc, suffixLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "Combine", _types.String, _types.String));
            il.Emit(OpCodes.Stloc, resultLocal);

            // Directory.CreateDirectory
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "CreateDirectory", _types.String));
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object FsOpendirSync(object path)
    /// Opens a directory for iteration.
    /// </summary>
    private void EmitFsOpendirSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsOpendirSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.FsOpendirSync = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "opendir", afterTry =>
        {
            var dirType = typeof(SharpTS.Runtime.Types.SharpTSDir);
            var dirCtor = dirType.GetConstructor([typeof(string)])!;

            // Check if directory exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw DirectoryNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory, opendir '");
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldstr, "'");
            il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
            il.Emit(OpCodes.Newobj, typeof(DirectoryNotFoundException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Create new SharpTSDir(path)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Newobj, dirCtor);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Hard Links

    /// <summary>
    /// Emits: public static void FsLinkSync(object existingPath, object newPath)
    /// Creates a hard link.
    /// </summary>
    private void EmitFsLinkSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsLinkSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.FsLinkSync = method;

        var il = method.GetILGenerator();

        // Convert paths to strings
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var existingPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, existingPathLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var newPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, newPathLocal);

        EmitWithFsErrorHandling(il, runtime, newPathLocal, "link", afterTry =>
        {
            var libCType = typeof(SharpTS.Runtime.BuiltIns.Modules.Interop.LibC);
            var createHardLinkMethod = libCType.GetMethod("CreateHardLink", [typeof(string), typeof(string)])!;

            // Check if source exists
            var existsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brtrue, existsLabel);

            // Throw FileNotFoundException
            il.Emit(OpCodes.Ldstr, "no such file or directory");
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileNotFoundException, _types.String, _types.String));
            il.Emit(OpCodes.Throw);

            il.MarkLabel(existsLabel);

            // Check if destination already exists
            var notExistsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, notExistsLabel);

            // Throw IOException for EEXIST
            // Use Concat overload that takes string array
            il.Emit(OpCodes.Ldc_I4_5);
            il.Emit(OpCodes.Newarr, _types.String);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldstr, "EEXIST: file already exists, link '");
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldstr, "' -> '");
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Ldstr, "'");
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string[])])!);
            il.Emit(OpCodes.Newobj, typeof(IOException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(notExistsLabel);

            // LibC.CreateHardLink(existingPath, newPath)
            il.Emit(OpCodes.Ldloc, existingPathLocal);
            il.Emit(OpCodes.Ldloc, newPathLocal);
            il.Emit(OpCodes.Call, createHardLinkMethod);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Async FS Methods

    /// <summary>
    /// Emits all async fs methods with inline IL (no external FsAsyncHelpers dependency).
    /// Each method calls the corresponding sync implementation and wraps result with Task.FromResult.
    /// </summary>
    private void EmitFsAsyncMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
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
    /// Emits: Task&lt;object?&gt; FsReadFileAsync(object path, object? encoding)
    /// Calls FsReadFileSync and wraps result in Task.FromResult.
    /// </summary>
    private void EmitFsReadFileAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsReadFileAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object]
        );
        runtime.FsReadFileAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsReadFileSync(path, encoding)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsReadFileSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "readFile", method);
    }

    /// <summary>
    /// Emits: Task&lt;object?&gt; FsWriteFileAsync(object path, object data, object? options)
    /// Calls FsWriteFileSync and returns Task.FromResult(null).
    /// </summary>
    private void EmitFsWriteFileAsync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsWriteFileAsync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsWriteFileAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsWriteFileSync(path, data) - ignores options for now
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsWriteFileSync);

        // Return Task.FromResult(null) for void operations
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "writeFile", method);
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
        runtime.FsAppendFileAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsAppendFileSync(path, data)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsAppendFileSync);

        // Return Task.FromResult(null) for void operations
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "appendFile", method);
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
        runtime.FsStatAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsStatSync(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FsStatSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "stat", method);
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
        runtime.FsLstatAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsLstatSync(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FsLstatSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "lstat", method);
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
        runtime.FsUnlinkAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsUnlinkSync(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FsUnlinkSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "unlink", method);
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
        runtime.FsMkdirAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsMkdirSync(path, options)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsMkdirSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "mkdir", method);
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
        runtime.FsRmdirAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsRmdirSync(path, options)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsRmdirSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "rmdir", method);
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
        runtime.FsRmAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Check options for recursive and force flags
        var recursiveLocal = il.DeclareLocal(_types.Boolean);
        var forceLocal = il.DeclareLocal(_types.Boolean);
        var afterOptionsLabel = il.DefineLabel();
        var afterRecursiveLabel = il.DefineLabel();

        // If options is null, skip
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, afterOptionsLabel);

        // Check recursive option
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "recursive");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, recursiveLocal);

        // Check force option
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "force");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, forceLocal);
        il.Emit(OpCodes.Br, afterRecursiveLabel);

        il.MarkLabel(afterOptionsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, recursiveLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, forceLocal);

        il.MarkLabel(afterRecursiveLabel);

        // Check if path exists
        var existsLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
        il.Emit(OpCodes.Brtrue, existsLabel);

        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
        il.Emit(OpCodes.Brtrue, existsLabel);

        // Path doesn't exist - if force, just return; otherwise throw
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, forceLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "ENOENT: no such file or directory");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(existsLabel);

        // Check if it's a directory
        var isFileLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
        il.Emit(OpCodes.Brfalse, isFileLabel);

        // It's a directory - delete recursively if recursive flag set
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Ldloc, recursiveLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Delete", _types.String, _types.Boolean));
        il.Emit(OpCodes.Br, doneLabel);

        // It's a file - delete it
        il.MarkLabel(isFileLabel);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Delete", _types.String));

        il.MarkLabel(doneLabel);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "rm", method);
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
        runtime.FsReaddirAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsReaddirSync(path, options)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsReaddirSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "readdir", method);
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
        runtime.FsRenameAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsRenameSync(oldPath, newPath)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsRenameSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "rename", method);
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
        runtime.FsCopyFileAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsCopyFileSync(src, dest) - ignores mode for now
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsCopyFileSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "copyFile", method);
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
        runtime.FsAccessAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsAccessSync(path, mode)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsAccessSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "access", method);
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
        runtime.FsChmodAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsChmodSync(path, mode)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsChmodSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "chmod", method);
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
        runtime.FsTruncateAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsTruncateSync(path, len)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsTruncateSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "truncate", method);
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
        runtime.FsUtimesAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsUtimesSync(path, atime, mtime)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.FsUtimesSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "utimes", method);
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
        runtime.FsReadlinkAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsReadlinkSync(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FsReadlinkSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "readlink", method);
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
        runtime.FsRealpathAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsRealpathSync(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FsRealpathSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "realpath", method);
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
        runtime.FsSymlinkAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsSymlinkSync(target, path, type)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.FsSymlinkSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "symlink", method);
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
        runtime.FsLinkAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsLinkSync(existingPath, newPath)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FsLinkSync);

        // Return Task.FromResult(null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "link", method);
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
        runtime.FsMkdtempAsync = method;

        var il = method.GetILGenerator();
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);

        // Call FsMkdtempSync(prefix)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.FsMkdtempSync);

        // Wrap in Task.FromResult
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("fs/promises", "mkdtemp", method);
    }

    /// <summary>
    /// Emits: public static object FsGetPromisesNamespace()
    /// Returns a namespace object containing all fs.promises methods.
    /// Creates TSFunctions that wrap the async helper methods and return Promises.
    /// </summary>
    private void EmitFsGetPromisesNamespace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First, emit wrapper methods that call async helpers and wrap results in Promises
        EmitFsPromisesWrapperMethods(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "FsGetPromisesNamespace",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.FsGetPromisesNamespace = method;

        var il = method.GetILGenerator();

        // Create a new Dictionary<string, object?>
        var dictCtor = _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!;
        var addMethod = _types.DictionaryStringObject.GetMethod("Add", [typeof(string), typeof(object)])!;

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add each wrapper method as a TSFunction
        var fsPromisesWrappers = runtime.FsPromisesWrapperMethods;
        foreach (var (name, wrapper) in fsPromisesWrappers)
        {
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, name);

            // Create TSFunction: new $TSFunction(null, wrapperMethod)
            il.Emit(OpCodes.Ldnull); // target
            il.Emit(OpCodes.Ldtoken, wrapper);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(MethodBase), "GetMethodFromHandle", typeof(RuntimeMethodHandle)));
            il.Emit(OpCodes.Castclass, typeof(MethodInfo));
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);

            il.Emit(OpCodes.Call, addMethod);
        }

        // Add constants
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "constants");
        il.Emit(OpCodes.Call, runtime.FsGetConstants);
        il.Emit(OpCodes.Call, addMethod);

        // Create a SharpTSObject from the dictionary
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits wrapper methods for fs.promises that call the async helpers and wrap results in Promises.
    /// Each wrapper method takes List&lt;object?&gt; args (for TSFunction compatibility).
    /// </summary>
    private void EmitFsPromisesWrapperMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.FsPromisesWrapperMethods = new Dictionary<string, MethodBuilder>();

        EmitPromisesWrapper(typeBuilder, runtime, "readFile", runtime.FsReadFileAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "writeFile", runtime.FsWriteFileAsync, 3);
        EmitPromisesWrapper(typeBuilder, runtime, "appendFile", runtime.FsAppendFileAsync, 3);
        EmitPromisesWrapper(typeBuilder, runtime, "stat", runtime.FsStatAsync, 1);
        EmitPromisesWrapper(typeBuilder, runtime, "lstat", runtime.FsLstatAsync, 1);
        EmitPromisesWrapper(typeBuilder, runtime, "unlink", runtime.FsUnlinkAsync, 1);
        EmitPromisesWrapper(typeBuilder, runtime, "mkdir", runtime.FsMkdirAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "rmdir", runtime.FsRmdirAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "rm", runtime.FsRmAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "readdir", runtime.FsReaddirAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "rename", runtime.FsRenameAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "copyFile", runtime.FsCopyFileAsync, 3);
        EmitPromisesWrapper(typeBuilder, runtime, "access", runtime.FsAccessAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "chmod", runtime.FsChmodAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "truncate", runtime.FsTruncateAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "utimes", runtime.FsUtimesAsync, 3);
        EmitPromisesWrapper(typeBuilder, runtime, "readlink", runtime.FsReadlinkAsync, 1);
        EmitPromisesWrapper(typeBuilder, runtime, "realpath", runtime.FsRealpathAsync, 1);
        EmitPromisesWrapper(typeBuilder, runtime, "symlink", runtime.FsSymlinkAsync, 3);
        EmitPromisesWrapper(typeBuilder, runtime, "link", runtime.FsLinkAsync, 2);
        EmitPromisesWrapper(typeBuilder, runtime, "mkdtemp", runtime.FsMkdtempAsync, 1);
    }

    /// <summary>
    /// Emits a single wrapper method for fs.promises.
    /// Signature: object MethodName(object arg0, object arg1, ...)
    /// Takes individual object parameters to work with TSFunction.Invoke reflection call.
    /// Calls the async helper, wraps the Task in a Promise, and returns it.
    /// </summary>
    private void EmitPromisesWrapper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
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
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);

        il.Emit(OpCodes.Ret);

        runtime.FsPromisesWrapperMethods[name] = wrapper;
    }

    #endregion
}
