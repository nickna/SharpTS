using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Datagram metadata for one compilation. Declarations support forward references;
/// completion validates and freezes handles after the receive worker and types are finalized.
/// </summary>
public sealed class EmittedDgramRuntime
{
    internal EmittedDgramRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _createSocket;
    public MethodBuilder CreateSocket
    {
        get => Require(_createSocket);
        internal set => Set(ref _createSocket, value);
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

    private MethodBuilder? _receiveWorker;
    public MethodBuilder ReceiveWorker
    {
        get => Require(_receiveWorker);
        internal set => Set(ref _receiveWorker, value);
    }

    private ConstructorBuilder? _messageClosureCtor;
    public ConstructorBuilder MessageClosureCtor
    {
        get => Require(_messageClosureCtor);
        internal set => Set(ref _messageClosureCtor, value);
    }

    private MethodBuilder? _messageClosureRun;
    public MethodBuilder MessageClosureRun
    {
        get => Require(_messageClosureRun);
        internal set => Set(ref _messageClosureRun, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Dgram metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Dgram metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CreateSocket;
        _ = SocketType;
        _ = SocketCtor;
        _ = ReceiveWorker;
        _ = MessageClosureCtor;
        _ = MessageClosureRun;
        IsComplete = true;
    }
}
