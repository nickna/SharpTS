using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'stream' module.
/// </summary>
/// <remarks>
/// Provides stream classes for data processing:
/// - Readable: Read data from a source
/// - Writable: Write data to a destination
/// - Duplex: Read and write independently
/// - Transform: Transform data as it passes through
/// - PassThrough: Pass data through unchanged
/// - finished(): Callback when stream is no longer readable/writable
/// - pipeline(): Pipe streams together with error handling
/// </remarks>
public static class StreamModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the stream module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["Readable"] = SharpTSReadableConstructor.Instance,
            ["Writable"] = SharpTSWritableConstructor.Instance,
            ["Duplex"] = SharpTSDuplexConstructor.Instance,
            ["Transform"] = SharpTSTransformConstructor.Instance,
            ["PassThrough"] = SharpTSPassThroughConstructor.Instance,
            ["finished"] = BuiltInMethod.CreateV2("finished", 1, 3, Finished),
            ["pipeline"] = BuiltInMethod.CreateV2("pipeline", 2, int.MaxValue, Pipeline),
            ["addAbortSignal"] = BuiltInMethod.CreateV2("addAbortSignal", 2, AddAbortSignal),
            ["compose"] = BuiltInMethod.CreateV2("compose", 1, int.MaxValue, Compose),
            ["isErrored"] = BuiltInMethod.CreateV2("isErrored", 1, IsErrored),
            ["getDefaultHighWaterMark"] = BuiltInMethod.CreateV2("getDefaultHighWaterMark", 0, 1, GetDefaultHighWaterMark),
            ["setDefaultHighWaterMark"] = BuiltInMethod.CreateV2("setDefaultHighWaterMark", 2, SetDefaultHighWaterMark),
            ["promises"] = StreamPromisesModuleInterpreter.CreatePromisesNamespace()
        };
    }

    /// <summary>
    /// stream.finished(stream, options?, callback) — calls callback when stream is done.
    /// Returns a cleanup function that removes listeners.
    /// </summary>
    internal static object? FinishedInternal(Interp interpreter, object? receiver, List<object?> args)
    {
        var rvArgs = new RuntimeValue[args.Count];
        for (int i = 0; i < args.Count; i++)
            rvArgs[i] = RuntimeValue.FromBoxed(args[i]);
        return Finished(interpreter, RuntimeValue.FromBoxed(receiver), rvArgs).ToObject();
    }

    private static RuntimeValue Finished(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("finished() requires at least a stream argument");

        var stream = args[0].ToObject() as SharpTSEventEmitter
            ?? throw new Exception("finished() first argument must be a stream");

        // Parse options and callback
        SharpTSObject? options = null;
        ISharpTSCallable? callback = null;

        if (args.Length == 2)
        {
            if (args[1].ToObject() is ISharpTSCallable cb)
                callback = cb;
            else if (args[1].ToObject() is SharpTSObject opts)
                options = opts;
        }
        else if (args.Length >= 3)
        {
            options = args[1].ToObject() as SharpTSObject;
            callback = args[2].ToObject() as ISharpTSCallable;
        }

        if (callback == null)
            throw new Exception("finished() requires a callback function");

        bool watchReadable = true;
        bool watchWritable = true;

        if (options != null)
        {
            if (options.GetProperty("readable") is false)
                watchReadable = false;
            if (options.GetProperty("writable") is false)
                watchWritable = false;
        }

        bool called = false;
        var cbRef = callback;

        void CallOnce(Interp interp, object? error)
        {
            if (called) return;
            called = true;
            cbRef.Call(interp, [error]);
        }

        // Create listener callables
        var errorListener = new FinishedListener((interp, listenerArgs) =>
            CallOnce(interp, listenerArgs.Count > 0 ? listenerArgs[0] : null));
        var endListener = new FinishedListener((interp, _) =>
        {
            if (watchReadable) CallOnce(interp, null);
        });
        var finishListener = new FinishedListener((interp, _) =>
        {
            if (watchWritable) CallOnce(interp, null);
        });
        var closeListener = new FinishedListener((interp, _) => CallOnce(interp, null));

        stream.AddListenerDirect("error", errorListener, once: true);
        if (watchReadable)
            stream.AddListenerDirect("end", endListener, once: true);
        if (watchWritable)
            stream.AddListenerDirect("finish", finishListener, once: true);
        stream.AddListenerDirect("close", closeListener, once: true);

        // Return cleanup function
        return RuntimeValue.FromObject(BuiltInMethod.CreateV2("cleanup", 0, (Interp interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> a) =>
        {
            stream.RemoveListenerDirect("error", errorListener);
            stream.RemoveListenerDirect("end", endListener);
            stream.RemoveListenerDirect("finish", finishListener);
            stream.RemoveListenerDirect("close", closeListener);
            return RuntimeValue.Null;
        }));
    }

    /// <summary>
    /// stream.pipeline(source, ...transforms, dest, callback?) — pipes streams with error handling.
    /// Returns the destination stream.
    /// </summary>
    internal static object? PipelineInternal(Interp interpreter, object? receiver, List<object?> args)
    {
        var rvArgs = new RuntimeValue[args.Count];
        for (int i = 0; i < args.Count; i++)
            rvArgs[i] = RuntimeValue.FromBoxed(args[i]);
        return Pipeline(interpreter, RuntimeValue.FromBoxed(receiver), rvArgs).ToObject();
    }

    private static RuntimeValue Pipeline(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("pipeline() requires at least a source and destination");

        // Detect callback: if last arg is callable, it's the callback
        ISharpTSCallable? callback = null;
        var streams = new List<object?>(args.Length);
        for (int i = 0; i < args.Length; i++)
            streams.Add(args[i].ToObject());

        if (streams.Count > 0 && streams[^1] is ISharpTSCallable cb)
        {
            callback = cb;
            streams.RemoveAt(streams.Count - 1);
        }

        if (streams.Count < 2)
            throw new Exception("pipeline() requires at least a source and destination");

        // Pipe consecutive pairs
        for (int i = 0; i < streams.Count - 1; i++)
        {
            var source = streams[i];
            var dest = streams[i + 1];

            if (source is not SharpTSEventEmitter)
                throw new Exception("pipeline() arguments must be streams");

            // Call pipe on source
            var pipeMethod = GetStreamMethod(source, "pipe");
            pipeMethod?.Call(interpreter, [dest]);
        }

        // Attach error listeners on all streams for auto-destroy
        var finalCallback = callback;
        bool errorCalled = false;

        foreach (var s in streams)
        {
            if (s is SharpTSEventEmitter emitter)
            {
                emitter.AddListenerDirect("error", new FinishedListener((interp, errorArgs) =>
                {
                    if (errorCalled) return;
                    errorCalled = true;
                    var error = errorArgs.Count > 0 ? errorArgs[0] : null;

                    // Destroy all streams
                    foreach (var stream in streams)
                    {
                        var destroyMethod = GetStreamMethod(stream, "destroy");
                        destroyMethod?.Call(interp, []);
                    }

                    finalCallback?.Call(interp, [error]);
                }), once: true);
            }
        }

        // On success (last stream finishes), call callback(null)
        var lastStream = streams[^1];
        if (lastStream is SharpTSEventEmitter lastEmitter)
        {
            // Listen for finish (writable) or end (readable)
            var eventName = lastStream is SharpTSWritable or SharpTSDuplex ? "finish" : "end";
            lastEmitter.AddListenerDirect(eventName, new FinishedListener((interp, _) =>
            {
                if (!errorCalled)
                    finalCallback?.Call(interp, [null]);
            }), once: true);
        }

        return RuntimeValue.FromBoxed(lastStream);
    }

    /// <summary>
    /// stream.addAbortSignal(signal, stream) — destroys the stream with an AbortError when the
    /// signal fires (#1027). If the signal is already aborted, the stream is destroyed at once.
    /// </summary>
    private static RuntimeValue AddAbortSignal(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("addAbortSignal() requires signal and stream arguments");

        var signal = args[0].ToObject() as SharpTSAbortSignal;
        var stream = args[1].ToObject();

        if (signal != null && stream != null)
        {
            if (signal.Aborted)
                DestroyStreamWithAbort(interpreter, stream, signal);
            else
                signal.AddEventListener("abort", new AbortDestroyListener(stream, signal));
        }

        return args[1]; // Return the stream
    }

    /// <summary>Destroys a stream with the signal's reason (or a fresh AbortError).</summary>
    private static void DestroyStreamWithAbort(Interp interpreter, object stream, SharpTSAbortSignal signal)
    {
        var destroy = GetStreamMethod(stream, "destroy");
        destroy?.Call(interpreter, [MakeAbortError(signal)]);
    }

    /// <summary>Returns the signal's reason when it is an Error/object, else a Node-style AbortError.</summary>
    private static object MakeAbortError(SharpTSAbortSignal signal)
    {
        var reason = signal.Reason;
        if (reason is SharpTSError || reason is SharpTSObject)
            return reason;
        return new SharpTSError("The operation was aborted") { Name = "AbortError", Code = "ABORT_ERR" };
    }

    /// <summary>Listener that destroys the stream when its AbortSignal fires.</summary>
    private sealed class AbortDestroyListener : ISharpTSCallable
    {
        private readonly object _stream;
        private readonly SharpTSAbortSignal _signal;
        public AbortDestroyListener(object stream, SharpTSAbortSignal signal) { _stream = stream; _signal = signal; }
        public int Arity() => 0;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            DestroyStreamWithAbort(interpreter, _stream, _signal);
            return null;
        }
    }

    /// <summary>
    /// stream.compose(...streams) — composes streams into a single Duplex (#1028). Writing to the
    /// returned Duplex feeds the first stream; the chain is piped together; the last stream's output
    /// is pushed onto the Duplex's readable side.
    /// </summary>
    private static RuntimeValue Compose(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("compose() requires at least one stream");

        var streams = new List<object?>(args.Length);
        for (int i = 0; i < args.Length; i++)
            streams.Add(args[i].ToObject());

        // Pipe consecutive streams together.
        for (int i = 0; i < streams.Count - 1; i++)
            GetStreamMethod(streams[i], "pipe")?.Call(interpreter, [streams[i + 1]]);

        var first = streams[0];
        var last = streams[^1];

        var composed = new SharpTSDuplex { ObjectMode = true, WritableObjectMode = true };
        composed.SetWriteCallback(new ComposeForwardListener(first));
        composed.SetFinalCallback(new ComposeFinalListener(first));

        if (last is SharpTSEventEmitter lastEmitter)
        {
            lastEmitter.AddListenerDirect("data", new ComposeDataListener(composed));
            lastEmitter.AddListenerDirect("end", new ComposeEndListener(composed));
            lastEmitter.AddListenerDirect("error", new ComposeErrorListener(composed));
        }

        return RuntimeValue.FromObject(composed);
    }

    /// <summary>Write callback for a composed Duplex: forwards each chunk to the first stream.</summary>
    private sealed class ComposeForwardListener : ISharpTSCallable
    {
        private readonly object? _first;
        public ComposeForwardListener(object? first) => _first = first;
        public int Arity() => 3;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var done = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;
            GetStreamMethod(_first, "write")?.Call(interpreter, [chunk]);
            done?.Call(interpreter, []);
            return null;
        }
    }

    /// <summary>Final callback for a composed Duplex: ends the first stream when the Duplex ends.</summary>
    private sealed class ComposeFinalListener : ISharpTSCallable
    {
        private readonly object? _first;
        public ComposeFinalListener(object? first) => _first = first;
        public int Arity() => 1;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var done = arguments.Count > 0 ? arguments[0] as ISharpTSCallable : null;
            GetStreamMethod(_first, "end")?.Call(interpreter, []);
            done?.Call(interpreter, []);
            return null;
        }
    }

    /// <summary>Pushes the last stream's data onto the composed Duplex's readable side.</summary>
    private sealed class ComposeDataListener : ISharpTSCallable
    {
        private readonly SharpTSDuplex _composed;
        public ComposeDataListener(SharpTSDuplex composed) => _composed = composed;
        public int Arity() => 1;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            GetStreamMethod(_composed, "push")?.Call(interpreter, [chunk]);
            return null;
        }
    }

    /// <summary>Ends the composed Duplex's readable side when the last stream ends.</summary>
    private sealed class ComposeEndListener : ISharpTSCallable
    {
        private readonly SharpTSDuplex _composed;
        public ComposeEndListener(SharpTSDuplex composed) => _composed = composed;
        public int Arity() => 0;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            GetStreamMethod(_composed, "push")?.Call(interpreter, [(object?)null]);
            return null;
        }
    }

    /// <summary>Propagates the last stream's error to the composed Duplex.</summary>
    private sealed class ComposeErrorListener : ISharpTSCallable
    {
        private readonly SharpTSDuplex _composed;
        public ComposeErrorListener(SharpTSDuplex composed) => _composed = composed;
        public int Arity() => 1;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var error = arguments.Count > 0 ? arguments[0] : null;
            GetStreamMethod(_composed, "destroy")?.Call(interpreter, [error]);
            return null;
        }
    }

    // Module-level default highWaterMarks (#1030). Shared with the compiled $StreamUtils defaults.
    private static int _defaultHwmByte = 16384;
    private static int _defaultHwmObject = 16;

    /// <summary>stream.isErrored(stream) — true if the stream has errored.</summary>
    private static RuntimeValue IsErrored(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var obj = args.Length > 0 ? args[0].ToObject() : null;
        bool errored = obj switch
        {
            SharpTSReadable r => r.Errored,
            SharpTSWritable w => w.Errored,
            _ => false
        };
        return RuntimeValue.FromBoolean(errored);
    }

    /// <summary>stream.getDefaultHighWaterMark(objectMode?) — the current default highWaterMark.</summary>
    private static RuntimeValue GetDefaultHighWaterMark(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        bool objectMode = args.Length > 0 && args[0].IsTruthy();
        return RuntimeValue.FromNumber(objectMode ? _defaultHwmObject : _defaultHwmByte);
    }

    /// <summary>stream.setDefaultHighWaterMark(objectMode, value) — sets the default highWaterMark.</summary>
    private static RuntimeValue SetDefaultHighWaterMark(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        bool objectMode = args.Length > 0 && args[0].IsTruthy();
        int value = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
        if (objectMode)
            _defaultHwmObject = value;
        else
            _defaultHwmByte = value;
        return RuntimeValue.Undefined;
    }

    private static BuiltInMethod? GetStreamMethod(object? stream, string methodName)
    {
        return stream switch
        {
            SharpTSPassThrough pt => pt.GetMember(methodName) as BuiltInMethod,
            SharpTSTransform t => t.GetMember(methodName) as BuiltInMethod,
            SharpTSDuplex d => d.GetMember(methodName) as BuiltInMethod,
            SharpTSReadable r => r.GetMember(methodName) as BuiltInMethod,
            SharpTSWritable w => w.GetMember(methodName) as BuiltInMethod,
            _ => null
        };
    }

    /// <summary>
    /// Simple callable for finished/pipeline internal listeners.
    /// </summary>
    internal class FinishedListener : ISharpTSCallable
    {
        private readonly Action<Interp, List<object?>> _action;
        public FinishedListener(Action<Interp, List<object?>> action) => _action = action;
        public int Arity() => 0;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            _action(interpreter, arguments);
            return null;
        }
    }
}
