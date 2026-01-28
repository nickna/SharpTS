namespace SharpTS.Compilation;

/// <summary>
/// Static helper methods for POSIX and Win32 path operations.
/// Used by the compiled path module for platform-specific path handling.
/// </summary>
public static class PathHelpers
{
    #region POSIX Path Methods

    /// <summary>Joins path segments using POSIX separator (/).</summary>
    public static string PosixJoin(object?[] args)
    {
        if (args.Length == 0)
            return ".";

        var result = new List<string>();
        foreach (var arg in args)
        {
            var part = arg?.ToString() ?? "";
            if (string.IsNullOrEmpty(part))
                continue;
            // Normalize to forward slashes
            result.Add(part.Replace('\\', '/'));
        }
        return result.Count == 0 ? "." : string.Join("/", result);
    }

    /// <summary>Resolves path segments to an absolute POSIX path.</summary>
    public static string PosixResolve(object?[] args)
    {
        var result = Directory.GetCurrentDirectory().Replace('\\', '/');

        foreach (var arg in args)
        {
            var part = (arg?.ToString() ?? "").Replace('\\', '/');
            if (string.IsNullOrEmpty(part))
                continue;
            if (part.StartsWith('/'))
            {
                result = part;
            }
            else
            {
                result = result + "/" + part;
            }
        }
        return NormalizePosix(result);
    }

    /// <summary>Gets the basename of a POSIX path.</summary>
    public static string PosixBasename(string path, string? ext = null)
    {
        path = path.Replace('\\', '/');
        var lastSep = path.LastIndexOf('/');
        var filename = lastSep >= 0 ? path[(lastSep + 1)..] : path;

        if (!string.IsNullOrEmpty(ext) && filename.EndsWith(ext, StringComparison.Ordinal))
        {
            filename = filename[..^ext.Length];
        }

        return filename;
    }

    /// <summary>Gets the directory name of a POSIX path.</summary>
    public static string PosixDirname(string path)
    {
        path = path.Replace('\\', '/');
        var lastSep = path.LastIndexOf('/');
        if (lastSep < 0)
            return ".";
        if (lastSep == 0)
            return "/";
        return path[..lastSep];
    }

    /// <summary>Normalizes a POSIX path.</summary>
    public static string PosixNormalize(string path)
    {
        return NormalizePosix(path);
    }

    /// <summary>Checks if a POSIX path is absolute.</summary>
    public static bool PosixIsAbsolute(string path)
    {
        return path.StartsWith('/');
    }

    /// <summary>Gets the relative path between two POSIX paths.</summary>
    public static string PosixRelative(string from, string to)
    {
        from = NormalizePosix(from);
        to = NormalizePosix(to);
        return ComputeRelative(from, to, '/');
    }

    /// <summary>Parses a POSIX path into its components.</summary>
    public static Dictionary<string, object?> PosixParse(string path)
    {
        path = path.Replace('\\', '/');
        var root = path.StartsWith('/') ? "/" : "";
        var lastSep = path.LastIndexOf('/');
        var dir = lastSep > 0 ? path[..lastSep] : (lastSep == 0 ? "/" : "");
        var baseName = lastSep >= 0 ? path[(lastSep + 1)..] : path;
        var extIdx = baseName.LastIndexOf('.');
        var ext = extIdx > 0 ? baseName[extIdx..] : "";
        var name = extIdx > 0 ? baseName[..extIdx] : baseName;

        return new Dictionary<string, object?>
        {
            ["root"] = root,
            ["dir"] = dir,
            ["base"] = baseName,
            ["name"] = name,
            ["ext"] = ext
        };
    }

    /// <summary>Formats a parsed path object into a POSIX path string.</summary>
    public static string PosixFormat(object? pathObj)
    {
        if (pathObj == null)
            return "";

        var dir = GetProperty(pathObj, "dir");
        var root = GetProperty(pathObj, "root");
        var baseName = GetProperty(pathObj, "base");
        var name = GetProperty(pathObj, "name");
        var ext = GetProperty(pathObj, "ext");

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = name + ext;
        }

        var directory = !string.IsNullOrEmpty(dir) ? dir : root;

        if (string.IsNullOrEmpty(directory))
            return baseName ?? "";

        return directory + "/" + baseName;
    }

    private static string NormalizePosix(string path)
    {
        if (string.IsNullOrEmpty(path))
            return ".";

        path = path.Replace('\\', '/');
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

    /// <summary>Joins path segments using Win32 separator (\).</summary>
    public static string Win32Join(object?[] args)
    {
        if (args.Length == 0)
            return ".";

        var result = new List<string>();
        foreach (var arg in args)
        {
            var part = arg?.ToString() ?? "";
            if (string.IsNullOrEmpty(part))
                continue;
            // Normalize to backslashes
            result.Add(part.Replace('/', '\\'));
        }
        return result.Count == 0 ? "." : string.Join("\\", result);
    }

    /// <summary>Resolves path segments to an absolute Win32 path.</summary>
    public static string Win32Resolve(object?[] args)
    {
        var result = Directory.GetCurrentDirectory().Replace('/', '\\');

        foreach (var arg in args)
        {
            var part = (arg?.ToString() ?? "").Replace('/', '\\');
            if (string.IsNullOrEmpty(part))
                continue;
            if (IsWin32AbsoluteInternal(part))
            {
                result = part;
            }
            else
            {
                result = result + "\\" + part;
            }
        }
        return NormalizeWin32(result);
    }

    /// <summary>Gets the basename of a Win32 path.</summary>
    public static string Win32Basename(string path, string? ext = null)
    {
        var lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        var filename = lastSep >= 0 ? path[(lastSep + 1)..] : path;

        if (!string.IsNullOrEmpty(ext) && filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            filename = filename[..^ext.Length];
        }

        return filename;
    }

    /// <summary>Gets the directory name of a Win32 path.</summary>
    public static string Win32Dirname(string path)
    {
        var lastSep = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        if (lastSep < 0)
            return ".";

        // Handle drive root like C:\
        if (lastSep <= 2 && path.Length >= 2 && path[1] == ':')
            return path[..(lastSep + 1)];

        return path[..lastSep];
    }

    /// <summary>Normalizes a Win32 path.</summary>
    public static string Win32Normalize(string path)
    {
        return NormalizeWin32(path);
    }

    /// <summary>Checks if a Win32 path is absolute.</summary>
    public static bool Win32IsAbsolute(string path)
    {
        return IsWin32AbsoluteInternal(path);
    }

    /// <summary>Gets the relative path between two Win32 paths.</summary>
    public static string Win32Relative(string from, string to)
    {
        from = NormalizeWin32(from);
        to = NormalizeWin32(to);
        return ComputeRelative(from, to, '\\');
    }

    /// <summary>Parses a Win32 path into its components.</summary>
    public static Dictionary<string, object?> Win32Parse(string path)
    {
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

        return new Dictionary<string, object?>
        {
            ["root"] = root,
            ["dir"] = dir,
            ["base"] = baseName,
            ["name"] = name,
            ["ext"] = ext
        };
    }

    /// <summary>Formats a parsed path object into a Win32 path string.</summary>
    public static string Win32Format(object? pathObj)
    {
        if (pathObj == null)
            return "";

        var dir = GetProperty(pathObj, "dir");
        var root = GetProperty(pathObj, "root");
        var baseName = GetProperty(pathObj, "base");
        var name = GetProperty(pathObj, "name");
        var ext = GetProperty(pathObj, "ext");

        if (string.IsNullOrEmpty(baseName))
        {
            baseName = name + ext;
        }

        var directory = !string.IsNullOrEmpty(dir) ? dir : root;

        if (string.IsNullOrEmpty(directory))
            return baseName ?? "";

        return directory + "\\" + baseName;
    }

    private static bool IsWin32AbsoluteInternal(string path)
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

    private static string NormalizeWin32(string path)
    {
        if (string.IsNullOrEmpty(path))
            return ".";

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

    private static string ComputeRelative(string from, string to, char separator)
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

    private static string GetProperty(object obj, string name)
    {
        // Handle SharpTSObject
        if (obj is Runtime.Types.SharpTSObject tsObj)
        {
            return tsObj.GetProperty(name)?.ToString() ?? "";
        }

        // Handle Dictionary
        if (obj is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue(name, out var value) ? value?.ToString() ?? "" : "";
        }

        return "";
    }

    #endregion
}
