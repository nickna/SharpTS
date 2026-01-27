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
            ["delimiter"] = Path.PathSeparator.ToString(),

            // Platform-specific variants
            ["posix"] = GetPosixExports(),
            ["win32"] = GetWin32Exports()
        };
    }

    /// <summary>
    /// Gets the POSIX-style path module exports.
    /// </summary>
    private static SharpTSObject GetPosixExports()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["join"] = new BuiltInMethod("join", 0, int.MaxValue, PosixJoin),
            ["resolve"] = new BuiltInMethod("resolve", 0, int.MaxValue, PosixResolve),
            ["basename"] = new BuiltInMethod("basename", 1, 2, PosixBasename),
            ["dirname"] = new BuiltInMethod("dirname", 1, 1, PosixDirname),
            ["extname"] = new BuiltInMethod("extname", 1, 1, Extname), // Same for both
            ["normalize"] = new BuiltInMethod("normalize", 1, 1, PosixNormalize),
            ["isAbsolute"] = new BuiltInMethod("isAbsolute", 1, 1, PosixIsAbsolute),
            ["relative"] = new BuiltInMethod("relative", 2, 2, PosixRelative),
            ["parse"] = new BuiltInMethod("parse", 1, 1, PosixParse),
            ["format"] = new BuiltInMethod("format", 1, 1, PosixFormat),
            ["sep"] = "/",
            ["delimiter"] = ":"
        });
    }

    /// <summary>
    /// Gets the Windows-style path module exports.
    /// </summary>
    private static SharpTSObject GetWin32Exports()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["join"] = new BuiltInMethod("join", 0, int.MaxValue, Win32Join),
            ["resolve"] = new BuiltInMethod("resolve", 0, int.MaxValue, Win32Resolve),
            ["basename"] = new BuiltInMethod("basename", 1, 2, Win32Basename),
            ["dirname"] = new BuiltInMethod("dirname", 1, 1, Win32Dirname),
            ["extname"] = new BuiltInMethod("extname", 1, 1, Extname), // Same for both
            ["normalize"] = new BuiltInMethod("normalize", 1, 1, Win32Normalize),
            ["isAbsolute"] = new BuiltInMethod("isAbsolute", 1, 1, Win32IsAbsolute),
            ["relative"] = new BuiltInMethod("relative", 2, 2, Win32Relative),
            ["parse"] = new BuiltInMethod("parse", 1, 1, Win32Parse),
            ["format"] = new BuiltInMethod("format", 1, 1, Win32Format),
            ["sep"] = "\\",
            ["delimiter"] = ";"
        });
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

    #region POSIX Path Methods

    private static object? PosixJoin(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var parts = args.Select(a => a?.ToString() ?? "").ToArray();
        return JoinWithSeparator(parts, '/');
    }

    private static object? PosixResolve(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return Directory.GetCurrentDirectory().Replace('\\', '/');

        var result = Directory.GetCurrentDirectory();
        foreach (var arg in args)
        {
            var part = arg?.ToString() ?? "";
            if (part.StartsWith('/'))
            {
                result = part;
            }
            else
            {
                result = result + "/" + part;
            }
        }
        return NormalizePosixPath(result);
    }

    private static object? PosixBasename(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var path = args[0]?.ToString() ?? "";
        // Use POSIX separator only
        var lastSep = path.LastIndexOf('/');
        var filename = lastSep >= 0 ? path[(lastSep + 1)..] : path;

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

    private static object? PosixDirname(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var path = args[0]?.ToString() ?? "";
        var lastSep = path.LastIndexOf('/');
        if (lastSep < 0)
            return ".";
        if (lastSep == 0)
            return "/";
        return path[..lastSep];
    }

    private static object? PosixNormalize(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var path = args[0]?.ToString() ?? "";
        return NormalizePosixPath(path);
    }

    private static object? PosixIsAbsolute(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return false;

        var path = args[0]?.ToString() ?? "";
        return path.StartsWith('/');
    }

    private static object? PosixRelative(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            return "";

        var from = NormalizePosixPath(args[0]?.ToString() ?? "");
        var to = NormalizePosixPath(args[1]?.ToString() ?? "");

        return ComputeRelativePath(from, to, '/');
    }

    private static object? PosixParse(Interp interpreter, object? receiver, List<object?> args)
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
        var root = path.StartsWith('/') ? "/" : "";
        var lastSep = path.LastIndexOf('/');
        var dir = lastSep > 0 ? path[..lastSep] : (lastSep == 0 ? "/" : "");
        var baseName = lastSep >= 0 ? path[(lastSep + 1)..] : path;
        var extIdx = baseName.LastIndexOf('.');
        var ext = extIdx > 0 ? baseName[extIdx..] : "";
        var name = extIdx > 0 ? baseName[..extIdx] : baseName;

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["root"] = root,
            ["dir"] = dir,
            ["base"] = baseName,
            ["name"] = name,
            ["ext"] = ext
        });
    }

    private static object? PosixFormat(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var pathObj = args[0];
        if (pathObj is not SharpTSObject obj)
            return "";

        var dir = GetStringProperty(obj, "dir");
        var root = GetStringProperty(obj, "root");
        var baseName = GetStringProperty(obj, "base");
        var name = GetStringProperty(obj, "name");
        var ext = GetStringProperty(obj, "ext");

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = name + ext;
        }

        var directory = !string.IsNullOrEmpty(dir) ? dir : root;

        if (string.IsNullOrEmpty(directory))
            return baseName;

        return directory + "/" + baseName;
    }

    private static string NormalizePosixPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return ".";

        var isAbsolute = path.StartsWith('/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();

        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0 && stack[^1] != "..")
                    stack.RemoveAt(stack.Count - 1);
                else if (!isAbsolute)
                    stack.Add("..");
            }
            else
            {
                stack.Add(part);
            }
        }

        var result = string.Join("/", stack);
        if (isAbsolute)
            result = "/" + result;
        return string.IsNullOrEmpty(result) ? (isAbsolute ? "/" : ".") : result;
    }

    #endregion

    #region Win32 Path Methods

    private static object? Win32Join(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var parts = args.Select(a => a?.ToString() ?? "").ToArray();
        return JoinWithSeparator(parts, '\\');
    }

    private static object? Win32Resolve(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return Directory.GetCurrentDirectory().Replace('/', '\\');

        var result = Directory.GetCurrentDirectory().Replace('/', '\\');
        foreach (var arg in args)
        {
            var part = (arg?.ToString() ?? "").Replace('/', '\\');
            if (IsWin32Absolute(part))
            {
                result = part;
            }
            else
            {
                result = result + "\\" + part;
            }
        }
        return NormalizeWin32Path(result);
    }

    private static object? Win32Basename(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var path = args[0]?.ToString() ?? "";
        // Handle both separators for Win32
        var lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        var filename = lastSep >= 0 ? path[(lastSep + 1)..] : path;

        if (args.Count >= 2)
        {
            var ext = args[1]?.ToString() ?? "";
            if (filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                filename = filename[..^ext.Length];
            }
        }

        return filename;
    }

    private static object? Win32Dirname(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var path = args[0]?.ToString() ?? "";
        var lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        if (lastSep < 0)
            return ".";

        // Handle drive root like C:\
        if (lastSep <= 2 && path.Length >= 2 && path[1] == ':')
            return path[..(lastSep + 1)];

        return path[..lastSep];
    }

    private static object? Win32Normalize(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return ".";

        var path = args[0]?.ToString() ?? "";
        return NormalizeWin32Path(path);
    }

    private static object? Win32IsAbsolute(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return false;

        var path = args[0]?.ToString() ?? "";
        return IsWin32Absolute(path);
    }

    private static object? Win32Relative(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            return "";

        var from = NormalizeWin32Path(args[0]?.ToString() ?? "");
        var to = NormalizeWin32Path(args[1]?.ToString() ?? "");

        return ComputeRelativePath(from, to, '\\');
    }

    private static object? Win32Parse(Interp interpreter, object? receiver, List<object?> args)
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
        var root = "";
        if (path.Length >= 2 && path[1] == ':')
        {
            root = path.Length >= 3 && (path[2] == '\\' || path[2] == '/') ? path[..3] : path[..2];
        }
        else if (path.StartsWith("\\\\") || path.StartsWith("//"))
        {
            root = "\\\\";
        }

        var lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        var dir = lastSep >= 0 ? path[..lastSep] : "";
        var baseName = lastSep >= 0 ? path[(lastSep + 1)..] : path;
        var extIdx = baseName.LastIndexOf('.');
        var ext = extIdx > 0 ? baseName[extIdx..] : "";
        var name = extIdx > 0 ? baseName[..extIdx] : baseName;

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["root"] = root,
            ["dir"] = dir,
            ["base"] = baseName,
            ["name"] = name,
            ["ext"] = ext
        });
    }

    private static object? Win32Format(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var pathObj = args[0];
        if (pathObj is not SharpTSObject obj)
            return "";

        var dir = GetStringProperty(obj, "dir");
        var root = GetStringProperty(obj, "root");
        var baseName = GetStringProperty(obj, "base");
        var name = GetStringProperty(obj, "name");
        var ext = GetStringProperty(obj, "ext");

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = name + ext;
        }

        var directory = !string.IsNullOrEmpty(dir) ? dir : root;

        if (string.IsNullOrEmpty(directory))
            return baseName;

        return directory + "\\" + baseName;
    }

    private static bool IsWin32Absolute(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        // Drive letter like C:\ or C:/
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return true;
        // UNC path like \\server or //server
        if (path.Length >= 2 && ((path[0] == '\\' && path[1] == '\\') || (path[0] == '/' && path[1] == '/')))
            return true;
        return false;
    }

    private static string NormalizeWin32Path(string path)
    {
        if (string.IsNullOrEmpty(path))
            return ".";

        // Replace forward slashes with backslashes
        path = path.Replace('/', '\\');

        var root = "";
        var startIdx = 0;

        // Extract root
        if (path.Length >= 2 && path[1] == ':')
        {
            root = path.Length >= 3 && path[2] == '\\' ? path[..3] : path[..2] + "\\";
            startIdx = root.Length;
        }
        else if (path.StartsWith("\\\\"))
        {
            root = "\\\\";
            startIdx = 2;
        }

        var parts = path[startIdx..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();

        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0 && stack[^1] != "..")
                    stack.RemoveAt(stack.Count - 1);
                else if (string.IsNullOrEmpty(root))
                    stack.Add("..");
            }
            else
            {
                stack.Add(part);
            }
        }

        var result = root + string.Join("\\", stack);
        return string.IsNullOrEmpty(result) ? "." : result;
    }

    #endregion

    #region Shared Helpers

    private static string JoinWithSeparator(string[] parts, char separator)
    {
        var result = new List<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;
            // Normalize separators
            var normalized = separator == '/' ? part.Replace('\\', '/') : part.Replace('/', '\\');
            result.Add(normalized);
        }
        return string.Join(separator.ToString(), result);
    }

    private static string ComputeRelativePath(string from, string to, char separator)
    {
        var fromParts = from.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var toParts = to.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        // Find common prefix
        var commonLength = 0;
        var minLength = Math.Min(fromParts.Length, toParts.Length);
        for (int i = 0; i < minLength; i++)
        {
            var comparison = separator == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(fromParts[i], toParts[i], comparison))
                break;
            commonLength++;
        }

        // Build relative path
        var result = new List<string>();

        // Add ".." for each remaining part in 'from'
        for (int i = commonLength; i < fromParts.Length; i++)
        {
            result.Add("..");
        }

        // Add remaining parts from 'to'
        for (int i = commonLength; i < toParts.Length; i++)
        {
            result.Add(toParts[i]);
        }

        return result.Count == 0 ? "." : string.Join(separator.ToString(), result);
    }

    #endregion
}
