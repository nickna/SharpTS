using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required MessagePort declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedMessagePortRuntime
{
    internal EmittedMessagePortRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private FieldBuilder? _partner;
    public FieldBuilder Partner
    {
        get => Require(_partner);
        internal set => Set(ref _partner, value);
    }

    private FieldBuilder? _pending;
    public FieldBuilder Pending
    {
        get => Require(_pending);
        internal set => Set(ref _pending, value);
    }

    private FieldBuilder? _started;
    public FieldBuilder Started
    {
        get => Require(_started);
        internal set => Set(ref _started, value);
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

    // Set on both peers when transferred; only started cross-thread ports keep their owner loop alive.
    private FieldBuilder? _crossThread;
    public FieldBuilder CrossThread
    {
        get => Require(_crossThread);
        internal set => Set(ref _crossThread, value);
    }

    // Reflectively installed bridge notification; posting reads it with the original volatile semantics.
    private FieldBuilder? _onEnqueue;
    public FieldBuilder OnEnqueue
    {
        get => Require(_onEnqueue);
        internal set => Set(ref _onEnqueue, value);
    }

    // Per-assembly sentinel created by the port static constructor for receiver-side messageerror delivery.
    private FieldBuilder? _cloneError;
    public FieldBuilder CloneError
    {
        get => Require(_cloneError);
        internal set => Set(ref _cloneError, value);
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

    private MethodBuilder? _start;
    public MethodBuilder Start
    {
        get => Require(_start);
        internal set => Set(ref _start, value);
    }

    private MethodBuilder? _ref;
    public MethodBuilder Ref
    {
        get => Require(_ref);
        internal set => Set(ref _ref, value);
    }

    private MethodBuilder? _unref;
    public MethodBuilder Unref
    {
        get => Require(_unref);
        internal set => Set(ref _unref, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"MessagePort metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("MessagePort metadata emission is already complete.");
    }

    internal void ValidateDeclarations()
    {
        _ = Type;
        _ = Partner;
        _ = Pending;
        _ = Started;
        _ = Closed;
        _ = Refed;
        _ = CrossThread;
        _ = OnEnqueue;
        _ = CloneError;
        _ = Ctor;
        _ = Drain;
        _ = Start;
        _ = Ref;
        _ = Unref;
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        ValidateDeclarations();
        IsComplete = true;
    }
}
