using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the WHATWG Streams <c>WritableStream</c>.
/// </summary>
/// <remarks>
/// Simplified spec implementation covering the default controller and writer.
/// Writes are serialized through an internal queue: a new write doesn't start
/// its sink <c>write()</c> callback until the previous one resolves.
/// </remarks>
public class SharpTSWritableStream : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    internal enum WritableState { Writable, Erroring, Errored, Closed }

    internal WritableState State;
    internal object? StoredError;

    internal object? WriteAlgorithm;
    internal object? CloseAlgorithm;
    internal object? AbortAlgorithm;
    internal object? SizeAlgorithm;
    internal double HighWaterMark;

    internal bool Started;
    internal bool CloseRequested;
    internal bool InFlight;
    internal double QueueTotalSize;

    internal readonly Queue<WriteRequest> WriteQueue = new();

    internal SharpTSWritableStreamDefaultWriter? Writer;
    internal readonly SharpTSWritableStreamDefaultController Controller;

    internal Interp? OwnerInterpreter;

    internal class WriteRequest
    {
        public object? Chunk;
        public double Size;
        public TaskCompletionSource<object?> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Pending close request (set by writer.close()/controller).
    internal TaskCompletionSource<object?>? PendingCloseTcs;

    public SharpTSWritableStream(Interp? interp, object? underlyingSink, object? strategy)
    {
        OwnerInterpreter = interp;
        Controller = new SharpTSWritableStreamDefaultController(this);
        State = WritableState.Writable;

        (HighWaterMark, SizeAlgorithm) = WebStreamsHelpers.ExtractStrategy(strategy, defaultHwm: 1.0);

        if (underlyingSink != null)
        {
            WriteAlgorithm = StreamFields.GetCallback(underlyingSink, "write");
            CloseAlgorithm = StreamFields.GetCallback(underlyingSink, "close");
            AbortAlgorithm = StreamFields.GetCallback(underlyingSink, "abort");

            var startFn = StreamFields.GetCallback(underlyingSink, "start");
            if (startFn != null)
            {
                InvokeStart(startFn);
            }
            else
            {
                Started = true;
            }
        }
        else
        {
            Started = true;
        }
    }

    private void InvokeStart(object startFn)
    {
        try
        {
            var result = RuntimeCallableDispatcher.Invoke(OwnerInterpreter, startFn, Controller);
            if (result is SharpTSPromise p)
            {
                _ = CompleteStartAsync(p);
            }
            else
            {
                Started = true;
                AdvanceQueue();
            }
        }
        catch (Exception ex)
        {
            ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    private async Task CompleteStartAsync(SharpTSPromise p)
    {
        try
        {
            await p.GetValueAsync();
            Started = true;
            AdvanceQueue();
        }
        catch (Exception ex)
        {
            ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    public bool Locked => Writer != null;

    internal double DesiredSize => HighWaterMark - QueueTotalSize;

    internal Task<object?> EnqueueWrite(object? chunk)
    {
        if (State != WritableState.Writable)
        {
            var tcs = new TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(StoredError ?? "WritableStream not writable"));
            return tcs.Task;
        }

        double size;
        try
        {
            size = SizeAlgorithm != null
                ? Interp.ToNumber(RuntimeCallableDispatcher.Invoke(OwnerInterpreter, SizeAlgorithm, chunk))
                : 1.0;
        }
        catch (Exception ex)
        {
            ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
            var tcs = new TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(StoredError));
            return tcs.Task;
        }

        var req = new WriteRequest { Chunk = chunk, Size = size };
        WriteQueue.Enqueue(req);
        QueueTotalSize += size;
        AdvanceQueue();
        return req.Tcs.Task;
    }

    private void AdvanceQueue()
    {
        if (!Started || InFlight) return;
        if (State != WritableState.Writable && State != WritableState.Closed) return;

        if (WriteQueue.Count == 0)
        {
            // No more pending writes: process close if requested.
            if (CloseRequested && State == WritableState.Writable)
            {
                FinishClose();
            }
            return;
        }

        var req = WriteQueue.Dequeue();
        InFlight = true;

        if (WriteAlgorithm is null)
        {
            QueueTotalSize -= req.Size;
            if (QueueTotalSize < 0) QueueTotalSize = 0;
            req.Tcs.TrySetResult(SharpTSUndefined.Instance);
            InFlight = false;
            Writer?.NotifyDrained();
            AdvanceQueue();
            return;
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(OwnerInterpreter, WriteAlgorithm, req.Chunk, Controller);
            if (result is SharpTSPromise p)
            {
                _ = AwaitWriteAsync(p, req);
            }
            else
            {
                FinishWrite(req, null);
            }
        }
        catch (Exception ex)
        {
            FinishWrite(req, ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    private async Task AwaitWriteAsync(SharpTSPromise p, WriteRequest req)
    {
        try
        {
            await p.GetValueAsync();
            FinishWrite(req, null);
        }
        catch (Exception ex)
        {
            FinishWrite(req, ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    private void FinishWrite(WriteRequest req, object? error)
    {
        InFlight = false;
        QueueTotalSize -= req.Size;
        if (QueueTotalSize < 0) QueueTotalSize = 0;

        if (error != null)
        {
            req.Tcs.TrySetException(new SharpTSPromiseRejectedException(error));
            ErrorInternal(error);
            return;
        }

        req.Tcs.TrySetResult(SharpTSUndefined.Instance);
        Writer?.NotifyDrained();
        AdvanceQueue();
    }

    private void FinishClose()
    {
        if (State == WritableState.Closed) return;

        if (CloseAlgorithm is null)
        {
            State = WritableState.Closed;
            PendingCloseTcs?.TrySetResult(SharpTSUndefined.Instance);
            Writer?.NotifyClosed();
            return;
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(OwnerInterpreter, CloseAlgorithm);
            if (result is SharpTSPromise p)
            {
                _ = AwaitCloseAsync(p);
            }
            else
            {
                State = WritableState.Closed;
                PendingCloseTcs?.TrySetResult(SharpTSUndefined.Instance);
                Writer?.NotifyClosed();
            }
        }
        catch (Exception ex)
        {
            var err = ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex;
            PendingCloseTcs?.TrySetException(new SharpTSPromiseRejectedException(err));
            ErrorInternal(err);
        }
    }

    private async Task AwaitCloseAsync(SharpTSPromise p)
    {
        try
        {
            await p.GetValueAsync();
            State = WritableState.Closed;
            PendingCloseTcs?.TrySetResult(SharpTSUndefined.Instance);
            Writer?.NotifyClosed();
        }
        catch (Exception ex)
        {
            var err = ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex;
            PendingCloseTcs?.TrySetException(new SharpTSPromiseRejectedException(err));
            ErrorInternal(err);
        }
    }

    internal void CloseRequestedInternal()
    {
        if (State != WritableState.Writable) return;
        CloseRequested = true;
        PendingCloseTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        AdvanceQueue();
    }

    internal Task<object?> CloseAsyncInternal()
    {
        if (State != WritableState.Writable)
        {
            var tcs = new TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(StoredError ?? "WritableStream not writable"));
            return tcs.Task;
        }
        PendingCloseTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CloseRequestedInternal();
        return PendingCloseTcs.Task;
    }

    internal Task<object?> AbortInternal(object? reason)
    {
        if (State == WritableState.Closed || State == WritableState.Errored)
        {
            return Task.FromResult<object?>(SharpTSUndefined.Instance);
        }

        // Reject pending writes.
        while (WriteQueue.Count > 0)
        {
            var req = WriteQueue.Dequeue();
            req.Tcs.TrySetException(new SharpTSPromiseRejectedException(reason));
        }
        QueueTotalSize = 0;

        State = WritableState.Errored;
        StoredError = reason;
        PendingCloseTcs?.TrySetException(new SharpTSPromiseRejectedException(reason));
        Writer?.NotifyErrored(reason);

        if (AbortAlgorithm is null)
        {
            return Task.FromResult<object?>(SharpTSUndefined.Instance);
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(OwnerInterpreter, AbortAlgorithm, reason);
            if (result is SharpTSPromise p) return p.Task;
            return Task.FromResult<object?>(SharpTSUndefined.Instance);
        }
        catch (Exception ex)
        {
            var tcs = new TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex));
            return tcs.Task;
        }
    }

    internal void ErrorInternal(object? error)
    {
        if (State == WritableState.Errored || State == WritableState.Closed) return;
        State = WritableState.Errored;
        StoredError = error;

        while (WriteQueue.Count > 0)
        {
            var req = WriteQueue.Dequeue();
            req.Tcs.TrySetException(new SharpTSPromiseRejectedException(error));
        }
        QueueTotalSize = 0;
        PendingCloseTcs?.TrySetException(new SharpTSPromiseRejectedException(error));
        Writer?.NotifyErrored(error);
    }

    public object? GetMember(string name)
    {
        return name switch
        {
            "locked" => (object)Locked,
            "getWriter" => BuiltInMethod.CreateV2("getWriter", 0, (_, _, _) =>
            {
                if (Writer != null) throw new Exception("TypeError: WritableStream is already locked to a writer");
                Writer = new SharpTSWritableStreamDefaultWriter(this);
                return RuntimeValue.FromObject(Writer);
            }),
            "abort" => BuiltInMethod.CreateV2("abort", 1, (_, _, args) =>
            {
                var reason = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                return RuntimeValue.FromObject(new SharpTSPromise(AbortInternal(reason)));
            }),
            "close" => BuiltInMethod.CreateV2("close", 0, (_, _, _) =>
            {
                return RuntimeValue.FromObject(new SharpTSPromise(CloseAsyncInternal()));
            }),
            _ => null,
        };
    }

    public override string ToString() => "WritableStream {}";
}

/// <summary>Writable stream default controller. Exposes <c>signal</c> and <c>error()</c>.</summary>
public class SharpTSWritableStreamDefaultController : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;
    private readonly SharpTSWritableStream _stream;
    private readonly SharpTSAbortSignal _signal = new(CancellationToken.None);

    internal SharpTSWritableStreamDefaultController(SharpTSWritableStream stream)
    {
        _stream = stream;
    }

    public object? GetMember(string name)
    {
        return name switch
        {
            "signal" => _signal,
            "error" => BuiltInMethod.CreateV2("error", 1, (_, _, args) =>
            {
                _stream.ErrorInternal(args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);
                return RuntimeValue.Undefined;
            }),
            _ => null,
        };
    }

    public override string ToString() => "WritableStreamDefaultController {}";
}

/// <summary>Writer handle for a <see cref="SharpTSWritableStream"/>.</summary>
public class SharpTSWritableStreamDefaultWriter : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    private readonly SharpTSWritableStream _stream;

    // ready resolves when desiredSize > 0.
    private TaskCompletionSource<object?> _readyTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SharpTSPromise? _readyPromise;

    // closed resolves when the stream closes cleanly, rejects on error.
    private readonly TaskCompletionSource<object?> _closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SharpTSPromise? _closedPromise;

    internal SharpTSWritableStreamDefaultWriter(SharpTSWritableStream stream)
    {
        _stream = stream;
        // Initially the ready promise resolves immediately if there's headroom.
        if (stream.DesiredSize > 0) _readyTcs.TrySetResult(SharpTSUndefined.Instance);
    }

    internal void NotifyDrained()
    {
        if (_stream.DesiredSize > 0 && !_readyTcs.Task.IsCompleted)
        {
            _readyTcs.TrySetResult(SharpTSUndefined.Instance);
        }
    }

    internal void NotifyClosed()
    {
        _closedTcs.TrySetResult(SharpTSUndefined.Instance);
    }

    internal void NotifyErrored(object? error)
    {
        _closedTcs.TrySetException(new SharpTSPromiseRejectedException(error));
        if (!_readyTcs.Task.IsCompleted)
        {
            _readyTcs.TrySetException(new SharpTSPromiseRejectedException(error));
        }
    }

    public object? GetMember(string name)
    {
        return name switch
        {
            "desiredSize" => (object)_stream.DesiredSize,
            "closed" => _closedPromise ??= new SharpTSPromise(_closedTcs.Task),
            "ready" => GetReadyPromise(),
            "write" => BuiltInMethod.CreateV2("write", 1, (_, _, args) =>
            {
                var chunk = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                var task = _stream.EnqueueWrite(chunk);
                // After enqueuing, reset ready if queue is now saturated.
                if (_stream.DesiredSize <= 0 && _readyTcs.Task.IsCompleted)
                {
                    _readyTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return RuntimeValue.FromObject(new SharpTSPromise(task));
            }),
            "close" => BuiltInMethod.CreateV2("close", 0, (_, _, _) =>
            {
                return RuntimeValue.FromObject(new SharpTSPromise(_stream.CloseAsyncInternal()));
            }),
            "abort" => BuiltInMethod.CreateV2("abort", 1, (_, _, args) =>
            {
                var reason = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                return RuntimeValue.FromObject(new SharpTSPromise(_stream.AbortInternal(reason)));
            }),
            "releaseLock" => BuiltInMethod.CreateV2("releaseLock", 0, (_, _, _) =>
            {
                if (_stream.Writer == this)
                {
                    _stream.Writer = null;
                }
                return RuntimeValue.Undefined;
            }),
            _ => null,
        };
    }

    private SharpTSPromise GetReadyPromise()
    {
        // _readyTcs is replaced when the queue saturates, so the cached
        // promise is only valid while it still wraps the current tcs.
        if (_readyPromise == null || _readyPromise.Task != _readyTcs.Task)
        {
            _readyPromise = new SharpTSPromise(_readyTcs.Task);
        }
        return _readyPromise;
    }

    public override string ToString() => "WritableStreamDefaultWriter {}";
}
