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
    /// Gets a member (method or property) by name for interpreter dispatch. The writable-side
    /// methods and properties come from the shared <see cref="WritableCore"/> dispatch;
    /// <c>destroyed</c> intentionally falls through to the Readable base.
    /// </summary>
    public override object? GetMember(string name)
    {
        if (_writeCore.GetWritableMember(name) is { } writableMember)
            return writableMember;

        return name switch
        {
            // Override destroy to handle both sides
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, DestroyDuplex),

            // Inherit Readable methods and properties
            _ => base.GetMember(name)
        };
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
