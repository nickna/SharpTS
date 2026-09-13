using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// HTTP module and server metadata for one compilation. Handles are readable after declaration;
/// completion validates and freezes them after deferred accept-worker and closure emission.
/// </summary>
public sealed class EmittedHttpRuntime
{
    internal EmittedHttpRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _createServer;
    public MethodBuilder CreateServer
    {
        get => Require(_createServer);
        internal set => Set(ref _createServer, value);
    }

    private MethodBuilder? _request;
    public MethodBuilder Request
    {
        get => Require(_request);
        internal set => Set(ref _request, value);
    }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => Set(ref _get, value);
    }

    private MethodBuilder? _getMethods;
    public MethodBuilder GetMethods
    {
        get => Require(_getMethods);
        internal set => Set(ref _getMethods, value);
    }

    private MethodBuilder? _validateHeaderName;
    public MethodBuilder ValidateHeaderName
    {
        get => Require(_validateHeaderName);
        internal set => Set(ref _validateHeaderName, value);
    }

    private MethodBuilder? _validateHeaderValue;
    public MethodBuilder ValidateHeaderValue
    {
        get => Require(_validateHeaderValue);
        internal set => Set(ref _validateHeaderValue, value);
    }

    private MethodBuilder? _setMaxIdleParsers;
    public MethodBuilder SetMaxIdleParsers
    {
        get => Require(_setMaxIdleParsers);
        internal set => Set(ref _setMaxIdleParsers, value);
    }

    private MethodBuilder? _getStatusCodes;
    public MethodBuilder GetStatusCodes
    {
        get => Require(_getStatusCodes);
        internal set => Set(ref _getStatusCodes, value);
    }

    private MethodBuilder? _getGlobalAgent;
    public MethodBuilder GetGlobalAgent
    {
        get => Require(_getGlobalAgent);
        internal set => Set(ref _getGlobalAgent, value);
    }

    private MethodBuilder? _getAgentConstructor;
    public MethodBuilder GetAgentConstructor
    {
        get => Require(_getAgentConstructor);
        internal set => Set(ref _getAgentConstructor, value);
    }

    private MethodBuilder? _agentFactory;
    public MethodBuilder AgentFactory
    {
        get => Require(_agentFactory);
        internal set => Set(ref _agentFactory, value);
    }

    private TypeBuilder? _serverType;
    public TypeBuilder ServerType
    {
        get => Require(_serverType);
        internal set => Set(ref _serverType, value);
    }

    private ConstructorBuilder? _serverCtor;
    public ConstructorBuilder ServerCtor
    {
        get => Require(_serverCtor);
        internal set => Set(ref _serverCtor, value);
    }

    private MethodBuilder? _serverAddress;
    public MethodBuilder ServerAddress
    {
        get => Require(_serverAddress);
        internal set => Set(ref _serverAddress, value);
    }

    private TypeBuilder? _requestType;
    public TypeBuilder RequestType
    {
        get => Require(_requestType);
        internal set => Set(ref _requestType, value);
    }

    private ConstructorBuilder? _requestCtor;
    public ConstructorBuilder RequestCtor
    {
        get => Require(_requestCtor);
        internal set => Set(ref _requestCtor, value);
    }

    private TypeBuilder? _responseType;
    public TypeBuilder ResponseType
    {
        get => Require(_responseType);
        internal set => Set(ref _responseType, value);
    }

    private ConstructorBuilder? _responseCtor;
    public ConstructorBuilder ResponseCtor
    {
        get => Require(_responseCtor);
        internal set => Set(ref _responseCtor, value);
    }

    private FieldBuilder? _serverCallbackField;
    public FieldBuilder ServerCallbackField
    {
        get => Require(_serverCallbackField);
        internal set => Set(ref _serverCallbackField, value);
    }

    private FieldBuilder? _serverListenerField;
    public FieldBuilder ServerListenerField
    {
        get => Require(_serverListenerField);
        internal set => Set(ref _serverListenerField, value);
    }

    private FieldBuilder? _serverIsListeningField;
    public FieldBuilder ServerIsListeningField
    {
        get => Require(_serverIsListeningField);
        internal set => Set(ref _serverIsListeningField, value);
    }

    private FieldBuilder? _serverCtsField;
    public FieldBuilder ServerCtsField
    {
        get => Require(_serverCtsField);
        internal set => Set(ref _serverCtsField, value);
    }

    private FieldBuilder? _serverPortField;
    public FieldBuilder ServerPortField
    {
        get => Require(_serverPortField);
        internal set => Set(ref _serverPortField, value);
    }

    private FieldBuilder? _serverAddressField;
    public FieldBuilder ServerAddressField
    {
        get => Require(_serverAddressField);
        internal set => Set(ref _serverAddressField, value);
    }

    private FieldBuilder? _serverFamilyField;
    public FieldBuilder ServerFamilyField
    {
        get => Require(_serverFamilyField);
        internal set => Set(ref _serverFamilyField, value);
    }

    private FieldBuilder? _serverCloseRequestedField;
    public FieldBuilder ServerCloseRequestedField
    {
        get => Require(_serverCloseRequestedField);
        internal set => Set(ref _serverCloseRequestedField, value);
    }

    private FieldBuilder? _serverCloseFinishedField;
    public FieldBuilder ServerCloseFinishedField
    {
        get => Require(_serverCloseFinishedField);
        internal set => Set(ref _serverCloseFinishedField, value);
    }

    private FieldBuilder? _serverInFlightField;
    public FieldBuilder ServerInFlightField
    {
        get => Require(_serverInFlightField);
        internal set => Set(ref _serverInFlightField, value);
    }

    private FieldBuilder? _serverActiveResponsesField;
    public FieldBuilder ServerActiveResponsesField
    {
        get => Require(_serverActiveResponsesField);
        internal set => Set(ref _serverActiveResponsesField, value);
    }

    private FieldBuilder? _serverPendingCloseCallbackField;
    public FieldBuilder ServerPendingCloseCallbackField
    {
        get => Require(_serverPendingCloseCallbackField);
        internal set => Set(ref _serverPendingCloseCallbackField, value);
    }

    private MethodBuilder? _serverFinishCloseMethod;
    public MethodBuilder ServerFinishCloseMethod
    {
        get => Require(_serverFinishCloseMethod);
        internal set => Set(ref _serverFinishCloseMethod, value);
    }

    private MethodBuilder? _serverRequestCompletedMethod;
    public MethodBuilder ServerRequestCompletedMethod
    {
        get => Require(_serverRequestCompletedMethod);
        internal set => Set(ref _serverRequestCompletedMethod, value);
    }

    private FieldBuilder? _requestRequestField;
    public FieldBuilder RequestRequestField
    {
        get => Require(_requestRequestField);
        internal set => Set(ref _requestRequestField, value);
    }

    private FieldBuilder? _requestCompleteField;
    public FieldBuilder RequestCompleteField
    {
        get => Require(_requestCompleteField);
        internal set => Set(ref _requestCompleteField, value);
    }

    private FieldBuilder? _requestAbortedField;
    public FieldBuilder RequestAbortedField
    {
        get => Require(_requestAbortedField);
        internal set => Set(ref _requestAbortedField, value);
    }

    private FieldBuilder? _responseResponseField;
    public FieldBuilder ResponseResponseField
    {
        get => Require(_responseResponseField);
        internal set => Set(ref _responseResponseField, value);
    }

    private FieldBuilder? _responseHeadersSentField;
    public FieldBuilder ResponseHeadersSentField
    {
        get => Require(_responseHeadersSentField);
        internal set => Set(ref _responseHeadersSentField, value);
    }

    private FieldBuilder? _responseFinishedField;
    public FieldBuilder ResponseFinishedField
    {
        get => Require(_responseFinishedField);
        internal set => Set(ref _responseFinishedField, value);
    }

    private FieldBuilder? _responseBodyBufferField;
    public FieldBuilder ResponseBodyBufferField
    {
        get => Require(_responseBodyBufferField);
        internal set => Set(ref _responseBodyBufferField, value);
    }

    private FieldBuilder? _responseCompletionField;
    public FieldBuilder ResponseCompletionField
    {
        get => Require(_responseCompletionField);
        internal set => Set(ref _responseCompletionField, value);
    }

    private FieldBuilder? _responseStreamingField;
    public FieldBuilder ResponseStreamingField
    {
        get => Require(_responseStreamingField);
        internal set => Set(ref _responseStreamingField, value);
    }

    private MethodBuilder? _responseWriteMethod;
    public MethodBuilder ResponseWriteMethod
    {
        get => Require(_responseWriteMethod);
        internal set => Set(ref _responseWriteMethod, value);
    }

    private MethodBuilder? _acceptWorkerMethod;
    public MethodBuilder AcceptWorkerMethod
    {
        get => Require(_acceptWorkerMethod);
        internal set => Set(ref _acceptWorkerMethod, value);
    }

    private MethodBuilder? _agentDestroyMethod;
    public MethodBuilder AgentDestroyMethod
    {
        get => Require(_agentDestroyMethod);
        internal set => Set(ref _agentDestroyMethod, value);
    }

    private MethodBuilder? _agentGetNameMethod;
    public MethodBuilder AgentGetNameMethod
    {
        get => Require(_agentGetNameMethod);
        internal set => Set(ref _agentGetNameMethod, value);
    }

    private ConstructorBuilder? _acceptClosureCtor;
    public ConstructorBuilder AcceptClosureCtor
    {
        get => Require(_acceptClosureCtor);
        internal set => Set(ref _acceptClosureCtor, value);
    }

    private MethodBuilder? _acceptClosureRun;
    public MethodBuilder AcceptClosureRun
    {
        get => Require(_acceptClosureRun);
        internal set => Set(ref _acceptClosureRun, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"HTTP metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("HTTP metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CreateServer;
        _ = Request;
        _ = Get;
        _ = GetMethods;
        _ = ValidateHeaderName;
        _ = ValidateHeaderValue;
        _ = SetMaxIdleParsers;
        _ = GetStatusCodes;
        _ = GetGlobalAgent;
        _ = GetAgentConstructor;
        _ = AgentFactory;
        _ = ServerType;
        _ = ServerCtor;
        _ = ServerAddress;
        _ = RequestType;
        _ = RequestCtor;
        _ = ResponseType;
        _ = ResponseCtor;
        _ = ServerCallbackField;
        _ = ServerListenerField;
        _ = ServerIsListeningField;
        _ = ServerCtsField;
        _ = ServerPortField;
        _ = ServerAddressField;
        _ = ServerFamilyField;
        _ = ServerCloseRequestedField;
        _ = ServerCloseFinishedField;
        _ = ServerInFlightField;
        _ = ServerActiveResponsesField;
        _ = ServerPendingCloseCallbackField;
        _ = ServerFinishCloseMethod;
        _ = ServerRequestCompletedMethod;
        _ = RequestRequestField;
        _ = RequestCompleteField;
        _ = RequestAbortedField;
        _ = ResponseResponseField;
        _ = ResponseHeadersSentField;
        _ = ResponseFinishedField;
        _ = ResponseBodyBufferField;
        _ = ResponseCompletionField;
        _ = ResponseStreamingField;
        _ = ResponseWriteMethod;
        _ = AcceptWorkerMethod;
        _ = AgentDestroyMethod;
        _ = AgentGetNameMethod;
        _ = AcceptClosureCtor;
        _ = AcceptClosureRun;
        IsComplete = true;
    }
}
