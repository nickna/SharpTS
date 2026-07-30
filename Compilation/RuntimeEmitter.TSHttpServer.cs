using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $HttpServer, $HttpRequest, and $HttpResponse classes for standalone HTTP support.
/// These replace the reflection-based SharpTSHttpServer/Request/Response types.
/// </summary>
public partial class RuntimeEmitter
{
    // Field builders for HTTP types
    private FieldBuilder _httpServerCallbackField = null!;
    private FieldBuilder _httpServerListenerField = null!;
    private FieldBuilder _httpServerIsListeningField = null!;
    private FieldBuilder _httpServerCtsField = null!;
    private FieldBuilder _httpServerPortField = null!;

    private FieldBuilder _httpRequestRequestField = null!;

    private FieldBuilder _httpResponseResponseField = null!;
    private FieldBuilder _httpResponseHeadersSentField = null!;
    private FieldBuilder _httpResponseFinishedField = null!;
    private FieldBuilder _httpResponseBodyBufferField = null!;
    private MethodBuilder _httpResponseWriteMethod = null!;
    private MethodBuilder _httpAcceptWorkerMethod = null!;

    /// <summary>
    /// Emits all HTTP types for standalone operation.
    /// </summary>
    private void EmitHttpTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitHttpRequestClass(moduleBuilder, runtime);
        EmitHttpResponseClass(moduleBuilder, runtime);
        EmitHttpServerClass(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits: public class $HttpRequest
    /// Wraps HttpListenerRequest for standalone HTTP server support.
    /// </summary>
    private void EmitHttpRequestClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var httpListenerRequestType = typeof(HttpListenerRequest);

        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$HttpRequest",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        runtime.TSHttpRequestType = typeBuilder;

        // Field: private HttpListenerRequest _request
        _httpRequestRequestField = typeBuilder.DefineField("_request", httpListenerRequestType, FieldAttributes.Private);

        // Constructor: public $HttpRequest(HttpListenerRequest request)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [httpListenerRequestType]
        );
        runtime.TSHttpRequestCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, _httpRequestRequestField);
        ctorIL.Emit(OpCodes.Ret);

        // GetMember method
        EmitHttpRequestGetMember(typeBuilder, runtime, httpListenerRequestType);

        typeBuilder.CreateType();
    }

    private void EmitHttpRequestGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerRequestType)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        // Switch on member name
        var methodLabel = il.DefineLabel();
        var urlLabel = il.DefineLabel();
        var httpVersionLabel = il.DefineLabel();
        var headersLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "method"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "method");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, methodLabel);

        // Check "url"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "url");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, urlLabel);

        // Check "httpVersion"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "httpVersion");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, httpVersionLabel);

        // Check "headers"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "headers");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, headersLabel);

        // Check "rawHeaders"
        var rawHeadersLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "rawHeaders");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, rawHeadersLabel);

        // #1048 additions: version parts, complete/aborted, trailers, socket.
        var verMajorLabel = il.DefineLabel();
        var verMinorLabel = il.DefineLabel();
        var completeLabel = il.DefineLabel();
        var abortedLabel = il.DefineLabel();
        var trailersLabel = il.DefineLabel();
        var rawTrailersLabel = il.DefineLabel();
        var socketLabel = il.DefineLabel();

        void Check(string n, System.Reflection.Emit.Label lbl)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
            il.Emit(OpCodes.Brtrue, lbl);
        }
        Check("httpVersionMajor", verMajorLabel);
        Check("httpVersionMinor", verMinorLabel);
        Check("complete", completeLabel);
        Check("aborted", abortedLabel);
        Check("trailers", trailersLabel);
        Check("rawTrailers", rawTrailersLabel);
        Check("socket", socketLabel);
        Check("connection", socketLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        // httpVersionMajor / httpVersionMinor
        void EmitVersionPart(System.Reflection.Emit.Label lbl, string propName)
        {
            il.MarkLabel(lbl);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "ProtocolVersion")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, typeof(Version).GetProperty(propName)!.GetGetMethod()!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
        }
        EmitVersionPart(verMajorLabel, "Major");
        EmitVersionPart(verMinorLabel, "Minor");

        // complete / aborted → false (server request lifecycle not tracked in compiled)
        il.MarkLabel(completeLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(abortedLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // trailers → empty dictionary
        il.MarkLabel(trailersLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Ret);

        // rawTrailers → empty List<object?> (Array.isArray-compatible, same shape as rawHeaders;
        // runtime.CreateArray is not yet assigned at the $HttpRequest emit phase).
        il.MarkLabel(rawTrailersLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Ret);

        // socket → minimal { remoteAddress, remotePort, family }
        il.MarkLabel(socketLabel);
        EmitHttpRequestSocket(il, runtime, httpListenerRequestType);
        il.Emit(OpCodes.Ret);

        // "method" - return _request.HttpMethod
        il.MarkLabel(methodLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "HttpMethod")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // "url" - return _request.RawUrl ?? "/"
        il.MarkLabel(urlLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "RawUrl")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        var hasRawUrl = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasRawUrl);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "/");
        il.MarkLabel(hasRawUrl);
        il.Emit(OpCodes.Ret);

        // "httpVersion" - return major.minor string
        il.MarkLabel(httpVersionLabel);
        var versionLocal = il.DeclareLocal(typeof(Version));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "ProtocolVersion")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, versionLocal);
        il.Emit(OpCodes.Ldstr, "{0}.{1}");
        il.Emit(OpCodes.Ldloc, versionLocal);
        il.Emit(OpCodes.Callvirt, typeof(Version).GetProperty("Major")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Ldloc, versionLocal);
        il.Emit(OpCodes.Callvirt, typeof(Version).GetProperty("Minor")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Format", [_types.String, _types.Object, _types.Object])!);
        il.Emit(OpCodes.Ret);

        // "headers" - return dictionary of headers
        il.MarkLabel(headersLabel);
        EmitExtractRequestHeaders(il, httpListenerRequestType);
        il.Emit(OpCodes.Ret);

        // "rawHeaders" - return List<object?> with alternating key/value pairs
        il.MarkLabel(rawHeadersLabel);
        EmitExtractRawHeaders(il, httpListenerRequestType);
        il.Emit(OpCodes.Ret);

        // default - return undefined
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Builds a minimal request socket dictionary { remoteAddress, remotePort, family } from
    /// HttpListenerRequest.RemoteEndPoint and leaves it on the stack (#1048).
    /// </summary>
    private void EmitHttpRequestSocket(ILGenerator il, EmittedRuntime runtime, Type httpListenerRequestType)
    {
        var dictType = _types.DictionaryStringObject;
        var setItem = _types.GetMethod(dictType, "set_Item", [_types.String, _types.Object])!;
        var ipEndPointType = typeof(System.Net.IPEndPoint);

        var epLocal = il.DeclareLocal(ipEndPointType);
        var dictLocal = il.DeclareLocal(dictType);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Stloc, dictLocal);

        // ep = _request.RemoteEndPoint as IPEndPoint
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "RemoteEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, ipEndPointType);
        il.Emit(OpCodes.Stloc, epLocal);

        var noEpLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Brfalse, noEpLabel);

        // dict["remoteAddress"] = ep.Address.ToString()
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "remoteAddress");
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, ipEndPointType.GetProperty("Address")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.IPAddress).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, setItem);

        // dict["remotePort"] = (double)ep.Port
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "remotePort");
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, ipEndPointType.GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, setItem);

        il.MarkLabel(noEpLabel);

        // dict["family"] = "IPv4"
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Callvirt, setItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
    }

    private void EmitExtractRequestHeaders(ILGenerator il, Type httpListenerRequestType)
    {
        // Create new dictionary and populate from request headers
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // Get Headers from request
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "Headers")!.GetGetMethod()!);

        var headersLocal = il.DeclareLocal(typeof(System.Collections.Specialized.NameValueCollection));
        il.Emit(OpCodes.Stloc, headersLocal);

        // Get AllKeys array
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.Specialized.NameValueCollection).GetProperty("AllKeys")!.GetGetMethod()!);

        var keysLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Stloc, keysLocal);

        // Loop through keys
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // Get key
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Skip null keys
        var skipNull = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, skipNull);

        // Add to dictionary: dict[key.ToLowerInvariant()] = headers[key]
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.Specialized.NameValueCollection).GetMethod("Get", [_types.String])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.MarkLabel(skipNull);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, dictLocal);
    }

    /// <summary>
    /// Emits IL to build a List&lt;object?&gt; of alternating [key, value, key, value, ...]
    /// from the HttpListenerRequest headers — matches Node.js rawHeaders format.
    /// </summary>
    private void EmitExtractRawHeaders(ILGenerator il, Type httpListenerRequestType)
    {
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Get headers NameValueCollection
        var headersLocal = il.DeclareLocal(typeof(System.Collections.Specialized.NameValueCollection));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpRequestRequestField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerRequestType, "Headers")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, headersLocal);

        // string[] keys = headers.AllKeys
        var keysLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.Specialized.NameValueCollection).GetProperty("AllKeys")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keysLocal);

        // Loop: for each key, add key and value
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Skip null keys
        var skipNull = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, skipNull);

        // result.Add(key)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        // result.Add(headers[key])
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, headersLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.Specialized.NameValueCollection).GetMethod("Get", [_types.String])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.MarkLabel(skipNull);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    /// <summary>
    /// Emits: public class $HttpResponse
    /// Wraps HttpListenerResponse for standalone HTTP server support.
    /// </summary>
    private void EmitHttpResponseClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var httpListenerResponseType = typeof(HttpListenerResponse);

        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$HttpResponse",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        runtime.TSHttpResponseType = typeBuilder;

        // Fields
        _httpResponseResponseField = typeBuilder.DefineField("_response", httpListenerResponseType, FieldAttributes.Private);
        _httpResponseHeadersSentField = typeBuilder.DefineField("_headersSent", _types.Boolean, FieldAttributes.Private);
        _httpResponseFinishedField = typeBuilder.DefineField("_finished", _types.Boolean, FieldAttributes.Private);
        _httpResponseBodyBufferField = typeBuilder.DefineField("_bodyBuffer", typeof(List<byte>), FieldAttributes.Private);

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [httpListenerResponseType]
        );
        runtime.TSHttpResponseCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, _httpResponseResponseField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, typeof(List<byte>).GetConstructor(Type.EmptyTypes)!);
        ctorIL.Emit(OpCodes.Stfld, _httpResponseBodyBufferField);
        ctorIL.Emit(OpCodes.Ret);

        // Methods. SetHeader is emitted before WriteHead so WriteHead can call it to apply
        // the optional headers object.
        var setHeaderMethod = EmitHttpResponseSetHeader(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseWriteHead(typeBuilder, runtime, httpListenerResponseType, setHeaderMethod);
        EmitHttpResponseWrite(typeBuilder, runtime);
        EmitHttpResponseEnd(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseHasHeader(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseGetHeader(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseGetHeaderNames(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseRemoveHeader(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseGetMember(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseSetMember(typeBuilder, runtime, httpListenerResponseType);
        EmitHttpResponseExtraMembers(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the ServerResponse completeness surface (#1047): writeContinue/writeProcessing/
    /// addTrailers/flushHeaders (reflection-dispatched PascalCase methods). HttpListener
    /// auto-sends 100-continue and has no trailer/1xx API, so these are compatibility no-ops
    /// (matching the interpreter).
    /// </summary>
    private void EmitHttpResponseExtraMembers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        void EmitUndefMethod(string name, int argCount)
        {
            var paramTypes = new Type[argCount];
            for (int i = 0; i < argCount; i++) paramTypes[i] = _types.Object;
            var m = typeBuilder.DefineMethod(name, MethodAttributes.Public, _types.Object, paramTypes);
            var mil = m.GetILGenerator();
            mil.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            mil.Emit(OpCodes.Ret);
        }

        EmitUndefMethod("WriteContinue", 0);
        EmitUndefMethod("WriteProcessing", 0);
        EmitUndefMethod("AddTrailers", 1);
        EmitUndefMethod("FlushHeaders", 0);
    }

    private void EmitHttpResponseGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        var statusCodeLabel = il.DefineLabel();
        var headersSentLabel = il.DefineLabel();
        var finishedLabel = il.DefineLabel();
        var statusMessageLabel = il.DefineLabel();
        var sendDateLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check property names
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "statusCode");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, statusCodeLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "headersSent");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, headersSentLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "finished");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, finishedLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "statusMessage");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, statusMessageLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "sendDate");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, sendDateLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        // statusCode - return as double
        il.MarkLabel(statusCodeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "StatusCode")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // headersSent
        il.MarkLabel(headersSentLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseHeadersSentField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // finished
        il.MarkLabel(finishedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseFinishedField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // statusMessage - _response.StatusDescription
        il.MarkLabel(statusMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "StatusDescription")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // sendDate - true (HttpListener always sends Date)
        il.MarkLabel(sendDateLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // default - return undefined
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseSetMember(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        var method = typeBuilder.DefineMethod(
            "SetMember",
            MethodAttributes.Public,
            typeof(void),
            [_types.String, _types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();

        var statusCodeLabel = il.DefineLabel();
        var statusMessageLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check "statusCode"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "statusCode");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, statusCodeLabel);

        // Check "statusMessage"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "statusMessage");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, statusMessageLabel);

        il.Emit(OpCodes.Br, endLabel);

        // statusCode = (int)(double)value
        il.MarkLabel(statusCodeLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(double));
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "StatusCode")!.GetSetMethod()!);
        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Br, endLabel);

        // statusMessage = (string)value
        il.MarkLabel(statusMessageLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "StatusDescription")!.GetSetMethod()!);
        il.MarkLabel(notStringLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseWriteHead(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType, MethodBuilder setHeaderMethod)
    {
        // public object WriteHead(double statusCode, object? headers)
        var method = typeBuilder.DefineMethod(
            "WriteHead",
            MethodAttributes.Public,
            _types.Object,
            [_types.Double, _types.Object]
        );

        var il = method.GetILGenerator();

        // Set status code
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "StatusCode")!.GetSetMethod()!);

        // Apply the optional headers object. A compiled object literal (e.g. { 'Content-Type': ... })
        // is a bare Dictionary<string, object?>; for each entry call this.SetHeader(key,
        // value?.ToString() ?? "") which handles the Content-Type restricted-header special case.
        // (Previously a no-op TODO, so compiled writeHead silently dropped all headers.)
        var dictType = typeof(Dictionary<string, object?>);
        var enumType = typeof(Dictionary<string, object?>.Enumerator);
        var kvpType = typeof(KeyValuePair<string, object?>);
        var headersDict = il.DeclareLocal(dictType);
        var dictEnum = il.DeclareLocal(enumType);
        var kvLocal = il.DeclareLocal(kvpType);
        var skipHeaders = il.DefineLabel();

        // if ((headersDict = arg2 as Dictionary<string, object?>) == null) goto skipHeaders;
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, headersDict);
        il.Emit(OpCodes.Brfalse, skipHeaders);

        // var e = headersDict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, headersDict);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictEnum);

        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopBody);
        // var kv = e.Current;
        il.Emit(OpCodes.Ldloca, dictEnum);
        il.Emit(OpCodes.Call, enumType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvLocal);

        // this.SetHeader(kv.Key, kv.Value?.ToString() ?? "")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, kvLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        var valNotNull = il.DefineLabel();
        var valDone = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, valNotNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, valDone);
        il.MarkLabel(valNotNull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.MarkLabel(valDone);
        il.Emit(OpCodes.Callvirt, setHeaderMethod);
        il.Emit(OpCodes.Pop); // SetHeader returns this

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloca, dictEnum);
        il.Emit(OpCodes.Call, enumType.GetMethod("MoveNext", Type.EmptyTypes)!);
        il.Emit(OpCodes.Brtrue, loopBody);

        il.MarkLabel(skipHeaders);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public object Write(object data)
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        _httpResponseWriteMethod = method;

        var il = method.GetILGenerator();

        // if (data == null) return true
        var hasDataLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, hasDataLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasDataLabel);

        // Get data as string
        var dataLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString")!);
        il.Emit(OpCodes.Stloc, dataLocal);

        // Convert to bytes and add to buffer
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // _bodyBuffer.AddRange(bytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseBodyBufferField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("AddRange", [typeof(IEnumerable<byte>)])!);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseEnd(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        // public object End(object? data)
        var method = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // if (_finished) return this
        var notFinishedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseFinishedField);
        il.Emit(OpCodes.Brfalse, notFinishedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFinishedLabel);

        // If data provided, write it first
        var noDataLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noDataLabel);

        // Call Write(data) - use saved MethodBuilder, not typeBuilder.GetMethod
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _httpResponseWriteMethod);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noDataLabel);

        // Mark headers sent and finished
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _httpResponseHeadersSentField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _httpResponseFinishedField);

        // Write body and close (in try/catch)
        il.BeginExceptionBlock();

        var bufferLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseBodyBufferField);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("ToArray")!);
        il.Emit(OpCodes.Stloc, bufferLocal);

        // Set content length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "ContentLength64")!.GetSetMethod()!);

        // Write bytes if any
        var noBodyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, noBodyLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "OutputStream")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Write", [_types.ByteArray, _types.Int32, _types.Int32])!);

        il.MarkLabel(noBodyLabel);

        // Close output stream
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "OutputStream")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Close")!);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitHttpResponseSetHeader(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        // public object SetHeader(string name, string value)
        var method = typeBuilder.DefineMethod(
            "SetHeader",
            MethodAttributes.Public,
            _types.Object,
            [_types.String, _types.String]
        );

        var il = method.GetILGenerator();

        // Check for Content-Type special case
        var notContentTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "Content-Type");
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notContentTypeLabel);

        // Set ContentType property
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "ContentType")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notContentTypeLabel);

        // Set via Headers collection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "Headers")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(WebHeaderCollection).GetMethod("Set", [_types.String, _types.String])!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitHttpResponseHasHeader(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        // public object HasHeader(object name) — returns boxed bool
        var method = typeBuilder.DefineMethod(
            "HasHeader",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var nameLocal = il.DeclareLocal(_types.String);

        // string name = arg?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var storeNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, storeNameLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.MarkLabel(storeNameLabel);
        il.Emit(OpCodes.Stloc, nameLocal);

        // Check Content-Type special case
        var notContentType = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "Content-Type");
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notContentType);

        // return _response.ContentType != null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "ContentType")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notContentType);

        // return _response.Headers[name] != null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "Headers")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, typeof(WebHeaderCollection).GetMethod("Get", [_types.String])!);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseGetHeader(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        // public object GetHeader(object name) — returns header value or undefined
        var method = typeBuilder.DefineMethod(
            "GetHeader",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var nameLocal = il.DeclareLocal(_types.String);

        // string name = arg?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var storeNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, storeNameLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.MarkLabel(storeNameLabel);
        il.Emit(OpCodes.Stloc, nameLocal);

        // Check Content-Type special case
        var notContentType = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "Content-Type");
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notContentType);

        // return _response.ContentType ?? undefined
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "ContentType")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        var hasContentType = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasContentType);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(hasContentType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notContentType);

        // return _response.Headers[name] ?? undefined
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "Headers")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, typeof(WebHeaderCollection).GetMethod("Get", [_types.String])!);
        il.Emit(OpCodes.Dup);
        var hasValue = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(hasValue);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseGetHeaderNames(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        // public object GetHeaderNames() — returns List<object?> of lowercase header names
        var method = typeBuilder.DefineMethod(
            "GetHeaderNames",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // var result = new List<object?>()
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // string[] keys = _response.Headers.AllKeys
        var keysLocal = il.DeclareLocal(_types.StringArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "Headers")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.Specialized.NameValueCollection).GetProperty("AllKeys")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keysLocal);

        // for (int i = 0; i < keys.Length; i++)
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Skip null keys
        var skipNull = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, skipNull);

        // result.Add(keys[i].ToLowerInvariant())
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.MarkLabel(skipNull);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpResponseRemoveHeader(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerResponseType)
    {
        // public object RemoveHeader(object name)
        var method = typeBuilder.DefineMethod(
            "RemoveHeader",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var nameLocal = il.DeclareLocal(_types.String);

        // string name = arg?.ToString() ?? ""
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        var storeNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, storeNameLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.MarkLabel(storeNameLabel);
        il.Emit(OpCodes.Stloc, nameLocal);

        // Check Content-Type special case
        var notContentType = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "Content-Type");
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notContentType);

        // _response.ContentType = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "ContentType")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notContentType);

        // _response.Headers.Remove(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpResponseResponseField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerResponseType, "Headers")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, typeof(WebHeaderCollection).GetMethod("Remove", [_types.String])!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public class $HttpServer : $EventEmitter
    /// Standalone HTTP server implementation.
    /// </summary>
    private void EmitHttpServerClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var httpListenerType = typeof(HttpListener);

        // Define class: public class $HttpServer : $EventEmitter
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$HttpServer",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        runtime.TSHttpServerType = typeBuilder;

        // Fields
        _httpServerCallbackField = typeBuilder.DefineField("_callback", _types.Object, FieldAttributes.Assembly);
        _httpServerListenerField = typeBuilder.DefineField("_listener", httpListenerType, FieldAttributes.Private);
        _httpServerIsListeningField = typeBuilder.DefineField("_isListening", _types.Boolean, FieldAttributes.Private);
        _httpServerCtsField = typeBuilder.DefineField("_cts", typeof(CancellationTokenSource), FieldAttributes.Private);
        _httpServerPortField = typeBuilder.DefineField("_port", _types.Int32, FieldAttributes.Private);

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.TSHttpServerCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, _httpServerCallbackField);
        ctorIL.Emit(OpCodes.Ret);

        // Methods
        EmitHttpServerListen(typeBuilder, runtime, httpListenerType);
        EmitHttpServerClose(typeBuilder, runtime, httpListenerType);
        EmitHttpServerAddress(typeBuilder, runtime);
        EmitHttpServerGetMember(typeBuilder, runtime);

        // Property getters for reflection-based access
        EmitHttpServerPropertyGetters(typeBuilder, runtime);

        // Server-management surface (#1045): config getters (Node defaults) + lifecycle methods.
        EmitHttpServerLifecycleMembers(typeBuilder, runtime, httpListenerType);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the server-management surface (#1045): config property getters returning Node
    /// defaults (resolved via the GetProperty PascalCase-reflection fallback) plus
    /// closeAllConnections/closeIdleConnections/setTimeout. Compiled exposes the defaults for
    /// reads; mutating the timeouts is interpreter-only (documented).
    /// </summary>
    private void EmitHttpServerLifecycleMembers(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerType)
    {
        void EmitConstDoubleProperty(string name, double value)
        {
            var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, _types.Double, null);
            var getter = typeBuilder.DefineMethod(
                "get_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName,
                _types.Double,
                Type.EmptyTypes);
            var gil = getter.GetILGenerator();
            gil.Emit(OpCodes.Ldc_R8, value);
            gil.Emit(OpCodes.Ret);
            prop.SetGetMethod(getter);
        }

        EmitConstDoubleProperty("KeepAliveTimeout", 5000);
        EmitConstDoubleProperty("HeadersTimeout", 60000);
        EmitConstDoubleProperty("RequestTimeout", 300000);
        EmitConstDoubleProperty("Timeout", 0);
        EmitConstDoubleProperty("MaxHeadersCount", 2000);
        EmitConstDoubleProperty("MaxRequestsPerSocket", 0);

        // CloseAllConnections() → forcibly close (reuse Close); returns this.
        var closeAll = typeBuilder.DefineMethod("CloseAllConnections",
            MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var cail = closeAll.GetILGenerator();
        cail.Emit(OpCodes.Ldarg_0);
        cail.Emit(OpCodes.Ldnull);
        cail.Emit(OpCodes.Callvirt, runtime.TSHttpServerClose);
        cail.Emit(OpCodes.Pop);
        cail.Emit(OpCodes.Ldarg_0);
        cail.Emit(OpCodes.Ret);

        // CloseIdleConnections() → no-op (HttpListener manages keep-alive internally); returns undefined.
        var closeIdle = typeBuilder.DefineMethod("CloseIdleConnections",
            MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var ciil = closeIdle.GetILGenerator();
        ciil.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        ciil.Emit(OpCodes.Ret);

        // SetTimeout(msecs, cb) → register cb as a 'timeout' listener (best effort); returns this.
        var setTimeout = typeBuilder.DefineMethod("SetTimeout",
            MethodAttributes.Public, _types.Object, [_types.Object, _types.Object]);
        var stil = setTimeout.GetILGenerator();
        // if (cb != null) this.On("timeout", cb)
        var noCbLabel = stil.DefineLabel();
        stil.Emit(OpCodes.Ldarg_2);
        stil.Emit(OpCodes.Brfalse, noCbLabel);
        stil.Emit(OpCodes.Ldarg_0);
        stil.Emit(OpCodes.Ldstr, "timeout");
        stil.Emit(OpCodes.Ldarg_2);
        stil.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
        stil.Emit(OpCodes.Pop);
        stil.MarkLabel(noCbLabel);
        stil.Emit(OpCodes.Ldarg_0);
        stil.Emit(OpCodes.Ret);
    }

    private void EmitHttpServerPropertyGetters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // listening property - returns _isListening
        var listeningProp = typeBuilder.DefineProperty("Listening", PropertyAttributes.None, _types.Boolean, null);
        var getListening = typeBuilder.DefineMethod(
            "get_Listening",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        var il = getListening.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerIsListeningField);
        il.Emit(OpCodes.Ret);
        listeningProp.SetGetMethod(getListening);
    }

    /// <summary>
    /// Emits <c>private static int ProbeFreePort()</c>: binds a temporary
    /// TcpListener on loopback port 0 and returns the OS-assigned port.
    /// HttpListener has no dynamic-port support, so <c>listen(0)</c> needs
    /// the probe (#214). Small release/re-bind race window — standard
    /// practice for this workaround. BCL-only, safe for standalone DLLs.
    /// </summary>
    private MethodBuilder EmitHttpServerProbeFreePort(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ProbeFreePort",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var tcpListenerType = typeof(System.Net.Sockets.TcpListener);
        var probeLocal = il.DeclareLocal(tcpListenerType);
        var portLocal = il.DeclareLocal(_types.Int32);

        // var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        il.Emit(OpCodes.Ldsfld, typeof(IPAddress).GetField("Loopback")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, tcpListenerType.GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Stloc, probeLocal);
        il.Emit(OpCodes.Ldloc, probeLocal);
        il.Emit(OpCodes.Callvirt, tcpListenerType.GetMethod("Start", Type.EmptyTypes)!);

        // port = ((IPEndPoint)probe.LocalEndpoint).Port;
        il.Emit(OpCodes.Ldloc, probeLocal);
        il.Emit(OpCodes.Callvirt, tcpListenerType.GetProperty("LocalEndpoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(IPEndPoint));
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, portLocal);

        // probe.Stop(); return port;
        il.Emit(OpCodes.Ldloc, probeLocal);
        il.Emit(OpCodes.Callvirt, tcpListenerType.GetMethod("Stop")!);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitHttpServerListen(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerType)
    {
        var probeFreePort = EmitHttpServerProbeFreePort(typeBuilder);

        // public object Listen(double port, object? callback)
        var method = typeBuilder.DefineMethod(
            "Listen",
            MethodAttributes.Public,
            _types.Object,
            [_types.Double, _types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();

        // listen(0): substitute an OS-assigned ephemeral port before any
        // use of the port argument (#214).
        var portNonZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, portNonZeroLabel);
        il.Emit(OpCodes.Call, probeFreePort);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Starg_S, (byte)1);
        il.MarkLabel(portNonZeroLabel);

        // Store port
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, _httpServerPortField);

        // if (_isListening) throw
        var notListeningLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerIsListeningField);
        il.Emit(OpCodes.Brfalse, notListeningLabel);
        il.Emit(OpCodes.Ldstr, "Server is already listening");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notListeningLabel);

        // _listener = new HttpListener()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(httpListenerType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _httpServerListenerField);

        // Build prefix string: "http://127.0.0.1:{port}/"
        var prefixLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, "http://127.0.0.1:");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);  // Convert double to int (port is always an integer)
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, prefixLocal);

        // _listener.Prefixes.Add(prefix)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerListenerField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(httpListenerType, "Prefixes")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Callvirt, typeof(HttpListenerPrefixCollection).GetMethod("Add", [_types.String])!);

        // _listener.Start()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerListenerField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(httpListenerType, "Start")!);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _httpServerIsListeningField);

        // _cts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _httpServerCtsField);

        // EventLoop.Ref() to keep process alive
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Start HTTP accept loop on ThreadPool BEFORE callback,
        // so the server can accept connections even if the callback
        // fires a synchronous request (e.g., http.get).
        EmitHttpServerStartAccepting(typeBuilder, il, runtime);

        // Emit 'listening' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Call listening callback if provided
        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Check if callback is TSFunction
        var notTSFunc = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunc);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, noCallbackLabel);

        il.MarkLabel(notTSFunc);
        // Check BoundTSFunction
        var notBound = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBound);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notBound);
        il.MarkLabel(noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpServerClose(TypeBuilder typeBuilder, EmittedRuntime runtime, Type httpListenerType)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        runtime.TSHttpServerClose = method;

        var il = method.GetILGenerator();

        // if (!_isListening) return this
        var isListeningLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerIsListeningField);
        il.Emit(OpCodes.Brtrue, isListeningLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListeningLabel);

        // Cancel and stop
        il.BeginExceptionBlock();

        var noCtsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerCtsField);
        il.Emit(OpCodes.Brfalse, noCtsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.MarkLabel(noCtsLabel);

        var noListenerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerListenerField);
        il.Emit(OpCodes.Brfalse, noListenerLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerListenerField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(httpListenerType, "Stop")!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerListenerField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(httpListenerType, "Close")!);
        il.MarkLabel(noListenerLabel);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // _isListening = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _httpServerIsListeningField);

        // EventLoop.Unref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);

        // Emit 'close' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Call callback if provided
        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        var notTSFunc = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunc);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, noCallbackLabel);

        il.MarkLabel(notTSFunc);
        var notBound = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBound);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notBound);
        il.MarkLabel(noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpServerAddress(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Address",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TSHttpServerAddress = method;

        var il = method.GetILGenerator();

        // if (!_isListening) return null
        var isListeningLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerIsListeningField);
        il.Emit(OpCodes.Brtrue, isListeningLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListeningLabel);

        // Return a dictionary with address info
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerPortField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Ret);
    }

    private void EmitHttpServerGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        var listeningLabel = il.DefineLabel();
        var addressLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "listening"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, listeningLabel);

        // Check "address" (returns the address() result)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, addressLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        // listening - return _isListening
        il.MarkLabel(listeningLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _httpServerIsListeningField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // address - call Address()
        il.MarkLabel(addressLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.TSHttpServerAddress);
        il.Emit(OpCodes.Ret);

        // default - return undefined
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the HTTP accept loop as a private method and queues it on ThreadPool.
    /// Blocks on HttpListener.GetContext(), schedules request handling via EventLoop.
    /// </summary>
    /// <summary>
    /// Phase 1: Defines the accept worker method stub and wires ThreadPool.QueueUserWorkItem.
    /// The worker body is deferred to EmitHttpServerAcceptWorkerBody (Phase 2) because it
    /// needs $HttpAcceptClosure which isn't defined until after EmitRuntimeClass.
    /// </summary>
    private void EmitHttpServerStartAccepting(TypeBuilder typeBuilder, ILGenerator callerIl, EmittedRuntime runtime)
    {
        // Define method stub (body emitted in Phase 2)
        _httpAcceptWorkerMethod = typeBuilder.DefineMethod(
            "_HttpAcceptWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        // In caller: ThreadPool.QueueUserWorkItem(new WaitCallback(this._HttpAcceptWorker))
        callerIl.Emit(OpCodes.Ldarg_0);
        callerIl.Emit(OpCodes.Ldftn, _httpAcceptWorkerMethod);
        callerIl.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        callerIl.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        callerIl.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Phase 2: Emits the HTTP accept worker body using $HttpAcceptClosure.
    /// Must be called after EmitNetClosureTypes sets _httpAcceptClosureCtor/_httpAcceptClosureRun.
    /// </summary>
    private void EmitHttpServerAcceptWorkerBody(EmittedRuntime runtime)
    {
        var httpListenerType = typeof(HttpListener);
        var httpListenerContextType = typeof(HttpListenerContext);

        var wil = _httpAcceptWorkerMethod.GetILGenerator();
        var ctxLocal = wil.DeclareLocal(httpListenerContextType);

        var loopTop = wil.DefineLabel();
        var loopExit = wil.DefineLabel();

        wil.MarkLabel(loopTop);

        // Check _isListening
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _httpServerIsListeningField);
        wil.Emit(OpCodes.Brfalse, loopExit);

        // try { ctx = _listener.GetContext() } catch { break }
        wil.BeginExceptionBlock();
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _httpServerListenerField);
        wil.Emit(OpCodes.Callvirt, httpListenerType.GetMethod("GetContext")!);
        wil.Emit(OpCodes.Stloc, ctxLocal);

        var afterAccept = wil.DefineLabel();
        wil.Emit(OpCodes.Leave, afterAccept);

        wil.BeginCatchBlock(_types.Exception);
        wil.Emit(OpCodes.Pop);
        wil.Emit(OpCodes.Leave, loopExit);
        wil.EndExceptionBlock();

        wil.MarkLabel(afterAccept);

        // Schedule the accept closure on the EventLoop for single-threaded dispatch.
        // This is safe because Fetch is now non-blocking (uses Task.Run + Promise).
        // EventLoop.Schedule(new Action(new $HttpAcceptClosure(this, ctx).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, ctxLocal);
        wil.Emit(OpCodes.Newobj, _httpAcceptClosureCtor);
        wil.Emit(OpCodes.Ldftn, _httpAcceptClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

        wil.Emit(OpCodes.Br, loopTop);

        wil.MarkLabel(loopExit);
        wil.Emit(OpCodes.Ret);
    }
}
