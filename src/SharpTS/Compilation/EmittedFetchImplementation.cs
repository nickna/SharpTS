using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

// Enabled with HTTP, including HTTP-only imports. Client metadata is absent for the BCL fallback.
public sealed class EmittedFetchImplementation
{
    internal EmittedFetchImplementation() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _invoke;
    public MethodBuilder Invoke
    {
        get => Require(_invoke);
        internal set => Set(ref _invoke, value);
    }

    private TypeBuilder? _headersType;
    public TypeBuilder HeadersType
    {
        get => Require(_headersType);
        internal set => Set(ref _headersType, value);
    }

    private ConstructorBuilder? _headersCtor;
    public ConstructorBuilder HeadersCtor
    {
        get => Require(_headersCtor);
        internal set => Set(ref _headersCtor, value);
    }

    private MethodBuilder? _headersSetMethod;
    public MethodBuilder HeadersSetMethod
    {
        get => Require(_headersSetMethod);
        internal set => Set(ref _headersSetMethod, value);
    }

    private ConstructorBuilder? _fetchResponseCtor;
    public ConstructorBuilder FetchResponseCtor
    {
        get => Require(_fetchResponseCtor);
        internal set => Set(ref _fetchResponseCtor, value);
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

    private MethodBuilder? _responseJsonStatic;
    public MethodBuilder ResponseJsonStatic
    {
        get => Require(_responseJsonStatic);
        internal set => Set(ref _responseJsonStatic, value);
    }

    private MethodBuilder? _responseRedirectStatic;
    public MethodBuilder ResponseRedirectStatic
    {
        get => Require(_responseRedirectStatic);
        internal set => Set(ref _responseRedirectStatic, value);
    }

    private MethodBuilder? _responseErrorStatic;
    public MethodBuilder ResponseErrorStatic
    {
        get => Require(_responseErrorStatic);
        internal set => Set(ref _responseErrorStatic, value);
    }

    private FieldBuilder? _fetchResponseStatusField;
    public FieldBuilder FetchResponseStatusField
    {
        get => Require(_fetchResponseStatusField);
        internal set => Set(ref _fetchResponseStatusField, value);
    }

    private FieldBuilder? _fetchResponseStatusTextField;
    public FieldBuilder FetchResponseStatusTextField
    {
        get => Require(_fetchResponseStatusTextField);
        internal set => Set(ref _fetchResponseStatusTextField, value);
    }

    private FieldBuilder? _fetchResponseOkField;
    public FieldBuilder FetchResponseOkField
    {
        get => Require(_fetchResponseOkField);
        internal set => Set(ref _fetchResponseOkField, value);
    }

    private FieldBuilder? _fetchResponseUrlField;
    public FieldBuilder FetchResponseUrlField
    {
        get => Require(_fetchResponseUrlField);
        internal set => Set(ref _fetchResponseUrlField, value);
    }

    private FieldBuilder? _fetchResponseHeadersField;
    public FieldBuilder FetchResponseHeadersField
    {
        get => Require(_fetchResponseHeadersField);
        internal set => Set(ref _fetchResponseHeadersField, value);
    }

    private FieldBuilder? _fetchResponseBodyBytesField;
    public FieldBuilder FetchResponseBodyBytesField
    {
        get => Require(_fetchResponseBodyBytesField);
        internal set => Set(ref _fetchResponseBodyBytesField, value);
    }

    private FieldBuilder? _fetchResponseBodyConsumedField;
    public FieldBuilder FetchResponseBodyConsumedField
    {
        get => Require(_fetchResponseBodyConsumedField);
        internal set => Set(ref _fetchResponseBodyConsumedField, value);
    }

    private FieldBuilder? _headersImmutableField;
    public FieldBuilder HeadersImmutableField
    {
        get => Require(_headersImmutableField);
        internal set => Set(ref _headersImmutableField, value);
    }

    private FieldBuilder? _headersDataField;
    public FieldBuilder HeadersDataField
    {
        get => Require(_headersDataField);
        internal set => Set(ref _headersDataField, value);
    }

    private FieldBuilder? _requestMethodField;
    public FieldBuilder RequestMethodField
    {
        get => Require(_requestMethodField);
        internal set => Set(ref _requestMethodField, value);
    }

    private FieldBuilder? _requestUrlField;
    public FieldBuilder RequestUrlField
    {
        get => Require(_requestUrlField);
        internal set => Set(ref _requestUrlField, value);
    }

    private FieldBuilder? _requestHeadersField;
    public FieldBuilder RequestHeadersField
    {
        get => Require(_requestHeadersField);
        internal set => Set(ref _requestHeadersField, value);
    }

    private FieldBuilder? _requestBodyField;
    public FieldBuilder RequestBodyField
    {
        get => Require(_requestBodyField);
        internal set => Set(ref _requestBodyField, value);
    }

    private FieldBuilder? _requestBodyConsumedField;
    public FieldBuilder RequestBodyConsumedField
    {
        get => Require(_requestBodyConsumedField);
        internal set => Set(ref _requestBodyConsumedField, value);
    }

    private FieldBuilder? _responseStatusField;
    public FieldBuilder ResponseStatusField
    {
        get => Require(_responseStatusField);
        internal set => Set(ref _responseStatusField, value);
    }

    private FieldBuilder? _responseStatusTextField;
    public FieldBuilder ResponseStatusTextField
    {
        get => Require(_responseStatusTextField);
        internal set => Set(ref _responseStatusTextField, value);
    }

    private FieldBuilder? _responseOkField;
    public FieldBuilder ResponseOkField
    {
        get => Require(_responseOkField);
        internal set => Set(ref _responseOkField, value);
    }

    private FieldBuilder? _responseHeadersField;
    public FieldBuilder ResponseHeadersField
    {
        get => Require(_responseHeadersField);
        internal set => Set(ref _responseHeadersField, value);
    }

    private FieldBuilder? _responseBodyBytesField;
    public FieldBuilder ResponseBodyBytesField
    {
        get => Require(_responseBodyBytesField);
        internal set => Set(ref _responseBodyBytesField, value);
    }

    private FieldBuilder? _responseBodyConsumedField;
    public FieldBuilder ResponseBodyConsumedField
    {
        get => Require(_responseBodyConsumedField);
        internal set => Set(ref _responseBodyConsumedField, value);
    }

    private FieldBuilder? _responseTypeField;
    public FieldBuilder ResponseTypeField
    {
        get => Require(_responseTypeField);
        internal set => Set(ref _responseTypeField, value);
    }

    public EmittedFetchClientRuntime? Client { get; private set; }

    public EmittedFetchClientRuntime RequireClient() => Client
        ?? throw new InvalidOperationException("Fetch Web API client was not enabled for this compilation.");

    internal void BeginClientEmission()
    {
        EnsureMutable();
        if (Client is not null)
            throw new InvalidOperationException("Fetch Web API client emission has already started.");
        Client = new EmittedFetchClientRuntime();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Fetch Web API metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException($"Fetch Web API metadata '{name}' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Fetch Web API metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Invoke;
        _ = HeadersType;
        _ = HeadersCtor;
        _ = HeadersSetMethod;
        _ = FetchResponseCtor;
        _ = RequestCtor;
        _ = ResponseType;
        _ = ResponseCtor;
        _ = ResponseJsonStatic;
        _ = ResponseRedirectStatic;
        _ = ResponseErrorStatic;
        _ = FetchResponseStatusField;
        _ = FetchResponseStatusTextField;
        _ = FetchResponseOkField;
        _ = FetchResponseUrlField;
        _ = FetchResponseHeadersField;
        _ = FetchResponseBodyBytesField;
        _ = FetchResponseBodyConsumedField;
        _ = HeadersDataField;
        _ = HeadersImmutableField;
        _ = RequestMethodField;
        _ = RequestUrlField;
        _ = RequestHeadersField;
        _ = RequestBodyField;
        _ = RequestBodyConsumedField;
        _ = ResponseStatusField;
        _ = ResponseStatusTextField;
        _ = ResponseOkField;
        _ = ResponseHeadersField;
        _ = ResponseBodyBytesField;
        _ = ResponseBodyConsumedField;
        _ = ResponseTypeField;
        Client?.CompleteEmission();
        IsComplete = true;
    }
}
