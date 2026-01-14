using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits path module helper methods.
    /// </summary>
    private void EmitPathModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // PathFormat is used by the format wrapper and PathModuleEmitter
        EmitPathFormat(typeBuilder, runtime);
    }

    /// <summary>Emits: public static string PathJoin(object[] args)</summary>
    private void EmitPathJoin(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathJoin",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // If no args, return "."
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Start with first arg
        var resultLocal = il.DeclareLocal(_types.String);
        var indexLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Loop from index 1
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // result = Path.Combine(result, args[i].ToString())
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "Combine", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "join", method);
    }

    /// <summary>Emits: public static string PathResolve(object[] args)</summary>
    private void EmitPathResolve(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathResolve",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Start with current working directory
        var resultLocal = il.DeclareLocal(_types.String);
        var indexLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Directory, "GetCurrentDirectory"));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Loop through all args
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // result = Path.Combine(result, args[i].ToString())
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "Combine", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, resultLocal);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Return GetFullPath to resolve . and ..
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFullPath", _types.String));
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "resolve", method);
    }

    /// <summary>Emits: public static string PathBasename(object[] args)</summary>
    private void EmitPathBasename(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathBasename",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // If no args, return ""
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Get filename
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));

        // If second arg (ext), strip it
        var noExt = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, noExt);

        // Store filename
        var filenameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, filenameLocal);

        // Get extension to strip
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        var extLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, extLocal);

        // Check if filename ends with ext
        il.Emit(OpCodes.Ldloc, filenameLocal);
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "EndsWith", _types.String));

        var skipStrip = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipStrip);

        // Strip: filename.Substring(0, filename.Length - ext.Length)
        il.Emit(OpCodes.Ldloc, filenameLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, filenameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(skipStrip);
        il.Emit(OpCodes.Ldloc, filenameLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(noExt);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "basename", method);
    }

    /// <summary>Emits: public static string PathDirname(object[] args)</summary>
    private void EmitPathDirname(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathDirname",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // If no args, return "."
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Get directory name
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetDirectoryName", _types.String));

        // If null, return "/"
        var notNull = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(notNull);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "dirname", method);
    }

    /// <summary>Emits: public static string PathExtname(object[] args)</summary>
    private void EmitPathExtname(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathExtname",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // If no args, return ""
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetExtension", _types.String));
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "extname", method);
    }

    /// <summary>Emits: public static string PathNormalize(object[] args)</summary>
    private void EmitPathNormalize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathNormalize",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // If no args, return "."
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFullPath", _types.String));
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "normalize", method);
    }

    /// <summary>Emits: public static object PathIsAbsolute(object[] args)</summary>
    private void EmitPathIsAbsolute(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathIsAbsolute",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // If no args, return false
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "IsPathRooted", _types.String));
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "isAbsolute", method);
    }

    /// <summary>Emits: public static string PathRelative(object[] args)</summary>
    private void EmitPathRelative(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathRelative",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Need at least 2 args
        var hasEnoughArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, hasEnoughArgs);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasEnoughArgs);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);

        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetRelativePath", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "relative", method);
    }

    /// <summary>Emits: public static object PathParse(object[] args)</summary>
    private void EmitPathParse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathParse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Get path argument
        var pathLocal = il.DeclareLocal(_types.String);
        var hasArgs = il.DefineLabel();
        var storePathLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArgs);

        // No args: use empty string
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, storePathLabel);

        // Has args: extract first arg
        il.MarkLabel(hasArgs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);

        il.MarkLabel(storePathLabel);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Create dictionary
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // root
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "root");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetPathRoot", _types.String));
        EmitNullToEmptyString(il);
        il.Emit(OpCodes.Call, addMethod);

        // dir
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "dir");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetDirectoryName", _types.String));
        EmitNullToEmptyString(il);
        il.Emit(OpCodes.Call, addMethod);

        // base
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "base");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // name
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileNameWithoutExtension", _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // ext
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "ext");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetExtension", _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "parse", method);
    }

    /// <summary>
    /// Emits wrapper methods for path module to support named imports.
    /// Each wrapper takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitPathModulePropertyWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // basename(path, ext?) -> string
        EmitPathMethodWrapper(typeBuilder, runtime, "basename", 2, il =>
        {
            // Get filename: Path.GetFileName(Stringify(arg0))
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));

            // If arg1 (ext) is not null and filename ends with it, strip it
            var doneLabel = il.DefineLabel();
            var noExtLabel = il.DefineLabel();
            var filenameLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Stloc, filenameLocal);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, noExtLabel);

            // Has ext argument
            var extLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Stloc, extLocal);

            // Check if filename ends with ext
            il.Emit(OpCodes.Ldloc, filenameLocal);
            il.Emit(OpCodes.Ldloc, extLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "EndsWith", _types.String));
            il.Emit(OpCodes.Brfalse, noExtLabel);

            // Strip: filename.Substring(0, filename.Length - ext.Length)
            il.Emit(OpCodes.Ldloc, filenameLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, filenameLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
            il.Emit(OpCodes.Ldloc, extLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(noExtLabel);
            il.Emit(OpCodes.Ldloc, filenameLocal);

            il.MarkLabel(doneLabel);
        });

        // dirname(path) -> string
        EmitPathMethodWrapper(typeBuilder, runtime, "dirname", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetDirectoryName", _types.String));

            // If null, return "/"
            var notNull = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, notNull);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "/");
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(notNull);
            il.MarkLabel(done);
        });

        // extname(path) -> string
        EmitPathMethodWrapper(typeBuilder, runtime, "extname", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetExtension", _types.String));
        });

        // normalize(path) -> string
        EmitPathMethodWrapper(typeBuilder, runtime, "normalize", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFullPath", _types.String));
        });

        // isAbsolute(path) -> bool
        EmitPathMethodWrapper(typeBuilder, runtime, "isAbsolute", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "IsPathRooted", _types.String));
            il.Emit(OpCodes.Box, _types.Boolean);
        });

        // relative(from, to) -> string
        EmitPathMethodWrapper(typeBuilder, runtime, "relative", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetRelativePath", _types.String, _types.String));
        });

        // join(path1, path2, ...) -> string - max 8 args
        EmitPathJoinWrapper(typeBuilder, runtime);

        // resolve(path1, path2, ...) -> string - max 8 args
        EmitPathResolveWrapper(typeBuilder, runtime);

        // parse(path) -> object
        EmitPathParseWrapper(typeBuilder, runtime);

        // format(pathObject) -> string
        EmitPathMethodWrapper(typeBuilder, runtime, "format", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PathFormat);
        });

        // sep - directory separator (property but callable)
        EmitPathMethodWrapper(typeBuilder, runtime, "sep", 0, il =>
        {
            il.Emit(OpCodes.Ldsfld, _types.GetField(_types.Path, "DirectorySeparatorChar"));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));
        });

        // delimiter - path separator (property but callable)
        EmitPathMethodWrapper(typeBuilder, runtime, "delimiter", 0, il =>
        {
            il.Emit(OpCodes.Ldsfld, _types.GetField(_types.Path, "PathSeparator"));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));
        });
    }

    private void EmitPathMethodWrapper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator> emitCall)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            $"Path_{methodName}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();
        emitCall(il);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", methodName, method);
    }

    private void EmitPathJoinWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // join takes variable args, support up to 8
        var method = typeBuilder.DefineMethod(
            "Path_join_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object,
             _types.Object, _types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);

        // Start with empty string
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, resultLocal);

        // For each arg, if not null, combine
        for (int i = 0; i < 8; i++)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "Combine", _types.String, _types.String));
            il.Emit(OpCodes.Stloc, resultLocal);

            il.MarkLabel(skipLabel);
        }

        // If result is empty, return "."
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, ".");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "join", method);
    }

    private void EmitPathResolveWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // resolve takes variable args, support up to 8
        var method = typeBuilder.DefineMethod(
            "Path_resolve_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object,
             _types.Object, _types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.String);

        // Start with current directory
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Directory, "GetCurrentDirectory"));
        il.Emit(OpCodes.Stloc, resultLocal);

        // For each arg, if not null, combine
        for (int i = 0; i < 8; i++)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, runtime.Stringify);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "Combine", _types.String, _types.String));
            il.Emit(OpCodes.Stloc, resultLocal);

            il.MarkLabel(skipLabel);
        }

        // Return GetFullPath to resolve . and ..
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFullPath", _types.String));
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "resolve", method);
    }

    private void EmitPathParseWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Path_parse_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // Get path string
        var pathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Create dictionary
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var addMethod = _types.GetMethod(dictType, "Add", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // root
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "root");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetPathRoot", _types.String));
        EmitNullToEmptyString(il);
        il.Emit(OpCodes.Call, addMethod);

        // dir
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "dir");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetDirectoryName", _types.String));
        EmitNullToEmptyString(il);
        il.Emit(OpCodes.Call, addMethod);

        // base
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "base");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileName", _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // name
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetFileNameWithoutExtension", _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // ext
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "ext");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Path, "GetExtension", _types.String));
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("path", "parse", method);
    }

    private void EmitNullToEmptyString(ILGenerator il)
    {
        var notNull = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(notNull);
        il.MarkLabel(done);
    }

    /// <summary>
    /// Emits: public static string PathFormat(object pathObject)
    /// Implements path.format() which reconstructs a path from a parsed path object.
    /// </summary>
    private void EmitPathFormat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PathFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.PathFormat = method;

        var il = method.GetILGenerator();

        // Local variables
        var dirLocal = il.DeclareLocal(_types.String);      // 0: dir
        var rootLocal = il.DeclareLocal(_types.String);     // 1: root
        var baseLocal = il.DeclareLocal(_types.String);     // 2: base
        var nameLocal = il.DeclareLocal(_types.String);     // 3: name
        var extLocal = il.DeclareLocal(_types.String);      // 4: ext
        var resultLocal = il.DeclareLocal(_types.String);   // 5: result

        // Get the GetProperty method for extracting properties from the object
        var getPropertyMethod = runtime.GetProperty;

        // Helper to emit: string prop = GetProperty(pathObject, "propName")?.ToString() ?? ""
        void EmitGetStringProperty(string propName, LocalBuilder local)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, propName);
            il.Emit(OpCodes.Call, getPropertyMethod);

            // Convert to string
            var notNull = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, notNull);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(notNull);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
            il.MarkLabel(done);
            il.Emit(OpCodes.Stloc, local);
        }

        // Extract all properties
        EmitGetStringProperty("dir", dirLocal);
        EmitGetStringProperty("root", rootLocal);
        EmitGetStringProperty("base", baseLocal);
        EmitGetStringProperty("name", nameLocal);
        EmitGetStringProperty("ext", extLocal);

        // Algorithm from Node.js path.format():
        // 1. If base is provided, use dir + sep + base
        // 2. Otherwise, use dir + sep + name + ext
        // 3. If dir is empty but root is set, prepend root

        var hasBase = il.DefineLabel();
        var buildFromParts = il.DefineLabel();
        var checkDir = il.DefineLabel();
        var addSepAndBase = il.DefineLabel();
        var done2 = il.DefineLabel();

        // Check if base has a value
        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasBase);

        // base is empty, build from name + ext
        il.MarkLabel(buildFromParts);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, baseLocal);
        il.Emit(OpCodes.Br, checkDir);

        il.MarkLabel(hasBase);

        // Check if dir has a value
        il.MarkLabel(checkDir);
        il.Emit(OpCodes.Ldloc, dirLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, addSepAndBase);

        // dir is empty, check if root is set
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, done2);

        // Return root + base
        il.Emit(OpCodes.Ldloc, rootLocal);
        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        // dir has a value, return dir + sep + base
        il.MarkLabel(addSepAndBase);
        il.Emit(OpCodes.Ldloc, dirLocal);

        // Get separator as string using static Char.ToString(char)
        il.Emit(OpCodes.Ldsfld, _types.GetField(_types.Path, "DirectorySeparatorChar"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.Char));

        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        // Return just base (no dir, no root)
        il.MarkLabel(done2);
        il.Emit(OpCodes.Ldloc, baseLocal);
        il.Emit(OpCodes.Ret);
    }
}
