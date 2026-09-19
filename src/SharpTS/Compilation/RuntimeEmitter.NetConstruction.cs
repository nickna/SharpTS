using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Immutable construction values live only inside one EmitAll invocation.
    // Public declarations continue to belong to the checked EmittedNetRuntime.
    private sealed record NetSocketFields(
        FieldBuilder Client,
        FieldBuilder Stream,
        FieldBuilder Connecting,
        FieldBuilder Destroyed,
        FieldBuilder CloseEmitted,
        FieldBuilder Ended,
        FieldBuilder BytesRead,
        FieldBuilder BytesWritten,
        FieldBuilder Encoding,
        FieldBuilder ReadingStarted,
        FieldBuilder ReadCts,
        FieldBuilder IsIpc,
        FieldBuilder PipePath,
        FieldBuilder ReadReady,
        FieldBuilder ConnectHost,
        FieldBuilder ConnectPort,
        FieldBuilder WriteQueue,
        FieldBuilder WriteWorkerRunning,
        FieldBuilder ShutdownAfterFlush,
        FieldBuilder WritableLength,
        FieldBuilder WritableHwm,
        FieldBuilder NeedDrain,
        FieldBuilder PendingWriteCallbacks,
        FieldBuilder PendingWriteError,
        FieldBuilder PendingEndCallback,
        FieldBuilder AllowHalfOpen,
        FieldBuilder EndReceived,
        FieldBuilder FinishAfterEnd);

    private sealed record NetSocketMethods(
        MethodBuilder End,
        MethodBuilder Destroy,
        MethodBuilder SetEncoding,
        MethodBuilder EnqueueWrite,
        MethodBuilder WriteWorker,
        MethodBuilder FlushTick,
        MethodBuilder FireWriteCallbacks,
        MethodBuilder FireWriteError,
        MethodBuilder FireEndCallback,
        MethodBuilder ShutdownWritable);

    private sealed record NetServerFields(
        FieldBuilder Listener,
        FieldBuilder IsListening,
        FieldBuilder Cts,
        FieldBuilder ConnectionListener,
        FieldBuilder Port,
        FieldBuilder Host,
        FieldBuilder MaxConnections,
        FieldBuilder Connections,
        FieldBuilder IsIpc,
        FieldBuilder PipePath,
        FieldBuilder UnixSocket,
        FieldBuilder PipeReady,
        FieldBuilder SocketHwm,
        FieldBuilder BlockList,
        FieldBuilder SocketAllowHalfOpen);

    private sealed record NetServerMethods(
        MethodBuilder Listen,
        MethodBuilder Close,
        MethodBuilder Address,
        MethodBuilder GetConnections,
        MethodBuilder GetMember,
        MethodBuilder SetMember);

    private sealed record NetSocketConstruction(NetSocketFields Fields, NetSocketMethods Methods);
    private sealed record NetServerConstruction(NetServerFields Fields, NetServerMethods Methods);
    private sealed record NetConstruction(NetSocketConstruction Socket, NetServerConstruction Server);

    private readonly record struct NetClosureMethods(ConstructorBuilder Constructor, MethodBuilder Run);
    private sealed record NetSocketClosures(
        NetClosureMethods ReadData, NetClosureMethods ReadEnd,
        NetClosureMethods ConnectOk, NetClosureMethods ConnectErr);
    private sealed record NetServerClosures(NetClosureMethods TcpAccept, NetClosureMethods IpcAccept);
    private sealed record NetClosureConstruction(NetSocketClosures Socket, NetServerClosures Server);

    private static NetConstruction RequireNetConstruction(NetConstruction? construction)
        => construction ?? throw new InvalidOperationException("Net construction requires the Net feature declarations.");
}
