using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'url' module.
/// </summary>
/// <remarks>
/// Provides WHATWG URL API and legacy url.parse/url.format functions.
/// </remarks>
public static class UrlModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the url module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // WHATWG URL API classes
            ["URL"] = new UrlConstructor(),
            ["URLSearchParams"] = new UrlSearchParamsConstructor(),
            // Legacy functions
            ["parse"] = new BuiltInMethod("parse", 1, 3, Parse),
            ["format"] = new BuiltInMethod("format", 1, 1, Format),
            ["resolve"] = new BuiltInMethod("resolve", 2, 2, Resolve)
        };
    }

    /// <summary>
    /// Legacy url.parse() - parses a URL string into an object.
    /// </summary>
    private static object? Parse(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
            return new SharpTSObject(new Dictionary<string, object?>());

        var urlString = args[0].ToString()!;

        try
        {
            var uri = new Uri(urlString, UriKind.Absolute);
            return CreateUrlObject(uri);
        }
        catch
        {
            // Try parsing as relative URL
            try
            {
                var uri = new Uri("http://localhost/" + urlString.TrimStart('/'), UriKind.Absolute);
                return CreateUrlObject(uri, isRelative: true, originalPath: urlString);
            }
            catch
            {
                // Return partial object for invalid URLs
                return new SharpTSObject(new Dictionary<string, object?>
                {
                    ["href"] = urlString,
                    ["path"] = urlString
                });
            }
        }
    }

    private static SharpTSObject CreateUrlObject(Uri uri, bool isRelative = false, string? originalPath = null)
    {
        var result = new Dictionary<string, object?>
        {
            ["protocol"] = uri.Scheme + ":",
            ["slashes"] = true,
            ["auth"] = string.IsNullOrEmpty(uri.UserInfo) ? null : uri.UserInfo,
            ["host"] = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}",
            ["port"] = uri.IsDefaultPort ? null : uri.Port.ToString(),
            ["hostname"] = uri.Host,
            ["hash"] = string.IsNullOrEmpty(uri.Fragment) ? null : uri.Fragment,
            ["search"] = string.IsNullOrEmpty(uri.Query) ? null : uri.Query,
            ["query"] = string.IsNullOrEmpty(uri.Query) ? null : uri.Query.TrimStart('?'),
            ["pathname"] = uri.AbsolutePath,
            ["path"] = uri.PathAndQuery,
            ["href"] = uri.AbsoluteUri
        };

        if (isRelative && originalPath != null)
        {
            result["protocol"] = null;
            result["slashes"] = null;
            result["host"] = null;
            result["hostname"] = null;
            result["path"] = originalPath;
            result["pathname"] = originalPath.Split('?')[0];
            result["href"] = originalPath;
        }

        return new SharpTSObject(result);
    }

    /// <summary>
    /// Legacy url.format() - formats a URL object into a string.
    /// </summary>
    private static object? Format(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
            return "";

        if (args[0] is SharpTSURL url)
            return url.Href;

        if (args[0] is string s)
            return s;

        if (args[0] is SharpTSObject obj)
        {
            // Build URL from object parts
            var protocol = obj.Fields.GetValueOrDefault("protocol")?.ToString() ?? "";
            var hostname = obj.Fields.GetValueOrDefault("hostname")?.ToString() ??
                          obj.Fields.GetValueOrDefault("host")?.ToString() ?? "";
            var port = obj.Fields.GetValueOrDefault("port")?.ToString();
            var pathname = obj.Fields.GetValueOrDefault("pathname")?.ToString() ?? "/";
            var search = obj.Fields.GetValueOrDefault("search")?.ToString() ?? "";
            var hash = obj.Fields.GetValueOrDefault("hash")?.ToString() ?? "";

            var host = !string.IsNullOrEmpty(port) ? $"{hostname}:{port}" : hostname;

            var slashes = obj.Fields.GetValueOrDefault("slashes");
            var slashStr = slashes is true || (protocol.Length > 0 && slashes is not false) ? "//" : "";

            return $"{protocol}{slashStr}{host}{pathname}{search}{hash}";
        }

        return args[0].ToString() ?? "";
    }

    /// <summary>
    /// Legacy url.resolve() - resolves a target URL relative to a base URL.
    /// </summary>
    private static object? Resolve(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            return args.Count > 0 ? args[0]?.ToString() : "";

        var from = args[0]?.ToString() ?? "";
        var to = args[1]?.ToString() ?? "";

        try
        {
            var baseUri = new Uri(from, UriKind.Absolute);
            var resolvedUri = new Uri(baseUri, to);
            return resolvedUri.AbsoluteUri;
        }
        catch
        {
            // If base isn't absolute, try best effort
            return to.StartsWith('/') ? to : $"{from.TrimEnd('/')}/{to}";
        }
    }
}

/// <summary>
/// Constructor class for URL (used as `new URL(...)`).
/// </summary>
public class UrlConstructor
{
    public SharpTSURL Construct(List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Failed to construct 'URL': 1 argument required");

        var urlString = args[0]?.ToString() ?? "";

        if (args.Count > 1 && args[1] != null)
        {
            var baseUrl = args[1].ToString()!;
            return new SharpTSURL(urlString, baseUrl);
        }

        return new SharpTSURL(urlString);
    }

    public override string ToString() => "function URL() { [native code] }";
}

/// <summary>
/// Constructor class for URLSearchParams (used as `new URLSearchParams(...)`).
/// </summary>
public class UrlSearchParamsConstructor
{
    public SharpTSURLSearchParams Construct(List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
            return new SharpTSURLSearchParams();

        if (args[0] is string s)
            return new SharpTSURLSearchParams(s.TrimStart('?'));

        // Handle object/dictionary initialization
        if (args[0] is SharpTSObject obj)
        {
            var searchParams = new SharpTSURLSearchParams();
            foreach (var kvp in obj.Fields)
            {
                searchParams.Append(kvp.Key, kvp.Value?.ToString() ?? "");
            }
            return searchParams;
        }

        if (args[0] is Dictionary<string, object?> dict)
        {
            var searchParams = new SharpTSURLSearchParams();
            foreach (var kvp in dict)
            {
                searchParams.Append(kvp.Key, kvp.Value?.ToString() ?? "");
            }
            return searchParams;
        }

        return new SharpTSURLSearchParams(args[0].ToString() ?? "");
    }

    public override string ToString() => "function URLSearchParams() { [native code] }";
}
