using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Stats class for fs.stat() and related methods.
/// Provides Node.js-compatible Stats object with methods like isFile(), isDirectory(), etc.
/// </summary>
public partial class RuntimeEmitter
{
    // Fields for $Stats class
    private FieldBuilder _statsIsFileField = null!;
    private FieldBuilder _statsIsDirField = null!;
    private FieldBuilder _statsIsSymlinkField = null!;
    private FieldBuilder _statsSizeField = null!;
    private FieldBuilder _statsModeField = null!;
    private FieldBuilder _statsAtimeMsField = null!;
    private FieldBuilder _statsMtimeMsField = null!;
    private FieldBuilder _statsCtimeMsField = null!;
    private FieldBuilder _statsBirthtimeMsField = null!;

    /// <summary>
    /// Emits the $Stats class with Node.js-compatible API.
    /// </summary>
    private void EmitStatsClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Stats
        var typeBuilder = moduleBuilder.DefineType(
            "$Stats",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Define fields
        _statsIsFileField = typeBuilder.DefineField("_isFile", _types.Boolean, FieldAttributes.Private);
        _statsIsDirField = typeBuilder.DefineField("_isDirectory", _types.Boolean, FieldAttributes.Private);
        _statsIsSymlinkField = typeBuilder.DefineField("_isSymbolicLink", _types.Boolean, FieldAttributes.Private);
        _statsSizeField = typeBuilder.DefineField("_size", _types.Double, FieldAttributes.Private);
        _statsModeField = typeBuilder.DefineField("_mode", _types.Double, FieldAttributes.Private);
        _statsAtimeMsField = typeBuilder.DefineField("_atimeMs", _types.Double, FieldAttributes.Private);
        _statsMtimeMsField = typeBuilder.DefineField("_mtimeMs", _types.Double, FieldAttributes.Private);
        _statsCtimeMsField = typeBuilder.DefineField("_ctimeMs", _types.Double, FieldAttributes.Private);
        _statsBirthtimeMsField = typeBuilder.DefineField("_birthtimeMs", _types.Double, FieldAttributes.Private);

        // Constructor
        EmitStatsCtor(typeBuilder, runtime);

        // Methods that match Node.js Stats API
        EmitStatsIsFileMethod(typeBuilder, runtime);
        EmitStatsIsDirectoryMethod(typeBuilder, runtime);
        EmitStatsIsSymbolicLinkMethod(typeBuilder, runtime);
        EmitStatsIsBlockDeviceMethod(typeBuilder, runtime);
        EmitStatsIsCharacterDeviceMethod(typeBuilder, runtime);
        EmitStatsIsFIFOMethod(typeBuilder, runtime);
        EmitStatsIsSocketMethod(typeBuilder, runtime);

        // Properties
        EmitStatsSizeProperty(typeBuilder, runtime);
        EmitStatsModeProperty(typeBuilder, runtime);
        EmitStatsTimestampProperties(typeBuilder, runtime);

        // Finalize the type
        runtime.StatsType = typeBuilder.CreateType()!;
        runtime.StatsCtor = runtime.StatsType.GetConstructor([
            _types.Boolean, _types.Boolean, _types.Boolean,
            _types.Double, _types.Double,
            _types.Double, _types.Double, _types.Double, _types.Double
        ])!;
    }

    /// <summary>
    /// Emits constructor: public $Stats(bool isFile, bool isDir, bool isSymlink, double size, double mode,
    ///                                   double atimeMs, double mtimeMs, double ctimeMs, double birthtimeMs)
    /// </summary>
    private void EmitStatsCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // this._isFile = isFile
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _statsIsFileField);

        // this._isDirectory = isDirectory
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _statsIsDirField);

        // this._isSymbolicLink = isSymbolicLink
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, _statsIsSymlinkField);

        // this._size = size
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Stfld, _statsSizeField);

        // this._mode = mode
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stfld, _statsModeField);

        // this._atimeMs = atimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)6);
        il.Emit(OpCodes.Stfld, _statsAtimeMsField);

        // this._mtimeMs = mtimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)7);
        il.Emit(OpCodes.Stfld, _statsMtimeMsField);

        // this._ctimeMs = ctimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)8);
        il.Emit(OpCodes.Stfld, _statsCtimeMsField);

        // this._birthtimeMs = birthtimeMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)9);
        il.Emit(OpCodes.Stfld, _statsBirthtimeMsField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isFile() => _isFile;
    /// </summary>
    private void EmitStatsIsFileMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isFile",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsFile = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statsIsFileField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isDirectory() => _isDirectory;
    /// </summary>
    private void EmitStatsIsDirectoryMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isDirectory",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsDirectory = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statsIsDirField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isSymbolicLink() => _isSymbolicLink;
    /// </summary>
    private void EmitStatsIsSymbolicLinkMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isSymbolicLink",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsSymbolicLink = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statsIsSymlinkField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isBlockDevice() => false;
    /// </summary>
    private void EmitStatsIsBlockDeviceMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isBlockDevice",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsBlockDevice = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isCharacterDevice() => false;
    /// </summary>
    private void EmitStatsIsCharacterDeviceMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isCharacterDevice",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsCharacterDevice = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isFIFO() => false;
    /// </summary>
    private void EmitStatsIsFIFOMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isFIFO",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsFIFO = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool isSocket() => false;
    /// </summary>
    private void EmitStatsIsSocketMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "isSocket",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.StatsIsSocket = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits size property getter.
    /// </summary>
    private void EmitStatsSizeProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.StatsSizeGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statsSizeField);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits mode property getter.
    /// </summary>
    private void EmitStatsModeProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        runtime.StatsModeGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statsModeField);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits timestamp properties: atimeMs, mtimeMs, ctimeMs, birthtimeMs
    /// </summary>
    private void EmitStatsTimestampProperties(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // atimeMs
        EmitStatsTimestampProperty(typeBuilder, "atimeMs", _statsAtimeMsField);
        // mtimeMs
        EmitStatsTimestampProperty(typeBuilder, "mtimeMs", _statsMtimeMsField);
        // ctimeMs
        EmitStatsTimestampProperty(typeBuilder, "ctimeMs", _statsCtimeMsField);
        // birthtimeMs
        EmitStatsTimestampProperty(typeBuilder, "birthtimeMs", _statsBirthtimeMsField);
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
