using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible Writable stream.
/// Provides sync write mode with optional custom write callback.
/// </summary>
/// <remarks>
/// Extends <see cref="SharpTSEventEmitter"/> for event support (drain, finish, error, close).
/// The write-side machinery (write/end/cork/uncork/backpressure) lives in the shared
/// <see cref="WritableCore"/>; this class adds the destroy lifecycle and stdout/stderr reuse.
/// </remarks>
public class SharpTSWritable : SharpTSEventEmitter
{
    private readonly WritableCore _writeCore;
    private ISharpTSCallable? _destroyCallback;
    private int _highWaterMark = 16384;
    private bool _objectMode;
    private bool _autoDestroy;

    public SharpTSWritable()
    {
        // Writable emits "prefinish" before "finish" and may auto-destroy afterward.
        _writeCore = new WritableCore(
            this,
            objectMode: () => _objectMode,
            highWaterMark: () => _highWaterMark,
            emitPrefinish: true,
            onFinished: interp => { if (_autoDestroy) DoDestroy(interp, null); });
    }

    /// <summary>
    /// Whether this stream has errored — backs stream.isErrored (#1030).
    /// </summary>
    public bool Errored => _writeCore.Errored;

    /// <summary>
    /// Gets or sets whether this stream operates in object mode.
    /// </summary>
    public bool ObjectMode { get => _objectMode; set => _objectMode = value; }

    /// <summary>
    /// Gets or sets whether the stream auto-destroys after finishing.
    /// </summary>
    public bool AutoDestroy { get => _autoDestroy; set => _autoDestroy = value; }

    /// <summary>
    /// Gets or sets the high water mark for this stream.
    /// </summary>
    public int HighWaterMark { get => _highWaterMark; set => _highWaterMark = value; }

    /// <summary>
    /// Sets the custom write callback (from constructor options).
    /// </summary>
    public void SetWriteCallback(ISharpTSCallable callback) => _writeCore.SetWriteCallback(callback);

    /// <summary>
    /// Sets the custom final callback (from constructor options).
    /// </summary>
    public void SetFinalCallback(ISharpTSCallable callback) => _writeCore.SetFinalCallback(callback);

    /// <summary>
    /// Sets the custom destroy callback (from constructor options).
    /// </summary>
    public void SetDestroyCallback(ISharpTSCallable callback) => _destroyCallback = callback;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch. The writable-side
    /// methods and properties come from the shared <see cref="WritableCore"/> dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        if (_writeCore.GetWritableMember(name) is { } writableMember)
            return writableMember;

        return name switch
        {
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, Destroy),
            "setDefaultEncoding" => BuiltInMethod.CreateV2("setDefaultEncoding", 1, SetDefaultEncoding),
            "destroyed" => _writeCore.Destroyed,

            // Inherit from EventEmitter
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Internal write method for piped data. Returns false on backpressure.
    /// </summary>
    internal bool WriteInternal(Interp interpreter, object? chunk, string? encoding)
        => _writeCore.WriteDirect(interpreter, chunk, encoding);

    /// <summary>
    /// Internal end method for piped streams.
    /// </summary>
    internal void EndInternal(Interp interpreter, object? chunk, string? encoding)
        => _writeCore.EndDirect(interpreter, chunk, encoding);

    /// <summary>
    /// Destroys the stream.
    /// </summary>
    private RuntimeValue Destroy(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        DoDestroy(interpreter, args.Length > 0 ? args[0].ToObject() : null);
        return RuntimeValue.FromObject(this);
    }

    private void DoDestroy(Interp interpreter, object? error)
    {
        if (_writeCore.Destroyed)
            return;

        _writeCore.MarkDestroyed();

        if (_destroyCallback != null)
        {
            try
            {
                _destroyCallback.Call(interpreter, [error, new DestroyCallbackWrapper(interpreter, this)]);
            }
            catch (Exception ex)
            {
                _writeCore.EmitError(interpreter, ex.Message);
            }
        }
        else
        {
            if (error is { })
            {
                _writeCore.EmitError(interpreter, error);
            }
            EmitClose(interpreter);
        }
    }

    private RuntimeValue SetDefaultEncoding(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // Just accept it for compatibility
        return RuntimeValue.FromObject(this);
    }

    private void EmitClose(Interp interpreter)
    {
        EmitEvent(interpreter, "close", []);
    }

    /// <summary>
    /// Resets mutable state for singleton reuse (e.g., process.stdout between interpreter runs).
    /// </summary>
    internal void ResetWritableState()
    {
        _writeCore.Reset();
        ClearAllListenersInternal();
    }

    public override string ToString() => "Writable {}";

    /// <summary>
    /// Wrapper for destroy callback to emit close event.
    /// </summary>
    private sealed class DestroyCallbackWrapper : ISharpTSCallable
    {
        private readonly Interp _interpreter;
        private readonly SharpTSWritable _stream;

        public DestroyCallbackWrapper(Interp interpreter, SharpTSWritable stream)
        {
            _interpreter = interpreter;
            _stream = stream;
        }

        public int Arity() => 0;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var error = arguments.Count > 0 ? arguments[0] : null;
            if (error != null)
            {
                _stream._writeCore.EmitError(_interpreter, error);
            }
            _stream.EmitClose(_interpreter);
            return null;
        }
    }
}
