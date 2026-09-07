using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// TLS metadata for one compilation. Declarations support forward references;
/// completion validates and freezes handles after deferred bodies and types are finalized.
/// </summary>
public sealed class EmittedTlsRuntime
{
    internal EmittedTlsRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _createServer;
    public MethodBuilder CreateServer
    {
        get => Require(_createServer);
        internal set => Set(ref _createServer, value);
    }

    private MethodBuilder? _connect;
    public MethodBuilder Connect
    {
        get => Require(_connect);
        internal set => Set(ref _connect, value);
    }

    private MethodBuilder? _createSecureContext;
    public MethodBuilder CreateSecureContext
    {
        get => Require(_createSecureContext);
        internal set => Set(ref _createSecureContext, value);
    }

    private MethodBuilder? _getDefaultMinVersion;
    public MethodBuilder GetDefaultMinVersion
    {
        get => Require(_getDefaultMinVersion);
        internal set => Set(ref _getDefaultMinVersion, value);
    }

    private MethodBuilder? _getDefaultMaxVersion;
    public MethodBuilder GetDefaultMaxVersion
    {
        get => Require(_getDefaultMaxVersion);
        internal set => Set(ref _getDefaultMaxVersion, value);
    }

    private MethodBuilder? _getCiphers;
    public MethodBuilder GetCiphers
    {
        get => Require(_getCiphers);
        internal set => Set(ref _getCiphers, value);
    }

    private MethodBuilder? _rootCertificates;
    public MethodBuilder RootCertificates
    {
        get => Require(_rootCertificates);
        internal set => Set(ref _rootCertificates, value);
    }

    private MethodBuilder? _createSocket;
    public MethodBuilder CreateSocket
    {
        get => Require(_createSocket);
        internal set => Set(ref _createSocket, value);
    }

    private Type? _socketType;
    public Type SocketType
    {
        get => Require(_socketType);
        internal set => Set(ref _socketType, value);
    }

    private ConstructorBuilder? _socketCtor;
    public ConstructorBuilder SocketCtor
    {
        get => Require(_socketCtor);
        internal set => Set(ref _socketCtor, value);
    }

    private ConstructorBuilder? _serverCtor;
    public ConstructorBuilder ServerCtor
    {
        get => Require(_serverCtor);
        internal set => Set(ref _serverCtor, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"TLS metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("TLS metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CreateServer;
        _ = Connect;
        _ = CreateSecureContext;
        _ = GetDefaultMinVersion;
        _ = GetDefaultMaxVersion;
        _ = GetCiphers;
        _ = RootCertificates;
        _ = CreateSocket;
        _ = SocketType;
        _ = SocketCtor;
        _ = ServerCtor;
        IsComplete = true;
    }
}
