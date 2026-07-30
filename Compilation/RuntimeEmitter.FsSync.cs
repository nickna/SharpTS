using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
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
    /// Emits the BCL-only encoding helpers used by fs read/write/append. They mirror
    /// the interpreter's FsModuleInterpreter.EncodingName + BufferEncoding.ToBytes and
    /// reuse the (corrected) $Buffer encoder so fs encoding == Buffer encoding.
    /// </summary>
    private void EmitFsEncodingHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // string FsEncodingName(object opt): null when none; the string itself, or
        // opt.encoding when opt is an options object.
        var nameMethod = typeBuilder.DefineMethod(
            "FsEncodingName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.FsEncodingName = nameMethod;
        {
            var il = nameMethod.GetILGenerator();
            var retNull = il.DefineLabel();
            var retIt = il.DefineLabel();
            // if (opt == null) return null
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, retNull);
            // if (opt is string) return (string)opt
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, retIt);
            il.Emit(OpCodes.Pop);
            // e = opt.encoding; return (e is string) ? e : null
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "encoding");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, retIt);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(retNull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(retIt);
            il.Emit(OpCodes.Ret);
        }

        // byte[] FsToBytes(object data, object encodingOpt): a Buffer contributes its
        // bytes verbatim; a string is encoded via $Buffer.FromString (default utf8).
        var byteArr = _types.MakeArrayType(_types.Byte);
        var toBytesMethod = typeBuilder.DefineMethod(
            "FsToBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            byteArr,
            [_types.Object, _types.Object]
        );
        runtime.FsToBytes = toBytesMethod;
        {
            var il = toBytesMethod.GetILGenerator();
            var isBuf = il.DefineLabel();
            var haveEnc = il.DefineLabel();
            // buf = data as $Buffer
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, isBuf);
            il.Emit(OpCodes.Pop);
            // string s = Stringify(data)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            // string enc = FsEncodingName(encodingOpt) ?? "utf8"
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.FsEncodingName);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, haveEnc);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "utf8");
            il.MarkLabel(haveEnc);
            // $Buffer.FromString(s, enc).GetData()
            il.Emit(OpCodes.Call, runtime.TSBufferFromString);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            il.Emit(OpCodes.Ret);
            // ((TSBuffer)data).GetData()
            il.MarkLabel(isBuf);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits: public static object FsReadFileSync(object path, object? encoding)
    /// Reads raw bytes; returns a $Buffer when no encoding, else decodes via the
    /// $Buffer encoder (so every encoding matches the interpreter and Buffer).
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
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var encLocal = il.DeclareLocal(_types.String);

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // bytes = File.ReadAllBytes(path)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "ReadAllBytes", _types.String));
            il.Emit(OpCodes.Stloc, bytesLocal);

            // enc = FsEncodingName(encoding)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.FsEncodingName);
            il.Emit(OpCodes.Stloc, encLocal);

            var asBufferLabel = il.DefineLabel();
            var afterReadLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, encLocal);
            il.Emit(OpCodes.Brfalse, asBufferLabel);

            // Decode: new $Buffer(bytes).ToEncodedString(enc)
            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
            il.Emit(OpCodes.Ldloc, encLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferToString);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, afterReadLabel);

            // No encoding: wrap bytes in $Buffer
            il.MarkLabel(asBufferLabel);
            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
            il.Emit(OpCodes.Stloc, resultLocal);

            il.MarkLabel(afterReadLabel);
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsWriteFileSync(object path, object data, object? encoding)
    /// Writes raw bytes (Buffer verbatim, string encoded) — never ToString's a Buffer.
    /// </summary>
    private void EmitFsWriteFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsWriteFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsWriteFileSync = method;

        var il = method.GetILGenerator();

        // path = Stringify(arg0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // bytes = FsToBytes(data, encoding)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.FsToBytes);
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, bytesLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // File.WriteAllBytes(path, bytes)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "WriteAllBytes", _types.String, _types.MakeArrayType(_types.Byte)));
            il.Emit(OpCodes.Leave, afterTry);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void FsAppendFileSync(object path, object data, object? encoding)
    /// </summary>
    private void EmitFsAppendFileSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FsAppendFileSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.FsAppendFileSync = method;

        var il = method.GetILGenerator();

        // path = Stringify(arg0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // bytes = FsToBytes(data, encoding)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.FsToBytes);
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, bytesLocal);

        EmitWithFsErrorHandling(il, runtime, pathLocal, "open", afterTry =>
        {
            // File.AppendAllBytes(path, bytes)
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "AppendAllBytes", _types.String, _types.MakeArrayType(_types.Byte)));
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
            var getEntriesMethod = _types.GetMethod(_types.Directory, "GetFileSystemEntries", [_types.String, _types.String, searchOptionType])!;
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
            var getRelativePathMethod = _types.GetMethod(_types.Path, "GetRelativePath", [_types.String, _types.String])!;
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

        // mode = arg1 is a boxed double ? (int)(double)arg1 : 0. The async access
        // path forwards an undefined mode, so unbox only when it really is a number.
        var modeLocal = il.DeclareLocal(_types.Int32);
        var modeNotDouble = il.DefineLabel();
        var modeDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, modeNotDouble);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, modeLocal);
        il.Emit(OpCodes.Br, modeDone);
        il.MarkLabel(modeNotDouble);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, modeLocal);
        il.MarkLabel(modeDone);

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

            // W_OK (mode & 2): a read-only file denies writes (best-effort, mirrors the
            // interpreter's CheckAccess — surfaced as EACCES by the fs error wrapper).
            var skipWok = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, modeLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, skipWok);

            // Only files have a meaningful read-only attribute here.
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
            il.Emit(OpCodes.Brfalse, skipWok);

            // (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "GetAttributes", _types.String));
            il.Emit(OpCodes.Ldc_I4_1); // FileAttributes.ReadOnly
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, skipWok);

            il.Emit(OpCodes.Ldstr, "permission denied");
            il.Emit(OpCodes.Newobj, typeof(UnauthorizedAccessException).GetConstructor([_types.String])!);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(skipWok);
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
            var setUnixFileMode = _types.GetMethod(_types.File, "SetUnixFileMode", [typeof(string), typeof(UnixFileMode)])!;
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

            // Throw ENOSYS on Windows - use emitted $NodeError type for standalone DLL
            EmitNodeErrorThrow(il, runtime, "ENOSYS", "function not implemented", "chown", pathLocal);

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

            // Throw ENOSYS on Windows - use emitted $NodeError type for standalone DLL
            EmitNodeErrorThrow(il, runtime, "ENOSYS", "function not implemented", "lchown", pathLocal);

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
            var createFileSymlinkMethod = _types.GetMethod(_types.File, "CreateSymbolicLink", [typeof(string), typeof(string)])!;
            il.Emit(OpCodes.Ldloc, linkPathLocal);
            il.Emit(OpCodes.Ldloc, targetLocal);
            il.Emit(OpCodes.Call, createFileSymlinkMethod);
            il.Emit(OpCodes.Pop); // Discard FileSystemInfo result
            il.Emit(OpCodes.Leave, afterTry);

            // Create directory symlink
            il.MarkLabel(doneLabel);
            var createDirSymlinkMethod = _types.GetMethod(_types.Directory, "CreateSymbolicLink", [typeof(string), typeof(string)])!;
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

            // Not a symlink - throw EINVAL - use emitted $NodeError type for standalone DLL
            EmitNodeErrorThrow(il, runtime, "EINVAL", "invalid argument", "readlink", pathLocal);

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
            var resolveLinkTarget = _types.GetMethod(_types.FileInfo, "ResolveLinkTarget", [typeof(bool)])!;
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
            var dirResolveLinkTarget = _types.GetMethod(_types.DirectoryInfo, "ResolveLinkTarget", [typeof(bool)])!;
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
            var setLastAccessTime = _types.GetMethod(_types.File, "SetLastAccessTime", [typeof(string), typeof(DateTime)])!;
            il.Emit(OpCodes.Ldloc, pathLocal);
            il.Emit(OpCodes.Ldloc, atimeLocal);
            il.Emit(OpCodes.Call, setLastAccessTime);

            // File.SetLastWriteTime(path, mtime)
            var setLastWriteTime = _types.GetMethod(_types.File, "SetLastWriteTime", [typeof(string), typeof(DateTime)])!;
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
}
