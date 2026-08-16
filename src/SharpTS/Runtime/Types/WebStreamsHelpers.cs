using System.Collections.Concurrent;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Field-access adapter over any "object with fields": interpreter
/// <see cref="SharpTSObject"/>, compiled-mode <c>$Object</c> (via reflection
/// on the public <c>Fields</c> property), or a plain
/// <see cref="Dictionary{TKey, TValue}"/>.
/// </summary>
internal static class StreamFields
{
    // Per-type cache of (type → reflected Fields getter) so we don't pay reflection
    // on the hot enqueue path for compiled $Object instances.
    private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> _fieldsPropCache = new();

    public static bool TryGet(object? target, string name, out object? value)
    {
        value = null;
        if (target is null) return false;
        if (target is SharpTSObject obj)
        {
            return obj.Fields.TryGetValue(name, out value);
        }
        if (target is IDictionary<string, object?> dict)
        {
            return dict.TryGetValue(name, out value);
        }
        // Compiled-mode $Object: reflect the Fields property through the
        // managed-emitted-shape boundary. Other CLR objects are not part of
        // this adapter's documented contract.
        var targetType = target.GetType();
        if (!ManagedEmittedShapeReflection.IsShape(
                targetType, ManagedEmittedShape.HasFields))
        {
            return false;
        }
        var prop = _fieldsPropCache.GetOrAdd(targetType, t =>
            ManagedEmittedShapeReflection.GetPublicProperty(
                t, ManagedEmittedShape.HasFields, "Fields"));
        if (prop != null && prop.GetValue(target) is IDictionary<string, object?> reflected)
        {
            return reflected.TryGetValue(name, out value);
        }
        return false;
    }

    /// <summary>
    /// Returns the named field value if it's a callable shape recognised by
    /// <see cref="RuntimeCallableDispatcher"/>; otherwise <c>null</c>. Stream
    /// runtime types store these directly as <c>object?</c> and dispatch
    /// through the shared dispatcher at invocation time.
    /// </summary>
    public static object? GetCallback(object? target, string name)
    {
        if (TryGet(target, name, out var v) && RuntimeCallableDispatcher.IsCallable(v))
        {
            return v;
        }
        return null;
    }
}

/// <summary>
/// Shared utilities for the Web Streams runtime types: strategy extraction,
/// <c>pipeTo</c>, <c>tee</c>, and supporting helpers.
/// </summary>
internal static class WebStreamsHelpers
{
    /// <summary>
    /// Normalizes a user-provided queuing strategy into
    /// <c>(highWaterMark, sizeAlgorithm)</c>. Accepts a
    /// <see cref="SharpTSQueuingStrategy"/> instance or a plain
    /// <see cref="SharpTSObject"/> with <c>highWaterMark</c>/<c>size</c> fields.
    /// </summary>
    public static (double hwm, object? sizeAlgo) ExtractStrategy(object? strategy, double defaultHwm)
    {
        if (strategy is null) return (defaultHwm, null);
        if (strategy is SharpTSQueuingStrategy qs)
        {
            // qs.SizeCallable is already an ISharpTSCallable; the dispatcher
            // accepts it directly.
            return (qs.HighWaterMark, qs.SizeCallable);
        }

        double hwm = defaultHwm;
        if (StreamFields.TryGet(strategy, "highWaterMark", out var h))
        {
            hwm = h switch
            {
                double d => d,
                int i => i,
                long l => l,
                _ => defaultHwm,
            };
        }
        return (hwm, StreamFields.GetCallback(strategy, "size"));
    }

    /// <summary>
    /// Polymorphic pipeTo entry point. Accepts either an interpreter
    /// <see cref="SharpTSWritableStream"/> or an emitted-per-DLL
    /// <c>$WritableStream</c> as the destination. The emitted-type case is
    /// dispatched via reflection on the public <c>Write</c>/<c>Close</c>/
    /// <c>Abort</c> methods which the pure-IL emitter exposes.
    /// </summary>
    /// <remarks>
    /// This bridges the transitional state where <see cref="SharpTSReadableStream"/>
    /// is still interpreter-side but <c>$WritableStream</c> has moved to pure-IL.
    /// Once <c>$ReadableStream</c> also moves to pure-IL, the runtime path
    /// becomes a same-types-only call again (see Task 3a).
    /// </remarks>
    public static SharpTSPromise PipeToAny(Interp interp, SharpTSReadableStream source, object? dest, object? opts)
    {
        if (dest is SharpTSWritableStream ws)
        {
            return PipeTo(interp, source, ws, opts);
        }
        if (dest != null && ManagedEmittedShapeReflection.IsShape(
                dest.GetType(), ManagedEmittedShape.WritableStream))
        {
            return PipeToEmittedWritable(interp, source, dest, opts);
        }
        return SharpTSPromise.Reject("TypeError: pipeTo destination must be a WritableStream");
    }

    /// <summary>
    /// Pipes from a runtime <see cref="SharpTSReadableStream"/> into an
    /// emitted-per-DLL <c>$WritableStream</c>. Calls Write/Close/Abort via
    /// reflection.
    /// </summary>
    private static SharpTSPromise PipeToEmittedWritable(Interp interp, SharpTSReadableStream source, object dest, object? opts)
    {
        var destType = dest.GetType();
        var writeMethod = ManagedEmittedShapeReflection.GetPublicMethod(
                destType, ManagedEmittedShape.WritableStream, "Write", [typeof(object)])
            ?? throw new Exception("$WritableStream.Write not found");
        var closeMethod = ManagedEmittedShapeReflection.GetPublicMethod(
                destType, ManagedEmittedShape.WritableStream, "Close", Type.EmptyTypes)
            ?? throw new Exception("$WritableStream.Close not found");
        var abortMethod = ManagedEmittedShapeReflection.GetPublicMethod(
                destType, ManagedEmittedShape.WritableStream, "Abort", [typeof(object)])
            ?? throw new Exception("$WritableStream.Abort not found");

        bool preventClose = false, preventAbort = false, preventCancel = false;
        SharpTSAbortSignal? signal = null;
        if (opts != null)
        {
            if (StreamFields.TryGet(opts, "preventClose", out var pc) && pc is bool b1) preventClose = b1;
            if (StreamFields.TryGet(opts, "preventAbort", out var pa) && pa is bool b2) preventAbort = b2;
            if (StreamFields.TryGet(opts, "preventCancel", out var pcan) && pcan is bool b3) preventCancel = b3;
            if (StreamFields.TryGet(opts, "signal", out var sig)) signal = sig as SharpTSAbortSignal;
        }

        if (source.Locked) return SharpTSPromise.Reject("TypeError: source ReadableStream is already locked");

        var reader = new SharpTSReadableStreamDefaultReader(source);
        source.Reader = reader;

        async Task<object?> CallWrite(object chunk)
        {
            var task = (Task<object?>?)writeMethod.Invoke(dest, [chunk]);
            if (task != null) await task;
            return SharpTSUndefined.Instance;
        }
        async Task CallClose()
        {
            var task = (Task<object?>?)closeMethod.Invoke(dest, null);
            if (task != null) await task;
        }
        async Task CallAbort(object? reason)
        {
            var task = (Task<object?>?)abortMethod.Invoke(dest, [reason]);
            if (task != null) await task;
        }

        async Task<object?> PumpAsync()
        {
            // No ConfigureAwait(false) in the pump — resumes must ride the
            // interpreter's SynchronizationContext so they stay visible to the
            // event loop's exit check (see PipeTo's pump, #1212).
            //
            // Scope a Ref to the abort/cancel/reject teardown so the source
            // cancel() callback cannot be dropped by event-loop quiescence —
            // same liveness fix as PipeTo (#325/#320 doctrine). The steady-state
            // pipe stays un-Ref'd so an infinite source can never-settle.
            bool teardownRefed = false;
            void RefTeardown()
            {
                if (!teardownRefed)
                {
                    interp?.Ref();
                    teardownRefed = true;
                }
            }

            try
            {
                while (true)
                {
                    if (signal != null && signal.Aborted)
                    {
                        RefTeardown();
                        if (!preventAbort) await CallAbort(signal.Reason);
                        if (!preventCancel) await source.CancelInternal(signal.Reason).Task;
                        throw new SharpTSPromiseRejectedException(signal.Reason);
                    }

                    var readTask = source.ReadInternal();
                    bool parked = !readTask.IsCompleted;
                    var readResult = await readTask;
                    if (parked) EventLoopTestHooks.ParkedResumeDelay();
                    if (readResult is not IDictionary<string, object?> resultObj) throw new InvalidOperationException("read() returned non-object");
                    var done = resultObj.TryGetValue("done", out var d) && d is bool db && db;
                    if (done)
                    {
                        if (!preventClose) await CallClose();
                        if (source.Reader == reader) source.Reader = null;
                        return SharpTSUndefined.Instance;
                    }

                    var chunk = resultObj.TryGetValue("value", out var v) ? v : SharpTSUndefined.Instance;
                    await CallWrite(chunk!);
                }
            }
            catch (Exception ex)
            {
                RefTeardown();
                var reason = ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex;
                if (!preventAbort)
                {
                    try { await CallAbort(reason); } catch { }
                }
                if (!preventCancel && source.State == SharpTSReadableStream.StreamState.Readable)
                {
                    try { await source.CancelInternal(reason).Task; } catch { }
                }
                if (source.Reader == reader) source.Reader = null;
                throw;
            }
            finally
            {
                if (teardownRefed) interp?.Unref();
            }
        }

        return new SharpTSPromise(PumpAsync());
    }

    /// <summary>
    /// Pipes chunks from <paramref name="source"/> into <paramref name="dest"/>
    /// until source closes or errors. Returns a <see cref="SharpTSPromise"/>
    /// that resolves when piping completes. <paramref name="opts"/> may contain
    /// <c>preventClose</c>, <c>preventAbort</c>, <c>preventCancel</c>, and
    /// <c>signal</c> fields.
    /// </summary>
    public static SharpTSPromise PipeTo(Interp interp, SharpTSReadableStream source, SharpTSWritableStream dest, object? opts)
    {
        bool preventClose = false, preventAbort = false, preventCancel = false;
        SharpTSAbortSignal? signal = null;
        if (opts != null)
        {
            if (StreamFields.TryGet(opts, "preventClose", out var pc) && pc is bool b1) preventClose = b1;
            if (StreamFields.TryGet(opts, "preventAbort", out var pa) && pa is bool b2) preventAbort = b2;
            if (StreamFields.TryGet(opts, "preventCancel", out var pcan) && pcan is bool b3) preventCancel = b3;
            if (StreamFields.TryGet(opts, "signal", out var sig)) signal = sig as SharpTSAbortSignal;
        }

        if (source.Locked) return SharpTSPromise.Reject("TypeError: source ReadableStream is already locked");
        if (dest.Locked) return SharpTSPromise.Reject("TypeError: destination WritableStream is already locked");

        var reader = new SharpTSReadableStreamDefaultReader(source);
        source.Reader = reader;
        var writer = new SharpTSWritableStreamDefaultWriter(dest);
        dest.Writer = writer;

        async Task<object?> PumpAsync()
        {
            // No ConfigureAwait(false) anywhere in the pump: every resume of a
            // parked await (read, write, teardown) must ride the interpreter's
            // SynchronizationContext so the settling TrySetResult posts it into
            // the event loop's callback queue synchronously — visible to the
            // loop's exit check, and running guest callbacks (write/abort/
            // cancel) on the loop thread rather than a pool thread. A pool-hop
            // resume was invisible to the exit check, so a one-shot timer
            // producer whose callback settled the parked read let the program
            // exit before "wrote x" was ever printed (#1212).
            //
            // The pipe promise itself is deliberately NOT Ref'd — an infinite
            // source must be allowed to never settle, so a blanket Ref would
            // hang the process (#319/#320 doctrine). But the abort/cancel/reject
            // *teardown* sequence is real, bounded work that must fully drain:
            // once an abort has been observed, the un-Ref'd continuation chain
            // (abort dest → cancel source → reject) can take >250ms under load,
            // and the quiescence give-up could let the process exit after
            // "dest-aborted"/"pipe-rejected" but before the source cancel()
            // callback flushes (the #325 flake). Scope a Ref to just the
            // teardown so the loop stays alive until it completes, then Unref.
            bool teardownRefed = false;
            void RefTeardown()
            {
                if (!teardownRefed)
                {
                    interp?.Ref();
                    teardownRefed = true;
                }
            }

            try
            {
                while (true)
                {
                    // When a signal is present, yield per iteration so timer
                    // callbacks (e.g. setTimeout(() => ac.abort(), 0)) can
                    // fire between reads — otherwise a synchronous-pull source
                    // starves timers and mid-pipe aborts hang. Scoped to the
                    // signal path so non-aborting pipes keep their existing
                    // async ordering (pipeThrough/transform, etc.).
                    if (signal != null) await Task.Yield();

                    if (signal != null && signal.Aborted)
                    {
                        RefTeardown();
                        if (!preventAbort) await dest.AbortInternal(signal.Reason);
                        if (!preventCancel) await source.CancelInternal(signal.Reason).Task;
                        throw new SharpTSPromiseRejectedException(signal.Reason);
                    }

                    var readTask = source.ReadInternal();
                    bool parked = !readTask.IsCompleted;
                    var readResult = await readTask;
                    if (parked) EventLoopTestHooks.ParkedResumeDelay();
                    if (readResult is not IDictionary<string, object?> resultObj) throw new InvalidOperationException("read() returned non-object");
                    var done = resultObj.TryGetValue("done", out var d) && d is bool db && db;
                    if (done)
                    {
                        if (!preventClose && dest.State == SharpTSWritableStream.WritableState.Writable)
                        {
                            await dest.CloseAsyncInternal();
                        }
                        if (source.Reader == reader) source.Reader = null;
                        if (dest.Writer == writer) dest.Writer = null;
                        return SharpTSUndefined.Instance;
                    }

                    var chunk = resultObj.TryGetValue("value", out var v) ? v : SharpTSUndefined.Instance;
                    await dest.EnqueueWrite(chunk);
                }
            }
            catch (Exception ex)
            {
                // Keep the loop alive across this teardown too (read/write error
                // path, or the rethrow from the mid-pipe abort above, which has
                // already Ref'd). Ref before the first teardown await.
                RefTeardown();
                var reason = ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex;
                if (!preventAbort && dest.State == SharpTSWritableStream.WritableState.Writable)
                {
                    try { await dest.AbortInternal(reason); } catch { }
                }
                if (!preventCancel && source.State == SharpTSReadableStream.StreamState.Readable)
                {
                    try { await source.CancelInternal(reason).Task; } catch { }
                }
                if (source.Reader == reader) source.Reader = null;
                if (dest.Writer == writer) dest.Writer = null;
                throw;
            }
            finally
            {
                if (teardownRefed) interp?.Unref();
            }
        }

        return new SharpTSPromise(PumpAsync());
    }

    /// <summary>
    /// Forks a source stream into two independent streams that each see every chunk.
    /// </summary>
    /// <remarks>
    /// V1 implementation: eager drain. Reads all chunks from <paramref name="source"/>
    /// synchronously (or as synchronously as the source permits) and enqueues
    /// each into both branches before returning. Matches the pure-IL emitted
    /// <c>$ReadableStream.Tee</c> behaviour.
    ///
    /// This avoids a subtle race in the previous pull-on-demand design where
    /// <c>branch1</c>'s constructor triggered a pull whose callback closed over
    /// <c>branch2</c> — a local still <c>null</c> during construction, causing
    /// the first chunk to be lost. Spec-correct lazy pull-and-branch with
    /// proper cancellation handling is a follow-up.
    /// </remarks>
    public static SharpTSArray Tee(Interp interp, SharpTSReadableStream source)
    {
        if (source.Locked) throw new Exception("TypeError: ReadableStream is already locked");

        var reader = new SharpTSReadableStreamDefaultReader(source);
        source.Reader = reader;

        var branch1 = new SharpTSReadableStream(interp, underlyingSource: null, strategy: null);
        var branch2 = new SharpTSReadableStream(interp, underlyingSource: null, strategy: null);

        PumpNext();

        return new SharpTSArray(new List<object?> { branch1, branch2 });

        // Feeds both branches from the source. Synchronous reads drain inline
        // (the common already-queued case); an in-flight async pull() continues
        // on the event loop instead of blocking it — the previous sync
        // GetResult() here hung the loop for any source with an async pull.
        void PumpNext()
        {
            while (true)
            {
                Task<object?> readTask;
                try { readTask = source.ReadInternal(); }
                catch (Exception ex) { Finish(ex); return; }

                if (!readTask.IsCompleted)
                {
                    interp.Ref();
                    readTask.ContinueWith(t => interp.EnqueueCallback(() =>
                    {
                        interp.Unref();
                        if (t.IsFaulted) { Finish(t.Exception?.InnerException ?? t.Exception); return; }
                        if (HandleResult(t.Result)) PumpNext();
                    }), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    return;
                }

                if (readTask.IsFaulted) { Finish(readTask.Exception?.InnerException ?? readTask.Exception); return; }
                if (!HandleResult(readTask.Result)) return;
            }
        }

        // Returns true to keep pumping.
        bool HandleResult(object? result)
        {
            if (result is not IDictionary<string, object?> obj) { Finish(error: null); return false; }
            var done = obj.TryGetValue("done", out var d) && d is bool db && db;
            if (done) { Finish(error: null); return false; }
            var chunk = obj.TryGetValue("value", out var v) ? v : SharpTSUndefined.Instance;
            EnqueueToBranch(branch1, chunk);
            EnqueueToBranch(branch2, chunk);
            return true;
        }

        void Finish(object? error)
        {
            if (error != null)
            {
                // A failed source read errors both branches (WHATWG tee) —
                // silently ending them showed the consumer a short, *successful*
                // stream.
                branch1.ErrorInternal(error);
                branch2.ErrorInternal(error);
            }
            else
            {
                branch1.CloseInternal();
                branch2.CloseInternal();
            }
            if (source.Reader == reader) source.Reader = null;
        }

        static void EnqueueToBranch(SharpTSReadableStream branch, object? chunk)
        {
            // A closed/canceled/errored branch skips the chunk (spec: the other
            // branch keeps receiving); anything else must not be swallowed.
            try { branch.EnqueueInternal(chunk); }
            catch (InvalidOperationException) { }
        }
    }
}
