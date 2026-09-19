using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required EventEmitter and listener-wrapper metadata. Declarations support forward calls;
/// completion validates and freezes all handles after runtime emission has finished.
/// </summary>
public sealed class EmittedEventEmitterRuntime
{
    internal EmittedEventEmitterRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private ConstructorBuilder? _listenerWrapperCtor;
    public ConstructorBuilder ListenerWrapperCtor
    {
        get => Require(_listenerWrapperCtor);
        internal set => Set(ref _listenerWrapperCtor, value);
    }

    private FieldBuilder? _defaultMaxListeners;
    public FieldBuilder DefaultMaxListeners
    {
        get => Require(_defaultMaxListeners);
        internal set => Set(ref _defaultMaxListeners, value);
    }

    private MethodBuilder? _on;
    public MethodBuilder On
    {
        get => Require(_on);
        internal set => Set(ref _on, value);
    }

    private MethodBuilder? _once;
    public MethodBuilder Once
    {
        get => Require(_once);
        internal set => Set(ref _once, value);
    }

    private MethodBuilder? _off;
    public MethodBuilder Off
    {
        get => Require(_off);
        internal set => Set(ref _off, value);
    }

    private MethodBuilder? _emit;
    public MethodBuilder Emit
    {
        get => Require(_emit);
        internal set => Set(ref _emit, value);
    }

    private MethodBuilder? _removeAllListeners;
    public MethodBuilder RemoveAllListeners
    {
        get => Require(_removeAllListeners);
        internal set => Set(ref _removeAllListeners, value);
    }

    private MethodBuilder? _listeners;
    public MethodBuilder Listeners
    {
        get => Require(_listeners);
        internal set => Set(ref _listeners, value);
    }

    private MethodBuilder? _listenerCount;
    public MethodBuilder ListenerCount
    {
        get => Require(_listenerCount);
        internal set => Set(ref _listenerCount, value);
    }

    private MethodBuilder? _eventNames;
    public MethodBuilder EventNames
    {
        get => Require(_eventNames);
        internal set => Set(ref _eventNames, value);
    }

    private MethodBuilder? _prependListener;
    public MethodBuilder PrependListener
    {
        get => Require(_prependListener);
        internal set => Set(ref _prependListener, value);
    }

    private MethodBuilder? _prependOnceListener;
    public MethodBuilder PrependOnceListener
    {
        get => Require(_prependOnceListener);
        internal set => Set(ref _prependOnceListener, value);
    }

    private MethodBuilder? _setMaxListeners;
    public MethodBuilder SetMaxListeners
    {
        get => Require(_setMaxListeners);
        internal set => Set(ref _setMaxListeners, value);
    }

    private MethodBuilder? _getMaxListeners;
    public MethodBuilder GetMaxListeners
    {
        get => Require(_getMaxListeners);
        internal set => Set(ref _getMaxListeners, value);
    }

    private MethodBuilder? _addListenerInternal;
    public MethodBuilder AddListenerInternal
    {
        get => Require(_addListenerInternal);
        internal set => Set(ref _addListenerInternal, value);
    }

    private MethodBuilder? _onListenerAdded;
    public MethodBuilder OnListenerAdded
    {
        get => Require(_onListenerAdded);
        internal set => Set(ref _onListenerAdded, value);
    }

    private MethodBuilder? _enableCaptureRejections;
    public MethodBuilder EnableCaptureRejections
    {
        get => Require(_enableCaptureRejections);
        internal set => Set(ref _enableCaptureRejections, value);
    }

    private FieldBuilder? _eventsField;
    public FieldBuilder EventsField
    {
        get => Require(_eventsField);
        internal set => Set(ref _eventsField, value);
    }

    private FieldBuilder? _maxListenersField;
    public FieldBuilder MaxListenersField
    {
        get => Require(_maxListenersField);
        internal set => Set(ref _maxListenersField, value);
    }

    private FieldBuilder? _captureRejectionsField;
    public FieldBuilder CaptureRejectionsField
    {
        get => Require(_captureRejectionsField);
        internal set => Set(ref _captureRejectionsField, value);
    }

    private MethodBuilder? _routeCaptureRejection;
    public MethodBuilder RouteCaptureRejection
    {
        get => Require(_routeCaptureRejection);
        internal set => Set(ref _routeCaptureRejection, value);
    }

    private TypeBuilder? _listenerWrapperType;
    public TypeBuilder ListenerWrapperType
    {
        get => Require(_listenerWrapperType);
        internal set => Set(ref _listenerWrapperType, value);
    }

    private FieldBuilder? _listenerWrapperListenerField;
    public FieldBuilder ListenerWrapperListenerField
    {
        get => Require(_listenerWrapperListenerField);
        internal set => Set(ref _listenerWrapperListenerField, value);
    }

    private FieldBuilder? _listenerWrapperOnceField;
    public FieldBuilder ListenerWrapperOnceField
    {
        get => Require(_listenerWrapperOnceField);
        internal set => Set(ref _listenerWrapperOnceField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"EventEmitter metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException($"EventEmitter metadata '{name}' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("EventEmitter metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = ListenerWrapperCtor;
        _ = DefaultMaxListeners;
        _ = On;
        _ = Once;
        _ = Off;
        _ = Emit;
        _ = RemoveAllListeners;
        _ = Listeners;
        _ = ListenerCount;
        _ = EventNames;
        _ = PrependListener;
        _ = PrependOnceListener;
        _ = SetMaxListeners;
        _ = GetMaxListeners;
        _ = AddListenerInternal;
        _ = OnListenerAdded;
        _ = EnableCaptureRejections;
        _ = EventsField;
        _ = MaxListenersField;
        _ = CaptureRejectionsField;
        _ = RouteCaptureRejection;
        _ = ListenerWrapperType;
        _ = ListenerWrapperListenerField;
        _ = ListenerWrapperOnceField;
        IsComplete = true;
    }
}
