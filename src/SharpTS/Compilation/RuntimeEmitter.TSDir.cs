using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $Dir type for standalone DLLs.
    /// Represents a directory handle returned by fs.opendirSync().
    /// </summary>
    private void EmitDirType(ModuleBuilder module, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$Dir",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        _ = typeBuilder;

        // Fields
        runtime.RequireFileSystem().DirPathField = typeBuilder.DefineField("_path", _types.String, FieldAttributes.Private | FieldAttributes.InitOnly);
        runtime.RequireFileSystem().DirEnumeratorField = typeBuilder.DefineField("_enumerator", typeof(IEnumerator<string>), FieldAttributes.Private);
        runtime.RequireFileSystem().DirClosedField = typeBuilder.DefineField("_closed", _types.Boolean, FieldAttributes.Private);

        // Constructor: public $Dir(string path)
        EmitDirConstructor(typeBuilder, runtime.RequireFileSystem());

        // Properties
        EmitDirPathProperty(typeBuilder, runtime.RequireFileSystem());

        // Methods
        EmitDirReadSync(typeBuilder, runtime.RequireFileSystem());
        EmitDirCloseSync(typeBuilder, runtime.RequireFileSystem());

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitDirConstructor(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        fileSystem.DirCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _path = path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, fileSystem.DirPathField);

        // _enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "EnumerateFileSystemEntries", [_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(IEnumerable<string>).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stfld, fileSystem.DirEnumeratorField);

        // _closed = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, fileSystem.DirClosedField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitDirPathProperty(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var prop = typeBuilder.DefineProperty("Path", PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Path",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirPathField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
        _ = getter;
    }

    private void EmitDirReadSync(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "ReadSync",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();

        // if (_closed) throw new InvalidOperationException("Directory handle is closed");
        var notClosedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirClosedField);
        il.Emit(OpCodes.Brfalse, notClosedLabel);

        il.Emit(OpCodes.Ldstr, "Directory handle is closed");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notClosedLabel);

        // if (_enumerator.MoveNext()) return CreateDirent(_enumerator.Current);
        var nullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirEnumeratorField);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Create Dirent from _enumerator.Current
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirEnumeratorField);
        il.Emit(OpCodes.Callvirt, typeof(IEnumerator<string>).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, fileSystem.DirentCtor);
        il.Emit(OpCodes.Ret);

        // return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirCloseSync(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "CloseSync",
            MethodAttributes.Public,
            _types.Object, // Return null for JS compatibility
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();

        // if (!_closed) { _enumerator.Dispose(); _closed = true; }
        var alreadyClosedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirClosedField);
        il.Emit(OpCodes.Brtrue, alreadyClosedLabel);

        // _enumerator.Dispose()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirEnumeratorField);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // _closed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, fileSystem.DirClosedField);

        il.MarkLabel(alreadyClosedLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the $Dirent type for standalone DLLs.
    /// Represents a directory entry returned by Dir.readSync().
    /// </summary>
    private void EmitDirentType(ModuleBuilder module, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$Dirent",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        _ = typeBuilder;

        // Fields
        runtime.RequireFileSystem().DirentNameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private | FieldAttributes.InitOnly);
        runtime.RequireFileSystem().DirentIsFileField = typeBuilder.DefineField("_isFile", _types.Boolean, FieldAttributes.Private | FieldAttributes.InitOnly);
        runtime.RequireFileSystem().DirentIsDirField = typeBuilder.DefineField("_isDir", _types.Boolean, FieldAttributes.Private | FieldAttributes.InitOnly);
        runtime.RequireFileSystem().DirentIsSymlinkField = typeBuilder.DefineField("_isSymlink", _types.Boolean, FieldAttributes.Private | FieldAttributes.InitOnly);

        // Constructor: public $Dirent(string fullPath)
        EmitDirentConstructor(typeBuilder, runtime.RequireFileSystem());

        // Properties
        EmitDirentNameProperty(typeBuilder, runtime.RequireFileSystem());

        // Methods
        EmitDirentIsFile(typeBuilder, runtime.RequireFileSystem());
        EmitDirentIsDirectory(typeBuilder, runtime.RequireFileSystem());
        EmitDirentIsSymbolicLink(typeBuilder, runtime.RequireFileSystem());
        EmitDirentIsBlockDevice(typeBuilder, runtime);
        EmitDirentIsCharacterDevice(typeBuilder, runtime);
        EmitDirentIsFIFO(typeBuilder, runtime);
        EmitDirentIsSocket(typeBuilder, runtime);

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitDirentConstructor(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String] // fullPath
        );
        fileSystem.DirentCtor = ctor;

        var il = ctor.GetILGenerator();

        var isFileLocal = il.DeclareLocal(_types.Boolean);
        var isDirLocal = il.DeclareLocal(_types.Boolean);
        var fileInfoLocal = il.DeclareLocal(typeof(FileInfo));

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _name = Path.GetFileName(fullPath)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Path).GetMethod("GetFileName", [_types.String])!);
        il.Emit(OpCodes.Stfld, fileSystem.DirentNameField);

        // isFile = File.Exists(fullPath)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Exists", [_types.String])!);
        il.Emit(OpCodes.Stloc, isFileLocal);

        // isDir = Directory.Exists(fullPath)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", [_types.String])!);
        il.Emit(OpCodes.Stloc, isDirLocal);

        // _isFile = isFile && !isDir
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isFileLocal);
        il.Emit(OpCodes.Ldloc, isDirLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stfld, fileSystem.DirentIsFileField);

        // _isDir = isDir
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isDirLocal);
        il.Emit(OpCodes.Stfld, fileSystem.DirentIsDirField);

        // Check for symbolic link
        // var fileInfo = new FileInfo(fullPath);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, typeof(FileInfo).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Stloc, fileInfoLocal);

        // _isSymlink = (fileInfo.Exists || Directory.Exists(fullPath)) &&
        //              (fileInfo.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint
        var noSymlinkLabel = il.DefineLabel();
        var checkAttributesLabel = il.DefineLabel();
        var afterSymlinkLabel = il.DefineLabel();

        // Check fileInfo.Exists || Directory.Exists(fullPath)
        il.Emit(OpCodes.Ldloc, fileInfoLocal);
        il.Emit(OpCodes.Callvirt, typeof(FileInfo).GetProperty("Exists")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, checkAttributesLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Directory, "Exists", [_types.String])!);
        il.Emit(OpCodes.Brtrue, checkAttributesLabel);

        // Neither exists - not a symlink
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, fileSystem.DirentIsSymlinkField);
        il.Emit(OpCodes.Br, afterSymlinkLabel);

        // Check ReparsePoint attribute
        il.MarkLabel(checkAttributesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fileInfoLocal);
        il.Emit(OpCodes.Callvirt, typeof(FileInfo).GetProperty("Attributes")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I4, (int)FileAttributes.ReparsePoint);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Stfld, fileSystem.DirentIsSymlinkField);

        il.MarkLabel(afterSymlinkLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentNameProperty(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var prop = typeBuilder.DefineProperty("Name", PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Name",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirentNameField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
        _ = getter;
    }

    private void EmitDirentIsFile(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "IsFile",
            MethodAttributes.Public,
            _types.Object, // Return boxed bool for JS compatibility
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirentIsFileField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentIsDirectory(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "IsDirectory",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirentIsDirField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentIsSymbolicLink(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "IsSymbolicLink",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.DirentIsSymlinkField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentIsBlockDevice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsBlockDevice",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        // Always return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentIsCharacterDevice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsCharacterDevice",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        // Always return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentIsFIFO(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsFIFO",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        // Always return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDirentIsSocket(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsSocket",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        // Always return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }
}
