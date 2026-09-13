using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// ArrayBuffer metadata for one compilation. Declarations are readable before bodies exist;
/// completion validates and freezes the handles after runtime finalization.
/// </summary>
public sealed class EmittedArrayBufferRuntime
{
    internal EmittedArrayBufferRuntime() { }

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

    private MethodBuilder? _sliceDynamic;
    public MethodBuilder SliceDynamic
    {
        get => Require(_sliceDynamic);
        internal set => Set(ref _sliceDynamic, value);
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

    private MethodBuilder? _isView;
    public MethodBuilder IsView
    {
        get => Require(_isView);
        internal set => Set(ref _isView, value);
    }

    private FieldBuilder? _bufferField;
    public FieldBuilder BufferField
    {
        get => Require(_bufferField);
        internal set => Set(ref _bufferField, value);
    }

    private FieldBuilder? _detachedField;
    public FieldBuilder DetachedField
    {
        get => Require(_detachedField);
        internal set => Set(ref _detachedField, value);
    }

    private MethodBuilder? _detach;
    public MethodBuilder Detach
    {
        get => Require(_detach);
        internal set => Set(ref _detach, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"ArrayBuffer metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("ArrayBuffer metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = ByteLengthGetter;
        _ = GetBuffer;
        _ = Slice;
        _ = SliceDynamic;
        _ = Create;
        _ = GetByteLength;
        _ = SliceObject;
        _ = IsView;
        _ = BufferField;
        _ = DetachedField;
        _ = Detach;
        IsComplete = true;
    }
}
