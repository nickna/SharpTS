using System.Collections.Concurrent;
using System.Reflection;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Worker-side adapter that lets an interpreter worker drive a MessagePort that a
/// COMPILED parent created and transferred via <c>workerData</c>/<c>transferList</c> (#406).
/// </summary>
/// <remarks>
/// A compiled parent's <c>MessageChannel</c> produces two emitted <c>$MessagePort</c>
/// objects (see <c>RuntimeEmitter.MessageChannel.cs</c>) that communicate in-process:
/// <c>postMessage</c> enqueues a structured clone to the partner's internal
/// <c>_pending</c> queue and, if the partner has started, schedules the partner's
/// <c>Drain</c> on the process-global <c>$EventLoop</c>. A worker always runs the
/// interpreter on its own thread, so it cannot call methods on the raw emitted type
/// and cannot safely be driven by the parent's <c>$EventLoop</c> (a different thread).
///
/// This bridge wraps the transferred compiled port and presents the interpreter
/// MessagePort surface (<c>postMessage</c>/<c>on</c>/<c>once</c>/<c>start</c>/
/// <c>close</c> plus the inherited EventEmitter members):
/// <list type="bullet">
/// <item><b>send</b> — <c>postMessage(v)</c> structured-clones <c>v</c> (copy
/// semantics) then reflectively invokes the compiled port's <c>PostMessage</c>,
/// reusing its enqueue-to-partner + schedule-drain-on-<c>$EventLoop</c> logic so the
/// parent's compiled listeners run on the parent loop thread.</item>
/// <item><b>receive</b> — the compiled partner's posts are always enqueued to the
/// transferred port's <c>_pending</c> queue (the compiled <c>Drain</c> is only
/// scheduled when the port has started, which this transferred port never does on the
/// compiled side). The bridge instead installs an on-enqueue callback on the compiled
/// port (<c>_onEnqueue</c>): a parent post invokes it right after enqueuing, and it
/// marshals a drain onto the WORKER loop via the thread-safe <c>EnqueueCallback</c>,
/// which emits <c>'message'</c> to the worker's listeners on the worker thread. This
/// is event-driven — an idle bridged port no longer wakes the worker loop on a timer
/// (#465). A keep-alive <c>Ref</c> on the worker loop holds it open while the port is
/// open, matching Node's "a listening port is ref'd" semantics (#406, same liveness
/// class as #329).</item>
/// </list>
///
/// All access to the emitted type is via reflection cached at adoption time, since
/// this class lives in SharpTS.dll while the compiled port lives in the output
/// assembly. The field/method names mirrored here (<c>PostMessage</c>, <c>_pending</c>,
/// <c>_onEnqueue</c>) MUST stay in sync with <c>RuntimeEmitter.MessageChannel.cs</c>.
/// </remarks>
public sealed class CompiledMessagePortBridge : SharpTSEventEmitter
{
    private readonly object _compiledPort;               // transferred emitted $MessagePort
    private readonly MethodInfo _postMessageMethod;      // $MessagePort.PostMessage(object)
    private readonly ConcurrentQueue<object> _incoming;  // $MessagePort._pending
    private readonly FieldInfo _onEnqueueField;          // $MessagePort._onEnqueue (Action wake hook)

    // The worker interpreter that owns delivery. Captured the first time the worker
    // attaches a 'message' listener / starts the port; null until then.
    private Interp? _owner;
    private bool _started;
    private bool _closed;
    private bool _loopRefed;

    // RuntimeCategory deliberately not overridden — see SharpTSMessagePort. The base
    // returns Unknown for subclasses, routing member access through the per-type
    // registration in BuiltInRegistry, which reaches this class's GetMember.

    private CompiledMessagePortBridge(object compiledPort, MethodInfo postMessageMethod,
        ConcurrentQueue<object> incoming, FieldInfo onEnqueueField)
    {
        _compiledPort = compiledPort;
        _postMessageMethod = postMessageMethod;
        _incoming = incoming;
        _onEnqueueField = onEnqueueField;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a compiled-mode emitted <c>$MessagePort</c>.
    /// Detected by type name (the type cannot be referenced from SharpTS.dll), the same
    /// approach <see cref="StructuredClone"/> uses for <c>$ArrayBuffer</c>/<c>$SharedArrayBuffer</c>.
    /// </summary>
    public static bool IsEmittedMessagePort(object? value) => value?.GetType().Name == "$MessagePort";

    /// <summary>
    /// Adopts a transferred compiled <c>$MessagePort</c> into a worker-usable bridge.
    /// Caches the reflection handles for the port's <c>PostMessage</c> method and
    /// <c>_pending</c> queue. Throws <see cref="StructuredClone.DataCloneError"/> if the
    /// emitted shape doesn't match what <c>RuntimeEmitter.MessageChannel.cs</c> emits.
    /// </summary>
    public static CompiledMessagePortBridge Adopt(object compiledPort)
    {
        var type = compiledPort.GetType();

        var postMessage = type.GetMethod("PostMessage", [typeof(object)])
            ?? throw new StructuredClone.DataCloneError(
                "Cannot transfer $MessagePort: no PostMessage(object) method (emitted shape changed?)");

        var pendingField = type.GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new StructuredClone.DataCloneError(
                "Cannot transfer $MessagePort: no _pending field (emitted shape changed?)");

        if (pendingField.GetValue(compiledPort) is not ConcurrentQueue<object> incoming)
            throw new StructuredClone.DataCloneError(
                "Cannot transfer $MessagePort: _pending is not a ConcurrentQueue<object>");

        var onEnqueueField = type.GetField("_onEnqueue", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new StructuredClone.DataCloneError(
                "Cannot transfer $MessagePort: no _onEnqueue field (emitted shape changed?)");

        var markTransferred = type.GetMethod("MarkTransferredAcrossThreads", Type.EmptyTypes)
            ?? throw new StructuredClone.DataCloneError(
                "Cannot transfer $MessagePort: no MarkTransferredAcrossThreads method (emitted shape changed?)");
        // Flags this port AND its (compiled, still parent-side) partner cross-thread, so a
        // parent-side Start() on the partner Refs the parent loop while it awaits a reply
        // through this now-bridged port (#406) — a plain, never-transferred in-process port
        // must NOT Ref (#1254). This bridge manages its own worker-loop keep-alive separately
        // in Start() below, independent of the compiled port's _refed state.
        markTransferred.Invoke(compiledPort, null);

        return new CompiledMessagePortBridge(compiledPort, postMessage, incoming, onEnqueueField);
    }

    /// <summary>
    /// Posts a message to the compiled partner port. Structured-clones for copy
    /// semantics, then hands off to the compiled port's own delivery (enqueue +
    /// schedule the partner's drain on the parent <c>$EventLoop</c>).
    /// </summary>
    private void PostMessage(object? message)
    {
        if (_closed)
            return;

        // Copy semantics: clone on the worker side before handing to the compiled
        // port. The compiled PostMessage re-clones via its own (standalone) routine,
        // but that is a pass-through no-op for interpreter-shaped values, so the net
        // effect is exactly one structured copy and the worker never shares a mutable
        // graph with the parent.
        var clone = StructuredClone.Clone(message);
        _postMessageMethod.Invoke(_compiledPort, [clone]);
    }

    /// <summary>
    /// Begins delivery: installs an on-enqueue wake callback on the compiled port so a
    /// parent post drains the incoming queue onto the worker loop event-driven, and
    /// drains anything already queued. Idempotent. No-op until an owner interpreter has
    /// been captured (via the first <c>on('message')</c>/<c>start()</c>).
    /// </summary>
    private void Start()
    {
        if (_started || _closed || _owner == null)
            return;

        _started = true;

        // Keep the worker loop alive while the port is open. RunEventLoop's exit check
        // counts active handles and queued callbacks but NOT a port that is merely
        // waiting for a message, so without this Ref the worker could quiesce and exit
        // before the parent ever posts (Node: a listening port is ref'd; #406, same
        // liveness class as #329).
        if (!_loopRefed)
        {
            _loopRefed = true;
            _owner.Ref();
        }

        // Event-driven receive (#465): a parent post enqueues to _incoming on the
        // parent loop thread and then invokes this callback, which marshals a drain
        // onto the worker loop via the thread-safe EnqueueCallback (it wakes the
        // worker's blocking wait). This replaces a 10ms poll, so an idle bridged port
        // no longer wakes the worker loop 100×/second. The MemoryBarrier pairs with the
        // volatile read the emitted PostMessage does, so the parent reliably observes
        // the installed callback once set.
        _onEnqueueField.SetValue(_compiledPort, (Action)OnPartnerEnqueued);
        Thread.MemoryBarrier();

        // Drain anything the parent posted before the callback was installed.
        _owner.EnqueueCallback(Pump);
    }

    /// <summary>
    /// On-enqueue hook invoked by the compiled partner's <c>PostMessage</c> (on the
    /// parent loop thread) right after it enqueues to <c>_incoming</c>. Marshals a
    /// drain onto the worker loop; <see cref="Interp.EnqueueCallback"/> is thread-safe
    /// and wakes the worker's event loop.
    /// </summary>
    private void OnPartnerEnqueued() => _owner?.EnqueueCallback(Pump);

    /// <summary>
    /// Synchronously dequeues one message from the compiled partner's incoming queue,
    /// for <c>worker_threads.receiveMessageOnPort(port)</c> on a transferred compiled
    /// port (#465). Returns <c>{ message }</c> (the payload is already an independent
    /// structured clone) or <c>null</c> when the queue is empty or the port is closed,
    /// matching <see cref="SharpTSMessagePort.ReceiveMessageSync"/>.
    /// </summary>
    internal object? ReceiveMessageSync()
    {
        if (_closed || !_incoming.TryDequeue(out var message))
            return null;

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["message"] = message
        });
    }

    /// <summary>
    /// Drains the compiled port's incoming queue, emitting each message to the
    /// worker's 'message' listeners. Runs on the worker loop thread.
    /// </summary>
    private void Pump()
    {
        if (_closed || _owner == null)
            return;

        // The compiled sender already structured-cloned each payload before enqueuing,
        // so values here are independent copies — emit them directly (Node semantics:
        // the listener receives the value, not a {data} wrapper).
        while (_incoming.TryDequeue(out var message))
        {
            EmitEvent(_owner, "message", [message]);
        }
    }

    /// <summary>
    /// Stops worker-side delivery: clears the wake callback and releases the keep-alive
    /// Ref so the worker loop can quiesce and the worker can exit. Emits 'close' to the
    /// worker's listeners.
    /// </summary>
    private void Close()
    {
        if (_closed)
            return;

        _closed = true;

        // Stop the parent from waking this (now closed) bridge. A post that already
        // loaded the old callback is still harmless — Pump short-circuits on _closed.
        _onEnqueueField.SetValue(_compiledPort, null);
        Thread.MemoryBarrier();

        // Release the keep-alive Ref so the worker loop can quiesce and the worker
        // thread can exit.
        if (_loopRefed && _owner != null)
        {
            _loopRefed = false;
            _owner.Unref();
        }

        if (_owner != null)
            EmitEvent(_owner, "close", []);
    }

    /// <summary>
    /// Interpreter member dispatch. Adds the MessagePort surface on top of the
    /// inherited EventEmitter members; attaching a 'message' listener implicitly
    /// starts the port (Node worker_threads semantics, mirroring SharpTSMessagePort).
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "postMessage" => BuiltInMethod.CreateV2("postMessage", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("postMessage requires at least one argument");
                // A transferList on a re-post from the worker is not supported across
                // this bridge (the worker re-transferring a compiled port); the second
                // argument is ignored rather than rejected.
                PostMessage(args[0].ToObject());
                return RuntimeValue.Null;
            }),

            "start" => BuiltInMethod.CreateV2("start", 0, (interp, _, _) =>
            {
                _owner ??= interp;
                Start();
                return RuntimeValue.Null;
            }),

            "close" => BuiltInMethod.CreateV2("close", 0, (interp, _, _) =>
            {
                _owner ??= interp;
                Close();
                return RuntimeValue.Null;
            }),

            "on" or "addListener" or "once" => BuiltInMethod.CreateV2(name, 2, (interp, _, args) =>
            {
                var eventName = args[0].ToObject()?.ToString()
                    ?? throw new Exception("Event name must be a string");
                var listener = args[1].ToObject()
                    ?? throw new Exception("Listener must be a function");
                AddListenerDirect(eventName, listener, once: name == "once");
                if (eventName == "message")
                {
                    _owner ??= interp;
                    Start();
                }
                return RuntimeValue.FromObject(this);
            }),

            // Inherit EventEmitter methods (off/emit/listeners/...).
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => _closed ? "MessagePort { closed }" : "MessagePort {}";
}
