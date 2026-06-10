using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Compilation;

/// <summary>
/// HTTP support for compiled TypeScript: fetch(), http module methods.
/// </summary>
/// <remarks>
/// Emits runtime methods that use HttpClient for fetch and HttpListener for servers.
/// The emitted $FetchResponse class provides the Web API Response interface.
/// </remarks>
public partial class RuntimeEmitter
{
    // Fetch response class fields (set during emission)
    private FieldBuilder _fetchResponseStatusField = null!;
    private FieldBuilder _fetchResponseStatusTextField = null!;
    private FieldBuilder _fetchResponseOkField = null!;
    private FieldBuilder _fetchResponseUrlField = null!;
    private FieldBuilder _fetchResponseHeadersField = null!;
    private FieldBuilder _fetchResponseBodyBytesField = null!;
    private FieldBuilder _fetchResponseBodyConsumedField = null!;

    // Cached HttpClient helper (emitted during fetch emission)
    private MethodBuilder? _getOrCreateHttpClientMethod;

    // Fetch display class for async Task.Run dispatch
    private TypeBuilder _fetchDisplayClass = null!;
    private FieldBuilder _fetchDisplayUrl = null!;
    private FieldBuilder _fetchDisplayOptions = null!;
    private ConstructorBuilder _fetchDisplayCtor = null!;
    private MethodBuilder _fetchDisplayInvoke = null!;

    // HTTP types from BCL
    private Type? _httpClientType;
    private Type? _httpRequestMessageType;
    private Type? _httpResponseMessageType;
    private Type? _httpMethodType;
    private Type? _httpContentType;
    private Type? _stringContentType;
    private Type? _httpClientHandlerType;
    private Type? _httpRequestHeadersType;
    private Type? _httpResponseHeadersType;
    private Type? _httpContentHeadersType;
    private Type? _cookieContainerType;

    private void InitializeHttpTypes()
    {
        _httpClientType = Type.GetType("System.Net.Http.HttpClient, System.Net.Http");
        _httpRequestMessageType = Type.GetType("System.Net.Http.HttpRequestMessage, System.Net.Http");
        _httpResponseMessageType = Type.GetType("System.Net.Http.HttpResponseMessage, System.Net.Http");
        _httpMethodType = Type.GetType("System.Net.Http.HttpMethod, System.Net.Http");
        _httpContentType = Type.GetType("System.Net.Http.HttpContent, System.Net.Http");
        _stringContentType = Type.GetType("System.Net.Http.StringContent, System.Net.Http");
        _httpClientHandlerType = Type.GetType("System.Net.Http.HttpClientHandler, System.Net.Http");
        _httpRequestHeadersType = Type.GetType("System.Net.Http.Headers.HttpRequestHeaders, System.Net.Http");
        _httpResponseHeadersType = Type.GetType("System.Net.Http.Headers.HttpResponseHeaders, System.Net.Http");
        _httpContentHeadersType = Type.GetType("System.Net.Http.Headers.HttpContentHeaders, System.Net.Http");
        // CookieContainer lives in System.Net.Primitives — referenced for the
        // process-wide cookie jar wired into the with-cookies HttpClientHandler instances.
        _cookieContainerType = Type.GetType("System.Net.CookieContainer, System.Net.Primitives");
    }

    /// <summary>
    /// Emits all HTTP module methods.
    /// </summary>
    private void EmitHttpModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        InitializeHttpTypes();

        // Emit low-level helpers that use reflection for standalone DLLs (must be first)
        EmitHttpLowLevelHelpers(typeBuilder, runtime);

        // Emit the $Headers class first (used by $FetchResponse)
        var moduleBuilder = typeBuilder.Module as ModuleBuilder ?? throw new CompileException("Module is not a ModuleBuilder");
        EmitHeadersClass(moduleBuilder, runtime);

        // URL / URLSearchParams — migrated to stdlib/node/url.ts; no $URL emitted.

        // Emit the $FetchResponse class
        EmitFetchResponseClass(moduleBuilder, runtime);

        // Emit $Request and $Response classes
        EmitRequestClass(moduleBuilder, runtime);
        EmitResponseClass(moduleBuilder, runtime);

        // Emit Response static methods
        EmitResponseStaticMethods(typeBuilder, runtime);

        // Emit fetch display class (for async Task.Run dispatch) and fetch function
        EmitFetchDisplayClass(moduleBuilder, runtime);
        EmitFetch(typeBuilder, runtime);

        // Emit http module methods
        EmitHttpCreateServer(typeBuilder, runtime);
        EmitHttpRequest(typeBuilder, runtime);
        EmitHttpGet(typeBuilder, runtime);
        EmitHttpGetMethods(typeBuilder, runtime);
        EmitHttpGetStatusCodes(typeBuilder, runtime);
        EmitAgentHelperMethods(typeBuilder, runtime);
        EmitHttpGetGlobalAgent(typeBuilder, runtime);
        EmitHttpGetAgentConstructor(typeBuilder, runtime);

        // Emit wrappers for module import support
        EmitHttpModuleWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits low-level helpers for HTTP operations.
    /// These are now pure-IL implementations that don't require SharpTS.dll.
    /// </summary>
    private void EmitHttpLowLevelHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitExtractResponseHeadersHelper(typeBuilder, runtime);
        EmitWrapCallbackHelper(typeBuilder, runtime);
        EmitCreateHttpServerHelper(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object ExtractResponseHeadersHelper(object response)
    /// Extracts headers from HttpResponseMessage as a Dictionary.
    /// Pure-IL implementation - no SharpTS dependency.
    /// </summary>
    private void EmitExtractResponseHeadersHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExtractResponseHeadersHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ExtractResponseHeadersHelper = method;

        var il = method.GetILGenerator();

        // For now, return an empty dictionary
        // The fetch implementation creates its own headers dictionary
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object WrapCallbackHelper(object callback)
    /// In standalone mode, callbacks are already $TSFunction or $BoundTSFunction - just return as-is.
    /// Pure-IL implementation - no SharpTS dependency.
    /// </summary>
    private void EmitWrapCallbackHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WrapCallbackHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.WrapCallbackHelper = method;

        var il = method.GetILGenerator();

        // In compiled standalone mode, callbacks are already $TSFunction or $BoundTSFunction
        // Just return the callback as-is
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CreateHttpServerHelper(object callback)
    /// Creates a new $HttpServer instance directly.
    /// Pure-IL implementation - no SharpTS dependency.
    /// </summary>
    private void EmitCreateHttpServerHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateHttpServerHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateHttpServerHelper = method;

        var il = method.GetILGenerator();

        // Create new $HttpServer(callback) directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSHttpServerCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits wrapper methods for http module to support named imports and property access.
    /// Each wrapper takes object[] args (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitHttpModuleWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitHttpCreateServerWrapper(typeBuilder, runtime);
        EmitHttpRequestWrapper(typeBuilder, runtime);
        EmitHttpGetWrapper(typeBuilder, runtime);
    }

    private void EmitHttpCreateServerWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpCreateServerWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Get callback from args[0] if provided
        var hasArgsLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArgsLabel);

        // No args - pass null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, callLabel);

        // Has args - get args[0]
        il.MarkLabel(hasArgsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Call, runtime.HttpCreateServer);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("http", "createServer", method);
    }

    private void EmitHttpRequestWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpRequestWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Get url from args[0]
        var hasArg0Label = il.DefineLabel();
        var hasArg1Label = il.DefineLabel();
        var arg0Done = il.DefineLabel();
        var arg1Done = il.DefineLabel();

        // Load args[0] or null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArg0Label);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, arg0Done);
        il.MarkLabel(hasArg0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(arg0Done);

        // Load args[1] or null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasArg1Label);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, arg1Done);
        il.MarkLabel(hasArg1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(arg1Done);

        il.Emit(OpCodes.Call, runtime.HttpRequest);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("http", "request", method);
    }

    private void EmitHttpGetWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpGetWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Get url from args[0]
        var hasArg0Label = il.DefineLabel();
        var hasArg1Label = il.DefineLabel();
        var arg0Done = il.DefineLabel();
        var arg1Done = il.DefineLabel();

        // Load args[0] or null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArg0Label);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, arg0Done);
        il.MarkLabel(hasArg0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(arg0Done);

        // Load args[1] or null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasArg1Label);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, arg1Done);
        il.MarkLabel(hasArg1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(arg1Done);

        il.Emit(OpCodes.Call, runtime.HttpGet);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("http", "get", method);
    }

    // $Headers class fields (set during emission)
    private FieldBuilder _headersDataField = null!;

    /// <summary>
    /// Emits the $Headers class for compiled Headers support.
    /// Uses a Dictionary&lt;string, List&lt;string&gt;&gt; for case-insensitive multi-value storage.
    /// Methods are resolved via reflection by GetProperty's fallback path.
    /// </summary>
    private void EmitHeadersClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$Headers",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Internal storage: Dictionary<string, List<string>> with case-insensitive comparer
        var listOfStringType = typeof(List<string>);
        var dictType = typeof(Dictionary<string, List<string>>);
        _headersDataField = typeBuilder.DefineField("_data", dictType, FieldAttributes.Assembly);

        // Constructor: $Headers(object? init)
        // If init is Dictionary<string, object?>, populate from it
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _data = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        ctorIL.Emit(OpCodes.Ldarg_0);
        var ordinalIgnoreCase = typeof(StringComparer).GetProperty("OrdinalIgnoreCase")!.GetGetMethod()!;
        ctorIL.Emit(OpCodes.Call, ordinalIgnoreCase);
        var dictCtorWithComparer = dictType.GetConstructor([typeof(IEqualityComparer<string>)])!;
        ctorIL.Emit(OpCodes.Newobj, dictCtorWithComparer);
        ctorIL.Emit(OpCodes.Stfld, _headersDataField);

        // if (init is Dictionary<string, object?>) populate
        var initLocal = ctorIL.DeclareLocal(_types.DictionaryStringObject);
        var endCtorLabel = ctorIL.DefineLabel();
        var noInitLabel = ctorIL.DefineLabel();

        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        ctorIL.Emit(OpCodes.Stloc, initLocal);
        ctorIL.Emit(OpCodes.Ldloc, initLocal);
        ctorIL.Emit(OpCodes.Brfalse, noInitLabel);

        // Iterate over init dictionary entries
        var getEnumeratorMethod = _types.DictionaryStringObject.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumeratorMethod.ReturnType;
        var enumeratorLocal = ctorIL.DeclareLocal(enumeratorType);

        ctorIL.Emit(OpCodes.Ldloc, initLocal);
        ctorIL.Emit(OpCodes.Callvirt, getEnumeratorMethod);
        ctorIL.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = ctorIL.DefineLabel();
        var loopEnd = ctorIL.DefineLabel();
        ctorIL.MarkLabel(loopStart);
        ctorIL.Emit(OpCodes.Ldloca, enumeratorLocal);
        ctorIL.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        ctorIL.Emit(OpCodes.Brfalse, loopEnd);

        var currentProp = enumeratorType.GetProperty("Current")!;
        var kvpType = currentProp.PropertyType;
        var kvpLocal = ctorIL.DeclareLocal(kvpType);
        ctorIL.Emit(OpCodes.Ldloca, enumeratorLocal);
        ctorIL.Emit(OpCodes.Call, currentProp.GetGetMethod()!);
        ctorIL.Emit(OpCodes.Stloc, kvpLocal);

        // Get key
        var keyProp = kvpType.GetProperty("Key")!;
        var keyLocal = ctorIL.DeclareLocal(_types.String);
        ctorIL.Emit(OpCodes.Ldloca, kvpLocal);
        ctorIL.Emit(OpCodes.Call, keyProp.GetGetMethod()!);
        ctorIL.Emit(OpCodes.Stloc, keyLocal);

        // The value can be:
        //   - null         -> store as a single empty string
        //   - List<string> -> copy directly (this is how Set-Cookie preserves multi-value)
        //   - other        -> ToString() ?? "", store as single-element list
        var valueProp = kvpType.GetProperty("Value")!;
        var rawValueLocal = ctorIL.DeclareLocal(_types.Object);
        ctorIL.Emit(OpCodes.Ldloca, kvpLocal);
        ctorIL.Emit(OpCodes.Call, valueProp.GetGetMethod()!);
        ctorIL.Emit(OpCodes.Stloc, rawValueLocal);

        var listOfStringCtorFromEnum = listOfStringType.GetConstructor([typeof(IEnumerable<string>)])!;
        var listOfStringDefaultCtor = listOfStringType.GetConstructor(Type.EmptyTypes)!;
        var listOfStringAdd = listOfStringType.GetMethod("Add", [_types.String])!;

        // if (rawValue is List<string> existingList) { _data[key] = new List<string>(existingList); }
        var notListLabel = ctorIL.DefineLabel();
        var afterValueStoreLabel = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldloc, rawValueLocal);
        ctorIL.Emit(OpCodes.Isinst, listOfStringType);
        var asListLocal = ctorIL.DeclareLocal(listOfStringType);
        ctorIL.Emit(OpCodes.Stloc, asListLocal);
        ctorIL.Emit(OpCodes.Ldloc, asListLocal);
        ctorIL.Emit(OpCodes.Brfalse, notListLabel);

        // List branch: copy
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldfld, _headersDataField);
        ctorIL.Emit(OpCodes.Ldloc, keyLocal);
        ctorIL.Emit(OpCodes.Ldloc, asListLocal);
        ctorIL.Emit(OpCodes.Newobj, listOfStringCtorFromEnum);
        ctorIL.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);
        ctorIL.Emit(OpCodes.Br, afterValueStoreLabel);

        ctorIL.MarkLabel(notListLabel);

        // Scalar branch: value?.ToString() ?? "" → new List<string> { stringValue }
        var valueLocal = ctorIL.DeclareLocal(_types.String);
        var hasValueLabel = ctorIL.DefineLabel();
        var valueDoneLabel = ctorIL.DefineLabel();

        ctorIL.Emit(OpCodes.Ldloc, rawValueLocal);
        ctorIL.Emit(OpCodes.Dup);
        ctorIL.Emit(OpCodes.Brtrue, hasValueLabel);
        ctorIL.Emit(OpCodes.Pop);
        ctorIL.Emit(OpCodes.Ldstr, "");
        ctorIL.Emit(OpCodes.Br, valueDoneLabel);
        ctorIL.MarkLabel(hasValueLabel);
        ctorIL.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        ctorIL.Emit(OpCodes.Dup);
        var notNullLabel = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Brtrue, notNullLabel);
        ctorIL.Emit(OpCodes.Pop);
        ctorIL.Emit(OpCodes.Ldstr, "");
        ctorIL.MarkLabel(notNullLabel);
        ctorIL.MarkLabel(valueDoneLabel);
        ctorIL.Emit(OpCodes.Stloc, valueLocal);

        // _data[key] = new List<string> { value }
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldfld, _headersDataField);
        ctorIL.Emit(OpCodes.Ldloc, keyLocal);
        ctorIL.Emit(OpCodes.Newobj, listOfStringDefaultCtor);
        ctorIL.Emit(OpCodes.Dup);
        ctorIL.Emit(OpCodes.Ldloc, valueLocal);
        ctorIL.Emit(OpCodes.Callvirt, listOfStringAdd);
        ctorIL.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        ctorIL.MarkLabel(afterValueStoreLabel);

        ctorIL.Emit(OpCodes.Br, loopStart);
        ctorIL.MarkLabel(loopEnd);

        // Dispose enumerator
        var disposeMethod = enumeratorType.GetMethod("Dispose", Type.EmptyTypes);
        if (disposeMethod != null)
        {
            ctorIL.Emit(OpCodes.Ldloca, enumeratorLocal);
            ctorIL.Emit(OpCodes.Call, disposeMethod);
        }

        ctorIL.MarkLabel(noInitLabel);
        ctorIL.Emit(OpCodes.Ret);

        // Emit instance methods
        EmitHeadersGetMethod(typeBuilder, listOfStringType, dictType);
        EmitHeadersSetMethod(typeBuilder, listOfStringType, dictType);
        EmitHeadersHasMethod(typeBuilder, dictType);
        EmitHeadersDeleteMethod(typeBuilder, dictType);
        EmitHeadersAppendMethod(typeBuilder, listOfStringType, dictType);
        EmitHeadersForEachMethod(typeBuilder, listOfStringType, dictType, runtime);
        EmitHeadersEntriesMethod(typeBuilder, listOfStringType, dictType, runtime);
        EmitHeadersKeysMethod(typeBuilder, listOfStringType, dictType, runtime);
        EmitHeadersValuesMethod(typeBuilder, listOfStringType, dictType, runtime);
        EmitHeadersGetSetCookieMethod(typeBuilder, listOfStringType, dictType);

        runtime.TSHeadersType = typeBuilder;
        runtime.TSHeadersCtor = ctor;
        runtime.TSHeadersSetMethod = _headersSetMethodBuilder;

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public object get(object name) → string | null
    /// </summary>
    /// <remarks>
    /// Per WHATWG fetch, <c>Set-Cookie</c> returns the first value only — use
    /// <c>getSetCookie()</c> to get the full list.
    /// </remarks>
    private void EmitHeadersGetMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType)
    {
        var method = typeBuilder.DefineMethod("get", MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        // string name = arg?.ToString() ?? ""
        var nameLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 1, nameLocal);

        // if (_data.TryGetValue(name, out var values))
        var valuesLocal = il.DeclareLocal(listOfStringType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, valuesLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("TryGetValue")!);
        var notFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        // if (string.Equals(name, "set-cookie", OrdinalIgnoreCase)) return values.Count > 0 ? values[0] : null;
        var notSetCookieLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "set-cookie");
        il.Emit(OpCodes.Ldc_I4_5); // StringComparison.OrdinalIgnoreCase
        il.Emit(OpCodes.Call, _types.String.GetMethod("Equals", [_types.String, _types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notSetCookieLabel);

        // values.Count > 0 ? values[0] : null
        var emptySetCookieLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Callvirt, listOfStringType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, emptySetCookieLabel);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, listOfStringType.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(emptySetCookieLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Default: return string.Join(", ", values)
        il.MarkLabel(notSetCookieLabel);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, typeof(IEnumerable<string>)])!);
        il.Emit(OpCodes.Ret);

        // return null
        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object getSetCookie() → string[]
    /// </summary>
    /// <remarks>
    /// Returns all <c>Set-Cookie</c> values as a <c>string[]</c>. Empty array if none.
    /// The compiled-mode array dispatch path treats <c>string[]</c> the same as a JS
    /// array for length and indexed access.
    /// </remarks>
    private void EmitHeadersGetSetCookieMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType)
    {
        var method = typeBuilder.DefineMethod("getSetCookie", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        var valuesLocal = il.DeclareLocal(listOfStringType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldstr, "Set-Cookie");
        il.Emit(OpCodes.Ldloca, valuesLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("TryGetValue")!);
        var notFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        // return values.ToArray()
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Callvirt, listOfStringType.GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        // return new string[0]
        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.String);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object set(object name, object value) → undefined
    /// </summary>
    private MethodBuilder _headersSetMethodBuilder = null!;

    private void EmitHeadersSetMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType)
    {
        var method = typeBuilder.DefineMethod("set", MethodAttributes.Public, _types.Object, [_types.Object, _types.Object]);
        _headersSetMethodBuilder = method;
        var il = method.GetILGenerator();

        // string name = arg0?.ToString() ?? ""
        var nameLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 1, nameLocal);

        // string value = arg1?.ToString() ?? ""
        var valueLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 2, valueLocal);

        // _data[name] = new List<string> { value }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Newobj, listOfStringType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, listOfStringType.GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object has(object name) → bool
    /// </summary>
    private void EmitHeadersHasMethod(TypeBuilder typeBuilder, Type dictType)
    {
        var method = typeBuilder.DefineMethod("has", MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var nameLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 1, nameLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("ContainsKey")!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object delete(object name) → bool
    /// </summary>
    private void EmitHeadersDeleteMethod(TypeBuilder typeBuilder, Type dictType)
    {
        var method = typeBuilder.DefineMethod("delete", MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var nameLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 1, nameLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("Remove", [_types.String])!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object append(object name, object value) → undefined
    /// </summary>
    private void EmitHeadersAppendMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType)
    {
        var method = typeBuilder.DefineMethod("append", MethodAttributes.Public, _types.Object, [_types.Object, _types.Object]);
        var il = method.GetILGenerator();

        var nameLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 1, nameLocal);

        var valueLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 2, valueLocal);

        // if (_data.TryGetValue(name, out var list)) list.Add(value); else _data[name] = new List { value }
        var listLocal = il.DeclareLocal(listOfStringType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldloca, listLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("TryGetValue")!);
        var existsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, existsLabel);

        // Not found - create new list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Newobj, listOfStringType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, listOfStringType.GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Found - add to existing list
        il.MarkLabel(existsLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, listOfStringType.GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object forEach(object callback) → undefined
    /// Iterates entries and calls callback(value, key) for each.
    /// </summary>
    private void EmitHeadersForEachMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("forEach", MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        // Get enumerator of _data
        var getEnumeratorMethod = dictType.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumeratorMethod.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Callvirt, getEnumeratorMethod);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        var currentProp = enumeratorType.GetProperty("Current")!;
        var kvpType = currentProp.PropertyType;
        var kvpLocal = il.DeclareLocal(kvpType);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, currentProp.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // string key = kvp.Key.ToLowerInvariant()
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // string value = string.Join(", ", kvp.Value)
        var valueLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, typeof(IEnumerable<string>)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // InvokeMethodValue(null, callback, [value, key])
        il.Emit(OpCodes.Ldnull); // receiver
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop); // Discard result

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Dispose
        var disposeMethod = enumeratorType.GetMethod("Dispose", Type.EmptyTypes);
        if (disposeMethod != null)
        {
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, disposeMethod);
        }

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object entries() → array of [key, value] pairs
    /// </summary>
    private void EmitHeadersEntriesMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("entries", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();
        EmitHeadersCollectionMethod(il, dictType, listOfStringType, runtime, collectMode: "entries");
    }

    /// <summary>
    /// Emits: public object keys() → array of keys
    /// </summary>
    private void EmitHeadersKeysMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("keys", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();
        EmitHeadersCollectionMethod(il, dictType, listOfStringType, runtime, collectMode: "keys");
    }

    /// <summary>
    /// Emits: public object values() → array of values
    /// </summary>
    private void EmitHeadersValuesMethod(TypeBuilder typeBuilder, Type listOfStringType, Type dictType, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("values", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();
        EmitHeadersCollectionMethod(il, dictType, listOfStringType, runtime, collectMode: "values");
    }

    /// <summary>
    /// Shared IL emission for entries/keys/values - builds a List&lt;object&gt; then wraps in array.
    /// </summary>
    private void EmitHeadersCollectionMethod(ILGenerator il, Type dictType, Type listOfStringType, EmittedRuntime runtime, string collectMode)
    {
        // var result = new List<object>()
        var listOfObjectType = typeof(List<object?>);
        var resultLocal = il.DeclareLocal(listOfObjectType);
        il.Emit(OpCodes.Newobj, listOfObjectType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Iterate over _data
        var getEnumeratorMethod = dictType.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumeratorMethod.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Callvirt, getEnumeratorMethod);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        var currentProp = enumeratorType.GetProperty("Current")!;
        var kvpType = currentProp.PropertyType;
        var kvpLocal = il.DeclareLocal(kvpType);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, currentProp.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // key = kvp.Key.ToLowerInvariant()
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // value = string.Join(", ", kvp.Value)
        var valueLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, typeof(IEnumerable<string>)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Add to result based on mode
        il.Emit(OpCodes.Ldloc, resultLocal);

        switch (collectMode)
        {
            case "keys":
                il.Emit(OpCodes.Ldloc, keyLocal);
                break;
            case "values":
                il.Emit(OpCodes.Ldloc, valueLocal);
                break;
            case "entries":
                // Create [key, value] array
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, keyLocal);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Call, runtime.CreateArray);
                break;
        }

        il.Emit(OpCodes.Callvirt, listOfObjectType.GetMethod("Add", [typeof(object)])!);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Dispose
        var disposeMethod = enumeratorType.GetMethod("Dispose", Type.EmptyTypes);
        if (disposeMethod != null)
        {
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, disposeMethod);
        }

        // Convert result list to array and wrap
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, listOfObjectType.GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, runtime.CreateArray);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to convert an argument at the given index to string, storing in the given local.
    /// </summary>
    private void EmitArgToString(ILGenerator il, int argIndex, LocalBuilder local)
    {
        il.Emit(OpCodes.Ldarg, argIndex);
        var hasArgLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasArgLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(hasArgLabel);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(notNullLabel);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Emits the $FetchResponse class for compiled fetch support.
    /// </summary>
    private void EmitFetchResponseClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $FetchResponse
        var typeBuilder = moduleBuilder.DefineType(
            "$FetchResponse",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        _fetchResponseStatusField = typeBuilder.DefineField("_status", _types.Double, FieldAttributes.Private);
        _fetchResponseStatusTextField = typeBuilder.DefineField("_statusText", _types.String, FieldAttributes.Private);
        _fetchResponseOkField = typeBuilder.DefineField("_ok", _types.Boolean, FieldAttributes.Private);
        _fetchResponseUrlField = typeBuilder.DefineField("_url", _types.String, FieldAttributes.Private);
        _fetchResponseHeadersField = typeBuilder.DefineField("_headers", _types.Object, FieldAttributes.Private);
        _fetchResponseBodyBytesField = typeBuilder.DefineField("_bodyBytes", _types.ByteArray, FieldAttributes.Private);
        _fetchResponseBodyConsumedField = typeBuilder.DefineField("_bodyConsumed", _types.Boolean, FieldAttributes.Private);

        // Constructor: (double status, string statusText, bool ok, string url, object headers, byte[] body)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Double, _types.String, _types.Boolean, _types.String, _types.Object, _types.ByteArray]
        );

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // Store all fields
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, _fetchResponseStatusField);

        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, _fetchResponseStatusTextField);

        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_3);
        ctorIL.Emit(OpCodes.Stfld, _fetchResponseOkField);

        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg, 4);
        ctorIL.Emit(OpCodes.Stfld, _fetchResponseUrlField);

        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg, 5);
        ctorIL.Emit(OpCodes.Stfld, _fetchResponseHeadersField);

        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg, 6);
        ctorIL.Emit(OpCodes.Stfld, _fetchResponseBodyBytesField);

        ctorIL.Emit(OpCodes.Ret);

        // Property getters for status, statusText, ok, url, headers
        EmitFetchResponsePropertyGetter(typeBuilder, "status", _types.Double, _fetchResponseStatusField);
        EmitFetchResponsePropertyGetter(typeBuilder, "statusText", _types.String, _fetchResponseStatusTextField);
        EmitFetchResponsePropertyGetter(typeBuilder, "ok", _types.Boolean, _fetchResponseOkField);
        EmitFetchResponsePropertyGetter(typeBuilder, "url", _types.String, _fetchResponseUrlField);
        EmitFetchResponsePropertyGetter(typeBuilder, "headers", _types.Object, _fetchResponseHeadersField);
        EmitFetchResponsePropertyGetter(typeBuilder, "bodyUsed", _types.Boolean, _fetchResponseBodyConsumedField);

        // Constant properties
        EmitResponseConstantProperty(typeBuilder, "type", _types.String, "basic");
        EmitResponseConstantBoolProperty(typeBuilder, "redirected", false);

        // Method: json() - returns Promise
        EmitFetchResponseJsonMethod(typeBuilder, runtime);

        // Method: text() - returns Promise
        EmitFetchResponseTextMethod(typeBuilder, runtime);

        // Method: arrayBuffer() - returns Promise wrapping a Buffer
        EmitFetchResponseArrayBufferMethod(typeBuilder, runtime);

        // Method: clone() - creates a copy sharing body bytes
        EmitFetchResponseCloneMethod(typeBuilder, runtime, ctor);

        // Property: body - returns a Readable stream of the response body
        EmitFetchResponseBodyGetter(typeBuilder, runtime);

        // Store the type reference
        runtime.TSFetchResponseType = typeBuilder;
        runtime.TSFetchResponseCtor = ctor;

        typeBuilder.CreateType();
    }

    private void EmitFetchResponsePropertyGetter(TypeBuilder typeBuilder, string name, Type returnType, FieldBuilder field)
    {
        var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, returnType, null);
        // Use PascalCase for getter name to match ReflectionCache.GetGetter expectations
        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        var getter = typeBuilder.DefineMethod(
            $"get_{pascalName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            returnType,
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        if (returnType.IsValueType && field.FieldType.IsValueType)
        {
            // No boxing needed
        }
        else if (returnType == _types.Object && field.FieldType.IsValueType)
        {
            il.Emit(OpCodes.Box, field.FieldType);
        }
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitFetchResponseJsonMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public object json()
        // Returns a Promise that resolves to the parsed JSON
        var method = typeBuilder.DefineMethod(
            "json",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // Get the body bytes and parse as JSON
        // For simplicity, we convert to string first then use JSON.Parse
        var textLocal = il.DeclareLocal(_types.String);

        // string text = Encoding.UTF8.GetString(_bodyBytes)
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseBodyBytesField);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.ByteArray])!);
        il.Emit(OpCodes.Stloc, textLocal);

        // Mark body as consumed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _fetchResponseBodyConsumedField);

        // Parse JSON using the runtime's JsonParse method
        il.Emit(OpCodes.Ldloc, textLocal);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        // Wrap in a resolved Promise
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);

        il.Emit(OpCodes.Ret);
    }

    private void EmitFetchResponseTextMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public object text()
        // Returns a Promise that resolves to the body as string
        var method = typeBuilder.DefineMethod(
            "text",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // string text = Encoding.UTF8.GetString(_bodyBytes)
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseBodyBytesField);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.ByteArray])!);

        // Mark body as consumed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _fetchResponseBodyConsumedField);

        // Wrap in a resolved Promise
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);

        il.Emit(OpCodes.Ret);
    }

    private void EmitFetchResponseArrayBufferMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public object arrayBuffer()
        // Returns a Promise that resolves to a Buffer containing the body bytes
        var method = typeBuilder.DefineMethod(
            "arrayBuffer",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // Create a new $Buffer from the body bytes
        // new $Buffer(_bodyBytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseBodyBytesField);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);

        // Mark body as consumed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _fetchResponseBodyConsumedField);

        // Wrap in a resolved Promise
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object clone()
    /// Creates a new $FetchResponse with the same fields (shared body bytes).
    /// </summary>
    private void EmitFetchResponseCloneMethod(TypeBuilder typeBuilder, EmittedRuntime runtime, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod(
            "clone",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // Load all fields from this instance and create a new $FetchResponse
        // Constructor params: (double status, string statusText, bool ok, string url, object headers, byte[] body)

        // status
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseStatusField);

        // statusText
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseStatusTextField);

        // ok
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseOkField);

        // url
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseUrlField);

        // headers
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseHeadersField);

        // bodyBytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseBodyBytesField);

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a 'body' property getter on $FetchResponse that returns a Readable stream.
    /// Uses the emitted $Readable type directly for compiled-mode compatibility.
    /// </summary>
    private void EmitFetchResponseBodyGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Define a cached field for the body readable
        var bodyStreamField = typeBuilder.DefineField("_bodyStream", _types.Object, FieldAttributes.Private);

        var prop = typeBuilder.DefineProperty("body", PropertyAttributes.None, _types.Object, null);
        var getter = typeBuilder.DefineMethod(
            "get_Body",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Object,
            Type.EmptyTypes
        );
        prop.SetGetMethod(getter);

        var il = getter.GetILGenerator();

        // if (_bodyStream != null) return _bodyStream;
        var createLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bodyStreamField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, createLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(createLabel);
        il.Emit(OpCodes.Pop); // pop the null

        // Create emitted $Readable directly (no reflection needed)
        il.Emit(OpCodes.Newobj, runtime.TSReadableCtor);
        var readableLocal = il.DeclareLocal(runtime.TSReadableType);
        il.Emit(OpCodes.Stloc, readableLocal);

        // Push body bytes as a single chunk, then push null for EOF
        // readable.Push(new $Buffer(bodyBytes))
        var bodyBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fetchResponseBodyBytesField);
        il.Emit(OpCodes.Stloc, bodyBytesLocal);

        // Check if bodyBytes is not null and has length > 0
        var pushEofLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bodyBytesLocal);
        il.Emit(OpCodes.Brfalse, pushEofLabel);
        il.Emit(OpCodes.Ldloc, bodyBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, pushEofLabel);

        // Push body bytes: readable.Push(new $Buffer(bodyBytes))
        il.Emit(OpCodes.Ldloc, readableLocal);
        il.Emit(OpCodes.Ldloc, bodyBytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor); // new $Buffer(byte[])
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop); // discard bool return

        // Push null for EOF: readable.Push(null)
        il.MarkLabel(pushEofLabel);
        il.Emit(OpCodes.Ldloc, readableLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop); // discard bool return

        // Mark body as consumed: this._bodyConsumed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _fetchResponseBodyConsumedField);

        // Cache: this._bodyStream = readable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, readableLocal);
        il.Emit(OpCodes.Stfld, bodyStreamField);

        // Return readable
        il.Emit(OpCodes.Ldloc, readableLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object Fetch(object url, object? options)
    /// Returns a Promise that resolves to a $FetchResponse.
    /// </summary>
    /// <summary>
    /// Emits $FetchDisplayClass: captures _url and _options, Invoke() calls FetchHelper
    /// and constructs $FetchResponse from the result. Used with Task.Run for async dispatch.
    /// </summary>
    private void EmitFetchDisplayClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        if (_httpClientType == null || _httpRequestMessageType == null)
            return; // No HttpClient available, Fetch will return rejected promise

        _fetchDisplayClass = moduleBuilder.DefineType(
            "$FetchDisplayClass",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _fetchDisplayUrl = _fetchDisplayClass.DefineField("_url", _types.Object, FieldAttributes.Public);
        _fetchDisplayOptions = _fetchDisplayClass.DefineField("_options", _types.Object, FieldAttributes.Public);
        // FetchHelper MethodInfo stored at runtime via ldtoken
        var methodField = _fetchDisplayClass.DefineField("_fetchHelper", typeof(MethodInfo), FieldAttributes.Public);

        _fetchDisplayCtor = _fetchDisplayClass.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = _fetchDisplayCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        // Invoke(): calls FetchHelper(_url, _options), constructs $FetchResponse or throws
        _fetchDisplayInvoke = _fetchDisplayClass.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = _fetchDisplayInvoke.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.ObjectArray);
            var retLocal = il.DeclareLocal(_types.Object);
            var endLabel = il.DefineLabel();

            // try { ... } finally { $EventLoop.GetInstance().Unref(); }
            // Fetch() Ref'd the loop before dispatching this work to the thread
            // pool; the Unref must run on success AND on the throw path, or an
            // errored fetch would pin the event loop open forever.
            il.BeginExceptionBlock();

            // object[] result = (object[]) _fetchHelper.Invoke(null, new object[] { _url, _options })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodField);
            il.Emit(OpCodes.Ldnull); // target (static method)
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _fetchDisplayUrl);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _fetchDisplayOptions);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Castclass, _types.ObjectArray);
            il.Emit(OpCodes.Stloc, resultLocal);

            // Check if result[0] (success) is true
            var errorLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            il.Emit(OpCodes.Brfalse, errorLabel);

            // Success: construct $FetchResponse(status, statusText, ok, url, headers, bodyBytes)
            // Array indices: 0=success, 1=status, 2=statusText, 3=ok, 4=url, 5=headers, 6=bodyBytes

            // status (double)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Double);

            // statusText (string)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, _types.String);

            // ok (bool)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);

            // url (string)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, _types.String);

            // headers - wrap in $Headers
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_5);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Newobj, runtime.TSHeadersCtor);

            // bodyBytes (byte[])
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_6);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, _types.ByteArray);

            il.Emit(OpCodes.Newobj, runtime.TSFetchResponseCtor);
            il.Emit(OpCodes.Stloc, retLocal);
            il.Emit(OpCodes.Leave, endLabel);

            // Error path: throw exception with error message (unwinds through
            // the finally so the event loop still gets Unref'd)
            il.MarkLabel(errorLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_7);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
            il.Emit(OpCodes.Throw);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);
            il.EndExceptionBlock();

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldloc, retLocal);
            il.Emit(OpCodes.Ret);
        }

        _fetchDisplayClass.CreateType();
    }

    /// <remarks>
    /// Emits a helper method to perform the HTTP request with try/catch,
    /// then dispatches it asynchronously via Task.Run and wraps in a Promise.
    /// </remarks>
    private void EmitFetch(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        if (_httpClientType == null || _httpRequestMessageType == null)
        {
            // Emit a method that returns rejected promise
            var method = typeBuilder.DefineMethod(
                "Fetch",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]
            );
            runtime.Fetch = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "HttpClient not available");
            il.Emit(OpCodes.Call, runtime.TSPromiseReject);
            il.Emit(OpCodes.Ret);
            return;
        }

        // Emit cached HttpClient infrastructure (four static fields + getter method)
        EmitCachedHttpClients(typeBuilder);

        // Emit the fetch.cookieJar.{getCookies,setCookie,clear} helpers — they
        // operate on the same _cookieContainer static field as the with-cookies
        // HttpClient handlers, so the JS-facing introspection sees the same jar
        // that fetch reads/writes.
        EmitCookieJarHelpers(typeBuilder, runtime);

        // First emit the headers helper
        var applyHeadersMethod = EmitApplyRequestHeaders(typeBuilder, runtime);

        // Then emit the helper that does the actual fetch work
        var fetchHelperMethod = EmitFetchHelper(typeBuilder, runtime, applyHeadersMethod);

        // Now emit the Fetch method: creates display class, dispatches via Task.Run,
        // returns a pending Promise that resolves when the HTTP call completes.
        var fetchMethod = typeBuilder.DefineMethod(
            "Fetch",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.Fetch = fetchMethod;

        var fetchIL = fetchMethod.GetILGenerator();

        // var dc = new $FetchDisplayClass();
        fetchIL.Emit(OpCodes.Newobj, _fetchDisplayCtor);
        var dcLocal = fetchIL.DeclareLocal(_fetchDisplayClass);
        fetchIL.Emit(OpCodes.Stloc, dcLocal);

        // dc._url = arg0;
        fetchIL.Emit(OpCodes.Ldloc, dcLocal);
        fetchIL.Emit(OpCodes.Ldarg_0);
        fetchIL.Emit(OpCodes.Stfld, _fetchDisplayUrl);

        // dc._options = arg1;
        fetchIL.Emit(OpCodes.Ldloc, dcLocal);
        fetchIL.Emit(OpCodes.Ldarg_1);
        fetchIL.Emit(OpCodes.Stfld, _fetchDisplayOptions);

        // dc._fetchHelper = FetchHelper method (via ldtoken)
        fetchIL.Emit(OpCodes.Ldloc, dcLocal);
        fetchIL.Emit(OpCodes.Ldtoken, fetchHelperMethod);
        fetchIL.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        fetchIL.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));
        fetchIL.Emit(OpCodes.Stfld, _fetchDisplayClass.GetField("_fetchHelper")!);

        // Ref the event loop BEFORE dispatching to the thread pool — an
        // in-flight fetch must keep the process alive (Node: an active request
        // is a libuv handle). Without this, the entry point's WaitForTask sees
        // a quiescent loop while the request is on the wire and exits early.
        // The display class's Invoke Unrefs in its finally.
        fetchIL.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        fetchIL.Emit(OpCodes.Callvirt, runtime.EventLoopRef);

        // Task.Run<object?>(new Func<object?>(dc.Invoke))
        fetchIL.Emit(OpCodes.Ldloc, dcLocal);
        fetchIL.Emit(OpCodes.Ldftn, _fetchDisplayInvoke);
        fetchIL.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructors()[0]);
        fetchIL.Emit(OpCodes.Call, typeof(Task).GetMethod("Run", 1, [typeof(Func<>).MakeGenericType(Type.MakeGenericMethodParameter(0))])!.MakeGenericMethod(typeof(object)));

        // WrapTaskAsPromise(task) — returns pending $Promise
        fetchIL.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        fetchIL.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits two static HttpClient fields and a getter method that returns the appropriate
    /// cached client based on the redirect mode string. Creates the client lazily on first use.
    /// This avoids creating a new HttpClient per fetch call (the .NET recommended pattern).
    /// </summary>
    /// <summary>
    /// Emits four cached HttpClient static fields and a 2-arg
    /// <c>GetOrCreateHttpClient(redirectMode, useCookies)</c> selector. The two
    /// with-cookies clients share a single static <c>_cookieContainer</c> field that
    /// is lazily constructed on first use, giving each compiled DLL its own
    /// process-wide cookie jar.
    /// </summary>
    /// <remarks>
    /// Pure IL — no reflection back to SharpTS.dll. <see cref="System.Net.CookieContainer"/>
    /// lives in <c>System.Net.Primitives</c>, which is part of the BCL surface always
    /// present at runtime.
    /// </remarks>
    private FieldBuilder _cookieContainerField = null!;

    private void EmitCachedHttpClients(TypeBuilder typeBuilder)
    {
        // Static fields for the four cached clients
        var followField = typeBuilder.DefineField(
            "_httpClientFollow", _httpClientType!, FieldAttributes.Private | FieldAttributes.Static);
        var noRedirectField = typeBuilder.DefineField(
            "_httpClientNoRedirect", _httpClientType!, FieldAttributes.Private | FieldAttributes.Static);
        var followCookiesField = typeBuilder.DefineField(
            "_httpClientFollowCookies", _httpClientType!, FieldAttributes.Private | FieldAttributes.Static);
        var noRedirectCookiesField = typeBuilder.DefineField(
            "_httpClientNoRedirectCookies", _httpClientType!, FieldAttributes.Private | FieldAttributes.Static);

        // Static field holding the shared CookieContainer (process-wide, lazy-init)
        _cookieContainerField = typeBuilder.DefineField(
            "_cookieContainer", _cookieContainerType!, FieldAttributes.Assembly | FieldAttributes.Static);

        // GetOrCreateHttpClient(string redirectMode, bool useCookies) -> HttpClient
        var method = typeBuilder.DefineMethod(
            "GetOrCreateHttpClient",
            MethodAttributes.Private | MethodAttributes.Static,
            _httpClientType!,
            [_types.String, _types.Boolean]
        );
        _getOrCreateHttpClientMethod = method;

        var il = method.GetILGenerator();
        var handlerCtor = _httpClientHandlerType!.GetConstructor(Type.EmptyTypes)!;
        var httpClientCtor = _httpClientType!.GetConstructor([typeof(System.Net.Http.HttpMessageHandler)])!;
        var allowAutoRedirectProp = _httpClientHandlerType.GetProperty("AllowAutoRedirect")!;
        var useCookiesProp = _httpClientHandlerType.GetProperty("UseCookies")!;
        var cookieContainerProp = _httpClientHandlerType.GetProperty("CookieContainer")!;
        var cookieContainerCtor = _cookieContainerType!.GetConstructor(Type.EmptyTypes)!;
        var timeoutProp = _httpClientType.GetProperty("Timeout")!;
        var fromSecondsMethod = _types.TimeSpan.GetMethod("FromSeconds", [_types.Double])!;

        // Local helper: emit the create-and-cache sequence for one of the four field combinations.
        // Note: this is C# code that emits IL, not a runtime helper.
        void EmitCreateAndCacheClient(FieldBuilder field, bool followRedirects, bool useCookies)
        {
            var doneLabel = il.DefineLabel();

            // if (field != null) goto done
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Brtrue, doneLabel);

            // var handler = new HttpClientHandler();
            var handlerLocal = il.DeclareLocal(_httpClientHandlerType);
            il.Emit(OpCodes.Newobj, handlerCtor);
            il.Emit(OpCodes.Stloc, handlerLocal);

            // handler.AllowAutoRedirect = followRedirects;
            il.Emit(OpCodes.Ldloc, handlerLocal);
            il.Emit(followRedirects ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, allowAutoRedirectProp.GetSetMethod()!);

            // handler.UseCookies = useCookies;
            il.Emit(OpCodes.Ldloc, handlerLocal);
            il.Emit(useCookies ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, useCookiesProp.GetSetMethod()!);

            if (useCookies)
            {
                // _cookieContainer ??= new CookieContainer();
                var cookieReadyLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldsfld, _cookieContainerField);
                il.Emit(OpCodes.Brtrue, cookieReadyLabel);
                il.Emit(OpCodes.Newobj, cookieContainerCtor);
                il.Emit(OpCodes.Stsfld, _cookieContainerField);
                il.MarkLabel(cookieReadyLabel);

                // handler.CookieContainer = _cookieContainer;
                il.Emit(OpCodes.Ldloc, handlerLocal);
                il.Emit(OpCodes.Ldsfld, _cookieContainerField);
                il.Emit(OpCodes.Callvirt, cookieContainerProp.GetSetMethod()!);
            }

            // var client = new HttpClient(handler); client.Timeout = TimeSpan.FromSeconds(30);
            il.Emit(OpCodes.Ldloc, handlerLocal);
            il.Emit(OpCodes.Newobj, httpClientCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_R8, 30.0);
            il.Emit(OpCodes.Call, fromSecondsMethod);
            il.Emit(OpCodes.Callvirt, timeoutProp.GetSetMethod()!);
            // field = client;
            il.Emit(OpCodes.Stsfld, field);

            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Ret);
        }

        // Decision tree:
        //   bool follow = redirectMode == "follow"  (the only follow value; "manual"/"error" both → no-redirect)
        //   if (follow)
        //     if (useCookies) -> followCookies else -> follow
        //   else
        //     if (useCookies) -> noRedirectCookies else -> noRedirect

        var notFollowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "follow");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notFollowLabel);

        // follow path: branch on useCookies
        var followNoCookiesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, followNoCookiesLabel);
        EmitCreateAndCacheClient(followCookiesField, followRedirects: true, useCookies: true);

        il.MarkLabel(followNoCookiesLabel);
        EmitCreateAndCacheClient(followField, followRedirects: true, useCookies: false);

        // not-follow path: branch on useCookies
        il.MarkLabel(notFollowLabel);
        var noRedirectNoCookiesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noRedirectNoCookiesLabel);
        EmitCreateAndCacheClient(noRedirectCookiesField, followRedirects: false, useCookies: true);

        il.MarkLabel(noRedirectNoCookiesLabel);
        EmitCreateAndCacheClient(noRedirectField, followRedirects: false, useCookies: false);
    }

    /// <summary>
    /// Emits the three <c>fetch.cookieJar.*</c> introspection helpers as static
    /// methods on the runtime type. They share the lazily-initialized
    /// <c>_cookieContainer</c> field that the with-cookies HttpClientHandlers point at,
    /// so the script-side jar view always matches what fetch is sending/receiving.
    /// </summary>
    /// <remarks>
    /// Pure-IL — no reflection back to SharpTS.dll. <c>CookieContainer</c> lives in
    /// <c>System.Net.Primitives</c> which is part of the BCL surface.
    /// </remarks>
    private void EmitCookieJarHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var cookieContainerCtor = _cookieContainerType!.GetConstructor(Type.EmptyTypes)!;
        var getCookieHeaderMethod = _cookieContainerType.GetMethod("GetCookieHeader", [typeof(Uri)])!;
        var setCookiesMethod = _cookieContainerType.GetMethod("SetCookies", [typeof(Uri), _types.String])!;
        var getAllCookiesMethod = _cookieContainerType.GetMethod("GetAllCookies", Type.EmptyTypes);
        var uriTryCreate = typeof(Uri).GetMethod("TryCreate", [_types.String, typeof(UriKind), typeof(Uri).MakeByRefType()])!;
        var uriKindAbsolute = (int)UriKind.Absolute;
        var typeErrorCtor = runtime.TSTypeErrorCtor;
        var cookieType = Type.GetType("System.Net.Cookie, System.Net.Primitives")!;
        var cookieExpiredProp = cookieType.GetProperty("Expired")!;

        // Local helper: lazily initialize _cookieContainer if null and leave it on the stack.
        void EmitEnsureContainerOnStack(ILGenerator gen)
        {
            var readyLabel = gen.DefineLabel();
            gen.Emit(OpCodes.Ldsfld, _cookieContainerField);
            gen.Emit(OpCodes.Brtrue, readyLabel);
            gen.Emit(OpCodes.Newobj, cookieContainerCtor);
            gen.Emit(OpCodes.Stsfld, _cookieContainerField);
            gen.MarkLabel(readyLabel);
            gen.Emit(OpCodes.Ldsfld, _cookieContainerField);
        }

        // Local helper: validate url arg via Uri.TryCreate, throw "TypeError: Invalid URL: ..."
        // on failure, leave the Uri on the stack on success. Matches the interpreter-side
        // SharpTSCookieJar contract where invalid URLs throw a JS-catchable TypeError.
        void EmitParseUrlOrThrow(ILGenerator gen, int argIndex)
        {
            var uriLocal = gen.DeclareLocal(typeof(Uri));
            var okLabel = gen.DefineLabel();

            // Uri.TryCreate(url, UriKind.Absolute, out uriLocal)
            gen.Emit(OpCodes.Ldarg, argIndex);
            gen.Emit(OpCodes.Ldc_I4, uriKindAbsolute);
            gen.Emit(OpCodes.Ldloca, uriLocal);
            gen.Emit(OpCodes.Call, uriTryCreate);
            gen.Emit(OpCodes.Brtrue, okLabel);

            // throw new $TypeError("Invalid URL: " + url) — wrapped in .NET exception
            // via CreateException so try/catch can catch it as a JS TypeError.
            gen.Emit(OpCodes.Ldstr, "Invalid URL: ");
            gen.Emit(OpCodes.Ldarg, argIndex);
            gen.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
            gen.Emit(OpCodes.Newobj, typeErrorCtor);
            gen.Emit(OpCodes.Call, runtime.CreateException);
            gen.Emit(OpCodes.Throw);

            gen.MarkLabel(okLabel);
            gen.Emit(OpCodes.Ldloc, uriLocal);
        }

        // public static string CookieJarGetCookies(string url)
        {
            var method = typeBuilder.DefineMethod(
                "CookieJarGetCookies",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.String,
                [_types.String]
            );
            runtime.CookieJarGetCookies = method;
            var il = method.GetILGenerator();

            // Reject null/empty url with the same TypeError as the validation path
            var validLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, validLabel);
            il.Emit(OpCodes.Ldstr, "Invalid URL: (null)");
            il.Emit(OpCodes.Newobj, typeErrorCtor);
            il.Emit(OpCodes.Call, runtime.CreateException);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(validLabel);

            EmitEnsureContainerOnStack(il);
            EmitParseUrlOrThrow(il, 0);
            il.Emit(OpCodes.Callvirt, getCookieHeaderMethod);
            il.Emit(OpCodes.Ret);
        }

        // public static void CookieJarSetCookie(string cookie, string url)
        {
            var method = typeBuilder.DefineMethod(
                "CookieJarSetCookie",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Void,
                [_types.String, _types.String]
            );
            runtime.CookieJarSetCookie = method;
            var il = method.GetILGenerator();

            // Reject null url
            var validLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brtrue, validLabel);
            il.Emit(OpCodes.Ldstr, "Invalid URL: (null)");
            il.Emit(OpCodes.Newobj, typeErrorCtor);
            il.Emit(OpCodes.Call, runtime.CreateException);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(validLabel);

            EmitEnsureContainerOnStack(il);
            EmitParseUrlOrThrow(il, 1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, setCookiesMethod);
            il.Emit(OpCodes.Ret);
        }

        // public static void CookieJarClear()
        //
        // Walks every cookie, sets Expired=true, collects unique domains, then calls
        // GetCookieHeader for each domain to trigger the in-place expiry sweep. After
        // this method returns, GetAllCookies().Count is zero. See SharpTSCookieJar.Clear
        // for the rationale (CookieContainer has no public Clear and the snapshot
        // returned by GetAllCookies isn't live).
        {
            var method = typeBuilder.DefineMethod(
                "CookieJarClear",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Void,
                Type.EmptyTypes
            );
            runtime.CookieJarClear = method;
            var il = method.GetILGenerator();

            // If the container is null there's nothing to clear.
            var doneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, _cookieContainerField);
            il.Emit(OpCodes.Brfalse, doneLabel);

            if (getAllCookiesMethod != null)
            {
                var enumerableType = typeof(System.Collections.IEnumerable);
                var enumeratorMethod = enumerableType.GetMethod("GetEnumerator")!;
                var moveNextMethod = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
                var currentProp = typeof(System.Collections.IEnumerator).GetProperty("Current")!;
                var cookieDomainProp = cookieType.GetProperty("Domain")!;

                // var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hashSetType = typeof(HashSet<string>);
                var hashSetCtor = hashSetType.GetConstructor([typeof(IEqualityComparer<string>)])!;
                var hashSetAdd = hashSetType.GetMethod("Add", [_types.String])!;
                var ordinalIgnoreCase = typeof(StringComparer).GetProperty("OrdinalIgnoreCase")!.GetGetMethod()!;
                var domainsLocal = il.DeclareLocal(hashSetType);
                il.Emit(OpCodes.Call, ordinalIgnoreCase);
                il.Emit(OpCodes.Newobj, hashSetCtor);
                il.Emit(OpCodes.Stloc, domainsLocal);

                // Phase 1: foreach (Cookie c in _cookieContainer.GetAllCookies())
                //   c.Expired = true; domains.Add(c.Domain);
                il.Emit(OpCodes.Ldsfld, _cookieContainerField);
                il.Emit(OpCodes.Callvirt, getAllCookiesMethod);
                il.Emit(OpCodes.Castclass, enumerableType);
                il.Emit(OpCodes.Callvirt, enumeratorMethod);
                var enumLocal = il.DeclareLocal(typeof(System.Collections.IEnumerator));
                il.Emit(OpCodes.Stloc, enumLocal);

                var phase1Start = il.DefineLabel();
                var phase1End = il.DefineLabel();
                il.MarkLabel(phase1Start);
                il.Emit(OpCodes.Ldloc, enumLocal);
                il.Emit(OpCodes.Callvirt, moveNextMethod);
                il.Emit(OpCodes.Brfalse, phase1End);

                var cookieLocal = il.DeclareLocal(cookieType);
                il.Emit(OpCodes.Ldloc, enumLocal);
                il.Emit(OpCodes.Callvirt, currentProp.GetGetMethod()!);
                il.Emit(OpCodes.Castclass, cookieType);
                il.Emit(OpCodes.Stloc, cookieLocal);

                // c.Expired = true
                il.Emit(OpCodes.Ldloc, cookieLocal);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Callvirt, cookieExpiredProp.GetSetMethod()!);

                // domains.Add(c.Domain)
                il.Emit(OpCodes.Ldloc, domainsLocal);
                il.Emit(OpCodes.Ldloc, cookieLocal);
                il.Emit(OpCodes.Callvirt, cookieDomainProp.GetGetMethod()!);
                il.Emit(OpCodes.Callvirt, hashSetAdd);
                il.Emit(OpCodes.Pop); // discard Add's bool result

                il.Emit(OpCodes.Br, phase1Start);
                il.MarkLabel(phase1End);

                // Phase 2: foreach (string domain in domains)
                //   try { _cookieContainer.GetCookieHeader(new Uri("http://" + domain.TrimStart('.') + "/")); }
                //   catch { }
                var domainsEnumMethod = hashSetType.GetMethod("GetEnumerator", Type.EmptyTypes)!;
                var domainsEnumeratorType = domainsEnumMethod.ReturnType;
                var domainsMoveNext = domainsEnumeratorType.GetMethod("MoveNext")!;
                var domainsCurrent = domainsEnumeratorType.GetProperty("Current")!;
                var domainsEnumLocal = il.DeclareLocal(domainsEnumeratorType);

                il.Emit(OpCodes.Ldloc, domainsLocal);
                il.Emit(OpCodes.Callvirt, domainsEnumMethod);
                il.Emit(OpCodes.Stloc, domainsEnumLocal);

                var phase2Start = il.DefineLabel();
                var phase2End = il.DefineLabel();
                il.MarkLabel(phase2Start);
                il.Emit(OpCodes.Ldloca, domainsEnumLocal);
                il.Emit(OpCodes.Call, domainsMoveNext);
                il.Emit(OpCodes.Brfalse, phase2End);

                // var host = domain.TrimStart('.')
                var hostLocal = il.DeclareLocal(_types.String);
                il.Emit(OpCodes.Ldloca, domainsEnumLocal);
                il.Emit(OpCodes.Call, domainsCurrent.GetGetMethod()!);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, typeof(char));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldc_I4, (int)'.');
                il.Emit(OpCodes.Stelem_I2);
                il.Emit(OpCodes.Callvirt, _types.String.GetMethod("TrimStart", [typeof(char[])])!);
                il.Emit(OpCodes.Stloc, hostLocal);

                // if (string.IsNullOrEmpty(host)) continue;
                il.Emit(OpCodes.Ldloc, hostLocal);
                il.Emit(OpCodes.Call, _types.String.GetMethod("IsNullOrEmpty", [_types.String])!);
                il.Emit(OpCodes.Brtrue, phase2Start);

                // try { _cookieContainer.GetCookieHeader(new Uri("http://" + host + "/")); } catch { }
                il.BeginExceptionBlock();
                il.Emit(OpCodes.Ldsfld, _cookieContainerField);
                il.Emit(OpCodes.Ldstr, "http://");
                il.Emit(OpCodes.Ldloc, hostLocal);
                il.Emit(OpCodes.Ldstr, "/");
                il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
                il.Emit(OpCodes.Newobj, typeof(Uri).GetConstructor([_types.String])!);
                il.Emit(OpCodes.Callvirt, getCookieHeaderMethod);
                il.Emit(OpCodes.Pop);
                il.BeginCatchBlock(_types.Exception);
                il.Emit(OpCodes.Pop);
                il.EndExceptionBlock();

                il.Emit(OpCodes.Br, phase2Start);
                il.MarkLabel(phase2End);
            }

            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits helper method that performs the actual HTTP request with try/catch.
    /// Returns object[] { success, status, statusText, ok, url, headers, bodyBytes, errorMessage }
    /// </summary>
    private MethodBuilder EmitFetchHelper(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder applyHeadersMethod)
    {
        var method = typeBuilder.DefineMethod(
            "FetchHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.ObjectArray,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.ObjectArray);
        var urlLocal = il.DeclareLocal(_types.String);
        var clientLocal = il.DeclareLocal(_httpClientType!);
        var requestLocal = il.DeclareLocal(_httpRequestMessageType!);
        var responseLocal = il.DeclareLocal(_httpResponseMessageType!);
        var bodyBytesLocal = il.DeclareLocal(_types.ByteArray);
        var statusLocal = il.DeclareLocal(_types.Double);
        var okLocal = il.DeclareLocal(_types.Boolean);
        var statusTextLocal = il.DeclareLocal(_types.String);
        var headersLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var methodStrLocal = il.DeclareLocal(_types.String);

        // try block
        il.BeginExceptionBlock();

        // Validate URL: if arg0 is null or undefined, throw TypeError
        var urlValidLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, urlValidLabel);
        // arg0 is null - throw
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "TypeError: fetch requires a URL argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(urlValidLabel);
        // Check for Undefined
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefinedLabel);
        // arg0 is undefined - throw
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "TypeError: fetch requires a URL argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notUndefinedLabel);
        il.Emit(OpCodes.Pop); // pop the duplicated arg0

        // string url = arg0?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, urlLocal);

        // Extract redirect option from options (default: "follow")
        var redirectLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, "follow");
        il.Emit(OpCodes.Stloc, redirectLocal);

        var skipRedirectExtract = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, skipRedirectExtract);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "redirect");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        var redirectValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, redirectValLocal);
        il.Emit(OpCodes.Ldloc, redirectValLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skipRedirectExtract);
        il.Emit(OpCodes.Ldloc, redirectValLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, redirectLocal);
        il.MarkLabel(skipRedirectExtract);

        // Extract credentials option from options (default: "same-origin")
        // 'omit' → useCookies=false, 'same-origin'/'include'/anything-else → useCookies=true
        var useCookiesLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, useCookiesLocal);

        var skipCredsExtract = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, skipCredsExtract);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "credentials");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        var credsValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, credsValLocal);
        il.Emit(OpCodes.Ldloc, credsValLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skipCredsExtract);

        // useCookies = (creds != "omit")
        il.Emit(OpCodes.Ldloc, credsValLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldstr, "omit");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Inequality", _types.String, _types.String));
        il.Emit(OpCodes.Stloc, useCookiesLocal);
        il.MarkLabel(skipCredsExtract);

        // Select cached HttpClient based on (redirect mode, useCookies)
        // Uses four static fields wired into a 2-arg selector. Lazy-initialized on
        // first use to avoid static constructor ordering issues.
        il.Emit(OpCodes.Ldloc, redirectLocal);
        il.Emit(OpCodes.Ldloc, useCookiesLocal);
        il.Emit(OpCodes.Call, _getOrCreateHttpClientMethod!);
        il.Emit(OpCodes.Stloc, clientLocal);

        // Parse method from options (default: "GET")
        var useDefaultMethod1Label = il.DefineLabel();
        var useDefaultMethod2Label = il.DefineLabel();
        var methodDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, useDefaultMethod1Label);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "method");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, useDefaultMethod2Label);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, methodStrLocal);
        il.Emit(OpCodes.Br, methodDoneLabel);

        il.MarkLabel(useDefaultMethod2Label);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "GET");
        il.Emit(OpCodes.Stloc, methodStrLocal);
        il.Emit(OpCodes.Br, methodDoneLabel);

        il.MarkLabel(useDefaultMethod1Label);
        il.Emit(OpCodes.Ldstr, "GET");
        il.Emit(OpCodes.Stloc, methodStrLocal);

        il.MarkLabel(methodDoneLabel);

        // Create HttpRequestMessage
        var httpMethodCtor = _httpMethodType!.GetConstructor([_types.String])!;
        var requestCtor = _httpRequestMessageType!.GetConstructor([_httpMethodType, _types.String])!;

        il.Emit(OpCodes.Ldloc, methodStrLocal);
        il.Emit(OpCodes.Newobj, httpMethodCtor);
        il.Emit(OpCodes.Ldloc, urlLocal);
        il.Emit(OpCodes.Newobj, requestCtor);
        il.Emit(OpCodes.Stloc, requestLocal);

        // Apply headers from options
        il.Emit(OpCodes.Ldloc, requestLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, applyHeadersMethod);

        // Apply body from options if present
        var bodyDoneLabel = il.DefineLabel();
        var hasBodyLabel = il.DefineLabel();
        var hasBodyLabel2 = il.DefineLabel();

        // if (options == null) skip body
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, bodyDoneLabel);

        // Get "body" property from options
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "body");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, hasBodyLabel);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, hasBodyLabel2);

        // Body is present and not undefined - convert to string and set as content
        il.Emit(OpCodes.Call, runtime.Stringify);
        var bodyStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, bodyStrLocal);

        // request.Content = new StringContent(bodyStr)
        il.Emit(OpCodes.Ldloc, requestLocal);
        il.Emit(OpCodes.Ldloc, bodyStrLocal);
        il.Emit(OpCodes.Newobj, _stringContentType!.GetConstructor([_types.String])!);
        var requestContentProperty = _httpRequestMessageType!.GetProperty("Content")!;
        il.Emit(OpCodes.Callvirt, requestContentProperty.GetSetMethod()!);
        il.Emit(OpCodes.Br, bodyDoneLabel);

        // Body is null or undefined - pop and skip
        il.MarkLabel(hasBodyLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, bodyDoneLabel);

        il.MarkLabel(hasBodyLabel2);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(bodyDoneLabel);

        // Check abort signal: if options?.signal?.aborted, throw
        var signalCheckDoneLabel = il.DefineLabel();
        var noSignalLabel1 = il.DefineLabel();
        var noSignalLabel2 = il.DefineLabel();
        var noSignalLabel3 = il.DefineLabel();

        // if (options == null) skip
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, signalCheckDoneLabel);

        // Get "signal" property from options
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "signal");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        var signalLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, signalLocal);

        // if (signal == null || signal is Undefined) skip
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Brfalse, signalCheckDoneLabel);
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, signalCheckDoneLabel);

        // Check if signal is aborted: AbortSignalGetAborted(signal)
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, runtime.AbortSignalGetAborted);
        il.Emit(OpCodes.Brfalse, signalCheckDoneLabel);

        // Signal is aborted - throw AbortError
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, runtime.AbortSignalGetReason);
        var abortReasonLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, abortReasonLocal);
        il.Emit(OpCodes.Ldloc, abortReasonLocal);
        var hasAbortReasonLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasAbortReasonLabel);
        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        var throwAbortLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, throwAbortLabel);
        il.MarkLabel(hasAbortReasonLabel);
        il.Emit(OpCodes.Ldloc, abortReasonLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.MarkLabel(throwAbortLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(signalCheckDoneLabel);

        // var response = client.SendAsync(request).Result
        var sendAsyncMethod = _httpClientType!.GetMethod("SendAsync", [_httpRequestMessageType])!;
        var taskOfResponseType = sendAsyncMethod.ReturnType;
        var getResultMethod = taskOfResponseType.GetProperty("Result")!.GetGetMethod()!;

        il.Emit(OpCodes.Ldloc, clientLocal);
        il.Emit(OpCodes.Ldloc, requestLocal);
        il.Emit(OpCodes.Callvirt, sendAsyncMethod);
        il.Emit(OpCodes.Callvirt, getResultMethod);
        il.Emit(OpCodes.Stloc, responseLocal);

        // If redirect mode is "error" and status is 3xx, throw
        var statusCodeProperty2 = _httpResponseMessageType!.GetProperty("StatusCode")!;
        var skipRedirectError = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, redirectLocal);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipRedirectError);
        // Check status >= 300 && status < 400
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, statusCodeProperty2.GetGetMethod()!);
        il.Emit(OpCodes.Conv_I4);
        var notRedirectStatus = il.DefineLabel();
        var redirectStatusLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, redirectStatusLocal);
        il.Emit(OpCodes.Ldloc, redirectStatusLocal);
        il.Emit(OpCodes.Ldc_I4, 300);
        il.Emit(OpCodes.Blt, notRedirectStatus);
        il.Emit(OpCodes.Ldloc, redirectStatusLocal);
        il.Emit(OpCodes.Ldc_I4, 400);
        il.Emit(OpCodes.Bge, notRedirectStatus);
        // It's a redirect in error mode — throw
        il.Emit(OpCodes.Ldstr, "fetch failed: redirect mode is set to 'error'");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notRedirectStatus);
        il.MarkLabel(skipRedirectError);

        // byte[] bodyBytes = response.Content.ReadAsByteArrayAsync().Result
        var contentProperty = _httpResponseMessageType!.GetProperty("Content")!;
        var readAsByteArrayMethod = _httpContentType!.GetMethod("ReadAsByteArrayAsync", Type.EmptyTypes)!;
        var taskOfBytesResultProp = readAsByteArrayMethod.ReturnType.GetProperty("Result")!;

        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, contentProperty.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, readAsByteArrayMethod);
        il.Emit(OpCodes.Callvirt, taskOfBytesResultProp.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, bodyBytesLocal);

        // double status = (double)response.StatusCode
        var statusCodeProperty = _httpResponseMessageType.GetProperty("StatusCode")!;
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, statusCodeProperty.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, statusLocal);

        // bool ok = response.IsSuccessStatusCode
        var isSuccessProperty = _httpResponseMessageType.GetProperty("IsSuccessStatusCode")!;
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, isSuccessProperty.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, okLocal);

        // string statusText = response.ReasonPhrase ?? ""
        var reasonPhraseProperty = _httpResponseMessageType.GetProperty("ReasonPhrase")!;
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, reasonPhraseProperty.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        var hasValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasValueLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(hasValueLabel);
        il.Emit(OpCodes.Stloc, statusTextLocal);

        // headers = new Dictionary<string, object>() - populated with response headers
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, headersLocal);

        // Extract response headers into dictionary
        // Iterate response.Headers
        var responseHeadersProperty = _httpResponseMessageType!.GetProperty("Headers")!;
        var getEnumeratorMethodForHeaders = typeof(IEnumerable<KeyValuePair<string, IEnumerable<string>>>)
            .GetMethod("GetEnumerator")!;
        var moveNextMethodForHeaders = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;

        // For simplicity, use reflection to iterate:
        // foreach (var header in response.Headers) { headers[header.Key.ToLower()] = string.Join(", ", header.Value); }
        // Since HttpResponseHeaders implements IEnumerable<KVP<string, IEnumerable<string>>>,
        // we just call the enumerator pattern.

        // Get enumerator from response.Headers (which is HttpResponseHeaders : IEnumerable<KVP<string, IEnumerable<string>>>)
        var respHdrsLocal = il.DeclareLocal(_httpResponseHeadersType!);
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, responseHeadersProperty.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, respHdrsLocal);

        var kvpEnumerableType = typeof(IEnumerable<KeyValuePair<string, IEnumerable<string>>>);
        var kvpEnumeratorType = typeof(IEnumerator<KeyValuePair<string, IEnumerable<string>>>);
        var hdrEnumeratorLocal = il.DeclareLocal(kvpEnumeratorType);

        il.Emit(OpCodes.Ldloc, respHdrsLocal);
        il.Emit(OpCodes.Callvirt, kvpEnumerableType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, hdrEnumeratorLocal);

        var hdrLoopStart = il.DefineLabel();
        var hdrLoopEnd = il.DefineLabel();
        il.MarkLabel(hdrLoopStart);
        il.Emit(OpCodes.Ldloc, hdrEnumeratorLocal);
        il.Emit(OpCodes.Callvirt, moveNextMethodForHeaders);
        il.Emit(OpCodes.Brfalse, hdrLoopEnd);

        // Get Current
        var hdrCurrentProp = kvpEnumeratorType.GetProperty("Current")!;
        var hdrKvpType = typeof(KeyValuePair<string, IEnumerable<string>>);
        var hdrKvpLocal = il.DeclareLocal(hdrKvpType);
        il.Emit(OpCodes.Ldloc, hdrEnumeratorLocal);
        il.Emit(OpCodes.Callvirt, hdrCurrentProp.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hdrKvpLocal);

        // string lowerKey = kvp.Key.ToLowerInvariant();
        var lowerKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, hdrKvpLocal);
        il.Emit(OpCodes.Call, hdrKvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, lowerKeyLocal);

        // if (lowerKey == "set-cookie") store as List<string>; else store joined string.
        var notSetCookieLabel = il.DefineLabel();
        var afterStoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lowerKeyLocal);
        il.Emit(OpCodes.Ldstr, "set-cookie");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notSetCookieLabel);

        // Set-Cookie branch: headers["set-cookie"] = new List<string>(kvp.Value)
        var listStringCtorFromEnum = typeof(List<string>).GetConstructor([typeof(IEnumerable<string>)])!;
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Ldloc, lowerKeyLocal);
        il.Emit(OpCodes.Ldloca, hdrKvpLocal);
        il.Emit(OpCodes.Call, hdrKvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, listStringCtorFromEnum);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, afterStoreLabel);

        // Default branch: headers[lowerKey] = string.Join(", ", kvp.Value)
        il.MarkLabel(notSetCookieLabel);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Ldloc, lowerKeyLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloca, hdrKvpLocal);
        il.Emit(OpCodes.Call, hdrKvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, typeof(IEnumerable<string>)])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.MarkLabel(afterStoreLabel);
        il.Emit(OpCodes.Br, hdrLoopStart);
        il.MarkLabel(hdrLoopEnd);

        // Also extract Content headers
        var contentPropertyForHeaders = _httpResponseMessageType.GetProperty("Content")!;
        var contentHeadersProperty = _httpContentType!.GetProperty("Headers")!;

        var contentLocal = il.DeclareLocal(_httpContentType);
        il.Emit(OpCodes.Ldloc, responseLocal);
        il.Emit(OpCodes.Callvirt, contentPropertyForHeaders.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, contentLocal);

        var noContentLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, contentLocal);
        il.Emit(OpCodes.Brfalse, noContentLabel);

        var contentHdrsLocal = il.DeclareLocal(_httpContentHeadersType!);
        il.Emit(OpCodes.Ldloc, contentLocal);
        il.Emit(OpCodes.Callvirt, contentHeadersProperty.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, contentHdrsLocal);

        var noContentHdrsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, contentHdrsLocal);
        il.Emit(OpCodes.Brfalse, noContentHdrsLabel);

        var chdrEnumeratorLocal = il.DeclareLocal(kvpEnumeratorType);
        il.Emit(OpCodes.Ldloc, contentHdrsLocal);
        il.Emit(OpCodes.Callvirt, kvpEnumerableType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, chdrEnumeratorLocal);

        var chdrLoopStart = il.DefineLabel();
        var chdrLoopEnd = il.DefineLabel();
        il.MarkLabel(chdrLoopStart);
        il.Emit(OpCodes.Ldloc, chdrEnumeratorLocal);
        il.Emit(OpCodes.Callvirt, moveNextMethodForHeaders);
        il.Emit(OpCodes.Brfalse, chdrLoopEnd);

        var chdrKvpLocal = il.DeclareLocal(hdrKvpType);
        il.Emit(OpCodes.Ldloc, chdrEnumeratorLocal);
        il.Emit(OpCodes.Callvirt, hdrCurrentProp.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, chdrKvpLocal);

        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Ldloca, chdrKvpLocal);
        il.Emit(OpCodes.Call, hdrKvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloca, chdrKvpLocal);
        il.Emit(OpCodes.Call, hdrKvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, typeof(IEnumerable<string>)])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Br, chdrLoopStart);
        il.MarkLabel(chdrLoopEnd);

        il.MarkLabel(noContentHdrsLabel);
        il.MarkLabel(noContentLabel);

        // NOTE: do not dispose the client — it is one of the cached static instances
        // shared across all fetch calls (see EmitCachedHttpClients).

        // Build success result array
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        // [0] = true (success)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stelem_Ref);

        // [1] = status
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, statusLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // [2] = statusText
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, statusTextLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // [3] = ok
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldloc, okLocal);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stelem_Ref);

        // [4] = url
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldloc, urlLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // [5] = headers
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // [6] = bodyBytes
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Ldloc, bodyBytesLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // [7] = null (no error)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);

        // Leave try block and return result
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Leave, endLabel);

        // Catch block for Exception
        il.BeginCatchBlock(_types.Exception);
        var exLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);

        // Build error result array
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        // [0] = false (failure)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stelem_Ref);

        // [7] = "fetch failed: " + ex.Message
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Ldstr, "fetch failed: ");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.Exception.GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Leave, endLabel);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits a helper method to apply request headers from an options object.
    /// ApplyRequestHeaders(HttpRequestMessage request, object? options)
    /// </summary>
    private MethodBuilder EmitApplyRequestHeaders(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ApplyRequestHeaders",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            [_httpRequestMessageType!, _types.Object]
        );

        var il = method.GetILGenerator();

        // if (options == null) return
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Get "headers" property from options
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "headers");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        var headersObjLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, headersObjLocal);

        il.Emit(OpCodes.Ldloc, headersObjLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check if it's a Dictionary<string, object?> (plain object literal)
        var isDictLabel = il.DefineLabel();
        var checkHeadersTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, headersObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var headersLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, headersLocal);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Brtrue, isDictLabel);

        // Not a dict - check if it's a $Headers instance
        il.Emit(OpCodes.Ldloc, headersObjLocal);
        il.Emit(OpCodes.Isinst, runtime.TSHeadersType);
        il.Emit(OpCodes.Brfalse, endLabel);

        // It's a $Headers - get its _data field and iterate
        // _data is Dictionary<string, List<string>>
        var listOfStringType = typeof(List<string>);
        var dictOfListType = typeof(Dictionary<string, List<string>>);
        var headersDataLocal = il.DeclareLocal(dictOfListType);
        il.Emit(OpCodes.Ldloc, headersObjLocal);
        il.Emit(OpCodes.Castclass, runtime.TSHeadersType);
        il.Emit(OpCodes.Ldfld, _headersDataField);
        il.Emit(OpCodes.Stloc, headersDataLocal);

        // Iterate _data entries
        var hGetEnumMethod = dictOfListType.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        var hEnumType = hGetEnumMethod.ReturnType;
        var hEnumLocal = il.DeclareLocal(hEnumType);
        il.Emit(OpCodes.Ldloc, headersDataLocal);
        il.Emit(OpCodes.Callvirt, hGetEnumMethod);
        il.Emit(OpCodes.Stloc, hEnumLocal);

        var hLoopStart = il.DefineLabel();
        var hLoopEnd = il.DefineLabel();
        il.MarkLabel(hLoopStart);
        il.Emit(OpCodes.Ldloca, hEnumLocal);
        il.Emit(OpCodes.Call, hEnumType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, hLoopEnd);

        var hCurrentProp = hEnumType.GetProperty("Current")!;
        var hKvpType = hCurrentProp.PropertyType;
        var hKvpLocal = il.DeclareLocal(hKvpType);
        il.Emit(OpCodes.Ldloca, hEnumLocal);
        il.Emit(OpCodes.Call, hCurrentProp.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hKvpLocal);

        // string key = kvp.Key
        var hKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, hKvpLocal);
        il.Emit(OpCodes.Call, hKvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hKeyLocal);

        // Skip Content-Type
        var hNotCtLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hKeyLocal);
        il.Emit(OpCodes.Ldstr, "Content-Type");
        var stringEqualsMethod = _types.String.GetMethod("Equals", [_types.String, _types.String, typeof(StringComparison)])!;
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, stringEqualsMethod);
        il.Emit(OpCodes.Brfalse, hNotCtLabel);
        il.Emit(OpCodes.Br, hLoopStart);

        il.MarkLabel(hNotCtLabel);

        // string value = string.Join(", ", kvp.Value)
        var hValueLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloca, hKvpLocal);
        il.Emit(OpCodes.Call, hKvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Join", [_types.String, typeof(IEnumerable<string>)])!);
        il.Emit(OpCodes.Stloc, hValueLocal);

        // request.Headers.TryAddWithoutValidation(key, value)
        var headersProperty = _httpRequestMessageType!.GetProperty("Headers")!;
        var tryAddMethod = _httpRequestHeadersType!.GetMethod("TryAddWithoutValidation", [_types.String, _types.String])!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, headersProperty.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, hKeyLocal);
        il.Emit(OpCodes.Ldloc, hValueLocal);
        il.Emit(OpCodes.Callvirt, tryAddMethod);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, hLoopStart);
        il.MarkLabel(hLoopEnd);

        // Dispose enumerator
        var hDisposeMethod = hEnumType.GetMethod("Dispose", Type.EmptyTypes);
        if (hDisposeMethod != null)
        {
            il.Emit(OpCodes.Ldloca, hEnumLocal);
            il.Emit(OpCodes.Call, hDisposeMethod);
        }

        il.Emit(OpCodes.Br, endLabel);

        // Dictionary path (plain object literal headers)
        il.MarkLabel(isDictLabel);

        // Get enumerator: var enumerator = headers.GetEnumerator()
        var getEnumeratorMethod = _types.DictionaryStringObject.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        var enumeratorType = getEnumeratorMethod.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);

        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Callvirt, getEnumeratorMethod);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop: while (enumerator.MoveNext()) { ... }
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var moveNextMethod = enumeratorType.GetMethod("MoveNext")!;
        il.Emit(OpCodes.Call, moveNextMethod);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // Get current key and value
        var currentProperty = enumeratorType.GetProperty("Current")!;
        var kvpType = currentProperty.PropertyType;
        var kvpLocal = il.DeclareLocal(kvpType);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, currentProperty.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // string key = kvp.Key
        var keyProperty = kvpType.GetProperty("Key")!;
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, keyProperty.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Skip Content-Type header (it's set via Content)
        var notContentTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldstr, "Content-Type");
        var stringEqualsOrdinalIgnoreCase = _types.String.GetMethod("Equals", [_types.String, _types.String, typeof(StringComparison)])!;
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, stringEqualsOrdinalIgnoreCase);
        il.Emit(OpCodes.Brfalse, notContentTypeLabel);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(notContentTypeLabel);

        // string value = Stringify(kvp.Value)
        var valueProperty = kvpType.GetProperty("Value")!;
        var valueLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, valueProperty.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, valueLocal);

        // request.Headers.TryAddWithoutValidation(key, value)
        var dictHeadersProperty = _httpRequestMessageType!.GetProperty("Headers")!;
        var dictTryAddMethod = _httpRequestHeadersType!.GetMethod("TryAddWithoutValidation", [_types.String, _types.String])!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, dictHeadersProperty.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryAddMethod);
        il.Emit(OpCodes.Pop); // Discard bool result

        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // Dispose enumerator if IDisposable
        var disposeMethod = enumeratorType.GetMethod("Dispose", Type.EmptyTypes);
        if (disposeMethod != null)
        {
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, disposeMethod);
        }

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits helper method to extract headers from HttpResponseMessage as a dictionary object.
    /// Uses the reflection-based ExtractResponseHeadersHelper to avoid compile-time dependency on SharpTS.dll.
    /// </summary>
    private MethodBuilder EmitExtractResponseHeaders(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExtractResponseHeaders",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_httpResponseMessageType!]
        );

        var il = method.GetILGenerator();

        // Call the reflection-based helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ExtractResponseHeadersHelper);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits: public static object HttpCreateServer(object? callback)
    /// Creates a new $HttpServer instance with EventEmitter support.
    /// Pure-IL implementation using emitted $HttpServer type - no SharpTS dependency.
    /// </summary>
    private void EmitHttpCreateServer(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpCreateServer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.HttpCreateServer = method;

        var il = method.GetILGenerator();

        // Create new $HttpServer(callback) directly
        // In compiled mode, callback is already a $TSFunction or $BoundTSFunction
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSHttpServerCtor);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object HttpRequest(object urlOrOptions, object? options)
    /// Delegates to Fetch.
    /// </summary>
    private void EmitHttpRequest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpRequest",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.HttpRequest = method;

        var il = method.GetILGenerator();

        // Delegate to Fetch
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Fetch);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object HttpGet(object urlOrOptions, object? options)
    /// Delegates to Fetch (GET is default).
    /// </summary>
    private void EmitHttpGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.HttpGet = method;

        var il = method.GetILGenerator();

        // Delegate to Fetch
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Fetch);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object HttpGetMethods()
    /// Returns array of HTTP method names.
    /// </summary>
    private void EmitHttpGetMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpGetMethods",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.HttpGetMethods = method;

        var il = method.GetILGenerator();

        // Create array with common HTTP methods
        string[] methods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "CONNECT", "TRACE"];

        il.Emit(OpCodes.Ldc_I4, methods.Length);
        il.Emit(OpCodes.Newarr, _types.Object);

        for (int i = 0; i < methods.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldstr, methods[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        // Wrap in $Array
        il.Emit(OpCodes.Call, runtime.CreateArray);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object HttpGetStatusCodes()
    /// Returns object mapping status codes to messages.
    /// </summary>
    private void EmitHttpGetStatusCodes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpGetStatusCodes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.HttpGetStatusCodes = method;

        var il = method.GetILGenerator();

        // Create new dictionary for the status codes object
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));

        // Add common status codes
        (string code, string message)[] statusCodes =
        [
            ("200", "OK"),
            ("201", "Created"),
            ("204", "No Content"),
            ("301", "Moved Permanently"),
            ("302", "Found"),
            ("304", "Not Modified"),
            ("400", "Bad Request"),
            ("401", "Unauthorized"),
            ("403", "Forbidden"),
            ("404", "Not Found"),
            ("405", "Method Not Allowed"),
            ("500", "Internal Server Error"),
            ("501", "Not Implemented"),
            ("502", "Bad Gateway"),
            ("503", "Service Unavailable")
        ];

        foreach (var (code, message) in statusCodes)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, code);
            il.Emit(OpCodes.Ldstr, message);
            il.Emit(OpCodes.Call, runtime.SetProperty);
        }

        il.Emit(OpCodes.Ret);
    }

    // Agent helper method references
    private MethodBuilder _agentDestroyMethod = null!;
    private MethodBuilder _agentGetNameMethod = null!;

    /// <summary>
    /// Emits static helper methods for Agent instances: AgentDestroy and AgentGetName.
    /// These are wrapped as TSFunction and added to each Agent dictionary.
    /// </summary>
    private void EmitAgentHelperMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // AgentDestroy(object?[] args) → null (no-op)
        _agentDestroyMethod = typeBuilder.DefineMethod(
            "AgentDestroy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]);
        {
            var il = _agentDestroyMethod.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // AgentGetName(object?[] args) → string "host:port:localAddress:family"
        _agentGetNameMethod = typeBuilder.DefineMethod(
            "AgentGetName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]);
        {
            var il = _agentGetNameMethod.GetILGenerator();

            // Default values
            var hostLocal = il.DeclareLocal(_types.String);
            var portLocal = il.DeclareLocal(_types.String);
            var localAddrLocal = il.DeclareLocal(_types.String);
            var familyLocal = il.DeclareLocal(_types.String);

            il.Emit(OpCodes.Ldstr, "localhost");
            il.Emit(OpCodes.Stloc, hostLocal);
            il.Emit(OpCodes.Ldstr, "80");
            il.Emit(OpCodes.Stloc, portLocal);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Stloc, localAddrLocal);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Stloc, familyLocal);

            // If args.Length > 0 && args[0] != null, extract options
            var useDefaults = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, useDefaults);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Brfalse, useDefaults);

            // Extract properties from options using GetProperty
            var optionsLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, optionsLocal);

            EmitAgentExtractStringOption(il, runtime, optionsLocal, "host", hostLocal);
            EmitAgentExtractDoubleAsIntStringOption(il, runtime, optionsLocal, "port", portLocal);
            EmitAgentExtractStringOption(il, runtime, optionsLocal, "localAddress", localAddrLocal);
            EmitAgentExtractDoubleAsIntStringOption(il, runtime, optionsLocal, "family", familyLocal);

            il.MarkLabel(useDefaults);

            // Return string.Concat(host, ":", port, ":", localAddress, ":", family)
            il.Emit(OpCodes.Ldc_I4, 7);
            il.Emit(OpCodes.Newarr, _types.String);

            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldloc, hostLocal); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldstr, ":"); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldloc, portLocal); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Ldstr, ":"); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Ldloc, localAddrLocal); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_5); il.Emit(OpCodes.Ldstr, ":"); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_6); il.Emit(OpCodes.Ldloc, familyLocal); il.Emit(OpCodes.Stelem_Ref);

            il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string[])])!);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitAgentExtractStringOption(ILGenerator il, EmittedRuntime runtime,
        LocalBuilder optionsLocal, string propName, LocalBuilder targetLocal)
    {
        var skipLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        // If value is string, use it
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, targetLocal);
        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits IL to add destroy() and getName() TSFunction methods to an Agent dictionary.
    /// Expects the dictionary on top of the stack (with Dup pattern).
    /// </summary>
    private void EmitAgentMethods(ILGenerator il, EmittedRuntime runtime)
    {
        // Add destroy method as TSFunction
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "destroy");
        il.Emit(OpCodes.Ldnull); // target (static)
        il.Emit(OpCodes.Ldtoken, _agentDestroyMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // Add getName method as TSFunction
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "getName");
        il.Emit(OpCodes.Ldnull); // target (static)
        il.Emit(OpCodes.Ldtoken, _agentGetNameMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Call, runtime.SetProperty);
    }

    private void EmitAgentExtractDoubleAsIntStringOption(ILGenerator il, EmittedRuntime runtime,
        LocalBuilder optionsLocal, string propName, LocalBuilder targetLocal)
    {
        var skipLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        // If value is boxed double, convert to int string
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, skipLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        var intLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Stloc, intLocal);
        il.Emit(OpCodes.Ldloca, intLocal);
        il.Emit(OpCodes.Call, typeof(int).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, targetLocal);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits: public static object HttpGetGlobalAgent()
    /// Returns the global agent object with full Agent properties.
    /// </summary>
    private void EmitHttpGetGlobalAgent(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "HttpGetGlobalAgent",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.HttpGetGlobalAgent = method;

        var il = method.GetILGenerator();
        EmitAgentObjectCreation(il, runtime, keepAlive: true, maxSockets: double.PositiveInfinity,
            maxTotalSockets: double.PositiveInfinity, maxFreeSockets: 256, keepAliveMsecs: 1000, timeout: 0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object HttpGetAgentConstructor()
    /// Returns a TSFunction that creates new Agent instances from options.
    /// </summary>
    private void EmitHttpGetAgentConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit the Agent factory method: AgentFactory(object? options) -> object
        var factoryMethod = typeBuilder.DefineMethod(
            "AgentFactory",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.HttpAgentFactory = factoryMethod;

        {
            var il = factoryMethod.GetILGenerator();

            // Get keepAlive from options (default: false)
            var keepAliveLocal = il.DeclareLocal(_types.Boolean);
            var maxSocketsLocal = il.DeclareLocal(_types.Double);
            var maxTotalSocketsLocal = il.DeclareLocal(_types.Double);
            var maxFreeSocketsLocal = il.DeclareLocal(_types.Double);
            var keepAliveMsecsLocal = il.DeclareLocal(_types.Double);
            var timeoutLocal = il.DeclareLocal(_types.Double);

            // Set defaults
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, keepAliveLocal);
            il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
            il.Emit(OpCodes.Stloc, maxSocketsLocal);
            il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
            il.Emit(OpCodes.Stloc, maxTotalSocketsLocal);
            il.Emit(OpCodes.Ldc_R8, 256.0);
            il.Emit(OpCodes.Stloc, maxFreeSocketsLocal);
            il.Emit(OpCodes.Ldc_R8, 1000.0);
            il.Emit(OpCodes.Stloc, keepAliveMsecsLocal);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Stloc, timeoutLocal);

            // If options is not null and is a dictionary, extract values
            var createObjLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, createObjLabel);

            // Try to extract keepAlive
            EmitExtractBoolOption(il, runtime, "keepAlive", keepAliveLocal);
            EmitExtractDoubleOption(il, runtime, "maxSockets", maxSocketsLocal);
            EmitExtractDoubleOption(il, runtime, "maxTotalSockets", maxTotalSocketsLocal);
            EmitExtractDoubleOption(il, runtime, "maxFreeSockets", maxFreeSocketsLocal);
            EmitExtractDoubleOption(il, runtime, "keepAliveMsecs", keepAliveMsecsLocal);
            EmitExtractDoubleOption(il, runtime, "timeout", timeoutLocal);

            il.MarkLabel(createObjLabel);

            // Create agent object
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));

            // Set all properties
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "keepAlive");
            il.Emit(OpCodes.Ldloc, keepAliveLocal);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "keepAliveMsecs");
            il.Emit(OpCodes.Ldloc, keepAliveMsecsLocal);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "maxSockets");
            il.Emit(OpCodes.Ldloc, maxSocketsLocal);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "maxTotalSockets");
            il.Emit(OpCodes.Ldloc, maxTotalSocketsLocal);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "maxFreeSockets");
            il.Emit(OpCodes.Ldloc, maxFreeSocketsLocal);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "timeout");
            il.Emit(OpCodes.Ldloc, timeoutLocal);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "scheduling");
            il.Emit(OpCodes.Ldstr, "lifo");
            il.Emit(OpCodes.Call, runtime.SetProperty);

            // Add empty objects for sockets/freeSockets/requests
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "sockets");
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "freeSockets");
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Call, runtime.SetProperty);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "requests");
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Call, runtime.SetProperty);

            // Add methods
            EmitAgentMethods(il, runtime);

            // Wrap in $Object
            il.Emit(OpCodes.Call, runtime.CreateObject);
            il.Emit(OpCodes.Ret);
        }

        // Emit the getter that returns a TSFunction wrapping AgentFactory
        var getterMethod = typeBuilder.DefineMethod(
            "HttpGetAgentConstructor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.HttpGetAgentConstructor = getterMethod;

        {
            var il = getterMethod.GetILGenerator();
            il.Emit(OpCodes.Ldnull); // target (static)
            il.Emit(OpCodes.Ldtoken, factoryMethod);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitExtractBoolOption(ILGenerator il, EmittedRuntime runtime,
        string name, LocalBuilder local)
    {
        var skipLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // GetProperty(options, name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, skipLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Stloc, local);

        il.MarkLabel(skipLabel);
    }

    private void EmitExtractDoubleOption(ILGenerator il, EmittedRuntime runtime,
        string name, LocalBuilder local)
    {
        var skipLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // GetProperty(options, name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, skipLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, local);

        il.MarkLabel(skipLabel);
    }

    private void EmitAgentObjectCreation(ILGenerator il, EmittedRuntime runtime,
        bool keepAlive, double maxSockets, double maxTotalSockets,
        double maxFreeSockets, double keepAliveMsecs, double timeout)
    {
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "keepAlive");
        il.Emit(keepAlive ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "keepAliveMsecs");
        il.Emit(OpCodes.Ldc_R8, keepAliveMsecs);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "maxSockets");
        il.Emit(OpCodes.Ldc_R8, maxSockets);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "maxTotalSockets");
        il.Emit(OpCodes.Ldc_R8, maxTotalSockets);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "maxFreeSockets");
        il.Emit(OpCodes.Ldc_R8, maxFreeSockets);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "timeout");
        il.Emit(OpCodes.Ldc_R8, timeout);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "scheduling");
        il.Emit(OpCodes.Ldstr, "lifo");
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // Add empty objects for sockets/freeSockets/requests
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "sockets");
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "freeSockets");
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "requests");
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Call, runtime.SetProperty);

        // Add methods
        EmitAgentMethods(il, runtime);

        // Wrap in $Object
        il.Emit(OpCodes.Call, runtime.CreateObject);
    }

    // ===== $Request class fields =====
    private FieldBuilder _requestMethodField = null!;
    private FieldBuilder _requestUrlField = null!;
    private FieldBuilder _requestHeadersField = null!;
    private FieldBuilder _requestBodyField = null!;
    private FieldBuilder _requestBodyBytesField = null!;
    private FieldBuilder _requestBodyConsumedField = null!;

    /// <summary>
    /// Emits the $Request class for standalone Request constructor support.
    /// Constructor: (object url, object? init) — parses init for method, headers, body.
    /// </summary>
    private void EmitRequestClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$Request",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        _requestMethodField = typeBuilder.DefineField("_method", _types.String, FieldAttributes.Private);
        _requestUrlField = typeBuilder.DefineField("_url", _types.String, FieldAttributes.Private);
        _requestHeadersField = typeBuilder.DefineField("_headers", _types.Object, FieldAttributes.Private);
        _requestBodyField = typeBuilder.DefineField("_body", _types.Object, FieldAttributes.Private);
        _requestBodyBytesField = typeBuilder.DefineField("_bodyBytes", _types.ByteArray, FieldAttributes.Private);
        _requestBodyConsumedField = typeBuilder.DefineField("_bodyConsumed", _types.Boolean, FieldAttributes.Private);

        // Constructor: (object url, object? init)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]
        );

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _url = url?.ToString() ?? ""
        var urlLocal = il.DeclareLocal(_types.String);
        EmitArgToString(il, 1, urlLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, urlLocal);
        il.Emit(OpCodes.Stfld, _requestUrlField);

        // _method = "GET" (default)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GET");
        il.Emit(OpCodes.Stfld, _requestMethodField);

        // _headers = new $Headers(null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, runtime.TSHeadersCtor);
        il.Emit(OpCodes.Stfld, _requestHeadersField);

        // if (init != null) parse init properties
        var endInit = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, endInit);

        // _method = GetProperty(init, "method")?.ToString()?.ToUpperInvariant() ?? "GET"
        var methodLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "method");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, methodLocal);

        var skipMethod = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, skipMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToUpperInvariant")!);
        il.Emit(OpCodes.Stfld, _requestMethodField);
        il.MarkLabel(skipMethod);

        // headers from init
        var headersLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "headers");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, headersLocal);

        var skipHeaders = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Brfalse, skipHeaders);
        // _headers = new $Headers(headersObj)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Newobj, runtime.TSHeadersCtor);
        il.Emit(OpCodes.Stfld, _requestHeadersField);
        il.MarkLabel(skipHeaders);

        // body from init
        var bodyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "body");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, bodyLocal);

        var skipBody = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Brfalse, skipBody);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Stfld, _requestBodyField);
        il.MarkLabel(skipBody);

        il.MarkLabel(endInit);
        il.Emit(OpCodes.Ret);

        // Property getters
        EmitFetchResponsePropertyGetter(typeBuilder, "method", _types.String, _requestMethodField);
        EmitFetchResponsePropertyGetter(typeBuilder, "url", _types.String, _requestUrlField);
        EmitFetchResponsePropertyGetter(typeBuilder, "headers", _types.Object, _requestHeadersField);
        EmitFetchResponsePropertyGetter(typeBuilder, "body", _types.Object, _requestBodyField);
        EmitFetchResponsePropertyGetter(typeBuilder, "bodyUsed", _types.Boolean, _requestBodyConsumedField);

        // Body reading methods (text, json, arrayBuffer) and clone
        EmitRequestTextMethod(typeBuilder, runtime);
        EmitRequestJsonMethod(typeBuilder, runtime);
        EmitRequestArrayBufferMethod(typeBuilder, runtime);
        EmitRequestCloneMethod(typeBuilder, runtime, ctor);

        runtime.TSRequestType = typeBuilder;
        runtime.TSRequestCtor = ctor;

        typeBuilder.CreateType();
    }

    private void EmitRequestTextMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("text", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        // Get body as string: body?.ToString() ?? ""
        var bodyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestBodyField);
        il.Emit(OpCodes.Stloc, bodyLocal);

        var hasBody = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Brtrue, hasBody);

        // No body — return Promise.resolve("")
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(hasBody);
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);

        il.MarkLabel(done);
        // Mark consumed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _requestBodyConsumedField);
        // Wrap in Promise
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRequestJsonMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("json", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        // Get body as string then parse JSON
        var bodyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestBodyField);
        il.Emit(OpCodes.Stloc, bodyLocal);

        var hasBody = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Brtrue, hasBody);

        // No body — parse empty string
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(hasBody);
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Call, runtime.JsonParse);
        // Mark consumed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _requestBodyConsumedField);
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRequestArrayBufferMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("arrayBuffer", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        // Convert body to bytes, create Buffer
        var bodyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestBodyField);
        il.Emit(OpCodes.Stloc, bodyLocal);

        var hasBody = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Brtrue, hasBody);

        // No body — empty buffer
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(hasBody);
        // Encoding.UTF8.GetBytes(body.ToString())
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.ByteArray])!.DeclaringType!
            .GetMethod("GetBytes", [_types.String])!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        // Mark consumed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _requestBodyConsumedField);
        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRequestCloneMethod(TypeBuilder typeBuilder, EmittedRuntime runtime, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod("clone", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        // Build an init object with current properties, call constructor
        // For simplicity: new $Request(url, null) then copy fields
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestUrlField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, ctor);

        // Copy _method
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestMethodField);
        il.Emit(OpCodes.Stfld, _requestMethodField);

        // Copy _headers
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestHeadersField);
        il.Emit(OpCodes.Stfld, _requestHeadersField);

        // Copy _body
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _requestBodyField);
        il.Emit(OpCodes.Stfld, _requestBodyField);

        il.Emit(OpCodes.Ret);
    }

    // ===== $Response class fields =====
    private FieldBuilder _responseStatusField = null!;
    private FieldBuilder _responseStatusTextField = null!;
    private FieldBuilder _responseOkField = null!;
    private FieldBuilder _responseHeadersField = null!;
    private FieldBuilder _responseBodyBytesField = null!;
    private FieldBuilder _responseBodyConsumedField = null!;
    private FieldBuilder _responseTypeField = null!;

    /// <summary>
    /// Emits the $Response class for standalone Response constructor support.
    /// Constructor: (object? body, object? init) — parses init for status, statusText, headers.
    /// </summary>
    private void EmitResponseClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$Response",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        _responseStatusField = typeBuilder.DefineField("_status", _types.Double, FieldAttributes.Assembly);
        _responseStatusTextField = typeBuilder.DefineField("_statusText", _types.String, FieldAttributes.Assembly);
        _responseOkField = typeBuilder.DefineField("_ok", _types.Boolean, FieldAttributes.Assembly);
        _responseHeadersField = typeBuilder.DefineField("_headers", _types.Object, FieldAttributes.Assembly);
        _responseBodyBytesField = typeBuilder.DefineField("_bodyBytes", _types.ByteArray, FieldAttributes.Assembly);
        _responseBodyConsumedField = typeBuilder.DefineField("_bodyConsumed", _types.Boolean, FieldAttributes.Assembly);
        _responseTypeField = typeBuilder.DefineField("_type", _types.String, FieldAttributes.Assembly);

        // Constructor: (object? body, object? init)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]
        );

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // Defaults
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_R8, 200.0);
        il.Emit(OpCodes.Stfld, _responseStatusField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stfld, _responseStatusTextField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1); // ok = true (200 is in range)
        il.Emit(OpCodes.Stfld, _responseOkField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "default");
        il.Emit(OpCodes.Stfld, _responseTypeField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, runtime.TSHeadersCtor);
        il.Emit(OpCodes.Stfld, _responseHeadersField);

        // Convert body to bytes
        // if (body == null) _bodyBytes = new byte[0]
        // else if (body is string) _bodyBytes = Encoding.UTF8.GetBytes(body.ToString())
        // else _bodyBytes = Encoding.UTF8.GetBytes(body.ToString())
        var bodyIsNull = il.DefineLabel();
        var bodyDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, bodyIsNull);

        // body != null: Encoding.UTF8.GetBytes(body.ToString())
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stfld, _responseBodyBytesField);
        il.Emit(OpCodes.Br, bodyDone);

        il.MarkLabel(bodyIsNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stfld, _responseBodyBytesField);

        il.MarkLabel(bodyDone);

        // Parse init if present
        var endInit = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, endInit);

        // status from init
        var statusLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "status");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, statusLocal);

        var skipStatus = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, statusLocal);
        il.Emit(OpCodes.Brfalse, skipStatus);
        // _status = (double)status
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, statusLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stfld, _responseStatusField);
        // _ok = status >= 200 && status <= 299
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseStatusField);
        il.Emit(OpCodes.Ldc_R8, 200.0);
        var notOk = il.DefineLabel();
        var setOk = il.DefineLabel();
        il.Emit(OpCodes.Blt, notOk);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseStatusField);
        il.Emit(OpCodes.Ldc_R8, 299.0);
        il.Emit(OpCodes.Bgt, notOk);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Br, setOk);
        il.MarkLabel(notOk);
        il.Emit(OpCodes.Ldc_I4_0);
        il.MarkLabel(setOk);
        il.Emit(OpCodes.Stfld, _responseOkField);
        il.MarkLabel(skipStatus);

        // statusText from init
        var stLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "statusText");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, stLocal);

        var skipST = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, stLocal);
        il.Emit(OpCodes.Brfalse, skipST);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, stLocal);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _responseStatusTextField);
        il.MarkLabel(skipST);

        // headers from init
        var hLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "headers");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, hLocal);

        var skipH = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hLocal);
        il.Emit(OpCodes.Brfalse, skipH);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hLocal);
        il.Emit(OpCodes.Newobj, runtime.TSHeadersCtor);
        il.Emit(OpCodes.Stfld, _responseHeadersField);
        il.MarkLabel(skipH);

        il.MarkLabel(endInit);
        il.Emit(OpCodes.Ret);

        // Property getters
        EmitFetchResponsePropertyGetter(typeBuilder, "status", _types.Double, _responseStatusField);
        EmitFetchResponsePropertyGetter(typeBuilder, "statusText", _types.String, _responseStatusTextField);
        EmitFetchResponsePropertyGetter(typeBuilder, "ok", _types.Boolean, _responseOkField);
        EmitFetchResponsePropertyGetter(typeBuilder, "headers", _types.Object, _responseHeadersField);
        EmitFetchResponsePropertyGetter(typeBuilder, "bodyUsed", _types.Boolean, _responseBodyConsumedField);
        EmitFetchResponsePropertyGetter(typeBuilder, "type", _types.String, _responseTypeField);

        // Computed properties (url = "", redirected = false)
        EmitResponseConstantProperty(typeBuilder, "url", _types.String, "");
        EmitResponseConstantBoolProperty(typeBuilder, "redirected", false);

        // Body reading methods
        EmitResponseTextMethod(typeBuilder, runtime);
        EmitResponseJsonMethod(typeBuilder, runtime);
        EmitResponseArrayBufferMethod(typeBuilder, runtime);
        EmitResponseCloneMethod(typeBuilder, runtime, ctor);

        runtime.TSResponseType = typeBuilder;
        runtime.TSResponseCtor = ctor;

        typeBuilder.CreateType();
    }

    private void EmitResponseConstantProperty(TypeBuilder typeBuilder, string name, Type returnType, string value)
    {
        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        var getter = typeBuilder.DefineMethod(
            $"get_{pascalName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            returnType,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Ret);

        var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, returnType, null);
        prop.SetGetMethod(getter);
    }

    private void EmitResponseConstantBoolProperty(TypeBuilder typeBuilder, string name, bool value)
    {
        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        var getter = typeBuilder.DefineMethod(
            $"get_{pascalName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        var il = getter.GetILGenerator();
        il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, _types.Boolean, null);
        prop.SetGetMethod(getter);
    }

    private void EmitResponseTextMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("text", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseBodyBytesField);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.ByteArray])!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _responseBodyConsumedField);

        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    private void EmitResponseJsonMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("json", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseBodyBytesField);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.ByteArray])!);
        il.Emit(OpCodes.Call, runtime.JsonParse);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _responseBodyConsumedField);

        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    private void EmitResponseArrayBufferMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("arrayBuffer", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseBodyBytesField);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _responseBodyConsumedField);

        il.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        il.Emit(OpCodes.Ret);
    }

    private void EmitResponseCloneMethod(TypeBuilder typeBuilder, EmittedRuntime runtime, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod("clone", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        // new $Response(null, null) then copy all fields
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, ctor);

        // Copy status
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseStatusField);
        il.Emit(OpCodes.Stfld, _responseStatusField);

        // Copy statusText
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseStatusTextField);
        il.Emit(OpCodes.Stfld, _responseStatusTextField);

        // Copy ok
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseOkField);
        il.Emit(OpCodes.Stfld, _responseOkField);

        // Copy type
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseTypeField);
        il.Emit(OpCodes.Stfld, _responseTypeField);

        // Copy headers
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseHeadersField);
        il.Emit(OpCodes.Stfld, _responseHeadersField);

        // Copy body bytes
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _responseBodyBytesField);
        il.Emit(OpCodes.Stfld, _responseBodyBytesField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Response static methods: ResponseJson, ResponseRedirect, ResponseError.
    /// These are emitted as static methods on $Runtime but access $Response assembly-internal fields.
    /// Headers are set by calling the $Headers "set" method directly.
    /// </summary>
    private void EmitResponseStaticMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Get the $Headers.set method for calling on headers objects
        var headersSetMethod = runtime.TSHeadersSetMethod;

        // ResponseJson(object? data, object? init) → $Response
        var jsonMethod = typeBuilder.DefineMethod(
            "ResponseJson",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ResponseJsonStatic = jsonMethod;

        var il = jsonMethod.GetILGenerator();
        // JSON.stringify the data → string body
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.JsonStringify);
        var bodyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, bodyLocal);

        // new $Response(body, init)
        il.Emit(OpCodes.Ldloc, bodyLocal);
        il.Emit(OpCodes.Ldarg_1); // init
        il.Emit(OpCodes.Newobj, runtime.TSResponseCtor);
        var respLocal = il.DeclareLocal(runtime.TSResponseType);
        il.Emit(OpCodes.Stloc, respLocal);

        // Set content-type on the response's headers via $Headers.set
        il.Emit(OpCodes.Ldloc, respLocal);
        il.Emit(OpCodes.Ldfld, _responseHeadersField);
        il.Emit(OpCodes.Castclass, runtime.TSHeadersType);
        il.Emit(OpCodes.Ldstr, "content-type");
        il.Emit(OpCodes.Ldstr, "application/json");
        il.Emit(OpCodes.Callvirt, headersSetMethod);
        il.Emit(OpCodes.Pop); // set returns undefined/null

        il.Emit(OpCodes.Ldloc, respLocal);
        il.Emit(OpCodes.Ret);

        // ResponseRedirect(object url, object? status) → $Response
        var redirectMethod = typeBuilder.DefineMethod(
            "ResponseRedirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ResponseRedirectStatic = redirectMethod;

        il = redirectMethod.GetILGenerator();
        il.Emit(OpCodes.Ldnull); // body
        il.Emit(OpCodes.Ldnull); // init
        il.Emit(OpCodes.Newobj, runtime.TSResponseCtor);
        var rLocal = il.DeclareLocal(runtime.TSResponseType);
        il.Emit(OpCodes.Stloc, rLocal);

        // Set status: default 302, or from arg
        var useDefault = il.DefineLabel();
        var statusDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, useDefault);
        // status = ToNumber(arg1)
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stfld, _responseStatusField);
        il.Emit(OpCodes.Br, statusDone);

        il.MarkLabel(useDefault);
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldc_R8, 302.0);
        il.Emit(OpCodes.Stfld, _responseStatusField);

        il.MarkLabel(statusDone);
        // ok = false for all redirects
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _responseOkField);

        // Set Location header via $Headers.set
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ldfld, _responseHeadersField);
        il.Emit(OpCodes.Castclass, runtime.TSHeadersType);
        il.Emit(OpCodes.Ldstr, "location");
        il.Emit(OpCodes.Ldarg_0); // url
        il.Emit(OpCodes.Callvirt, headersSetMethod);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Ret);

        // ResponseError() → $Response
        var errorMethod = typeBuilder.DefineMethod(
            "ResponseError",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ResponseErrorStatic = errorMethod;

        il = errorMethod.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, runtime.TSResponseCtor);
        var eLocal = il.DeclareLocal(runtime.TSResponseType);
        il.Emit(OpCodes.Stloc, eLocal);

        // Set status to 0
        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stfld, _responseStatusField);

        // Set ok to false
        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _responseOkField);

        // Set type to "error"
        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Stfld, _responseTypeField);

        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Ret);
    }
}
