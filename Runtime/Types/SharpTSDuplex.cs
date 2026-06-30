using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible Duplex stream.
/// Combines both Readable and Writable capabilities.
/// </summary>
/// <remarks>
/// Extends <see cref="SharpTSReadable"/> and adds the Writable side via a composed
/// <see cref="WritableCore"/> (C# single inheritance prevents inheriting a writable
/// base — #1138). The read and write sides operate independently.
/// </remarks>
public class SharpTSDuplex : SharpTSReadable
{
    private readonly WritableCore _writeCore;
    private ISharpTSCallable? _readCallback;
    private int _writableHighWaterMark = 16384;
    private bool _writableObjectMode;

    public SharpTSDuplex()
    {
        // The Duplex writable side emits "finish" with no "prefinish" and no auto-destroy
        // (destroy is handled jointly with the readable side via DestroyDuplex).
        _writeCore = new WritableCore(
            this,
            objectMode: () => _writableObjectMode,
            highWaterMark: () => _writableHighWaterMark,
            emitPrefinish: false,
            onFinished: null);
    }

    /// <summary>
    /// Sets the custom write callback (from constructor options).
    /// </summary>
    public void SetWriteCallback(ISharpTSCallable callback) => _writeCore.SetWriteCallback(callback);

    /// <summary>
    /// Sets the custom final callback (from constructor options).
    /// </summary>
    public void SetFinalCallback(ISharpTSCallable callback) => _writeCore.SetFinalCallback(callback);

    /// <summary>
    /// Sets the custom read callback (from constructor options).
    /// </summary>
    public void SetReadCallback(ISharpTSCallable callback) => _readCallback = callback;

    /// <summary>
    /// Gets or sets the writable-side high water mark.
    /// </summary>
    public int WritableHighWaterMark { get => _writableHighWaterMark; set => _writableHighWaterMark = value; }

    /// <summary>
    /// Gets or sets whether the writable side operates in object mode.
    /// </summary>
    public bool WritableObjectMode { get => _writableObjectMode; set => _writableObjectMode = value; }

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            // Writable-side methods
            "write" => BuiltInMethod.CreateV2("write", 1, 3, Write),
            "end" => BuiltInMethod.CreateV2("end", 0, 3, End),
            "cork" => BuiltInMethod.CreateV2("cork", 0, Cork),
            "uncork" => BuiltInMethod.CreateV2("uncork", 0, Uncork),

            // Writable-side properties
            "writable" => _writeCore.IsWritable,
            "writableEnded" => _writeCore.Ended,
            "writableFinished" => _writeCore.Finished,
            "writableLength" => (double)_writeCore.WritableLength,
            "writableCorked" => (double)(_writeCore.Corked ? 1 : 0),
            "writableHighWaterMark" => (double)_writableHighWaterMark,
            "writableObjectMode" => _writableObjectMode,

            // Override destroy to handle both sides
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, DestroyDuplex),

            // Inherit Readable methods and properties
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue Write(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => _writeCore.Write(interpreter, args);

    private RuntimeValue End(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => _writeCore.End(interpreter, args);

    private RuntimeValue Cork(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _writeCore.Cork();
        return RuntimeValue.Null;
    }

    private RuntimeValue Uncork(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _writeCore.Uncork(interpreter);
        return RuntimeValue.Null;
    }

    private RuntimeValue DestroyDuplex(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _writeCore.MarkDestroyed();

        // Destroy the readable side too via the base Destroy method
        var baseDestroy = base.GetMember("destroy") as BuiltInMethod;
        baseDestroy?.Bind(this).CallV2(interpreter, args);

        return RuntimeValue.FromObject(this);
    }

    public override string ToString() => "Duplex {}";
}
