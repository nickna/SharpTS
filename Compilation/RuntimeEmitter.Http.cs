using System.Reflection;
using System.Reflection.Emit;

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
    }

    /// <summary>
    /// Emits all HTTP module methods.
    /// </summary>
    private void EmitHttpModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        InitializeHttpTypes();

        // Emit the $FetchResponse class first
        EmitFetchResponseClass(typeBuilder.Module as ModuleBuilder ?? throw new InvalidOperationException(), runtime);

        // Emit fetch function
        EmitFetch(typeBuilder, runtime);

        // Emit http module methods
        EmitHttpCreateServer(typeBuilder, runtime);
        EmitHttpRequest(typeBuilder, runtime);
        EmitHttpGet(typeBuilder, runtime);
        EmitHttpGetMethods(typeBuilder, runtime);
        EmitHttpGetStatusCodes(typeBuilder, runtime);
        EmitHttpGetGlobalAgent(typeBuilder, runtime);

        // Emit wrappers for module import support
        EmitHttpModuleWrappers(typeBuilder, runtime);
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

        // Method: json() - returns Promise
        EmitFetchResponseJsonMethod(typeBuilder, runtime);

        // Method: text() - returns Promise
        EmitFetchResponseTextMethod(typeBuilder, runtime);

        // Method: arrayBuffer() - returns Promise wrapping a Buffer
        EmitFetchResponseArrayBufferMethod(typeBuilder, runtime);

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
    /// Emits: public static object Fetch(object url, object? options)
    /// Returns a Promise that resolves to a $FetchResponse.
    /// </summary>
    /// <remarks>
    /// Emits a helper method to perform the HTTP request with try/catch,
    /// then calls that helper and wraps result in a Promise.
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

        // First emit the headers helper
        var applyHeadersMethod = EmitApplyRequestHeaders(typeBuilder, runtime);

        // Then emit the helper that does the actual fetch work
        var fetchHelperMethod = EmitFetchHelper(typeBuilder, runtime, applyHeadersMethod);

        // Now emit the Fetch method that calls the helper and wraps result
        var fetchMethod = typeBuilder.DefineMethod(
            "Fetch",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.Fetch = fetchMethod;

        var fetchIL = fetchMethod.GetILGenerator();

        var resultLocal = fetchIL.DeclareLocal(_types.ObjectArray);

        // Call helper method
        fetchIL.Emit(OpCodes.Ldarg_0);
        fetchIL.Emit(OpCodes.Ldarg_1);
        fetchIL.Emit(OpCodes.Call, fetchHelperMethod);
        fetchIL.Emit(OpCodes.Stloc, resultLocal);

        // Check if result[0] (success) is true
        var errorLabel = fetchIL.DefineLabel();

        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_0);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Unbox_Any, _types.Boolean);
        fetchIL.Emit(OpCodes.Brfalse, errorLabel);

        // Success path: create $FetchResponse and wrap in resolved Promise
        // Array indices: 0=success, 1=status, 2=statusText, 3=ok, 4=url, 5=headers, 6=bodyBytes, 7=errorMessage

        // status (double)
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_1);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Unbox_Any, _types.Double);

        // statusText (string)
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_2);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Castclass, _types.String);

        // ok (bool)
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_3);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Unbox_Any, _types.Boolean);

        // url (string)
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_4);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Castclass, _types.String);

        // headers (object)
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_5);
        fetchIL.Emit(OpCodes.Ldelem_Ref);

        // bodyBytes (byte[])
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_6);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Castclass, _types.ByteArray);

        fetchIL.Emit(OpCodes.Newobj, runtime.TSFetchResponseCtor);

        // Wrap in resolved Promise
        fetchIL.Emit(OpCodes.Call, runtime.TSPromiseResolve);
        fetchIL.Emit(OpCodes.Ret);

        // Error path: return rejected Promise with error message
        fetchIL.MarkLabel(errorLabel);

        // errorMessage is at index 7
        fetchIL.Emit(OpCodes.Ldloc, resultLocal);
        fetchIL.Emit(OpCodes.Ldc_I4_7);
        fetchIL.Emit(OpCodes.Ldelem_Ref);
        fetchIL.Emit(OpCodes.Call, runtime.TSPromiseReject);
        fetchIL.Emit(OpCodes.Ret);
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

        // string url = arg0?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, urlLocal);

        // var client = new HttpClient()
        var httpClientCtor = _httpClientType!.GetConstructor(Type.EmptyTypes)!;
        il.Emit(OpCodes.Newobj, httpClientCtor);
        il.Emit(OpCodes.Stloc, clientLocal);

        // Set timeout to 30 seconds
        var timeoutProp = _httpClientType.GetProperty("Timeout")!;
        var fromSecondsMethod = _types.TimeSpan.GetMethod("FromSeconds", [_types.Double])!;
        il.Emit(OpCodes.Ldloc, clientLocal);
        il.Emit(OpCodes.Ldc_R8, 30.0);
        il.Emit(OpCodes.Call, fromSecondsMethod);
        il.Emit(OpCodes.Callvirt, timeoutProp.GetSetMethod()!);

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

        // var response = client.SendAsync(request).Result
        var sendAsyncMethod = _httpClientType.GetMethod("SendAsync", [_httpRequestMessageType])!;
        var taskOfResponseType = sendAsyncMethod.ReturnType;
        var getResultMethod = taskOfResponseType.GetProperty("Result")!.GetGetMethod()!;

        il.Emit(OpCodes.Ldloc, clientLocal);
        il.Emit(OpCodes.Ldloc, requestLocal);
        il.Emit(OpCodes.Callvirt, sendAsyncMethod);
        il.Emit(OpCodes.Callvirt, getResultMethod);
        il.Emit(OpCodes.Stloc, responseLocal);

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

        // headers = new Dictionary<string, object>()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, headersLocal);

        // Dispose client
        il.Emit(OpCodes.Ldloc, clientLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

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

        // Check if it's a Dictionary<string, object?>
        il.Emit(OpCodes.Ldloc, headersObjLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var headersLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, headersLocal);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

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
        var headersProperty = _httpRequestMessageType!.GetProperty("Headers")!;
        var tryAddMethod = _httpRequestHeadersType!.GetMethod("TryAddWithoutValidation", [_types.String, _types.String])!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, headersProperty.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, tryAddMethod);
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
    /// Calls a static C# helper method since the IL for iterating value type enumerators is complex.
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

        // Call the static helper method in FetchBuiltIns
        var helperMethod = typeof(SharpTS.Runtime.BuiltIns.FetchHelpers).GetMethod(
            "ExtractResponseHeaders",
            [_httpResponseMessageType!])!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, helperMethod);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits: public static object HttpCreateServer(object? callback)
    /// Creates a real SharpTSHttpServer instance with EventEmitter support.
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

        // Get the types and methods we need
        var adapterType = typeof(SharpTS.Runtime.Types.TSFunctionCallableAdapter);
        var wrapCallbackMethod = adapterType.GetMethod("WrapCallback",
            BindingFlags.Public | BindingFlags.Static)!;
        var serverType = typeof(SharpTS.Runtime.Types.SharpTSHttpServer);
        var callableInterface = typeof(SharpTS.Runtime.Types.ISharpTSCallable);
        var serverCtor = serverType.GetConstructor([callableInterface])!;

        // Wrap the callback to get an ISharpTSCallable
        // ISharpTSCallable handler = TSFunctionCallableAdapter.WrapCallback(callback);
        il.Emit(OpCodes.Ldarg_0);  // Load callback argument
        il.Emit(OpCodes.Call, wrapCallbackMethod);

        // Create new SharpTSHttpServer(handler)
        il.Emit(OpCodes.Newobj, serverCtor);

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

    /// <summary>
    /// Emits: public static object HttpGetGlobalAgent()
    /// Returns a stub global agent object.
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

        // Create new dictionary for the agent object
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "keepAlive");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "maxSockets");
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.SetProperty);

        il.Emit(OpCodes.Ret);
    }
}
