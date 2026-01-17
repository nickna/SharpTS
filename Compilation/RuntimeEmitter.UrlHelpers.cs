using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits url module helper methods with full IL (no external dependencies).
    /// </summary>
    private void EmitUrlMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitUrlCreateUrlObject(typeBuilder, runtime);
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
    /// Emits: private static Dictionary&lt;string, object?&gt; CreateUrlObject(Uri uri, bool isRelative, string? originalPath)
    /// Helper method to create URL object from parsed Uri.
    /// </summary>
    private void EmitUrlCreateUrlObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UrlCreateUrlObject",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.Uri, _types.Boolean, _types.String]
        );
        runtime.UrlCreateUrlObject = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var hostLocal = il.DeclareLocal(_types.String);
        var isRelativeLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // var result = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // result["protocol"] = uri.Scheme + ":"
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "protocol");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Scheme")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["slashes"] = true (boxed)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "slashes");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["auth"] = string.IsNullOrEmpty(uri.UserInfo) ? null : uri.UserInfo
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "auth");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("UserInfo")!.GetGetMethod()!);
        var authNullLabel = il.DefineLabel();
        var authDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, authNullLabel);
        il.Emit(OpCodes.Br, authDoneLabel);
        il.MarkLabel(authNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(authDoneLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"
        var hostDefaultLabel = il.DefineLabel();
        var hostDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("IsDefaultPort")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hostDefaultLabel);
        // Non-default port
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Host")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Br, hostDoneLabel);
        il.MarkLabel(hostDefaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Host")!.GetGetMethod()!);
        il.MarkLabel(hostDoneLabel);
        il.Emit(OpCodes.Stloc, hostLocal);

        // result["host"] = host
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "host");
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["port"] = uri.IsDefaultPort ? null : uri.Port.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "port");
        var portDefaultLabel = il.DefineLabel();
        var portDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("IsDefaultPort")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, portDefaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, portDoneLabel);
        il.MarkLabel(portDefaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(portDoneLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["hostname"] = uri.Host
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "hostname");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Host")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["hash"] = string.IsNullOrEmpty(uri.Fragment) ? null : uri.Fragment
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "hash");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Fragment")!.GetGetMethod()!);
        var hashNullLabel = il.DefineLabel();
        var hashDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, hashNullLabel);
        il.Emit(OpCodes.Br, hashDoneLabel);
        il.MarkLabel(hashNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(hashDoneLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["search"] = string.IsNullOrEmpty(uri.Query) ? null : uri.Query
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "search");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Query")!.GetGetMethod()!);
        var searchNullLabel = il.DefineLabel();
        var searchDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, searchNullLabel);
        il.Emit(OpCodes.Br, searchDoneLabel);
        il.MarkLabel(searchNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(searchDoneLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["query"] = string.IsNullOrEmpty(uri.Query) ? null : uri.Query.TrimStart('?')
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "query");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("Query")!.GetGetMethod()!);
        var queryNullLabel = il.DefineLabel();
        var queryDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, queryNullLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_S, (byte)'?');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("TrimStart", [typeof(char[])])!);
        il.Emit(OpCodes.Br, queryDoneLabel);
        il.MarkLabel(queryNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(queryDoneLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["pathname"] = uri.AbsolutePath
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "pathname");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("AbsolutePath")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["path"] = uri.PathAndQuery
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "path");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("PathAndQuery")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["href"] = uri.AbsoluteUri
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "href");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("AbsoluteUri")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // if (!isRelative) return result
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, isRelativeLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Handle relative URL overrides
        il.MarkLabel(isRelativeLabel);

        // result["protocol"] = null
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "protocol");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["slashes"] = null
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "slashes");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["host"] = null
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "host");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["hostname"] = null
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "hostname");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["path"] = originalPath
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "path");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["pathname"] = originalPath.Split('?')[0]
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "pathname");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_S, (byte)'?');
        il.Emit(OpCodes.Ldc_I4_2); // StringSplitOptions.None
        il.Emit(OpCodes.Call, _types.String.GetMethod("Split", [_types.Char, typeof(StringSplitOptions)])!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        // result["href"] = originalPath
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "href");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object UrlParse(object? url)
    /// Parses a URL string and returns a dictionary with URL components.
    /// Uses Uri.TryCreate to avoid try/catch IL verification issues.
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
        var urlStringLocal = il.DeclareLocal(_types.String);
        var uriLocal = il.DeclareLocal(_types.Uri);
        var relativeUrlLocal = il.DeclareLocal(_types.String);
        var tryRelativeLabel = il.DefineLabel();
        var fallbackLabel = il.DefineLabel();

        // if (url == null) return new Dictionary<string, object?>()
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);
        // var urlString = url.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Stloc, urlStringLocal);

        // if (Uri.TryCreate(urlString, UriKind.Absolute, out uri)) return CreateUrlObject(uri, false, null)
        il.Emit(OpCodes.Ldloc, urlStringLocal);
        il.Emit(OpCodes.Ldc_I4_1); // UriKind.Absolute
        il.Emit(OpCodes.Ldloca, uriLocal);
        il.Emit(OpCodes.Call, _types.Uri.GetMethod("TryCreate", [_types.String, _types.UriKind, _types.Uri.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryRelativeLabel);

        // Success: return CreateUrlObject(uri, false, null)
        il.Emit(OpCodes.Ldloc, uriLocal);
        il.Emit(OpCodes.Ldc_I4_0); // isRelative = false
        il.Emit(OpCodes.Ldnull); // originalPath = null
        il.Emit(OpCodes.Call, runtime.UrlCreateUrlObject);
        il.Emit(OpCodes.Ret);

        // Try parsing as relative URL
        il.MarkLabel(tryRelativeLabel);

        // var relativeUrl = "http://localhost/" + urlString.TrimStart('/')
        il.Emit(OpCodes.Ldstr, "http://localhost/");
        il.Emit(OpCodes.Ldloc, urlStringLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_S, (byte)'/');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("TrimStart", [typeof(char[])])!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, relativeUrlLocal);

        // if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out uri)) return CreateUrlObject(uri, true, urlString)
        il.Emit(OpCodes.Ldloc, relativeUrlLocal);
        il.Emit(OpCodes.Ldc_I4_1); // UriKind.Absolute
        il.Emit(OpCodes.Ldloca, uriLocal);
        il.Emit(OpCodes.Call, _types.Uri.GetMethod("TryCreate", [_types.String, _types.UriKind, _types.Uri.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // Success: return CreateUrlObject(uri, true, urlString)
        il.Emit(OpCodes.Ldloc, uriLocal);
        il.Emit(OpCodes.Ldc_I4_1); // isRelative = true
        il.Emit(OpCodes.Ldloc, urlStringLocal);
        il.Emit(OpCodes.Call, runtime.UrlCreateUrlObject);
        il.Emit(OpCodes.Ret);

        // Fallback: return partial object
        il.MarkLabel(fallbackLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "href");
        il.Emit(OpCodes.Ldloc, urlStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "path");
        il.Emit(OpCodes.Ldloc, urlStringLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object UrlFormat(object? urlObj)
    /// Formats a URL object back to a string.
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
        var isStringLabel = il.DefineLabel();
        var isDictLabel = il.DefineLabel();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var protocolLocal = il.DeclareLocal(_types.String);
        var hostnameLocal = il.DeclareLocal(_types.String);
        var portLocal = il.DeclareLocal(_types.String);
        var pathnameLocal = il.DeclareLocal(_types.String);
        var searchLocal = il.DeclareLocal(_types.String);
        var hashLocal = il.DeclareLocal(_types.String);
        var hostLocal = il.DeclareLocal(_types.String);
        var slashStrLocal = il.DeclareLocal(_types.String);
        var tempObjLocal = il.DeclareLocal(_types.Object);
        var tempBoolLocal = il.DeclareLocal(_types.Boolean);

        // if (urlObj == null) return ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, isStringLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        // if (urlObj is string s) return s
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, isDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);

        // if (urlObj is Dictionary<string, object?> dict)
        il.MarkLabel(isDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var notDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDictLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Helper: get string value from dict or default
        // protocol = dict.GetValueOrDefault("protocol")?.ToString() ?? ""
        EmitGetDictStringOrDefault(il, dictLocal, tempObjLocal, "protocol", "", protocolLocal);
        EmitGetDictStringOrDefault(il, dictLocal, tempObjLocal, "pathname", "/", pathnameLocal);
        EmitGetDictStringOrDefault(il, dictLocal, tempObjLocal, "search", "", searchLocal);
        EmitGetDictStringOrDefault(il, dictLocal, tempObjLocal, "hash", "", hashLocal);
        EmitGetDictStringOrDefault(il, dictLocal, tempObjLocal, "port", "", portLocal);

        // hostname = dict.GetValueOrDefault("hostname")?.ToString() ?? dict.GetValueOrDefault("host")?.ToString() ?? ""
        var hostnameGotLabel = il.DefineLabel();
        var tryHostLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "hostname");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryHostLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, tryHostLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Stloc, hostnameLocal);
        il.Emit(OpCodes.Br, hostnameGotLabel);

        il.MarkLabel(tryHostLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "host");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        var useEmptyHostname = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, useEmptyHostname);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, useEmptyHostname);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Stloc, hostnameLocal);
        il.Emit(OpCodes.Br, hostnameGotLabel);

        il.MarkLabel(useEmptyHostname);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, hostnameLocal);

        il.MarkLabel(hostnameGotLabel);

        // host = !string.IsNullOrEmpty(port) ? $"{hostname}:{port}" : hostname
        var portEmptyLabel = il.DefineLabel();
        var hostDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, portEmptyLabel);
        // port not empty
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Br, hostDoneLabel);
        il.MarkLabel(portEmptyLabel);
        il.Emit(OpCodes.Ldloc, hostnameLocal);
        il.MarkLabel(hostDoneLabel);
        il.Emit(OpCodes.Stloc, hostLocal);

        // slashStr = (slashes is true || (protocol.Length > 0 && slashes is not false)) ? "//" : ""
        // Simplified: if protocol has length, use "//"
        var noSlashLabel = il.DefineLabel();
        var slashDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "slashes");
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noSlashLabel);
        // Check if slashes is boxed true
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, noSlashLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, noSlashLabel);
        il.Emit(OpCodes.Ldstr, "//");
        il.Emit(OpCodes.Br, slashDoneLabel);

        il.MarkLabel(noSlashLabel);
        // Check if protocol has length
        il.Emit(OpCodes.Ldloc, protocolLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        var reallyNoSlash = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, reallyNoSlash);
        il.Emit(OpCodes.Ldstr, "//");
        il.Emit(OpCodes.Br, slashDoneLabel);
        il.MarkLabel(reallyNoSlash);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(slashDoneLabel);
        il.Emit(OpCodes.Stloc, slashStrLocal);

        // return $"{protocol}{slashStr}{host}{pathname}{search}{hash}"
        // Use String.Concat with array
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Newarr, _types.String);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, protocolLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, slashStrLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldloc, pathnameLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldloc, searchLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [typeof(string[])])!);
        il.Emit(OpCodes.Ret);

        // Not a dict - return urlObj.ToString() ?? ""
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        var toStringNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, toStringNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(toStringNullLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: Emit code to get a string value from a dictionary or use a default.
    /// </summary>
    private void EmitGetDictStringOrDefault(
        ILGenerator il,
        LocalBuilder dictLocal,
        LocalBuilder tempObjLocal,
        string key,
        string defaultValue,
        LocalBuilder resultLocal)
    {
        var gotValueLabel = il.DefineLabel();
        var useDefaultLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldloca, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, useDefaultLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Brfalse, useDefaultLabel);
        il.Emit(OpCodes.Ldloc, tempObjLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.Emit(OpCodes.Br, gotValueLabel);

        il.MarkLabel(useDefaultLabel);
        il.Emit(OpCodes.Ldstr, defaultValue);

        il.MarkLabel(gotValueLabel);
        il.Emit(OpCodes.Stloc, resultLocal);
    }

    /// <summary>
    /// Emits: public static object UrlResolve(object? from, object? to)
    /// Resolves a relative URL against a base URL.
    /// Uses Uri.TryCreate to avoid try/catch IL verification issues.
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
        var fromStrLocal = il.DeclareLocal(_types.String);
        var toStrLocal = il.DeclareLocal(_types.String);
        var baseUriLocal = il.DeclareLocal(_types.Uri);
        var resolvedUriLocal = il.DeclareLocal(_types.Uri);

        // var fromStr = from?.ToString() ?? ""
        var fromNotNullLabel = il.DefineLabel();
        var fromDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, fromNotNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, fromDoneLabel);
        il.MarkLabel(fromNotNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.MarkLabel(fromDoneLabel);
        il.Emit(OpCodes.Stloc, fromStrLocal);

        // var toStr = to?.ToString() ?? ""
        var toNotNullLabel = il.DefineLabel();
        var toDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, toNotNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, toDoneLabel);
        il.MarkLabel(toNotNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString")!);
        il.MarkLabel(toDoneLabel);
        il.Emit(OpCodes.Stloc, toStrLocal);

        // if (string.IsNullOrEmpty(fromStr)) return toStr
        var fromHasValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fromStrLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brfalse, fromHasValueLabel);
        il.Emit(OpCodes.Ldloc, toStrLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fromHasValueLabel);

        // if (Uri.TryCreate(fromStr, UriKind.Absolute, out baseUri))
        var fallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fromStrLocal);
        il.Emit(OpCodes.Ldc_I4_1); // UriKind.Absolute
        il.Emit(OpCodes.Ldloca, baseUriLocal);
        il.Emit(OpCodes.Call, _types.Uri.GetMethod("TryCreate", [_types.String, _types.UriKind, _types.Uri.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // if (Uri.TryCreate(baseUri, toStr, out resolvedUri)) return resolvedUri.AbsoluteUri
        il.Emit(OpCodes.Ldloc, baseUriLocal);
        il.Emit(OpCodes.Ldloc, toStrLocal);
        il.Emit(OpCodes.Ldloca, resolvedUriLocal);
        il.Emit(OpCodes.Call, _types.Uri.GetMethod("TryCreate", [_types.Uri, _types.String, _types.Uri.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // Success: return resolvedUri.AbsoluteUri
        il.Emit(OpCodes.Ldloc, resolvedUriLocal);
        il.Emit(OpCodes.Callvirt, _types.Uri.GetProperty("AbsoluteUri")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // Fallback: best effort string concatenation
        il.MarkLabel(fallbackLabel);

        // if (toStr.StartsWith('/')) return toStr
        var toStartsSlashLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toStrLocal);
        il.Emit(OpCodes.Ldc_I4_S, (byte)'/');
        il.Emit(OpCodes.Call, _types.String.GetMethod("StartsWith", [_types.Char])!);
        il.Emit(OpCodes.Brtrue, toStartsSlashLabel);

        // return fromStr.TrimEnd('/') + "/" + toStr
        il.Emit(OpCodes.Ldloc, fromStrLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_S, (byte)'/');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("TrimEnd", [typeof(char[])])!);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldloc, toStrLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(toStartsSlashLabel);
        il.Emit(OpCodes.Ldloc, toStrLocal);
        il.Emit(OpCodes.Ret);
    }
}
