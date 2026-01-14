using System.Reflection;
using System.Reflection.Emit;

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
        EmitFsReaddirSync(typeBuilder, runtime);
        EmitFsStatSync(typeBuilder, runtime);
        EmitFsRenameSync(typeBuilder, runtime);
        EmitFsCopyFileSync(typeBuilder, runtime);
        EmitFsAccessSync(typeBuilder, runtime);
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
    /// Returns string if encoding specified, byte[] otherwise.
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

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Check if encoding is provided (not null)
        var readBytesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, readBytesLabel);

        // Read as text: File.ReadAllText(path)
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "ReadAllText", _types.String));
        il.Emit(OpCodes.Ret);

        // Read as bytes: File.ReadAllBytes(path) - wrap in List<object>
        il.MarkLabel(readBytesLabel);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "ReadAllBytes", _types.String));

        // Convert byte[] to List<object> for JS array compatibility
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, bytesLocal);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject));
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Loop: for each byte, add to list
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var indexLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
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

        // File.WriteAllText(path, data)
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "WriteAllText", _types.String, _types.String));
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

        // File.AppendAllText(path, data)
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "AppendAllText", _types.String, _types.String));
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

        // Convert path to string and delete
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Delete", _types.String));
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

        // Directory.CreateDirectory(path)
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "CreateDirectory", _types.String));
        il.Emit(OpCodes.Pop); // CreateDirectory returns DirectoryInfo
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
        il.Emit(OpCodes.Ret);

        // Directory.Delete(path, false)
        il.MarkLabel(nonRecursive);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Delete", _types.String, _types.Boolean));
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
            [_types.Object]
        );
        runtime.FsReaddirSync = method;

        var il = method.GetILGenerator();

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Get entries: Directory.GetFileSystemEntries(path)
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "GetFileSystemEntries", _types.String));
        var entriesLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Stloc, entriesLocal);

        // Create result list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject));
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Loop through entries and add just the filename (not full path)
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

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, entriesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        // Get just the filename using Path.GetFileName
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
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

        // Convert path to string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Create dictionary for stats
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Check if it's a directory or file
        var isFileLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
        il.Emit(OpCodes.Brfalse, isFileLabel);

        // It's a directory - get DirectoryInfo
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DirectoryInfo, _types.String));
        var dirInfoLocal = il.DeclareLocal(_types.DirectoryInfo);
        il.Emit(OpCodes.Stloc, dirInfoLocal);

        // isDirectory: true
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isDirectory");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // isFile: false
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isFile");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // size: 0 for directories
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, addMethod);

        il.Emit(OpCodes.Br, doneLabel);

        // It's a file - get FileInfo
        il.MarkLabel(isFileLabel);
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FileInfo, _types.String));
        var fileInfoLocal = il.DeclareLocal(_types.FileInfo);
        il.Emit(OpCodes.Stloc, fileInfoLocal);

        // isDirectory: false
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isDirectory");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // isFile: true
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "isFile");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, addMethod);

        // size: fileInfo.Length
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Ldloc, fileInfoLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.FileInfo, "Length").GetMethod!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, addMethod);

        il.MarkLabel(doneLabel);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
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

        // Check if it's a directory
        var isFileLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oldPathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
        il.Emit(OpCodes.Brfalse, isFileLabel);

        // Directory.Move
        il.Emit(OpCodes.Ldloc, oldPathLocal);
        il.Emit(OpCodes.Ldloc, newPathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Move", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        // File.Move
        il.MarkLabel(isFileLabel);
        il.Emit(OpCodes.Ldloc, oldPathLocal);
        il.Emit(OpCodes.Ldloc, newPathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Move", _types.String, _types.String));
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

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);

        // File.Copy(src, dest, overwrite: true)
        il.Emit(OpCodes.Ldc_I4_1); // overwrite = true
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Copy", _types.String, _types.String, _types.Boolean));
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

        // Check if exists (File.Exists || Directory.Exists)
        var existsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.File, "Exists", _types.String));
        il.Emit(OpCodes.Brtrue, existsLabel);

        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", _types.String));
        il.Emit(OpCodes.Brtrue, existsLabel);

        // Throw FileNotFoundException
        il.Emit(OpCodes.Ldstr, "ENOENT: no such file or directory");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(FileNotFoundException), _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(existsLabel);
        il.Emit(OpCodes.Ret);
    }
}
