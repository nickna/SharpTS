using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required worker declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedWorkerRuntime
{
    internal EmittedWorkerRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => Set(ref _create, value);
    }

    private MethodBuilder? _isMainThread;
    public MethodBuilder IsMainThread
    {
        get => Require(_isMainThread);
        internal set => Set(ref _isMainThread, value);
    }

    private MethodBuilder? _threadId;
    public MethodBuilder ThreadId
    {
        get => Require(_threadId);
        internal set => Set(ref _threadId, value);
    }

    private MethodBuilder? _workerData;
    public MethodBuilder WorkerData
    {
        get => Require(_workerData);
        internal set => Set(ref _workerData, value);
    }

    private MethodBuilder? _parentPort;
    public MethodBuilder ParentPort
    {
        get => Require(_parentPort);
        internal set => Set(ref _parentPort, value);
    }

    // Forward-declared before MessagePort emission; its body is filled before RuntimeClass finalization.
    private MethodBuilder? _receiveMessageOnPort;
    public MethodBuilder ReceiveMessageOnPort
    {
        get => Require(_receiveMessageOnPort);
        internal set => Set(ref _receiveMessageOnPort, value);
    }

    private MethodBuilder? _getEnvironmentData;
    public MethodBuilder GetEnvironmentData
    {
        get => Require(_getEnvironmentData);
        internal set => Set(ref _getEnvironmentData, value);
    }

    private MethodBuilder? _setEnvironmentData;
    public MethodBuilder SetEnvironmentData
    {
        get => Require(_setEnvironmentData);
        internal set => Set(ref _setEnvironmentData, value);
    }

    private MethodBuilder? _markAsUntransferable;
    public MethodBuilder MarkAsUntransferable
    {
        get => Require(_markAsUntransferable);
        internal set => Set(ref _markAsUntransferable, value);
    }

    // Per-assembly cache of the host bridge receive ABI, including types with no matching method.
    private FieldBuilder? _foreignReceiveType;
    public FieldBuilder ForeignReceiveType
    {
        get => Require(_foreignReceiveType);
        internal set => Set(ref _foreignReceiveType, value);
    }

    private FieldBuilder? _foreignReceiveMethod;
    public FieldBuilder ForeignReceiveMethod
    {
        get => Require(_foreignReceiveMethod);
        internal set => Set(ref _foreignReceiveMethod, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"worker metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("worker metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Create;
        _ = IsMainThread;
        _ = ThreadId;
        _ = WorkerData;
        _ = ParentPort;
        _ = ReceiveMessageOnPort;
        _ = GetEnvironmentData;
        _ = SetEnvironmentData;
        _ = MarkAsUntransferable;
        _ = ForeignReceiveType;
        _ = ForeignReceiveMethod;
        IsComplete = true;
    }
}
