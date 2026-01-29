using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible Writable stream.
/// Provides sync write mode with optional custom write callback.
/// </summary>
/// <remarks>
/// Extends <see cref="SharpTSEventEmitter"/> for event support (drain, finish, error, close).
/// </remarks>
public class SharpTSWritable : SharpTSEventEmitter
{
    private bool _writable = true;
    private bool _ended;
    private bool _finished;
    private bool _destroyed;
    private bool _corked;
    private readonly List<object?> _corkBuffer = [];
    private ISharpTSCallable? _writeCallback;
    private ISharpTSCallable? _finalCallback;
    private ISharpTSCallable? _destroyCallback;

    /// <summary>
    /// Sets the custom write callback (from constructor options).
    /// </summary>
    public void SetWriteCallback(ISharpTSCallable callback) => _writeCallback = callback;

    /// <summary>
    /// Sets the custom final callback (from constructor options).
    /// </summary>
    public void SetFinalCallback(ISharpTSCallable callback) => _finalCallback = callback;

    /// <summary>
    /// Sets the custom destroy callback (from constructor options).
    /// </summary>
    public void SetDestroyCallback(ISharpTSCallable callback) => _destroyCallback = callback;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            // Writable-specific methods
            "write" => new BuiltInMethod("write", 1, 3, Write),
            "end" => new BuiltInMethod("end", 0, 3, End),
            "cork" => new BuiltInMethod("cork", 0, Cork),
            "uncork" => new BuiltInMethod("uncork", 0, Uncork),
            "destroy" => new BuiltInMethod("destroy", 0, 1, Destroy),
            "setDefaultEncoding" => new BuiltInMethod("setDefaultEncoding", 1, SetDefaultEncoding),

            // Properties
            "writable" => _writable && !_ended && !_destroyed,
            "writableEnded" => _ended,
            "writableFinished" => _finished,
            "writableLength" => (double)_corkBuffer.Count,
            "writableCorked" => (double)(_corked ? 1 : 0),
            "destroyed" => _destroyed,

            // Inherit from EventEmitter
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Writes data to the stream.
    /// </summary>
    private object? Write(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_destroyed || _ended)
        {
            EmitError(interpreter, "write after end");
            return false;
        }

        var chunk = args.Count > 0 ? args[0] : null;
        string? encoding = null;
        ISharpTSCallable? callback = null;

        // Parse arguments: (chunk, encoding?, callback?)
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

        if (_corked)
        {
            _corkBuffer.Add(new WriteChunk(chunk, encoding, callback));
            return false;
        }

        return DoWrite(interpreter, chunk, encoding, callback);
    }

    private record WriteChunk(object? Chunk, string? Encoding, ISharpTSCallable? Callback);

    private object? DoWrite(Interp interpreter, object? chunk, string? encoding, ISharpTSCallable? callback)
    {
        if (_writeCallback != null)
        {
            // Custom write callback: (chunk, encoding, callback)
            var cbWrapper = new WriteCallbackWrapper(callback, interpreter);
            var writeArgs = new List<object?> { chunk, encoding ?? "utf8", cbWrapper };
            try
            {
                _writeCallback.Call(interpreter, writeArgs);
            }
            catch (Exception ex)
            {
                EmitError(interpreter, ex.Message);
                return false;
            }
        }
        else
        {
            // Default behavior: just accept the data
            callback?.Call(interpreter, []);
        }

        return true;
    }

    /// <summary>
    /// Internal write method for piped data.
    /// </summary>
    internal void WriteInternal(Interp interpreter, object? chunk, string? encoding)
    {
        if (_destroyed || _ended)
        {
            return;
        }

        DoWrite(interpreter, chunk, encoding, null);
    }

    /// <summary>
    /// Ends the stream, optionally writing final data.
    /// </summary>
    private object? End(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_ended)
        {
            return this;
        }

        object? chunk = null;
        string? encoding = null;
        ISharpTSCallable? callback = null;

        // Parse arguments: (chunk?, encoding?, callback?)
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

        _ended = true;
        _writable = false;

        // Write final chunk if provided
        if (chunk != null)
        {
            DoWrite(interpreter, chunk, encoding, null);
        }

        // Flush cork buffer
        if (_corked)
        {
            Uncork(interpreter, null, []);
        }

        // Call final callback
        if (_finalCallback != null)
        {
            var finalCbWrapper = new WriteCallbackWrapper(null, interpreter);
            try
            {
                _finalCallback.Call(interpreter, [finalCbWrapper]);
            }
            catch (Exception ex)
            {
                EmitError(interpreter, ex.Message);
            }
        }

        _finished = true;
        callback?.Call(interpreter, []);
        EmitFinish(interpreter);

        return this;
    }

    /// <summary>
    /// Internal end method for piped streams.
    /// </summary>
    internal void EndInternal(Interp interpreter, object? chunk, string? encoding)
    {
        End(interpreter, this, chunk != null ? [chunk, encoding] : []);
    }

    /// <summary>
    /// Corks the stream, buffering all writes.
    /// </summary>
    private object? Cork(Interp interpreter, object? receiver, List<object?> args)
    {
        _corked = true;
        return null;
    }

    /// <summary>
    /// Uncorks the stream, flushing the buffer.
    /// </summary>
    private object? Uncork(Interp interpreter, object? receiver, List<object?> args)
    {
        if (!_corked)
        {
            return null;
        }

        _corked = false;

        // Flush the cork buffer
        foreach (var item in _corkBuffer.Cast<WriteChunk>())
        {
            DoWrite(interpreter, item.Chunk, item.Encoding, item.Callback);
        }
        _corkBuffer.Clear();

        return null;
    }

    /// <summary>
    /// Destroys the stream.
    /// </summary>
    private object? Destroy(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_destroyed)
        {
            return this;
        }

        _destroyed = true;
        _writable = false;
        _corkBuffer.Clear();

        if (_destroyCallback != null)
        {
            try
            {
                var err = args.Count > 0 ? args[0] : null;
                _destroyCallback.Call(interpreter, [err, new DestroyCallbackWrapper(interpreter, this)]);
            }
            catch (Exception ex)
            {
                EmitError(interpreter, ex.Message);
            }
        }
        else
        {
            if (args.Count > 0 && args[0] != null)
            {
                EmitError(interpreter, args[0]);
            }
            EmitClose(interpreter);
        }

        return this;
    }

    private object? SetDefaultEncoding(Interp interpreter, object? receiver, List<object?> args)
    {
        // Just accept it for compatibility
        return this;
    }

    private void EmitError(Interp interpreter, object? error)
    {
        EmitEvent(interpreter, "error", [error]);
    }

    private void EmitFinish(Interp interpreter)
    {
        EmitEvent(interpreter, "finish", []);
    }

    private void EmitClose(Interp interpreter)
    {
        EmitEvent(interpreter, "close", []);
    }

    internal void EmitEvent(Interp interpreter, string eventName, List<object?> args)
    {
        var emit = base.GetMember("emit") as BuiltInMethod;
        if (emit != null)
        {
            var fullArgs = new List<object?> { eventName };
            fullArgs.AddRange(args);
            emit.Bind(this).Call(interpreter, fullArgs);
        }
    }

    public override string ToString() => "Writable {}";

    /// <summary>
    /// Wrapper for write callbacks to match Node.js callback(error?) signature.
    /// </summary>
    private class WriteCallbackWrapper : ISharpTSCallable
    {
        private readonly ISharpTSCallable? _callback;
        private readonly Interp _interpreter;

        public WriteCallbackWrapper(ISharpTSCallable? callback, Interp interpreter)
        {
            _callback = callback;
            _interpreter = interpreter;
        }

        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            // error argument
            var error = arguments.Count > 0 ? arguments[0] : null;
            // For now, just call the original callback
            _callback?.Call(_interpreter, []);
            return null;
        }
    }

    /// <summary>
    /// Wrapper for destroy callback to emit close event.
    /// </summary>
    private class DestroyCallbackWrapper : ISharpTSCallable
    {
        private readonly Interp _interpreter;
        private readonly SharpTSWritable _stream;

        public DestroyCallbackWrapper(Interp interpreter, SharpTSWritable stream)
        {
            _interpreter = interpreter;
            _stream = stream;
        }

        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var error = arguments.Count > 0 ? arguments[0] : null;
            if (error != null)
            {
                _stream.EmitError(_interpreter, error);
            }
            _stream.EmitClose(_interpreter);
            return null;
        }
    }
}
