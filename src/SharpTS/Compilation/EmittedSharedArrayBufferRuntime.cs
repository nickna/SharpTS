using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// SharedArrayBuffer metadata for one compilation. Declarations support forward references;
/// completion validates and freezes the handles after runtime finalization.
/// </summary>
public sealed class EmittedSharedArrayBufferRuntime
{
    internal EmittedSharedArrayBufferRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => Set(ref _ctor, value);
    }

    private MethodBuilder? _byteLengthGetter;
    public MethodBuilder ByteLengthGetter
    {
        get => Require(_byteLengthGetter);
        internal set => Set(ref _byteLengthGetter, value);
    }

    private MethodBuilder? _getBuffer;
    public MethodBuilder GetBuffer
    {
        get => Require(_getBuffer);
        internal set => Set(ref _getBuffer, value);
    }

    private MethodBuilder? _slice;
    public MethodBuilder Slice
    {
        get => Require(_slice);
        internal set => Set(ref _slice, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => Set(ref _create, value);
    }

    private MethodBuilder? _getByteLength;
    public MethodBuilder GetByteLength
    {
        get => Require(_getByteLength);
        internal set => Set(ref _getByteLength, value);
    }

    private MethodBuilder? _sliceObject;
    public MethodBuilder SliceObject
    {
        get => Require(_sliceObject);
        internal set => Set(ref _sliceObject, value);
    }

    private FieldBuilder? _bufferField;
    public FieldBuilder BufferField
    {
        get => Require(_bufferField);
        internal set => Set(ref _bufferField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"SharedArrayBuffer metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("SharedArrayBuffer metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = ByteLengthGetter;
        _ = GetBuffer;
        _ = Slice;
        _ = Create;
        _ = GetByteLength;
        _ = SliceObject;
        _ = BufferField;
        IsComplete = true;
    }
}
