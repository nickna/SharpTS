using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits url module helper methods.
    /// </summary>
    private void EmitUrlMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitUrlParse(typeBuilder, runtime);
        EmitUrlFormat(typeBuilder, runtime);
        EmitUrlResolve(typeBuilder, runtime);
        EmitUrlMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits wrapper methods for url functions that can be used as first-class values.
    /// </summary>
    private void EmitUrlMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // parse(url) - 1 param
        EmitUrlWrapperSimple(typeBuilder, runtime, "parse", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.UrlParse);
        });

        // format(urlObj) - 1 param
        EmitUrlWrapperSimple(typeBuilder, runtime, "format", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.UrlFormat);
        });

        // resolve(from, to) - 2 params
        EmitUrlWrapperSimple(typeBuilder, runtime, "resolve", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.UrlResolve);
        });
    }

    private void EmitUrlWrapperSimple(
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
            $"Url_{methodName}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();
        emitCall(il);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("url", methodName, method);
    }

    /// <summary>
    /// Emits: public static object UrlParse(object? url)
    /// </summary>
    private void EmitUrlParse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UrlParse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.UrlParse = method;

        var il = method.GetILGenerator();

        // Call static helper: UrlHelpers.Parse(url)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UrlHelpers).GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UrlFormat(object? urlObj)
    /// </summary>
    private void EmitUrlFormat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UrlFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.UrlFormat = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UrlHelpers).GetMethod("Format", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UrlResolve(object? from, object? to)
    /// </summary>
    private void EmitUrlResolve(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UrlResolve",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.UrlResolve = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(UrlHelpers).GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper methods for url module in compiled mode.
/// </summary>
public static class UrlHelpers
{
    public static object Parse(object? url)
    {
        if (url == null)
            return new Dictionary<string, object?>();

        var urlString = url.ToString()!;

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
                return new Dictionary<string, object?>
                {
                    ["href"] = urlString,
                    ["path"] = urlString
                };
            }
        }
    }

    private static Dictionary<string, object?> CreateUrlObject(Uri uri, bool isRelative = false, string? originalPath = null)
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

        return result;
    }

    public static string Format(object? urlObj)
    {
        if (urlObj == null)
            return "";

        if (urlObj is SharpTSURL url)
            return url.Href;

        if (urlObj is string s)
            return s;

        if (urlObj is Dictionary<string, object?> dict)
        {
            // Build URL from object parts
            var protocol = dict.GetValueOrDefault("protocol")?.ToString() ?? "";
            var hostname = dict.GetValueOrDefault("hostname")?.ToString() ??
                          dict.GetValueOrDefault("host")?.ToString() ?? "";
            var port = dict.GetValueOrDefault("port")?.ToString();
            var pathname = dict.GetValueOrDefault("pathname")?.ToString() ?? "/";
            var search = dict.GetValueOrDefault("search")?.ToString() ?? "";
            var hash = dict.GetValueOrDefault("hash")?.ToString() ?? "";

            var host = !string.IsNullOrEmpty(port) ? $"{hostname}:{port}" : hostname;

            var slashes = dict.GetValueOrDefault("slashes");
            var slashStr = slashes is true || (protocol.Length > 0 && slashes is not false) ? "//" : "";

            return $"{protocol}{slashStr}{host}{pathname}{search}{hash}";
        }

        return urlObj.ToString() ?? "";
    }

    public static string Resolve(object? from, object? to)
    {
        var fromStr = from?.ToString() ?? "";
        var toStr = to?.ToString() ?? "";

        if (string.IsNullOrEmpty(fromStr))
            return toStr;

        try
        {
            var baseUri = new Uri(fromStr, UriKind.Absolute);
            var resolvedUri = new Uri(baseUri, toStr);
            return resolvedUri.AbsoluteUri;
        }
        catch
        {
            // If base isn't absolute, try best effort
            return toStr.StartsWith('/') ? toStr : $"{fromStr.TrimEnd('/')}/{toStr}";
        }
    }
}
