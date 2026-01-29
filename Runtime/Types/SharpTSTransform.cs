using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible Transform stream.
/// Extends Duplex and adds transformation capability.
/// </summary>
/// <remarks>
/// Transform streams read data, transform it, and write the result to the output.
/// The _transform callback performs the transformation.
/// </remarks>
public class SharpTSTransform : SharpTSDuplex
{
    private ISharpTSCallable? _transformCallback;
    private ISharpTSCallable? _flushCallback;

    /// <summary>
    /// Sets the custom transform callback (from constructor options).
    /// </summary>
    public void SetTransformCallback(ISharpTSCallable callback) => _transformCallback = callback;

    /// <summary>
    /// Sets the custom flush callback (from constructor options).
    /// </summary>
    public void SetFlushCallback(ISharpTSCallable callback) => _flushCallback = callback;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            // Override write to use transform
            "write" => new BuiltInMethod("write", 1, 3, TransformWrite),

            // Override end to call flush
            "end" => new BuiltInMethod("end", 0, 3, TransformEnd),

            // _transform method for subclasses
            "_transform" => new BuiltInMethod("_transform", 3, InternalTransform),

            // _flush method for subclasses
            "_flush" => new BuiltInMethod("_flush", 1, InternalFlush),

            // Inherit Duplex methods and properties
            _ => base.GetMember(name)
        };
    }

    private object? TransformWrite(Interp interpreter, object? receiver, List<object?> args)
    {
        var chunk = args.Count > 0 ? args[0] : null;
        string encoding = "utf8";
        ISharpTSCallable? callback = null;

        if (args.Count > 1)
        {
            if (args[1] is string enc)
            {
                encoding = enc;
                if (args.Count > 2 && args[2] is ISharpTSCallable cb)
                {
                    callback = cb;
                }
            }
            else if (args[1] is ISharpTSCallable cb)
            {
                callback = cb;
            }
        }

        // Create push callback that adds to readable side
        var pushCallback = new TransformPushCallback(this, interpreter);
        var doneCallback = new TransformDoneCallback(callback, interpreter, this);

        if (_transformCallback != null)
        {
            try
            {
                _transformCallback.Call(interpreter, [chunk, encoding, doneCallback]);
            }
            catch (Exception ex)
            {
                EmitEvent(interpreter, "error", [ex.Message]);
                return false;
            }
        }
        else
        {
            // Default transform: pass through
            PushToReadableSide(interpreter, chunk);
            doneCallback.Call(interpreter, []);
        }

        return true;
    }

    private object? TransformEnd(Interp interpreter, object? receiver, List<object?> args)
    {
        object? chunk = null;
        string? encoding = null;
        ISharpTSCallable? callback = null;

        if (args.Count > 0)
        {
            if (args[0] is ISharpTSCallable cb0)
            {
                callback = cb0;
            }
            else
            {
                chunk = args[0];
                if (args.Count > 1)
                {
                    if (args[1] is string enc)
                    {
                        encoding = enc;
                        if (args.Count > 2 && args[2] is ISharpTSCallable cb)
                        {
                            callback = cb;
                        }
                    }
                    else if (args[1] is ISharpTSCallable cb)
                    {
                        callback = cb;
                    }
                }
            }
        }

        // Write final chunk if provided
        if (chunk != null)
        {
            TransformWrite(interpreter, receiver, [chunk, encoding]);
        }

        // Call flush
        if (_flushCallback != null)
        {
            var flushDoneCallback = new TransformDoneCallback(callback, interpreter, this);
            try
            {
                _flushCallback.Call(interpreter, [flushDoneCallback]);
            }
            catch (Exception ex)
            {
                EmitEvent(interpreter, "error", [ex.Message]);
            }
        }
        else
        {
            callback?.Call(interpreter, []);
        }

        // End the readable side
        PushToReadableSide(interpreter, null);

        // Emit finish
        EmitEvent(interpreter, "finish", []);

        return this;
    }

    private object? InternalTransform(Interp interpreter, object? receiver, List<object?> args)
    {
        // Default _transform implementation (for subclasses to override)
        var chunk = args.Count > 0 ? args[0] : null;
        var callback = args.Count > 2 ? args[2] as ISharpTSCallable : null;

        // Pass through by default
        PushToReadableSide(interpreter, chunk);
        callback?.Call(interpreter, []);

        return null;
    }

    private object? InternalFlush(Interp interpreter, object? receiver, List<object?> args)
    {
        // Default _flush implementation (for subclasses to override)
        var callback = args.Count > 0 ? args[0] as ISharpTSCallable : null;
        callback?.Call(interpreter, []);
        return null;
    }

    internal void PushToReadableSide(Interp interpreter, object? chunk)
    {
        // Use the base push method to add to the readable buffer
        var push = base.GetMember("push") as BuiltInMethod;
        push?.Bind(this).Call(interpreter, [chunk]);
    }

    public override string ToString() => "Transform {}";

    private class TransformPushCallback : ISharpTSCallable
    {
        private readonly SharpTSTransform _stream;
        private readonly Interp _interpreter;

        public TransformPushCallback(SharpTSTransform stream, Interp interpreter)
        {
            _stream = stream;
            _interpreter = interpreter;
        }

        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            _stream.PushToReadableSide(_interpreter, chunk);
            return null;
        }
    }

    private class TransformDoneCallback : ISharpTSCallable
    {
        private readonly ISharpTSCallable? _callback;
        private readonly Interp _interpreter;
        private readonly SharpTSTransform _stream;

        public TransformDoneCallback(ISharpTSCallable? callback, Interp interpreter, SharpTSTransform stream)
        {
            _callback = callback;
            _interpreter = interpreter;
            _stream = stream;
        }

        public int Arity() => 2;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var error = arguments.Count > 0 ? arguments[0] : null;
            var data = arguments.Count > 1 ? arguments[1] : null;

            // If data is provided, push it to the readable side
            if (data != null)
            {
                _stream.PushToReadableSide(_interpreter, data);
            }

            _callback?.Call(_interpreter, []);
            return null;
        }
    }
}
