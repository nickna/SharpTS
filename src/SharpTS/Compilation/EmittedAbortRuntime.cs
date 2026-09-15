using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional AbortController/AbortSignal declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedAbortRuntime
{
    internal EmittedAbortRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _namespaceField;
    public FieldBuilder NamespaceField
    {
        get => Require(_namespaceField);
        internal set => Set(ref _namespaceField, value);
    }

    private MethodBuilder? _namespacePopulate;
    public MethodBuilder NamespacePopulate
    {
        get => Require(_namespacePopulate);
        internal set => Set(ref _namespacePopulate, value);
    }

    private MethodBuilder? _fireEvent;
    public MethodBuilder FireEvent
    {
        get => Require(_fireEvent);
        internal set => Set(ref _fireEvent, value);
    }

    private MethodBuilder? _createController;
    public MethodBuilder CreateController
    {
        get => Require(_createController);
        internal set => Set(ref _createController, value);
    }

    private MethodBuilder? _controllerAbort;
    public MethodBuilder ControllerAbort
    {
        get => Require(_controllerAbort);
        internal set => Set(ref _controllerAbort, value);
    }

    private MethodBuilder? _controllerGetSignal;
    public MethodBuilder ControllerGetSignal
    {
        get => Require(_controllerGetSignal);
        internal set => Set(ref _controllerGetSignal, value);
    }

    private MethodBuilder? _signalGetAborted;
    public MethodBuilder SignalGetAborted
    {
        get => Require(_signalGetAborted);
        internal set => Set(ref _signalGetAborted, value);
    }

    private MethodBuilder? _signalGetReason;
    public MethodBuilder SignalGetReason
    {
        get => Require(_signalGetReason);
        internal set => Set(ref _signalGetReason, value);
    }

    private MethodBuilder? _signalGetOnAbort;
    public MethodBuilder SignalGetOnAbort
    {
        get => Require(_signalGetOnAbort);
        internal set => Set(ref _signalGetOnAbort, value);
    }

    private MethodBuilder? _signalSetOnAbort;
    public MethodBuilder SignalSetOnAbort
    {
        get => Require(_signalSetOnAbort);
        internal set => Set(ref _signalSetOnAbort, value);
    }

    private MethodBuilder? _signalThrowIfAborted;
    public MethodBuilder SignalThrowIfAborted
    {
        get => Require(_signalThrowIfAborted);
        internal set => Set(ref _signalThrowIfAborted, value);
    }

    private MethodBuilder? _signalAddEventListener;
    public MethodBuilder SignalAddEventListener
    {
        get => Require(_signalAddEventListener);
        internal set => Set(ref _signalAddEventListener, value);
    }

    private MethodBuilder? _signalRemoveEventListener;
    public MethodBuilder SignalRemoveEventListener
    {
        get => Require(_signalRemoveEventListener);
        internal set => Set(ref _signalRemoveEventListener, value);
    }

    private MethodBuilder? _signalAddEventListenerThis;
    public MethodBuilder SignalAddEventListenerThis
    {
        get => Require(_signalAddEventListenerThis);
        internal set => Set(ref _signalAddEventListenerThis, value);
    }

    private MethodBuilder? _signalRemoveEventListenerThis;
    public MethodBuilder SignalRemoveEventListenerThis
    {
        get => Require(_signalRemoveEventListenerThis);
        internal set => Set(ref _signalRemoveEventListenerThis, value);
    }

    private MethodBuilder? _signalThrowIfAbortedThis;
    public MethodBuilder SignalThrowIfAbortedThis
    {
        get => Require(_signalThrowIfAbortedThis);
        internal set => Set(ref _signalThrowIfAbortedThis, value);
    }

    private MethodBuilder? _signalAbort;
    public MethodBuilder SignalAbort
    {
        get => Require(_signalAbort);
        internal set => Set(ref _signalAbort, value);
    }

    private MethodBuilder? _signalTimeout;
    public MethodBuilder SignalTimeout
    {
        get => Require(_signalTimeout);
        internal set => Set(ref _signalTimeout, value);
    }

    private MethodBuilder? _signalAny;
    public MethodBuilder SignalAny
    {
        get => Require(_signalAny);
        internal set => Set(ref _signalAny, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Abort metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Abort metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = NamespaceField;
        _ = NamespacePopulate;
        _ = FireEvent;
        _ = CreateController;
        _ = ControllerAbort;
        _ = ControllerGetSignal;
        _ = SignalGetAborted;
        _ = SignalGetReason;
        _ = SignalGetOnAbort;
        _ = SignalSetOnAbort;
        _ = SignalThrowIfAborted;
        _ = SignalAddEventListener;
        _ = SignalRemoveEventListener;
        _ = SignalAddEventListenerThis;
        _ = SignalRemoveEventListenerThis;
        _ = SignalThrowIfAbortedThis;
        _ = SignalAbort;
        _ = SignalTimeout;
        _ = SignalAny;
        IsComplete = true;
    }
}
