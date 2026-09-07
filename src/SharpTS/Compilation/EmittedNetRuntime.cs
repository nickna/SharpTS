using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// TCP/IPC transport metadata for one compilation. Declarations support forward references;
/// completion validates and freezes handles after socket/server bodies and types are finalized.
/// </summary>
public sealed class EmittedNetRuntime
{
    internal EmittedNetRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _createServer;
    public MethodBuilder CreateServer
    {
        get => Require(_createServer);
        internal set => Set(ref _createServer, value);
    }

    private MethodBuilder? _createConnection;
    public MethodBuilder CreateConnection
    {
        get => Require(_createConnection);
        internal set => Set(ref _createConnection, value);
    }

    private MethodBuilder? _createSocket;
    public MethodBuilder CreateSocket
    {
        get => Require(_createSocket);
        internal set => Set(ref _createSocket, value);
    }

    private MethodBuilder? _createBlockList;
    public MethodBuilder CreateBlockList
    {
        get => Require(_createBlockList);
        internal set => Set(ref _createBlockList, value);
    }

    private TypeBuilder? _blockListType;
    public TypeBuilder BlockListType
    {
        get => Require(_blockListType);
        internal set => Set(ref _blockListType, value);
    }

    private ConstructorBuilder? _blockListCtor;
    public ConstructorBuilder BlockListCtor
    {
        get => Require(_blockListCtor);
        internal set => Set(ref _blockListCtor, value);
    }

    private MethodBuilder? _blockListCheckIp;
    public MethodBuilder BlockListCheckIp
    {
        get => Require(_blockListCheckIp);
        internal set => Set(ref _blockListCheckIp, value);
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

    private TypeBuilder? _socketType;
    public TypeBuilder SocketType
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

    private ConstructorBuilder? _socketCtorTcpClient;
    public ConstructorBuilder SocketCtorTcpClient
    {
        get => Require(_socketCtorTcpClient);
        internal set => Set(ref _socketCtorTcpClient, value);
    }

    private ConstructorBuilder? _socketCtorStream;
    public ConstructorBuilder SocketCtorStream
    {
        get => Require(_socketCtorStream);
        internal set => Set(ref _socketCtorStream, value);
    }

    private MethodBuilder? _socketConnect;
    public MethodBuilder SocketConnect
    {
        get => Require(_socketConnect);
        internal set => Set(ref _socketConnect, value);
    }

    private MethodBuilder? _socketWrite;
    public MethodBuilder SocketWrite
    {
        get => Require(_socketWrite);
        internal set => Set(ref _socketWrite, value);
    }

    private MethodBuilder? _socketStartReading;
    public MethodBuilder SocketStartReading
    {
        get => Require(_socketStartReading);
        internal set => Set(ref _socketStartReading, value);
    }

    private MethodBuilder? _socketGetMember;
    public MethodBuilder SocketGetMember
    {
        get => Require(_socketGetMember);
        internal set => Set(ref _socketGetMember, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Net metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Net metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CreateServer;
        _ = CreateConnection;
        _ = CreateSocket;
        _ = CreateBlockList;
        _ = BlockListType;
        _ = BlockListCtor;
        _ = BlockListCheckIp;
        _ = ServerType;
        _ = ServerCtor;
        _ = SocketType;
        _ = SocketCtor;
        _ = SocketCtorTcpClient;
        _ = SocketCtorStream;
        _ = SocketConnect;
        _ = SocketWrite;
        _ = SocketStartReading;
        _ = SocketGetMember;
        IsComplete = true;
    }
}
