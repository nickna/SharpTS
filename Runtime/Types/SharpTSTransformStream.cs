using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the WHATWG Streams <c>TransformStream</c>.
/// </summary>
/// <remarks>
/// Owns one readable/writable pair. Chunks written to <see cref="Writable"/>
/// are piped through the user's <c>transform</c> callback which may call
/// <c>controller.enqueue(...)</c> to push into <see cref="Readable"/>. When the
/// writable side closes, the user's optional <c>flush</c> callback runs before
/// the readable side closes.
/// </remarks>
public class SharpTSTransformStream : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    public SharpTSReadableStream Readable { get; }
    public SharpTSWritableStream Writable { get; }

    private readonly SharpTSTransformStreamDefaultController _controller;

    private object? _transformFn;
    private object? _flushFn;

    public SharpTSTransformStream(Interp? interp, object? transformer, object? writableStrategy, object? readableStrategy)
    {
        if (transformer != null)
        {
            _transformFn = StreamFields.GetCallback(transformer, "transform");
            _flushFn = StreamFields.GetCallback(transformer, "flush");
        }

        // Build the readable side first (with a pull algorithm that's a no-op — data is pushed).
        Readable = new SharpTSReadableStream(interp, underlyingSource: null, strategy: readableStrategy);
        _controller = new SharpTSTransformStreamDefaultController(Readable);

        // Build a writable sink that runs the user's transform callback.
        var sinkFields = new Dictionary<string, object?>
        {
            ["write"] = BuiltInMethod.CreateV2("write", 2, (i, _, args) =>
            {
                var chunk = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                return RuntimeValue.FromBoxed(RunTransformAsync(i, chunk));
            }),
            ["close"] = BuiltInMethod.CreateV2("close", 0, (i, _, _) =>
            {
                return RuntimeValue.FromBoxed(RunFlushAsync(i));
            }),
            ["abort"] = BuiltInMethod.CreateV2("abort", 1, (_, _, args) =>
            {
                var reason = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                Readable.ErrorInternal(reason);
                return RuntimeValue.Undefined;
            }),
        };
        var sink = new SharpTSObject(sinkFields);

        Writable = new SharpTSWritableStream(interp, sink, writableStrategy);

        // Fire transformer.start(controller) if present.
        var startFn = StreamFields.GetCallback(transformer, "start");
        if (startFn != null)
        {
            try
            {
                RuntimeCallableDispatcher.Invoke(interp, startFn, _controller);
            }
            catch (Exception ex)
            {
                Readable.ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
            }
        }
    }

    private object? RunTransformAsync(Interp? interp, object? chunk)
    {
        if (_transformFn == null)
        {
            // Default: pass through.
            try
            {
                Readable.EnqueueInternal(chunk);
            }
            catch (Exception ex)
            {
                Readable.ErrorInternal(ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex);
            }
            return SharpTSUndefined.Instance;
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(interp, _transformFn, chunk, _controller);
            if (result is SharpTSPromise p) return p;
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            var err = ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex;
            Readable.ErrorInternal(err);
            return SharpTSPromise.Reject(err);
        }
    }

    private object? RunFlushAsync(Interp? interp)
    {
        Task<object?> finishAsync()
        {
            Readable.CloseInternal();
            return Task.FromResult<object?>(SharpTSUndefined.Instance);
        }

        if (_flushFn == null)
        {
            return new SharpTSPromise(finishAsync());
        }

        try
        {
            var result = RuntimeCallableDispatcher.Invoke(interp, _flushFn, _controller);
            if (result is SharpTSPromise p)
            {
                async Task<object?> awaitThenClose()
                {
                    await p.GetValueAsync();
                    Readable.CloseInternal();
                    return SharpTSUndefined.Instance;
                }
                return new SharpTSPromise(awaitThenClose());
            }
            Readable.CloseInternal();
            return SharpTSUndefined.Instance;
        }
        catch (Exception ex)
        {
            var err = ex is SharpTSPromiseRejectedException pre ? pre.Reason : ex;
            Readable.ErrorInternal(err);
            return SharpTSPromise.Reject(err);
        }
    }

    public object? GetMember(string name)
    {
        return name switch
        {
            "readable" => Readable,
            "writable" => Writable,
            _ => null,
        };
    }

    public override string ToString() => "TransformStream {}";
}

/// <summary>
/// Controller handed to a <see cref="SharpTSTransformStream"/>'s
/// <c>transform</c>/<c>flush</c> callbacks.
/// </summary>
public class SharpTSTransformStreamDefaultController : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    private readonly SharpTSReadableStream _readable;

    internal SharpTSTransformStreamDefaultController(SharpTSReadableStream readable)
    {
        _readable = readable;
    }

    public object? GetMember(string name)
    {
        return name switch
        {
            "desiredSize" => _readable.DesiredSize is { } d ? (object)d : null,
            "enqueue" => BuiltInMethod.CreateV2("enqueue", 1, (_, _, args) =>
            {
                _readable.EnqueueInternal(args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);
                return RuntimeValue.Undefined;
            }),
            "terminate" => BuiltInMethod.CreateV2("terminate", 0, (_, _, _) =>
            {
                // Per WHATWG spec: terminate() closes the readable side
                // (not errors it). Subsequent reader.read() returns
                // { value: undefined, done: true } as with a normal close.
                _readable.CloseInternal();
                return RuntimeValue.Undefined;
            }),
            "error" => BuiltInMethod.CreateV2("error", 1, (_, _, args) =>
            {
                _readable.ErrorInternal(args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance);
                return RuntimeValue.Undefined;
            }),
            _ => null,
        };
    }

    public override string ToString() => "TransformStreamDefaultController {}";
}
