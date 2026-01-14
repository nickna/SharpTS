using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'path' module.
/// </summary>
/// <remarks>
/// Provides runtime values for path manipulation functions.
/// Wraps .NET's System.IO.Path methods with Node.js-compatible behavior.
/// </remarks>
public static class PathModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the path module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Methods
            ["join"] = new BuiltInMethod("join", 0, int.MaxValue, Join),
            ["resolve"] = new BuiltInMethod("resolve", 0, int.MaxValue, Resolve),
            ["basename"] = new BuiltInMethod("basename", 1, 2, Basename),
            ["dirname"] = new BuiltInMethod("dirname", 1, 1, Dirname),
            ["extname"] = new BuiltInMethod("extname", 1, 1, Extname),
            ["normalize"] = new BuiltInMethod("normalize", 1, 1, Normalize),
            ["isAbsolute"] = new BuiltInMethod("isAbsolute", 1, 1, IsAbsolute),
            ["relative"] = new BuiltInMethod("relative", 2, 2, Relative),
            ["parse"] = new BuiltInMethod("parse", 1, 1, Parse),
            ["format"] = new BuiltInMethod("format", 1, 1, Format),

            // Properties
            ["sep"] = Path.DirectorySeparatorChar.ToString(),
            ["delimiter"] = Path.PathSeparator.ToString()
        };
    }

    private static object? Join(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var parts = args.Select(a => a?.ToString() ?? "").ToArray();
        return Path.Combine(parts);
    }

    private static object? Resolve(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return Directory.GetCurrentDirectory();

        var result = Directory.GetCurrentDirectory();
        foreach (var arg in args)
        {
            var part = arg?.ToString() ?? "";
            result = Path.Combine(result, part);
        }
        return Path.GetFullPath(result);
    }

    private static object? Basename(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var path = args[0]?.ToString() ?? "";
        var filename = Path.GetFileName(path);

        // Strip extension if provided
        if (args.Count >= 2)
        {
            var ext = args[1]?.ToString() ?? "";
            if (filename.EndsWith(ext, StringComparison.Ordinal))
            {
                filename = filename[..^ext.Length];
            }
        }

        return filename;
    }

    private static object? Dirname(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var path = args[0]?.ToString() ?? "";
        var dir = Path.GetDirectoryName(path);
        return dir ?? "/";
    }

    private static object? Extname(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var path = args[0]?.ToString() ?? "";
        return Path.GetExtension(path);
    }

    private static object? Normalize(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var path = args[0]?.ToString() ?? "";
        return Path.GetFullPath(path);
    }

    private static object? IsAbsolute(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return false;

        var path = args[0]?.ToString() ?? "";
        return Path.IsPathRooted(path);
    }

    private static object? Relative(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            return "";

        var from = args[0]?.ToString() ?? "";
        var to = args[1]?.ToString() ?? "";
        return Path.GetRelativePath(from, to);
    }

    private static object? Parse(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
        {
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["root"] = "",
                ["dir"] = "",
                ["base"] = "",
                ["name"] = "",
                ["ext"] = ""
            });
        }

        var path = args[0]?.ToString() ?? "";
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["root"] = Path.GetPathRoot(path) ?? "",
            ["dir"] = Path.GetDirectoryName(path) ?? "",
            ["base"] = Path.GetFileName(path),
            ["name"] = Path.GetFileNameWithoutExtension(path),
            ["ext"] = Path.GetExtension(path)
        });
    }

    private static object? Format(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var pathObj = args[0];
        if (pathObj is not SharpTSObject obj)
            return "";

        // Get properties from the path object
        var dir = GetStringProperty(obj, "dir");
        var root = GetStringProperty(obj, "root");
        var baseName = GetStringProperty(obj, "base");
        var name = GetStringProperty(obj, "name");
        var ext = GetStringProperty(obj, "ext");

        // If base is provided, use it; otherwise construct from name + ext
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = name + ext;
        }

        // If dir is provided, use it; otherwise use root
        var directory = !string.IsNullOrEmpty(dir) ? dir : root;

        if (string.IsNullOrEmpty(directory))
            return baseName;

        return Path.Combine(directory, baseName);
    }

    private static string GetStringProperty(SharpTSObject obj, string name)
    {
        var value = obj.GetProperty(name);
        return value?.ToString() ?? "";
    }
}
