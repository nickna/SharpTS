using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional Node stream declarations, backing fields, and callback helpers for one compilation.
/// Readable/duplex/transform forward references remain available before body emission;
/// completion validates and freezes every selected handle after runtime finalization.
/// </summary>
public sealed class EmittedNodeStreamRuntime
{
    internal EmittedNodeStreamRuntime(bool hasAbortSignal) => HasAbortSignal = hasAbortSignal;

    /// <summary>The addAbortSignal wrapper is emitted only when AbortController is also enabled.</summary>
    public bool HasAbortSignal { get; }
    public bool IsComplete { get; private set; }

    private Type? _readableType;
    public Type ReadableType
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

    private MethodBuilder? _readablePush;
    public MethodBuilder ReadablePush
    {
        get => Require(_readablePush);
        internal set => Set(ref _readablePush, value);
    }

    private MethodBuilder? _readablePipe;
    public MethodBuilder ReadablePipe
    {
        get => Require(_readablePipe);
        internal set => Set(ref _readablePipe, value);
    }

    private MethodBuilder? _readableDestroy;
    public MethodBuilder ReadableDestroy
    {
        get => Require(_readableDestroy);
        internal set => Set(ref _readableDestroy, value);
    }

    private MethodBuilder? _readableGetAsyncIterator;
    /// <summary>Registers the compiled Readable async iterator through the iterator dispatch hook.</summary>
    public MethodBuilder ReadableGetAsyncIterator
    {
        get => Require(_readableGetAsyncIterator);
        internal set => Set(ref _readableGetAsyncIterator, value);
    }

    private MethodBuilder? _addAbortSignal;
    public MethodBuilder AddAbortSignal
    {
        get
        {
            RequireAbortSignal();
            return Require(_addAbortSignal);
        }
        internal set
        {
            EnsureMutable();
            RequireAbortSignal();
            Set(ref _addAbortSignal, value);
        }
    }

    private Type? _abortCallbackType;
    public Type AbortCallbackType
    {
        get => Require(_abortCallbackType);
        internal set => Set(ref _abortCallbackType, value);
    }

    private ConstructorBuilder? _abortCallbackCtor;
    public ConstructorBuilder AbortCallbackCtor
    {
        get => Require(_abortCallbackCtor);
        internal set => Set(ref _abortCallbackCtor, value);
    }

    private MethodBuilder? _abortCallbackOnAbort;
    public MethodBuilder AbortCallbackOnAbort
    {
        get => Require(_abortCallbackOnAbort);
        internal set => Set(ref _abortCallbackOnAbort, value);
    }

    private MethodBuilder? _readableErroredGetter;
    public MethodBuilder ReadableErroredGetter
    {
        get => Require(_readableErroredGetter);
        internal set => Set(ref _readableErroredGetter, value);
    }

    private MethodBuilder? _writableErroredGetter;
    public MethodBuilder WritableErroredGetter
    {
        get => Require(_writableErroredGetter);
        internal set => Set(ref _writableErroredGetter, value);
    }

    private MethodBuilder? _getDefaultHighWaterMark;
    public MethodBuilder GetDefaultHighWaterMark
    {
        get => Require(_getDefaultHighWaterMark);
        internal set => Set(ref _getDefaultHighWaterMark, value);
    }

    private MethodBuilder? _setDefaultHighWaterMark;
    public MethodBuilder SetDefaultHighWaterMark
    {
        get => Require(_setDefaultHighWaterMark);
        internal set => Set(ref _setDefaultHighWaterMark, value);
    }

    private MethodBuilder? _duplexFrom;
    public MethodBuilder DuplexFrom
    {
        get => Require(_duplexFrom);
        internal set => Set(ref _duplexFrom, value);
    }

    private MethodBuilder? _compose;
    public MethodBuilder Compose
    {
        get => Require(_compose);
        internal set => Set(ref _compose, value);
    }

    private Type? _composeBridgeType;
    public Type ComposeBridgeType
    {
        get => Require(_composeBridgeType);
        internal set => Set(ref _composeBridgeType, value);
    }

    private ConstructorBuilder? _composeBridgeCtor;
    public ConstructorBuilder ComposeBridgeCtor
    {
        get => Require(_composeBridgeCtor);
        internal set => Set(ref _composeBridgeCtor, value);
    }

    private MethodBuilder? _composeBridgeForwardWrite;
    public MethodBuilder ComposeBridgeForwardWrite
    {
        get => Require(_composeBridgeForwardWrite);
        internal set => Set(ref _composeBridgeForwardWrite, value);
    }

    private MethodBuilder? _composeBridgePushData;
    public MethodBuilder ComposeBridgePushData
    {
        get => Require(_composeBridgePushData);
        internal set => Set(ref _composeBridgePushData, value);
    }

    private MethodBuilder? _composeBridgePushEnd;
    public MethodBuilder ComposeBridgePushEnd
    {
        get => Require(_composeBridgePushEnd);
        internal set => Set(ref _composeBridgePushEnd, value);
    }

    private MethodBuilder? _composeBridgeEndFirst;
    public MethodBuilder ComposeBridgeEndFirst
    {
        get => Require(_composeBridgeEndFirst);
        internal set => Set(ref _composeBridgeEndFirst, value);
    }

    private Type? _writableType;
    public Type WritableType
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

    private MethodBuilder? _writableWrite;
    public MethodBuilder WritableWrite
    {
        get => Require(_writableWrite);
        internal set => Set(ref _writableWrite, value);
    }

    private MethodBuilder? _writableEnd;
    public MethodBuilder WritableEnd
    {
        get => Require(_writableEnd);
        internal set => Set(ref _writableEnd, value);
    }

    private MethodBuilder? _writableUncork;
    public MethodBuilder WritableUncork
    {
        get => Require(_writableUncork);
        internal set => Set(ref _writableUncork, value);
    }

    private MethodBuilder? _writableDestroy;
    public MethodBuilder WritableDestroy
    {
        get => Require(_writableDestroy);
        internal set => Set(ref _writableDestroy, value);
    }

    private Type? _duplexType;
    public Type DuplexType
    {
        get => Require(_duplexType);
        internal set => Set(ref _duplexType, value);
    }

    private ConstructorBuilder? _duplexCtor;
    public ConstructorBuilder DuplexCtor
    {
        get => Require(_duplexCtor);
        internal set => Set(ref _duplexCtor, value);
    }

    private MethodBuilder? _duplexWrite;
    public MethodBuilder DuplexWrite
    {
        get => Require(_duplexWrite);
        internal set => Set(ref _duplexWrite, value);
    }

    private MethodBuilder? _duplexEnd;
    public MethodBuilder DuplexEnd
    {
        get => Require(_duplexEnd);
        internal set => Set(ref _duplexEnd, value);
    }

    private Type? _transformType;
    public Type TransformType
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

    private Type? _transformDoneCallbackType;
    public Type TransformDoneCallbackType
    {
        get => Require(_transformDoneCallbackType);
        internal set => Set(ref _transformDoneCallbackType, value);
    }

    private MethodBuilder? _transformDoneCallbackInvoke;
    public MethodBuilder TransformDoneCallbackInvoke
    {
        get => Require(_transformDoneCallbackInvoke);
        internal set => Set(ref _transformDoneCallbackInvoke, value);
    }

    private Type? _writeCallbackWrapperType;
    public Type WriteCallbackWrapperType
    {
        get => Require(_writeCallbackWrapperType);
        internal set => Set(ref _writeCallbackWrapperType, value);
    }

    private MethodBuilder? _writeCallbackWrapperInvoke;
    public MethodBuilder WriteCallbackWrapperInvoke
    {
        get => Require(_writeCallbackWrapperInvoke);
        internal set => Set(ref _writeCallbackWrapperInvoke, value);
    }

    private ConstructorBuilder? _passThroughCtor;
    public ConstructorBuilder PassThroughCtor
    {
        get => Require(_passThroughCtor);
        internal set => Set(ref _passThroughCtor, value);
    }

    private MethodBuilder? _readableSetObjectMode;
    public MethodBuilder ReadableSetObjectMode
    {
        get => Require(_readableSetObjectMode);
        internal set => Set(ref _readableSetObjectMode, value);
    }

    private MethodBuilder? _readableSetHighWaterMark;
    public MethodBuilder ReadableSetHighWaterMark
    {
        get => Require(_readableSetHighWaterMark);
        internal set => Set(ref _readableSetHighWaterMark, value);
    }

    private MethodBuilder? _writableSetObjectMode;
    public MethodBuilder WritableSetObjectMode
    {
        get => Require(_writableSetObjectMode);
        internal set => Set(ref _writableSetObjectMode, value);
    }

    private MethodBuilder? _writableSetWriteCallback;
    public MethodBuilder WritableSetWriteCallback
    {
        get => Require(_writableSetWriteCallback);
        internal set => Set(ref _writableSetWriteCallback, value);
    }

    private MethodBuilder? _writableSetFinalCallback;
    public MethodBuilder WritableSetFinalCallback
    {
        get => Require(_writableSetFinalCallback);
        internal set => Set(ref _writableSetFinalCallback, value);
    }

    private MethodBuilder? _duplexSetObjectMode;
    public MethodBuilder DuplexSetObjectMode
    {
        get => Require(_duplexSetObjectMode);
        internal set => Set(ref _duplexSetObjectMode, value);
    }

    private MethodBuilder? _duplexSetWriteCallback;
    public MethodBuilder DuplexSetWriteCallback
    {
        get => Require(_duplexSetWriteCallback);
        internal set => Set(ref _duplexSetWriteCallback, value);
    }

    private MethodBuilder? _transformSetTransformCallback;
    public MethodBuilder TransformSetTransformCallback
    {
        get => Require(_transformSetTransformCallback);
        internal set => Set(ref _transformSetTransformCallback, value);
    }

    private MethodBuilder? _transformSetFlushCallback;
    public MethodBuilder TransformSetFlushCallback
    {
        get => Require(_transformSetFlushCallback);
        internal set => Set(ref _transformSetFlushCallback, value);
    }

    private ConstructorBuilder? _mapTransformCallbackCtor;
    public ConstructorBuilder MapTransformCallbackCtor
    {
        get => Require(_mapTransformCallbackCtor);
        internal set => Set(ref _mapTransformCallbackCtor, value);
    }

    private ConstructorBuilder? _filterTransformCallbackCtor;
    public ConstructorBuilder FilterTransformCallbackCtor
    {
        get => Require(_filterTransformCallbackCtor);
        internal set => Set(ref _filterTransformCallbackCtor, value);
    }

    private MethodBuilder? _finished;
    public MethodBuilder Finished
    {
        get => Require(_finished);
        internal set => Set(ref _finished, value);
    }

    private MethodBuilder? _pipeline;
    public MethodBuilder Pipeline
    {
        get => Require(_pipeline);
        internal set => Set(ref _pipeline, value);
    }

    private MethodBuilder? _readableFrom;
    public MethodBuilder ReadableFrom
    {
        get => Require(_readableFrom);
        internal set => Set(ref _readableFrom, value);
    }

    private MethodBuilder? _promisePipeline;
    public MethodBuilder PromisePipeline
    {
        get => Require(_promisePipeline);
        internal set => Set(ref _promisePipeline, value);
    }

    private MethodBuilder? _promiseFinished;
    public MethodBuilder PromiseFinished
    {
        get => Require(_promiseFinished);
        internal set => Set(ref _promiseFinished, value);
    }

    private MethodBuilder? _readableFlushChunkToPipes;
    public MethodBuilder ReadableFlushChunkToPipes
    {
        get => Require(_readableFlushChunkToPipes);
        internal set => Set(ref _readableFlushChunkToPipes, value);
    }

    private FieldBuilder? _readableBufferField;
    public FieldBuilder ReadableBufferField
    {
        get => Require(_readableBufferField);
        internal set => Set(ref _readableBufferField, value);
    }

    private FieldBuilder? _readablePipeDestinationsField;
    public FieldBuilder ReadablePipeDestinationsField
    {
        get => Require(_readablePipeDestinationsField);
        internal set => Set(ref _readablePipeDestinationsField, value);
    }

    private FieldBuilder? _readableEndedField;
    public FieldBuilder ReadableEndedField
    {
        get => Require(_readableEndedField);
        internal set => Set(ref _readableEndedField, value);
    }

    private FieldBuilder? _readableDestroyedField;
    public FieldBuilder ReadableDestroyedField
    {
        get => Require(_readableDestroyedField);
        internal set => Set(ref _readableDestroyedField, value);
    }

    private FieldBuilder? _readableEncodingField;
    public FieldBuilder ReadableEncodingField
    {
        get => Require(_readableEncodingField);
        internal set => Set(ref _readableEncodingField, value);
    }

    private FieldBuilder? _readableReadableField;
    public FieldBuilder ReadableReadableField
    {
        get => Require(_readableReadableField);
        internal set => Set(ref _readableReadableField, value);
    }

    private FieldBuilder? _readableFlowingField;
    public FieldBuilder ReadableFlowingField
    {
        get => Require(_readableFlowingField);
        internal set => Set(ref _readableFlowingField, value);
    }

    private FieldBuilder? _readableObjectModeField;
    public FieldBuilder ReadableObjectModeField
    {
        get => Require(_readableObjectModeField);
        internal set => Set(ref _readableObjectModeField, value);
    }

    private FieldBuilder? _readableHighWaterMarkField;
    public FieldBuilder ReadableHighWaterMarkField
    {
        get => Require(_readableHighWaterMarkField);
        internal set => Set(ref _readableHighWaterMarkField, value);
    }

    private FieldBuilder? _readableErroredField;
    public FieldBuilder ReadableErroredField
    {
        get => Require(_readableErroredField);
        internal set => Set(ref _readableErroredField, value);
    }

    private FieldBuilder? _readableErrorField;
    public FieldBuilder ReadableErrorField
    {
        get => Require(_readableErrorField);
        internal set => Set(ref _readableErrorField, value);
    }

    private FieldBuilder? _readableIterWaiterField;
    public FieldBuilder ReadableIterWaiterField
    {
        get => Require(_readableIterWaiterField);
        internal set => Set(ref _readableIterWaiterField, value);
    }

    private MethodBuilder? _readableMakeIterResult;
    public MethodBuilder ReadableMakeIterResult
    {
        get => Require(_readableMakeIterResult);
        internal set => Set(ref _readableMakeIterResult, value);
    }

    private MethodBuilder? _readableIterNext;
    public MethodBuilder ReadableIterNext
    {
        get => Require(_readableIterNext);
        internal set => Set(ref _readableIterNext, value);
    }

    private MethodBuilder? _readableIterReturn;
    public MethodBuilder ReadableIterReturn
    {
        get => Require(_readableIterReturn);
        internal set => Set(ref _readableIterReturn, value);
    }

    private MethodBuilder? _readableDrainToList;
    public MethodBuilder ReadableDrainToList
    {
        get => Require(_readableDrainToList);
        internal set => Set(ref _readableDrainToList, value);
    }

    private ConstructorBuilder? _writeCallbackWrapperCtor;
    public ConstructorBuilder WriteCallbackWrapperCtor
    {
        get => Require(_writeCallbackWrapperCtor);
        internal set => Set(ref _writeCallbackWrapperCtor, value);
    }

    private FieldBuilder? _writeCallbackWrapperUserCallbackField;
    public FieldBuilder WriteCallbackWrapperUserCallbackField
    {
        get => Require(_writeCallbackWrapperUserCallbackField);
        internal set => Set(ref _writeCallbackWrapperUserCallbackField, value);
    }

    private FieldBuilder? _writeCallbackWrapperStreamField;
    public FieldBuilder WriteCallbackWrapperStreamField
    {
        get => Require(_writeCallbackWrapperStreamField);
        internal set => Set(ref _writeCallbackWrapperStreamField, value);
    }

    private FieldBuilder? _writeCallbackWrapperChunkSizeField;
    public FieldBuilder WriteCallbackWrapperChunkSizeField
    {
        get => Require(_writeCallbackWrapperChunkSizeField);
        internal set => Set(ref _writeCallbackWrapperChunkSizeField, value);
    }

    private FieldBuilder? _writableWritableField;
    public FieldBuilder WritableWritableField
    {
        get => Require(_writableWritableField);
        internal set => Set(ref _writableWritableField, value);
    }

    private FieldBuilder? _writableEndedField;
    public FieldBuilder WritableEndedField
    {
        get => Require(_writableEndedField);
        internal set => Set(ref _writableEndedField, value);
    }

    private FieldBuilder? _writableFinishedField;
    public FieldBuilder WritableFinishedField
    {
        get => Require(_writableFinishedField);
        internal set => Set(ref _writableFinishedField, value);
    }

    private FieldBuilder? _writableDestroyedField;
    public FieldBuilder WritableDestroyedField
    {
        get => Require(_writableDestroyedField);
        internal set => Set(ref _writableDestroyedField, value);
    }

    private FieldBuilder? _writableCorkedField;
    public FieldBuilder WritableCorkedField
    {
        get => Require(_writableCorkedField);
        internal set => Set(ref _writableCorkedField, value);
    }

    private FieldBuilder? _writableCorkBufferField;
    public FieldBuilder WritableCorkBufferField
    {
        get => Require(_writableCorkBufferField);
        internal set => Set(ref _writableCorkBufferField, value);
    }

    private FieldBuilder? _writableWriteCallbackField;
    public FieldBuilder WritableWriteCallbackField
    {
        get => Require(_writableWriteCallbackField);
        internal set => Set(ref _writableWriteCallbackField, value);
    }

    private FieldBuilder? _writableFinalCallbackField;
    public FieldBuilder WritableFinalCallbackField
    {
        get => Require(_writableFinalCallbackField);
        internal set => Set(ref _writableFinalCallbackField, value);
    }

    private FieldBuilder? _writableHighWaterMarkField;
    public FieldBuilder WritableHighWaterMarkField
    {
        get => Require(_writableHighWaterMarkField);
        internal set => Set(ref _writableHighWaterMarkField, value);
    }

    private FieldBuilder? _writableObjectModeField;
    public FieldBuilder WritableObjectModeField
    {
        get => Require(_writableObjectModeField);
        internal set => Set(ref _writableObjectModeField, value);
    }

    private FieldBuilder? _writableAutoDestroyField;
    public FieldBuilder WritableAutoDestroyField
    {
        get => Require(_writableAutoDestroyField);
        internal set => Set(ref _writableAutoDestroyField, value);
    }

    private FieldBuilder? _writableLengthField;
    public FieldBuilder WritableLengthField
    {
        get => Require(_writableLengthField);
        internal set => Set(ref _writableLengthField, value);
    }

    private FieldBuilder? _writableNeedDrainField;
    public FieldBuilder WritableNeedDrainField
    {
        get => Require(_writableNeedDrainField);
        internal set => Set(ref _writableNeedDrainField, value);
    }

    private FieldBuilder? _writableErroredField;
    public FieldBuilder WritableErroredField
    {
        get => Require(_writableErroredField);
        internal set => Set(ref _writableErroredField, value);
    }

    private FieldBuilder? _duplexWritableField;
    public FieldBuilder DuplexWritableField
    {
        get => Require(_duplexWritableField);
        internal set => Set(ref _duplexWritableField, value);
    }

    private FieldBuilder? _duplexWriteEndedField;
    public FieldBuilder DuplexWriteEndedField
    {
        get => Require(_duplexWriteEndedField);
        internal set => Set(ref _duplexWriteEndedField, value);
    }

    private FieldBuilder? _duplexWriteFinishedField;
    public FieldBuilder DuplexWriteFinishedField
    {
        get => Require(_duplexWriteFinishedField);
        internal set => Set(ref _duplexWriteFinishedField, value);
    }

    private FieldBuilder? _duplexWriteCorkedField;
    public FieldBuilder DuplexWriteCorkedField
    {
        get => Require(_duplexWriteCorkedField);
        internal set => Set(ref _duplexWriteCorkedField, value);
    }

    private FieldBuilder? _duplexWriteCorkBufferField;
    public FieldBuilder DuplexWriteCorkBufferField
    {
        get => Require(_duplexWriteCorkBufferField);
        internal set => Set(ref _duplexWriteCorkBufferField, value);
    }

    private FieldBuilder? _duplexWriteCallbackField;
    public FieldBuilder DuplexWriteCallbackField
    {
        get => Require(_duplexWriteCallbackField);
        internal set => Set(ref _duplexWriteCallbackField, value);
    }

    private FieldBuilder? _duplexFinalCallbackField;
    public FieldBuilder DuplexFinalCallbackField
    {
        get => Require(_duplexFinalCallbackField);
        internal set => Set(ref _duplexFinalCallbackField, value);
    }

    private FieldBuilder? _duplexWritableObjectModeField;
    public FieldBuilder DuplexWritableObjectModeField
    {
        get => Require(_duplexWritableObjectModeField);
        internal set => Set(ref _duplexWritableObjectModeField, value);
    }

    private FieldBuilder? _duplexWritableHighWaterMarkField;
    public FieldBuilder DuplexWritableHighWaterMarkField
    {
        get => Require(_duplexWritableHighWaterMarkField);
        internal set => Set(ref _duplexWritableHighWaterMarkField, value);
    }

    private FieldBuilder? _transformCallbackField;
    public FieldBuilder TransformCallbackField
    {
        get => Require(_transformCallbackField);
        internal set => Set(ref _transformCallbackField, value);
    }

    private FieldBuilder? _transformFlushCallbackField;
    public FieldBuilder TransformFlushCallbackField
    {
        get => Require(_transformFlushCallbackField);
        internal set => Set(ref _transformFlushCallbackField, value);
    }

    private MethodBuilder? _transformWriteMethod;
    public MethodBuilder TransformWriteMethod
    {
        get => Require(_transformWriteMethod);
        internal set => Set(ref _transformWriteMethod, value);
    }

    private ConstructorBuilder? _transformDoneCallbackCtor;
    public ConstructorBuilder TransformDoneCallbackCtor
    {
        get => Require(_transformDoneCallbackCtor);
        internal set => Set(ref _transformDoneCallbackCtor, value);
    }

    private FieldBuilder? _transformDoneCallbackStreamField;
    public FieldBuilder TransformDoneCallbackStreamField
    {
        get => Require(_transformDoneCallbackStreamField);
        internal set => Set(ref _transformDoneCallbackStreamField, value);
    }

    private FieldBuilder? _transformDoneCallbackUserCallbackField;
    public FieldBuilder TransformDoneCallbackUserCallbackField
    {
        get => Require(_transformDoneCallbackUserCallbackField);
        internal set => Set(ref _transformDoneCallbackUserCallbackField, value);
    }

    private FieldBuilder? _finishedCleanupStreamField;
    public FieldBuilder FinishedCleanupStreamField
    {
        get => Require(_finishedCleanupStreamField);
        internal set => Set(ref _finishedCleanupStreamField, value);
    }

    private FieldBuilder? _finishedCleanupCallbackField;
    public FieldBuilder FinishedCleanupCallbackField
    {
        get => Require(_finishedCleanupCallbackField);
        internal set => Set(ref _finishedCleanupCallbackField, value);
    }

    private ConstructorBuilder? _finishedCleanupCtor;
    public ConstructorBuilder FinishedCleanupCtor
    {
        get => Require(_finishedCleanupCtor);
        internal set => Set(ref _finishedCleanupCtor, value);
    }

    private MethodBuilder? _finishedCleanupInvokeMethod;
    public MethodBuilder FinishedCleanupInvokeMethod
    {
        get => Require(_finishedCleanupInvokeMethod);
        internal set => Set(ref _finishedCleanupInvokeMethod, value);
    }

    private void RequireAbortSignal()
    {
        if (!HasAbortSignal)
            throw new InvalidOperationException("Node stream abort-signal support was not enabled for this compilation.");
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Node stream metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Node stream metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ReadableType;
        _ = ReadableCtor;
        _ = ReadablePush;
        _ = ReadablePipe;
        _ = ReadableDestroy;
        _ = ReadableGetAsyncIterator;
        if (HasAbortSignal)
            _ = AddAbortSignal;
        _ = AbortCallbackType;
        _ = AbortCallbackCtor;
        _ = AbortCallbackOnAbort;
        _ = ReadableErroredGetter;
        _ = WritableErroredGetter;
        _ = GetDefaultHighWaterMark;
        _ = SetDefaultHighWaterMark;
        _ = DuplexFrom;
        _ = Compose;
        _ = ComposeBridgeType;
        _ = ComposeBridgeCtor;
        _ = ComposeBridgeForwardWrite;
        _ = ComposeBridgePushData;
        _ = ComposeBridgePushEnd;
        _ = ComposeBridgeEndFirst;
        _ = WritableType;
        _ = WritableCtor;
        _ = WritableWrite;
        _ = WritableEnd;
        _ = WritableUncork;
        _ = WritableDestroy;
        _ = DuplexType;
        _ = DuplexCtor;
        _ = DuplexWrite;
        _ = DuplexEnd;
        _ = TransformType;
        _ = TransformCtor;
        _ = TransformDoneCallbackType;
        _ = TransformDoneCallbackInvoke;
        _ = WriteCallbackWrapperType;
        _ = WriteCallbackWrapperInvoke;
        _ = PassThroughCtor;
        _ = ReadableSetObjectMode;
        _ = ReadableSetHighWaterMark;
        _ = WritableSetObjectMode;
        _ = WritableSetWriteCallback;
        _ = WritableSetFinalCallback;
        _ = DuplexSetObjectMode;
        _ = DuplexSetWriteCallback;
        _ = TransformSetTransformCallback;
        _ = TransformSetFlushCallback;
        _ = MapTransformCallbackCtor;
        _ = FilterTransformCallbackCtor;
        _ = Finished;
        _ = Pipeline;
        _ = ReadableFrom;
        _ = PromisePipeline;
        _ = PromiseFinished;
        _ = ReadableFlushChunkToPipes;
        _ = ReadableBufferField;
        _ = ReadablePipeDestinationsField;
        _ = ReadableEndedField;
        _ = ReadableDestroyedField;
        _ = ReadableEncodingField;
        _ = ReadableReadableField;
        _ = ReadableFlowingField;
        _ = ReadableObjectModeField;
        _ = ReadableHighWaterMarkField;
        _ = ReadableErroredField;
        _ = ReadableErrorField;
        _ = ReadableIterWaiterField;
        _ = ReadableMakeIterResult;
        _ = ReadableIterNext;
        _ = ReadableIterReturn;
        _ = ReadableDrainToList;
        _ = WriteCallbackWrapperCtor;
        _ = WriteCallbackWrapperUserCallbackField;
        _ = WriteCallbackWrapperStreamField;
        _ = WriteCallbackWrapperChunkSizeField;
        _ = WritableWritableField;
        _ = WritableEndedField;
        _ = WritableFinishedField;
        _ = WritableDestroyedField;
        _ = WritableCorkedField;
        _ = WritableCorkBufferField;
        _ = WritableWriteCallbackField;
        _ = WritableFinalCallbackField;
        _ = WritableHighWaterMarkField;
        _ = WritableObjectModeField;
        _ = WritableAutoDestroyField;
        _ = WritableLengthField;
        _ = WritableNeedDrainField;
        _ = WritableErroredField;
        _ = DuplexWritableField;
        _ = DuplexWriteEndedField;
        _ = DuplexWriteFinishedField;
        _ = DuplexWriteCorkedField;
        _ = DuplexWriteCorkBufferField;
        _ = DuplexWriteCallbackField;
        _ = DuplexFinalCallbackField;
        _ = DuplexWritableObjectModeField;
        _ = DuplexWritableHighWaterMarkField;
        _ = TransformCallbackField;
        _ = TransformFlushCallbackField;
        _ = TransformWriteMethod;
        _ = TransformDoneCallbackCtor;
        _ = TransformDoneCallbackStreamField;
        _ = TransformDoneCallbackUserCallbackField;
        _ = FinishedCleanupStreamField;
        _ = FinishedCleanupCallbackField;
        _ = FinishedCleanupCtor;
        _ = FinishedCleanupInvokeMethod;
        IsComplete = true;
    }
}
