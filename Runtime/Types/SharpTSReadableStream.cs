using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the WHATWG Streams <c>ReadableStream</c>.
/// </summary>
/// <remarks>
/// This is a simplified but spec-faithful implementation of the Streams
/// Standard algorithm for default readable streams. It omits byte streams
/// (<c>type: 'bytes'</c>), BYOB readers, and transferability; see STATUS.md.
///
/// The interpreter is single-threaded (event loop), so we can freely mutate
/// internal state inside callbacks without locking. Promise-returning user
/// callbacks are awaited via <see cref="SharpTSPromise.GetValueAsync"/>.
/// </remarks>
public class SharpTSReadableStream : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    // --- internal state ---------------------------------------------------
    internal enum StreamState { Readable, Closed, Errored }

    internal StreamState State;
    internal object? StoredError;

    internal readonly Queue<QueuedChunk> Queue = new();
    internal double QueueTotalSize;
    internal double HighWaterMark;
    internal object? SizeAlgorithm;

    internal object? PullAlgorithm;
    internal object? CancelAlgorithm;

    internal bool Started;
    internal bool Pulling;
    internal bool PullAgain;
    internal bool CloseRequested;
    internal bool Disturbed;

    internal SharpTSReadableStreamDefaultReader? Reader;
    internal readonly SharpTSReadableStreamDefaultController Controller;

    // Resolved when the stream is closed; rejected if errored.
    internal readonly TaskCompletionSource<object?> ClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Interp? OwnerInterpreter;

    internal readonly struct QueuedChunk
    {
        public readonly object? Value;
        public readonly double Size;
        public QueuedChunk(object? value, double size) { Value = value; Size = size; }
    }

    /// <summary>
    /// Creates a new ReadableStream. <paramref name="underlyingSource"/> may
    /// be a <see cref="SharpTSObject"/> with <c>start</c>/<c>pull</c>/<c>cancel</c>
    /// methods, or null. <paramref name="strategy"/> may be a
    /// <see cref="SharpTSQueuingStrategy"/> or a <see cref="SharpTSObject"/>
    /// with <c>highWaterMark</c>/<c>size</c>.
    /// </summary>
    public SharpTSReadableStream(Interp? interp, object? underlyingSource, object? strategy)
    {
        OwnerInterpreter = interp;
        Controller = new SharpTSReadableStreamDefaultController(this);
        State = StreamState.Readable;

        (HighWaterMark, SizeAlgorithm) = WebStreamsHelpers.ExtractStrategy(strategy, defaultHwm: 1.0);

        if (underlyingSource != null)
        {
            PullAlgorithm = StreamFields.GetCallback(underlyingSource, "pull");
            CancelAlgorithm = StreamFields.GetCallback(underlyingSource, "cancel");
            var startFn = StreamFields.GetCallback(underlyingSource, "start");

            if (startFn != null)
            {
                InvokeStart(startFn);
            }
            else
            {
                Started = true;
                CallPullIfNeeded();
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
                // Defer Started until the start promise resolves.
                _ = CompleteStartAsync(p);
            }
            else
            {
                Started = true;
                CallPullIfNeeded();
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
            CallPullIfNeeded();
        }
        catch (Exception ex)
        {
            ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    /// <summary>Whether a reader currently holds this stream's lock.</summary>
    public bool Locked => Reader != null;

    /// <summary>Desired queue headroom, per WHATWG spec.</summary>
    public double? DesiredSize => State switch
    {
        StreamState.Errored => null,
        StreamState.Closed => 0.0,
        _ => HighWaterMark - QueueTotalSize,
    };

    internal void CallPullIfNeeded()
    {
        if (!ShouldCallPull())
        {
            return;
        }
        if (Pulling)
        {
            PullAgain = true;
            return;
        }
        Pulling = true;

        if (PullAlgorithm is null)
        {
            Pulling = false;
            return;
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(OwnerInterpreter, PullAlgorithm, Controller);
            if (result is SharpTSPromise p)
            {
                _ = AwaitPullAsync(p);
            }
            else
            {
                Pulling = false;
                if (PullAgain)
                {
                    PullAgain = false;
                    CallPullIfNeeded();
                }
            }
        }
        catch (Exception ex)
        {
            Pulling = false;
            ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    private async Task AwaitPullAsync(SharpTSPromise p)
    {
        try
        {
            await p.GetValueAsync();
            Pulling = false;
            if (PullAgain)
            {
                PullAgain = false;
                CallPullIfNeeded();
            }
        }
        catch (Exception ex)
        {
            Pulling = false;
            ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    private bool ShouldCallPull()
    {
        if (!Started || CloseRequested || State != StreamState.Readable) return false;
        if (Reader == null && PullAlgorithm == null) return false; // no consumer, no source
        var desired = DesiredSize ?? 0.0;
        return desired > 0;
    }

    /// <summary>Push a chunk onto the queue (called by controller.enqueue).</summary>
    internal void EnqueueInternal(object? chunk)
    {
        if (State != StreamState.Readable || CloseRequested)
        {
            throw new InvalidOperationException("Cannot enqueue into a closed or errored ReadableStream");
        }

        // Deliver to a pending reader if any.
        if (Reader != null && Reader.PendingReads.Count > 0)
        {
            var tcs = Reader.PendingReads.Dequeue();
            tcs.TrySetResult(MakeReadResult(chunk, done: false));
            return;
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
            throw;
        }

        Queue.Enqueue(new QueuedChunk(chunk, size));
        QueueTotalSize += size;
        CallPullIfNeeded();
    }

    internal void CloseInternal()
    {
        if (State != StreamState.Readable || CloseRequested) return;
        CloseRequested = true;

        if (Queue.Count == 0)
        {
            FinishClose();
        }
    }

    private void FinishClose()
    {
        State = StreamState.Closed;
        ClosedTcs.TrySetResult(SharpTSUndefined.Instance);

        if (Reader != null)
        {
            // Resolve all pending reads with done:true.
            while (Reader.PendingReads.Count > 0)
            {
                var tcs = Reader.PendingReads.Dequeue();
                tcs.TrySetResult(MakeReadResult(SharpTSUndefined.Instance, done: true));
            }
            Reader.ClosedTcs.TrySetResult(SharpTSUndefined.Instance);
        }
    }

    internal void ErrorInternal(object? error)
    {
        if (State != StreamState.Readable) return;
        State = StreamState.Errored;
        StoredError = error;
        Queue.Clear();
        QueueTotalSize = 0;

        ClosedTcs.TrySetException(new SharpTSPromiseRejectedException(error));

        if (Reader != null)
        {
            while (Reader.PendingReads.Count > 0)
            {
                var tcs = Reader.PendingReads.Dequeue();
                tcs.TrySetException(new SharpTSPromiseRejectedException(error));
            }
            Reader.ClosedTcs.TrySetException(new SharpTSPromiseRejectedException(error));
        }
    }

    /// <summary>
    /// Satisfies a reader.read() request. Returns a Task that resolves to a
    /// <c>{ value, done }</c> <see cref="SharpTSObject"/>.
    /// </summary>
    internal Task<object?> ReadInternal()
    {
        Disturbed = true;

        if (Queue.Count > 0)
        {
            var chunk = Queue.Dequeue();
            QueueTotalSize -= chunk.Size;
            if (QueueTotalSize < 0) QueueTotalSize = 0;

            // If queue now empty and close was requested, finish closing.
            if (CloseRequested && Queue.Count == 0)
            {
                FinishClose();
            }
            else
            {
                CallPullIfNeeded();
            }

            return Task.FromResult<object?>(MakeReadResult(chunk.Value, done: false));
        }

        if (State == StreamState.Closed)
        {
            return Task.FromResult<object?>(MakeReadResult(SharpTSUndefined.Instance, done: true));
        }

        if (State == StreamState.Errored)
        {
            var tcs = new TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(StoredError));
            return tcs.Task;
        }

        // Queue empty, stream still readable — park the read.
        // NOTE: default TaskCreationOptions (NOT RunContinuationsAsynchronously).
        // With the InterpreterSynchronizationContext set up at the top of
        // InterpretModules, the await captured inside an async script
        // function correctly posts its continuation back through the
        // interpreter's callback queue. Using default options means
        // TrySetResult below can hand off via the continuation mechanism
        // rather than inlining on the timer-callback thread.
        var pending = new TaskCompletionSource<object?>();
        Reader!.PendingReads.Enqueue(pending);
        CallPullIfNeeded();
        return pending.Task;
    }

    internal SharpTSPromise CancelInternal(object? reason)
    {
        Disturbed = true;
        if (State == StreamState.Closed) return SharpTSPromise.Resolve(SharpTSUndefined.Instance);
        if (State == StreamState.Errored) return SharpTSPromise.Reject(StoredError);

        Queue.Clear();
        QueueTotalSize = 0;
        CloseInternal();
        if (State == StreamState.Readable)
        {
            // Force finish if there were no pending items.
            FinishClose();
        }

        if (CancelAlgorithm is null)
        {
            return SharpTSPromise.Resolve(SharpTSUndefined.Instance);
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(OwnerInterpreter, CancelAlgorithm, reason);
            if (result is SharpTSPromise p) return p;
            return SharpTSPromise.Resolve(SharpTSUndefined.Instance);
        }
        catch (Exception ex)
        {
            return SharpTSPromise.Reject(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
        }
    }

    // ------- static helpers -----------------------------------------------

    /// <summary>
    /// Builds an iterator-protocol result object (<c>{ value, done }</c>) as a
    /// plain <see cref="Dictionary{TKey, TValue}"/>.
    /// </summary>
    /// <remarks>
    /// Compiled-mode <c>$Runtime.GetKeys</c>/<c>GetValues</c>/<c>GetEntries</c>,
    /// <c>JSON.stringify</c>, object spread, and for-in all already have a
    /// <c>Dictionary&lt;string, object?&gt;</c> fast path. Interpreter-side parity
    /// is handled by the Dictionary branches added to <c>ObjectBuiltIns</c>,
    /// <c>Interpreter.Statements</c> for-in, spread <c>ApplySpreadToFields</c>,
    /// and <c>JSONBuiltIns.StringifyValue</c> (see Task 1c). Returning a dict
    /// instead of <see cref="SharpTSObject"/> avoids having to edit the
    /// compiled-mode IL emission paths for the corresponding operations.
    /// </remarks>
    internal static Dictionary<string, object?> MakeReadResult(object? value, bool done)
    {
        return new Dictionary<string, object?>
        {
            ["value"] = value,
            ["done"] = (object)done,
        };
    }

    // ------- public API (member dispatch) ---------------------------------

    public object? GetMember(string name)
    {
        return name switch
        {
            "locked" => (object)Locked,
            "getReader" => BuiltInMethod.CreateV2("getReader", 0, (_, _, _) =>
            {
                if (Reader != null) throw new Exception("TypeError: ReadableStream is already locked to a reader");
                Reader = new SharpTSReadableStreamDefaultReader(this);
                if (State == StreamState.Closed) Reader.ClosedTcs.TrySetResult(SharpTSUndefined.Instance);
                else if (State == StreamState.Errored) Reader.ClosedTcs.TrySetException(new SharpTSPromiseRejectedException(StoredError));
                return RuntimeValue.FromObject(Reader);
            }),
            "cancel" => BuiltInMethod.CreateV2("cancel", 1, (_, _, args) =>
            {
                var reason = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                return RuntimeValue.FromObject(CancelInternal(reason));
            }),
            "pipeTo" => BuiltInMethod.CreateV2("pipeTo", 1, 2, (interp, _, args) =>
            {
                var dest = args.Length > 0 ? args[0].ToObject() : null;
                var opts = args.Length > 1 ? args[1].ToObject() : null;
                // PipeToAny accepts both interpreter SharpTSWritableStream and
                // pure-IL emitted $WritableStream destinations.
                return RuntimeValue.FromObject(WebStreamsHelpers.PipeToAny(interp, this, dest, opts));
            }),
            "pipeThrough" => BuiltInMethod.CreateV2("pipeThrough", 1, 2, (interp, recv, args) =>
            {
                var transform = args.Length > 0 ? args[0].ToObject() : null;
                var opts = args.Length > 1 ? args[1].ToObject() : null;
                if (transform is not SharpTSTransformStream ts)
                    throw new Exception("TypeError: pipeThrough argument must be a TransformStream");
                // Start the pipe but return the readable side; ignore the returned promise.
                _ = WebStreamsHelpers.PipeTo(interp, this, ts.Writable, opts);
                return RuntimeValue.FromObject(ts.Readable);
            }),
            "tee" => BuiltInMethod.CreateV2("tee", 0, (interp, _, _) =>
            {
                return RuntimeValue.FromBoxed(WebStreamsHelpers.Tee(interp, this));
            }),
            _ => null,
        };
    }

    public override string ToString() => "ReadableStream {}";
}

/// <summary>
/// Default controller for <see cref="SharpTSReadableStream"/>, exposed to
/// user <c>start</c>/<c>pull</c> callbacks as the first argument.
/// </summary>
public class SharpTSReadableStreamDefaultController : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    private readonly SharpTSReadableStream _stream;

    internal SharpTSReadableStreamDefaultController(SharpTSReadableStream stream)
    {
        _stream = stream;
    }

    public object? GetMember(string name)
    {
        return name switch
        {
            "desiredSize" => _stream.DesiredSize is { } d ? (object)d : null,
            "enqueue" => BuiltInMethod.CreateV2("enqueue", 1, (_, _, args) =>
            {
                _stream.EnqueueInternal(args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);
                return RuntimeValue.Undefined;
            }),
            "close" => BuiltInMethod.CreateV2("close", 0, (_, _, _) =>
            {
                _stream.CloseInternal();
                return RuntimeValue.Undefined;
            }),
            "error" => BuiltInMethod.CreateV2("error", 1, (_, _, args) =>
            {
                _stream.ErrorInternal(args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);
                return RuntimeValue.Undefined;
            }),
            _ => null,
        };
    }

    public override string ToString() => "ReadableStreamDefaultController {}";
}

/// <summary>
/// Default reader for <see cref="SharpTSReadableStream"/>.
/// </summary>
public class SharpTSReadableStreamDefaultReader : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    private readonly SharpTSReadableStream _stream;

    internal readonly Queue<TaskCompletionSource<object?>> PendingReads = new();

    internal readonly TaskCompletionSource<object?> ClosedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SharpTSPromise? _closedPromise;

    internal SharpTSReadableStreamDefaultReader(SharpTSReadableStream stream)
    {
        _stream = stream;
    }

    public SharpTSReadableStream Stream => _stream;

    public object? GetMember(string name)
    {
        return name switch
        {
            "closed" => _closedPromise ??= new SharpTSPromise(ClosedTcs.Task),
            "read" => BuiltInMethod.CreateV2("read", 0, (_, _, _) =>
            {
                if (_stream.Reader != this)
                {
                    return RuntimeValue.FromObject(SharpTSPromise.Reject("TypeError: Reader is no longer attached to its stream"));
                }
                return RuntimeValue.FromObject(new SharpTSPromise(_stream.ReadInternal()));
            }),
            "releaseLock" => BuiltInMethod.CreateV2("releaseLock", 0, (_, _, _) =>
            {
                if (_stream.Reader == this)
                {
                    if (PendingReads.Count > 0)
                        throw new Exception("TypeError: Cannot release a reader with pending read requests");
                    _stream.Reader = null;
                }
                return RuntimeValue.Undefined;
            }),
            "cancel" => BuiltInMethod.CreateV2("cancel", 1, (_, _, args) =>
            {
                if (_stream.Reader != this)
                {
                    return RuntimeValue.FromObject(SharpTSPromise.Reject("TypeError: Reader is no longer attached to its stream"));
                }
                var reason = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                return RuntimeValue.FromObject(_stream.CancelInternal(reason));
            }),
            _ => null,
        };
    }

    public override string ToString() => "ReadableStreamDefaultReader {}";
}
