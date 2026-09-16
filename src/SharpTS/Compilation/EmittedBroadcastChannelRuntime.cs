using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional BroadcastChannel declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedBroadcastChannelRuntime
{
    internal EmittedBroadcastChannelRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private FieldBuilder? _registry;
    public FieldBuilder Registry
    {
        get => Require(_registry);
        internal set => Set(ref _registry, value);
    }

    private FieldBuilder? _nextId;
    public FieldBuilder NextId
    {
        get => Require(_nextId);
        internal set => Set(ref _nextId, value);
    }

    // Per-assembly sentinel for receiver-side clone failures, shared by all channel subscribers.
    private FieldBuilder? _cloneError;
    public FieldBuilder CloneError
    {
        get => Require(_cloneError);
        internal set => Set(ref _cloneError, value);
    }

    private FieldBuilder? _name;
    public FieldBuilder Name
    {
        get => Require(_name);
        internal set => Set(ref _name, value);
    }

    private FieldBuilder? _id;
    public FieldBuilder Id
    {
        get => Require(_id);
        internal set => Set(ref _id, value);
    }

    private FieldBuilder? _closed;
    public FieldBuilder Closed
    {
        get => Require(_closed);
        internal set => Set(ref _closed, value);
    }

    private FieldBuilder? _refed;
    public FieldBuilder Refed
    {
        get => Require(_refed);
        internal set => Set(ref _refed, value);
    }

    private FieldBuilder? _pending;
    public FieldBuilder Pending
    {
        get => Require(_pending);
        internal set => Set(ref _pending, value);
    }

    private FieldBuilder? _onMessage;
    public FieldBuilder OnMessage
    {
        get => Require(_onMessage);
        internal set => Set(ref _onMessage, value);
    }

    private FieldBuilder? _onMessageError;
    public FieldBuilder OnMessageError
    {
        get => Require(_onMessageError);
        internal set => Set(ref _onMessageError, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private MethodBuilder? _drain;
    public MethodBuilder Drain
    {
        get => Require(_drain);
        internal set => Set(ref _drain, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"BroadcastChannel metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("BroadcastChannel metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Registry;
        _ = NextId;
        _ = CloneError;
        _ = Name;
        _ = Id;
        _ = Closed;
        _ = Refed;
        _ = Pending;
        _ = OnMessage;
        _ = OnMessageError;
        _ = Ctor;
        _ = Drain;
        IsComplete = true;
    }
}
