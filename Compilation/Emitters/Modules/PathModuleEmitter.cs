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
        "sep", "delimiter"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return methodName switch
        {
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
}
