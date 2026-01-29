using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible Duplex stream.
/// Combines both Readable and Writable capabilities.
/// </summary>
/// <remarks>
/// Extends <see cref="SharpTSReadable"/> and adds Writable-side methods.
/// The read and write sides operate independently.
/// </remarks>
public class SharpTSDuplex : SharpTSReadable
{
    // Writable-side state
    private bool _writable = true;
    private bool _writableEnded;
    private bool _writableFinished;
    private bool _writableDestroyed;
    private bool _corked;
    private readonly List<object?> _corkBuffer = [];
    private ISharpTSCallable? _writeCallback;
    private ISharpTSCallable? _finalCallback;
    private ISharpTSCallable? _readCallback;

    /// <summary>
    /// Sets the custom write callback (from constructor options).
    /// </summary>
    public void SetWriteCallback(ISharpTSCallable callback) => _writeCallback = callback;

    /// <summary>
    /// Sets the custom final callback (from constructor options).
    /// </summary>
    public void SetFinalCallback(ISharpTSCallable callback) => _finalCallback = callback;

    /// <summary>
    /// Sets the custom read callback (from constructor options).
    /// </summary>
    public void SetReadCallback(ISharpTSCallable callback) => _readCallback = callback;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            // Writable-side methods
            "write" => new BuiltInMethod("write", 1, 3, Write),
            "end" => new BuiltInMethod("end", 0, 3, End),
            "cork" => new BuiltInMethod("cork", 0, Cork),
            "uncork" => new BuiltInMethod("uncork", 0, Uncork),

            // Writable-side properties
            "writable" => _writable && !_writableEnded && !_writableDestroyed,
            "writableEnded" => _writableEnded,
            "writableFinished" => _writableFinished,
            "writableLength" => (double)_corkBuffer.Count,
            "writableCorked" => (double)(_corked ? 1 : 0),

            // Override destroy to handle both sides
            "destroy" => new BuiltInMethod("destroy", 0, 1, DestroyDuplex),

            // Inherit Readable methods and properties
            _ => base.GetMember(name)
        };
    }

    private object? Write(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_writableDestroyed || _writableEnded)
        {
            EmitEvent(interpreter, "error", ["write after end"]);
            return false;
        }

        var chunk = args.Count > 0 ? args[0] : null;
        string? encoding = null;
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
            var cbWrapper = new WriteCallbackWrapper(callback, interpreter);
            var writeArgs = new List<object?> { chunk, encoding ?? "utf8", cbWrapper };
            try
            {
                _writeCallback.Call(interpreter, writeArgs);
            }
            catch (Exception ex)
            {
                EmitEvent(interpreter, "error", [ex.Message]);
                return false;
            }
        }
        else
        {
            callback?.Call(interpreter, []);
        }

        return true;
    }

    private object? End(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_writableEnded)
        {
            return this;
        }

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

        _writableEnded = true;
        _writable = false;

        if (chunk != null)
        {
            DoWrite(interpreter, chunk, encoding, null);
        }

        if (_corked)
        {
            Uncork(interpreter, null, []);
        }

        if (_finalCallback != null)
        {
            var finalCbWrapper = new WriteCallbackWrapper(null, interpreter);
            try
            {
                _finalCallback.Call(interpreter, [finalCbWrapper]);
            }
            catch (Exception ex)
            {
                EmitEvent(interpreter, "error", [ex.Message]);
            }
        }

        _writableFinished = true;
        callback?.Call(interpreter, []);
        EmitEvent(interpreter, "finish", []);

        return this;
    }

    private object? Cork(Interp interpreter, object? receiver, List<object?> args)
    {
        _corked = true;
        return null;
    }

    private object? Uncork(Interp interpreter, object? receiver, List<object?> args)
    {
        if (!_corked)
        {
            return null;
        }

        _corked = false;

        foreach (var item in _corkBuffer.Cast<WriteChunk>())
        {
            DoWrite(interpreter, item.Chunk, item.Encoding, item.Callback);
        }
        _corkBuffer.Clear();

        return null;
    }

    private object? DestroyDuplex(Interp interpreter, object? receiver, List<object?> args)
    {
        _writableDestroyed = true;
        _writable = false;
        _corkBuffer.Clear();

        // Destroy the readable side too via the base Destroy method
        var baseDestroy = base.GetMember("destroy") as BuiltInMethod;
        baseDestroy?.Bind(this).Call(interpreter, args);

        return this;
    }

    public override string ToString() => "Duplex {}";

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
            _callback?.Call(_interpreter, []);
            return null;
        }
    }
}
