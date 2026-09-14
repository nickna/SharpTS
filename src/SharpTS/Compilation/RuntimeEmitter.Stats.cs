using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Stats class for fs.stat() and related methods.
/// Provides Node.js-compatible Stats object with methods like isFile(), isDirectory(), etc.
/// </summary>
public partial class RuntimeEmitter
{

    /// <summary>
    /// Emits the $Stats class with Node.js-compatible API.
    /// </summary>
    private void EmitStatsClass(ModuleBuilder moduleBuilder, EmittedFileSystemRuntime fileSystem)
    {
        // Define class: public sealed class $Stats
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Stats",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Define fields
        fileSystem.StatsIsFileField = typeBuilder.DefineField("_isFile", _types.Boolean, FieldAttributes.Private);
        fileSystem.StatsIsDirField = typeBuilder.DefineField("_isDirectory", _types.Boolean, FieldAttributes.Private);
        fileSystem.StatsIsSymlinkField = typeBuilder.DefineField("_isSymbolicLink", _types.Boolean, FieldAttributes.Private);
        fileSystem.StatsSizeField = typeBuilder.DefineField("_size", _types.Double, FieldAttributes.Private);
        fileSystem.StatsModeField = typeBuilder.DefineField("_mode", _types.Double, FieldAttributes.Private);
        fileSystem.StatsAtimeMsField = typeBuilder.DefineField("_atimeMs", _types.Double, FieldAttributes.Private);
        fileSystem.StatsMtimeMsField = typeBuilder.DefineField("_mtimeMs", _types.Double, FieldAttributes.Private);
        fileSystem.StatsCtimeMsField = typeBuilder.DefineField("_ctimeMs", _types.Double, FieldAttributes.Private);
        fileSystem.StatsBirthtimeMsField = typeBuilder.DefineField("_birthtimeMs", _types.Double, FieldAttributes.Private);

        // Constructor
        EmitStatsCtor(typeBuilder, fileSystem);

        // Methods that match Node.js Stats API
        EmitStatsIsFileMethod(typeBuilder, fileSystem);
        EmitStatsIsDirectoryMethod(typeBuilder, fileSystem);
        EmitStatsIsSymbolicLinkMethod(typeBuilder, fileSystem);
        EmitStatsIsBlockDeviceMethod(typeBuilder, fileSystem);
        EmitStatsIsCharacterDeviceMethod(typeBuilder, fileSystem);
        EmitStatsIsFIFOMethod(typeBuilder, fileSystem);
        EmitStatsIsSocketMethod(typeBuilder, fileSystem);

        // Properties
        EmitStatsSizeProperty(typeBuilder, fileSystem);
        EmitStatsModeProperty(typeBuilder, fileSystem);
        EmitStatsTimestampProperties(typeBuilder, fileSystem);

        // Finalize the type
        fileSystem.StatsType = typeBuilder.CreateType()!;
        fileSystem.StatsCtor = _types.GetConstructor(fileSystem.StatsType, [
            _types.Boolean, _types.Boolean, _types.Boolean,
            _types.Double, _types.Double,
            _types.Double, _types.Double, _types.Double, _types.Double
        ])!;
    }

    /// <summary>
    /// Emits constructor: public $Stats(bool isFile, bool isDir, bool isSymlink, double size, double mode,
    ///                                   double atimeMs, double mtimeMs, double ctimeMs, double birthtimeMs)
    /// </summary>
    private void EmitStatsCtor(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Boolean, _types.Boolean, _types.Boolean,
             _types.Double, _types.Double,
             _types.Double, _types.Double, _types.Double, _types.Double]
        );

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // this._isFile = isFile
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, fileSystem.StatsIsFileField);

        // this._isDirectory = isDirectory
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, fileSystem.StatsIsDirField);

        // this._isSymbolicLink = isSymbolicLink
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, fileSystem.StatsIsSymlinkField);

        // this._size = size
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Stfld, fileSystem.StatsSizeField);

        // this._mode = mode
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stfld, fileSystem.StatsModeField);

        // this._atimeMs = atimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)6);
        il.Emit(OpCodes.Stfld, fileSystem.StatsAtimeMsField);

        // this._mtimeMs = mtimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)7);
        il.Emit(OpCodes.Stfld, fileSystem.StatsMtimeMsField);

        // this._ctimeMs = ctimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)8);
        il.Emit(OpCodes.Stfld, fileSystem.StatsCtimeMsField);

        // this._birthtimeMs = birthtimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)9);
        il.Emit(OpCodes.Stfld, fileSystem.StatsBirthtimeMsField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isFile() => _isFile;
    /// </summary>
    private void EmitStatsIsFileMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isFile",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsFile = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.StatsIsFileField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isDirectory() => _isDirectory;
    /// </summary>
    private void EmitStatsIsDirectoryMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isDirectory",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsDirectory = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.StatsIsDirField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isSymbolicLink() => _isSymbolicLink;
    /// </summary>
    private void EmitStatsIsSymbolicLinkMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isSymbolicLink",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsSymbolicLink = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.StatsIsSymlinkField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isBlockDevice() => false;
    /// </summary>
    private void EmitStatsIsBlockDeviceMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isBlockDevice",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsBlockDevice = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isCharacterDevice() => false;
    /// </summary>
    private void EmitStatsIsCharacterDeviceMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isCharacterDevice",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsCharacterDevice = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isFIFO() => false;
    /// </summary>
    private void EmitStatsIsFIFOMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isFIFO",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsFIFO = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isSocket() => false;
    /// </summary>
    private void EmitStatsIsSocketMethod(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var method = typeBuilder.DefineMethod(
            "isSocket",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        fileSystem.StatsIsSocket = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits size property getter.
    /// </summary>
    private void EmitStatsSizeProperty(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var property = typeBuilder.DefineProperty(
            "size",
            PropertyAttributes.None,
            _types.Double,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_size",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );
        fileSystem.StatsSizeGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.StatsSizeField);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits mode property getter.
    /// </summary>
    private void EmitStatsModeProperty(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        var property = typeBuilder.DefineProperty(
            "mode",
            PropertyAttributes.None,
            _types.Double,
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_mode",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );
        _ = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fileSystem.StatsModeField);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits timestamp properties: atimeMs, mtimeMs, ctimeMs, birthtimeMs
    /// </summary>
    private void EmitStatsTimestampProperties(TypeBuilder typeBuilder, EmittedFileSystemRuntime fileSystem)
    {
        // atimeMs
        EmitStatsTimestampProperty(typeBuilder, "atimeMs", fileSystem.StatsAtimeMsField);
        // mtimeMs
        EmitStatsTimestampProperty(typeBuilder, "mtimeMs", fileSystem.StatsMtimeMsField);
        // ctimeMs
        EmitStatsTimestampProperty(typeBuilder, "ctimeMs", fileSystem.StatsCtimeMsField);
        // birthtimeMs
        EmitStatsTimestampProperty(typeBuilder, "birthtimeMs", fileSystem.StatsBirthtimeMsField);
    }

    private void EmitStatsTimestampProperty(TypeBuilder typeBuilder, string name, FieldBuilder field)
    {
        var property = typeBuilder.DefineProperty(
            name,
            PropertyAttributes.None,
            _types.Double,
            null
        );

        var getter = typeBuilder.DefineMethod(
            $"get_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }
}
