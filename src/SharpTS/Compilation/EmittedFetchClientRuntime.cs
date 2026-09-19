using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

// Declared only when HttpClient is available; owns dispatch and the shared client/cookie caches.
public sealed class EmittedFetchClientRuntime
{
    internal EmittedFetchClientRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _cookieJarGetCookies;
    public MethodBuilder CookieJarGetCookies
    {
        get => Require(_cookieJarGetCookies);
        internal set => Set(ref _cookieJarGetCookies, value);
    }

    private MethodBuilder? _cookieJarSetCookie;
    public MethodBuilder CookieJarSetCookie
    {
        get => Require(_cookieJarSetCookie);
        internal set => Set(ref _cookieJarSetCookie, value);
    }

    private MethodBuilder? _cookieJarClear;
    public MethodBuilder CookieJarClear
    {
        get => Require(_cookieJarClear);
        internal set => Set(ref _cookieJarClear, value);
    }

    private MethodBuilder? _getOrCreateHttpClient;
    public MethodBuilder GetOrCreateHttpClient
    {
        get => Require(_getOrCreateHttpClient);
        internal set => Set(ref _getOrCreateHttpClient, value);
    }

    private TypeBuilder? _displayClass;
    public TypeBuilder DisplayClass
    {
        get => Require(_displayClass);
        internal set => Set(ref _displayClass, value);
    }

    private FieldBuilder? _displayUrl;
    public FieldBuilder DisplayUrl
    {
        get => Require(_displayUrl);
        internal set => Set(ref _displayUrl, value);
    }

    private FieldBuilder? _displayOptions;
    public FieldBuilder DisplayOptions
    {
        get => Require(_displayOptions);
        internal set => Set(ref _displayOptions, value);
    }

    private ConstructorBuilder? _displayCtor;
    public ConstructorBuilder DisplayCtor
    {
        get => Require(_displayCtor);
        internal set => Set(ref _displayCtor, value);
    }

    private MethodBuilder? _displayInvoke;
    public MethodBuilder DisplayInvoke
    {
        get => Require(_displayInvoke);
        internal set => Set(ref _displayInvoke, value);
    }

    private FieldBuilder? _cookieContainerField;
    public FieldBuilder CookieContainerField
    {
        get => Require(_cookieContainerField);
        internal set => Set(ref _cookieContainerField, value);
    }

    private FieldBuilder? _displayHelperField;
    public FieldBuilder DisplayHelperField
    {
        get => Require(_displayHelperField);
        internal set => Set(ref _displayHelperField, value);
    }

    private FieldBuilder? _followClientField;
    public FieldBuilder FollowClientField
    {
        get => Require(_followClientField);
        internal set => Set(ref _followClientField, value);
    }

    private FieldBuilder? _noRedirectClientField;
    public FieldBuilder NoRedirectClientField
    {
        get => Require(_noRedirectClientField);
        internal set => Set(ref _noRedirectClientField, value);
    }

    private FieldBuilder? _followCookiesClientField;
    public FieldBuilder FollowCookiesClientField
    {
        get => Require(_followCookiesClientField);
        internal set => Set(ref _followCookiesClientField, value);
    }

    private FieldBuilder? _noRedirectCookiesClientField;
    public FieldBuilder NoRedirectCookiesClientField
    {
        get => Require(_noRedirectCookiesClientField);
        internal set => Set(ref _noRedirectCookiesClientField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Fetch client metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException($"Fetch client metadata '{name}' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Fetch client metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CookieJarGetCookies;
        _ = CookieJarSetCookie;
        _ = CookieJarClear;
        _ = GetOrCreateHttpClient;
        _ = DisplayClass;
        _ = DisplayUrl;
        _ = DisplayOptions;
        _ = DisplayCtor;
        _ = DisplayInvoke;
        _ = CookieContainerField;
        _ = DisplayHelperField;
        _ = FollowClientField;
        _ = NoRedirectClientField;
        _ = FollowCookiesClientField;
        _ = NoRedirectCookiesClientField;
        IsComplete = true;
    }
}
