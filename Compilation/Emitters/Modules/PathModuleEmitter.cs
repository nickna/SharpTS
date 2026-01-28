using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'path' module.
/// All methods emit direct calls to System.IO.Path where possible.
/// </summary>
public sealed class PathModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "path";

    private static readonly string[] _exportedMembers =
    [
        "join", "resolve", "basename", "dirname", "extname",
        "normalize", "isAbsolute", "relative", "parse", "format",
        "sep", "delimiter", "posix", "win32"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return methodName switch
        {
            // Default platform path methods
            "join" => EmitJoin(emitter, arguments),
            "resolve" => EmitResolve(emitter, arguments),
            "basename" => EmitBasename(emitter, arguments),
            "dirname" => EmitDirname(emitter, arguments),
            "extname" => EmitExtname(emitter, arguments),
            "normalize" => EmitNormalize(emitter, arguments),
            "isAbsolute" => EmitIsAbsolute(emitter, arguments),
            "relative" => EmitRelative(emitter, arguments),
            "parse" => EmitParse(emitter, arguments),
            "format" => EmitFormat(emitter, arguments),

            // POSIX path methods (path.posix.*)
            "posix.join" => EmitPosixJoin(emitter, arguments),
            "posix.resolve" => EmitPosixResolve(emitter, arguments),
            "posix.basename" => EmitPosixBasename(emitter, arguments),
            "posix.dirname" => EmitPosixDirname(emitter, arguments),
            "posix.extname" => EmitExtname(emitter, arguments), // Same for all platforms
            "posix.normalize" => EmitPosixNormalize(emitter, arguments),
            "posix.isAbsolute" => EmitPosixIsAbsolute(emitter, arguments),
            "posix.relative" => EmitPosixRelative(emitter, arguments),
            "posix.parse" => EmitPosixParse(emitter, arguments),
            "posix.format" => EmitPosixFormat(emitter, arguments),

            // Win32 path methods (path.win32.*)
            "win32.join" => EmitWin32Join(emitter, arguments),
            "win32.resolve" => EmitWin32Resolve(emitter, arguments),
            "win32.basename" => EmitWin32Basename(emitter, arguments),
            "win32.dirname" => EmitWin32Dirname(emitter, arguments),
            "win32.extname" => EmitExtname(emitter, arguments), // Same for all platforms
            "win32.normalize" => EmitWin32Normalize(emitter, arguments),
            "win32.isAbsolute" => EmitWin32IsAbsolute(emitter, arguments),
            "win32.relative" => EmitWin32Relative(emitter, arguments),
            "win32.parse" => EmitWin32Parse(emitter, arguments),
            "win32.format" => EmitWin32Format(emitter, arguments),

            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "sep" => EmitSep(emitter),
            "delimiter" => EmitDelimiter(emitter),
            "posix" => EmitPosixObject(emitter),
            "win32" => EmitWin32Object(emitter),
            _ => false
        };
    }

    private static bool EmitJoin(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        if (arguments.Count == 1)
        {
            emitter.EmitExpression(arguments[0]);
            EmitToString(emitter, arguments[0]);
            return true;
        }

        // For multiple arguments, use Path.Combine iteratively
        // Path.Combine(Path.Combine(a, b), c)
        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);

        for (int i = 1; i < arguments.Count; i++)
        {
            emitter.EmitExpression(arguments[i]);
            EmitToString(emitter, arguments[i]);
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "Combine", ctx.Types.String, ctx.Types.String));
        }

        return true;
    }

    private static bool EmitResolve(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Return current working directory
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Directory, "GetCurrentDirectory"));
            return true;
        }

        // Start with cwd, then combine each argument
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Directory, "GetCurrentDirectory"));

        foreach (var arg in arguments)
        {
            emitter.EmitExpression(arg);
            EmitToString(emitter, arg);
            il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "Combine", ctx.Types.String, ctx.Types.String));
        }

        // Get the full path (normalizes and resolves . and ..)
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetFullPath", ctx.Types.String));

        return true;
    }

    private static bool EmitBasename(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetFileName", ctx.Types.String));

        // If extension is provided, strip it
        if (arguments.Count >= 2)
        {
            // Store filename temporarily
            var filenameLocal = il.DeclareLocal(ctx.Types.String);
            il.Emit(OpCodes.Stloc, filenameLocal);

            // Emit extension argument
            emitter.EmitExpression(arguments[1]);
            EmitToString(emitter, arguments[1]);
            var extLocal = il.DeclareLocal(ctx.Types.String);
            il.Emit(OpCodes.Stloc, extLocal);

            // Check if filename ends with extension
            il.Emit(OpCodes.Ldloc, filenameLocal);
            il.Emit(OpCodes.Ldloc, extLocal);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.String, "EndsWith", ctx.Types.String));

            var skipStrip = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, skipStrip);

            // Strip extension: filename.Substring(0, filename.Length - ext.Length)
            il.Emit(OpCodes.Ldloc, filenameLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, filenameLocal);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetProperty(ctx.Types.String, "Length").GetMethod!);
            il.Emit(OpCodes.Ldloc, extLocal);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetProperty(ctx.Types.String, "Length").GetMethod!);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.String, "Substring", ctx.Types.Int32, ctx.Types.Int32));
            il.Emit(OpCodes.Br, done);

            il.MarkLabel(skipStrip);
            il.Emit(OpCodes.Ldloc, filenameLocal);

            il.MarkLabel(done);
        }

        return true;
    }

    private static bool EmitDirname(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetDirectoryName", ctx.Types.String));

        // GetDirectoryName returns null for root paths, convert to "/"
        var notNull = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(notNull);
        il.MarkLabel(done);

        return true;
    }

    private static bool EmitExtname(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetExtension", ctx.Types.String));

        return true;
    }

    private static bool EmitNormalize(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetFullPath", ctx.Types.String));

        return true;
    }

    private static bool EmitIsAbsolute(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldc_I4_0); // false
            il.Emit(OpCodes.Box, ctx.Types.Boolean);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "IsPathRooted", ctx.Types.String));
        il.Emit(OpCodes.Box, ctx.Types.Boolean);

        return true;
    }

    private static bool EmitRelative(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        emitter.EmitExpression(arguments[1]);
        EmitToString(emitter, arguments[1]);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetRelativePath", ctx.Types.String, ctx.Types.String));

        return true;
    }

    private static bool EmitParse(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Return object with empty strings
            EmitParsedPathObject(emitter, "", "", "", "", "");
            return true;
        }

        // Store the path argument
        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        var pathLocal = il.DeclareLocal(ctx.Types.String);
        il.Emit(OpCodes.Stloc, pathLocal);

        // Create dictionary for the result
        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // root: Path.GetPathRoot(path) ?? ""
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "root");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetPathRoot", ctx.Types.String));
        EmitNullToEmpty(emitter);
        il.Emit(OpCodes.Call, addMethod);

        // dir: Path.GetDirectoryName(path) ?? ""
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "dir");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetDirectoryName", ctx.Types.String));
        EmitNullToEmpty(emitter);
        il.Emit(OpCodes.Call, addMethod);

        // base: Path.GetFileName(path)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "base");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetFileName", ctx.Types.String));
        il.Emit(OpCodes.Call, addMethod);

        // name: Path.GetFileNameWithoutExtension(path)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetFileNameWithoutExtension", ctx.Types.String));
        il.Emit(OpCodes.Call, addMethod);

        // ext: Path.GetExtension(path)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "ext");
        il.Emit(OpCodes.Ldloc, pathLocal);
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Path, "GetExtension", ctx.Types.String));
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);

        return true;
    }

    private static bool EmitFormat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        // path.format() is complex - it needs to read object properties
        // For simplicity, emit a runtime helper call
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Call, ctx.Runtime!.PathFormat);

        return true;
    }

    private static bool EmitSep(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Load Path.DirectorySeparatorChar and convert to string using the static Char.ToString method
        il.Emit(OpCodes.Ldsfld, ctx.Types.GetField(ctx.Types.Path, "DirectorySeparatorChar"));
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Char, "ToString", ctx.Types.Char));

        return true;
    }

    private static bool EmitDelimiter(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Load Path.PathSeparator and convert to string using the static Char.ToString method
        il.Emit(OpCodes.Ldsfld, ctx.Types.GetField(ctx.Types.Path, "PathSeparator"));
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(ctx.Types.Char, "ToString", ctx.Types.Char));

        return true;
    }

    private static bool EmitPosixObject(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Create dictionary with posix properties and methods
        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // Add sep = "/"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "sep");
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Callvirt, addMethod);

        // Add delimiter = ":"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "delimiter");
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, addMethod);

        // Add method wrappers using TSFunction
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "join", nameof(PathHelpers.PosixJoin), true);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "resolve", nameof(PathHelpers.PosixResolve), true);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "basename", nameof(PathHelpers.PosixBasename), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "dirname", nameof(PathHelpers.PosixDirname), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "extname", "Extname", false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "normalize", nameof(PathHelpers.PosixNormalize), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "isAbsolute", nameof(PathHelpers.PosixIsAbsolute), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "relative", nameof(PathHelpers.PosixRelative), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "parse", nameof(PathHelpers.PosixParse), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "format", nameof(PathHelpers.PosixFormat), false);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        return true;
    }

    private static bool EmitWin32Object(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Create dictionary with win32 properties and methods
        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // Add sep = "\\"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "sep");
        il.Emit(OpCodes.Ldstr, "\\");
        il.Emit(OpCodes.Callvirt, addMethod);

        // Add delimiter = ";"
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "delimiter");
        il.Emit(OpCodes.Ldstr, ";");
        il.Emit(OpCodes.Callvirt, addMethod);

        // Add method wrappers using TSFunction
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "join", nameof(PathHelpers.Win32Join), true);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "resolve", nameof(PathHelpers.Win32Resolve), true);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "basename", nameof(PathHelpers.Win32Basename), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "dirname", nameof(PathHelpers.Win32Dirname), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "extname", "Extname", false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "normalize", nameof(PathHelpers.Win32Normalize), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "isAbsolute", nameof(PathHelpers.Win32IsAbsolute), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "relative", nameof(PathHelpers.Win32Relative), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "parse", nameof(PathHelpers.Win32Parse), false);
        EmitPathSubObjectMethod(emitter, dictType, addMethod, "format", nameof(PathHelpers.Win32Format), false);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        return true;
    }

    private static void EmitPathSubObjectMethod(IEmitterContext emitter, Type dictType, MethodInfo addMethod,
        string methodName, string helperMethodName, bool isVarArgs)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, methodName);

        // Create TSFunction that wraps the helper method
        // For now, store null - the methods are handled via the nested call pattern in BuiltInModuleHandler
        // The actual method calls go through TryEmitMethodCall with "posix.join", "win32.join" etc.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, addMethod);
    }

    private static void EmitToString(IEmitterContext emitter, Expr expr)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Path arguments are typically strings, but we need to handle any type
        // Box value types first, then call ToString
        emitter.EmitBoxIfNeeded(expr);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.Object, "ToString"));
    }

    private static void EmitNullToEmpty(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // If null, replace with empty string
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

    private static void EmitParsedPathObject(IEmitterContext emitter, string root, string dir, string baseName, string name, string ext)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        void AddProperty(string key, string value)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldstr, value);
            il.Emit(OpCodes.Call, addMethod);
        }

        AddProperty("root", root);
        AddProperty("dir", dir);
        AddProperty("base", baseName);
        AddProperty("name", name);
        AddProperty("ext", ext);

        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
    }

    #region POSIX Path Methods

    private static bool EmitPosixJoin(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Build array of arguments
        EmitArgsArray(emitter, arguments);

        // Call PathHelpers.PosixJoin(args)
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixJoin))!);
        return true;
    }

    private static bool EmitPosixResolve(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitArgsArray(emitter, arguments);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixResolve))!);
        return true;
    }

    private static bool EmitPosixBasename(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            EmitToString(emitter, arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixBasename))!);
        return true;
    }

    private static bool EmitPosixDirname(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixDirname))!);
        return true;
    }

    private static bool EmitPosixNormalize(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixNormalize))!);
        return true;
    }

    private static bool EmitPosixIsAbsolute(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, ctx.Types.Boolean);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixIsAbsolute))!);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitPosixRelative(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        emitter.EmitExpression(arguments[1]);
        EmitToString(emitter, arguments[1]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixRelative))!);
        return true;
    }

    private static bool EmitPosixParse(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            EmitToString(emitter, arguments[0]);
        }

        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixParse))!);
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        return true;
    }

    private static bool EmitPosixFormat(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.PosixFormat))!);
        return true;
    }

    #endregion

    #region Win32 Path Methods

    private static bool EmitWin32Join(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitArgsArray(emitter, arguments);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Join))!);
        return true;
    }

    private static bool EmitWin32Resolve(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitArgsArray(emitter, arguments);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Resolve))!);
        return true;
    }

    private static bool EmitWin32Basename(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);

        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            EmitToString(emitter, arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Basename))!);
        return true;
    }

    private static bool EmitWin32Dirname(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Dirname))!);
        return true;
    }

    private static bool EmitWin32Normalize(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, ".");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Normalize))!);
        return true;
    }

    private static bool EmitWin32IsAbsolute(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, ctx.Types.Boolean);
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32IsAbsolute))!);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    private static bool EmitWin32Relative(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        EmitToString(emitter, arguments[0]);
        emitter.EmitExpression(arguments[1]);
        EmitToString(emitter, arguments[1]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Relative))!);
        return true;
    }

    private static bool EmitWin32Parse(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            EmitToString(emitter, arguments[0]);
        }

        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Parse))!);
        il.Emit(OpCodes.Call, ctx.Runtime!.CreateObject);
        return true;
    }

    private static bool EmitWin32Format(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "");
            return true;
        }

        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Call, typeof(PathHelpers).GetMethod(nameof(PathHelpers.Win32Format))!);
        return true;
    }

    #endregion

    #region Helper Methods

    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Create object?[] array
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);

        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    #endregion
}
