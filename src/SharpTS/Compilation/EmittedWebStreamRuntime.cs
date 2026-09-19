using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional Web stream types, queuing strategies, controller/reader/writer storage,
/// and transform-sink declarations for one compilation. Forward declarations remain
/// readable before body emission; completion validates and freezes every handle.
/// </summary>
public sealed class EmittedWebStreamRuntime
{
    internal EmittedWebStreamRuntime() { }

    public bool IsComplete { get; private set; }

    private ConstructorBuilder? _countQueuingStrategyCtor;
    public ConstructorBuilder CountQueuingStrategyCtor
    {
        get => Require(_countQueuingStrategyCtor);
        internal set => Set(ref _countQueuingStrategyCtor, value);
    }

    private ConstructorBuilder? _byteLengthQueuingStrategyCtor;
    public ConstructorBuilder ByteLengthQueuingStrategyCtor
    {
        get => Require(_byteLengthQueuingStrategyCtor);
        internal set => Set(ref _byteLengthQueuingStrategyCtor, value);
    }

    private TypeBuilder? _writableType;
    public TypeBuilder WritableType
    {
        get => Require(_writableType);
        internal set => Set(ref _writableType, value);
    }

    private ConstructorBuilder? _writableCtor;
    public ConstructorBuilder WritableCtor
    {
        get => Require(_writableCtor);
        internal set => Set(ref _writableCtor, value);
    }

    private TypeBuilder? _readableType;
    public TypeBuilder ReadableType
    {
        get => Require(_readableType);
        internal set => Set(ref _readableType, value);
    }

    private ConstructorBuilder? _readableCtor;
    public ConstructorBuilder ReadableCtor
    {
        get => Require(_readableCtor);
        internal set => Set(ref _readableCtor, value);
    }

    private MethodBuilder? _readableEnqueue;
    public MethodBuilder ReadableEnqueue
    {
        get => Require(_readableEnqueue);
        internal set => Set(ref _readableEnqueue, value);
    }

    private MethodBuilder? _readableCloseStream;
    public MethodBuilder ReadableCloseStream
    {
        get => Require(_readableCloseStream);
        internal set => Set(ref _readableCloseStream, value);
    }

    private MethodBuilder? _readableErrorStream;
    public MethodBuilder ReadableErrorStream
    {
        get => Require(_readableErrorStream);
        internal set => Set(ref _readableErrorStream, value);
    }

    private MethodBuilder? _readableDrainQueuedChunks;
    public MethodBuilder ReadableDrainQueuedChunks
    {
        get => Require(_readableDrainQueuedChunks);
        internal set => Set(ref _readableDrainQueuedChunks, value);
    }

    private MethodBuilder? _readableFrom;
    /// <summary>Static ReadableStream.from drains a guest iterable into a closed readable stream.</summary>
    public MethodBuilder ReadableFrom
    {
        get => Require(_readableFrom);
        internal set => Set(ref _readableFrom, value);
    }

    private TypeBuilder? _transformType;
    public TypeBuilder TransformType
    {
        get => Require(_transformType);
        internal set => Set(ref _transformType, value);
    }

    private ConstructorBuilder? _transformCtor;
    public ConstructorBuilder TransformCtor
    {
        get => Require(_transformCtor);
        internal set => Set(ref _transformCtor, value);
    }

    private MethodBuilder? _buildTransformSink;
    public MethodBuilder BuildTransformSink
    {
        get => Require(_buildTransformSink);
        internal set => Set(ref _buildTransformSink, value);
    }

    private FieldBuilder? _readableQueueField;
    public FieldBuilder ReadableQueueField
    {
        get => Require(_readableQueueField);
        internal set => Set(ref _readableQueueField, value);
    }

    private FieldBuilder? _readableStateField;
    /// <summary>Readable state: 0 = readable, 1 = closed, 2 = errored.</summary>
    public FieldBuilder ReadableStateField
    {
        get => Require(_readableStateField);
        internal set => Set(ref _readableStateField, value);
    }

    private FieldBuilder? _readableStoredErrorField;
    public FieldBuilder ReadableStoredErrorField
    {
        get => Require(_readableStoredErrorField);
        internal set => Set(ref _readableStoredErrorField, value);
    }

    private FieldBuilder? _readableLockedField;
    public FieldBuilder ReadableLockedField
    {
        get => Require(_readableLockedField);
        internal set => Set(ref _readableLockedField, value);
    }

    private FieldBuilder? _readablePullCbField;
    public FieldBuilder ReadablePullCbField
    {
        get => Require(_readablePullCbField);
        internal set => Set(ref _readablePullCbField, value);
    }

    private FieldBuilder? _readableCancelCbField;
    public FieldBuilder ReadableCancelCbField
    {
        get => Require(_readableCancelCbField);
        internal set => Set(ref _readableCancelCbField, value);
    }

    private FieldBuilder? _readableHwmField;
    public FieldBuilder ReadableHwmField
    {
        get => Require(_readableHwmField);
        internal set => Set(ref _readableHwmField, value);
    }

    private FieldBuilder? _readableCloseRequestedField;
    public FieldBuilder ReadableCloseRequestedField
    {
        get => Require(_readableCloseRequestedField);
        internal set => Set(ref _readableCloseRequestedField, value);
    }

    private FieldBuilder? _readableControllerField;
    public FieldBuilder ReadableControllerField
    {
        get => Require(_readableControllerField);
        internal set => Set(ref _readableControllerField, value);
    }

    private FieldBuilder? _readableReaderField;
    public FieldBuilder ReadableReaderField
    {
        get => Require(_readableReaderField);
        internal set => Set(ref _readableReaderField, value);
    }

    private FieldBuilder? _readablePendingReadsField;
    /// <summary>Queue of parked reads, settled before enqueued chunks enter the main queue.</summary>
    public FieldBuilder ReadablePendingReadsField
    {
        get => Require(_readablePendingReadsField);
        internal set => Set(ref _readablePendingReadsField, value);
    }

    private FieldBuilder? _readableControllerStreamField;
    public FieldBuilder ReadableControllerStreamField
    {
        get => Require(_readableControllerStreamField);
        internal set => Set(ref _readableControllerStreamField, value);
    }

    private FieldBuilder? _readableReaderStreamField;
    public FieldBuilder ReadableReaderStreamField
    {
        get => Require(_readableReaderStreamField);
        internal set => Set(ref _readableReaderStreamField, value);
    }

    private FieldBuilder? _writableWriteCallbackField;
    public FieldBuilder WritableWriteCallbackField
    {
        get => Require(_writableWriteCallbackField);
        internal set => Set(ref _writableWriteCallbackField, value);
    }

    private FieldBuilder? _writableCloseCallbackField;
    public FieldBuilder WritableCloseCallbackField
    {
        get => Require(_writableCloseCallbackField);
        internal set => Set(ref _writableCloseCallbackField, value);
    }

    private FieldBuilder? _writableAbortCallbackField;
    public FieldBuilder WritableAbortCallbackField
    {
        get => Require(_writableAbortCallbackField);
        internal set => Set(ref _writableAbortCallbackField, value);
    }

    private FieldBuilder? _writableHwmField;
    public FieldBuilder WritableHwmField
    {
        get => Require(_writableHwmField);
        internal set => Set(ref _writableHwmField, value);
    }

    private FieldBuilder? _writableStateField;
    /// <summary>Writable state: 0 = writable, 1 = closed, 2 = errored.</summary>
    public FieldBuilder WritableStateField
    {
        get => Require(_writableStateField);
        internal set => Set(ref _writableStateField, value);
    }

    private FieldBuilder? _writableStoredErrorField;
    public FieldBuilder WritableStoredErrorField
    {
        get => Require(_writableStoredErrorField);
        internal set => Set(ref _writableStoredErrorField, value);
    }

    private FieldBuilder? _writableWriterField;
    public FieldBuilder WritableWriterField
    {
        get => Require(_writableWriterField);
        internal set => Set(ref _writableWriterField, value);
    }

    private FieldBuilder? _writableControllerField;
    public FieldBuilder WritableControllerField
    {
        get => Require(_writableControllerField);
        internal set => Set(ref _writableControllerField, value);
    }

    private FieldBuilder? _writableControllerStreamField;
    public FieldBuilder WritableControllerStreamField
    {
        get => Require(_writableControllerStreamField);
        internal set => Set(ref _writableControllerStreamField, value);
    }

    private FieldBuilder? _writableWriterStreamField;
    public FieldBuilder WritableWriterStreamField
    {
        get => Require(_writableWriterStreamField);
        internal set => Set(ref _writableWriterStreamField, value);
    }

    private FieldBuilder? _transformReadableField;
    public FieldBuilder TransformReadableField
    {
        get => Require(_transformReadableField);
        internal set => Set(ref _transformReadableField, value);
    }

    private FieldBuilder? _transformWritableField;
    public FieldBuilder TransformWritableField
    {
        get => Require(_transformWritableField);
        internal set => Set(ref _transformWritableField, value);
    }

    private FieldBuilder? _transformHolderTransformerField;
    public FieldBuilder TransformHolderTransformerField
    {
        get => Require(_transformHolderTransformerField);
        internal set => Set(ref _transformHolderTransformerField, value);
    }

    private FieldBuilder? _transformHolderReadableField;
    public FieldBuilder TransformHolderReadableField
    {
        get => Require(_transformHolderReadableField);
        internal set => Set(ref _transformHolderReadableField, value);
    }

    private ConstructorBuilder? _transformHolderCtor;
    public ConstructorBuilder TransformHolderCtor
    {
        get => Require(_transformHolderCtor);
        internal set => Set(ref _transformHolderCtor, value);
    }

    private MethodBuilder? _transformHolderWriteMethod;
    public MethodBuilder TransformHolderWriteMethod
    {
        get => Require(_transformHolderWriteMethod);
        internal set => Set(ref _transformHolderWriteMethod, value);
    }

    private MethodBuilder? _transformHolderCloseMethod;
    public MethodBuilder TransformHolderCloseMethod
    {
        get => Require(_transformHolderCloseMethod);
        internal set => Set(ref _transformHolderCloseMethod, value);
    }

    private MethodBuilder? _transformHolderAbortMethod;
    public MethodBuilder TransformHolderAbortMethod
    {
        get => Require(_transformHolderAbortMethod);
        internal set => Set(ref _transformHolderAbortMethod, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Web stream metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException($"Web stream metadata '{name}' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Web stream metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CountQueuingStrategyCtor;
        _ = ByteLengthQueuingStrategyCtor;
        _ = WritableType;
        _ = WritableCtor;
        _ = ReadableType;
        _ = ReadableCtor;
        _ = ReadableEnqueue;
        _ = ReadableCloseStream;
        _ = ReadableErrorStream;
        _ = ReadableDrainQueuedChunks;
        _ = ReadableFrom;
        _ = TransformType;
        _ = TransformCtor;
        _ = BuildTransformSink;
        _ = ReadableQueueField;
        _ = ReadableStateField;
        _ = ReadableStoredErrorField;
        _ = ReadableLockedField;
        _ = ReadablePullCbField;
        _ = ReadableCancelCbField;
        _ = ReadableHwmField;
        _ = ReadableCloseRequestedField;
        _ = ReadableControllerField;
        _ = ReadableReaderField;
        _ = ReadablePendingReadsField;
        _ = ReadableControllerStreamField;
        _ = ReadableReaderStreamField;
        _ = WritableWriteCallbackField;
        _ = WritableCloseCallbackField;
        _ = WritableAbortCallbackField;
        _ = WritableHwmField;
        _ = WritableStateField;
        _ = WritableStoredErrorField;
        _ = WritableWriterField;
        _ = WritableControllerField;
        _ = WritableControllerStreamField;
        _ = WritableWriterStreamField;
        _ = TransformReadableField;
        _ = TransformWritableField;
        _ = TransformHolderTransformerField;
        _ = TransformHolderReadableField;
        _ = TransformHolderCtor;
        _ = TransformHolderWriteMethod;
        _ = TransformHolderCloseMethod;
        _ = TransformHolderAbortMethod;
        IsComplete = true;
    }
}
