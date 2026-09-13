using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// DataView metadata for one compilation. Declarations support forward references;
/// completion validates and freezes the handles after runtime finalization.
/// </summary>
public sealed class EmittedDataViewRuntime
{
    internal EmittedDataViewRuntime() { }

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

    private MethodBuilder? _byteOffsetGetter;
    public MethodBuilder ByteOffsetGetter
    {
        get => Require(_byteOffsetGetter);
        internal set => Set(ref _byteOffsetGetter, value);
    }

    private MethodBuilder? _bufferGetter;
    public MethodBuilder BufferGetter
    {
        get => Require(_bufferGetter);
        internal set => Set(ref _bufferGetter, value);
    }

    private MethodBuilder? _getInt8;
    public MethodBuilder GetInt8
    {
        get => Require(_getInt8);
        internal set => Set(ref _getInt8, value);
    }

    private MethodBuilder? _getUint8;
    public MethodBuilder GetUint8
    {
        get => Require(_getUint8);
        internal set => Set(ref _getUint8, value);
    }

    private MethodBuilder? _getInt16;
    public MethodBuilder GetInt16
    {
        get => Require(_getInt16);
        internal set => Set(ref _getInt16, value);
    }

    private MethodBuilder? _getUint16;
    public MethodBuilder GetUint16
    {
        get => Require(_getUint16);
        internal set => Set(ref _getUint16, value);
    }

    private MethodBuilder? _getInt32;
    public MethodBuilder GetInt32
    {
        get => Require(_getInt32);
        internal set => Set(ref _getInt32, value);
    }

    private MethodBuilder? _getUint32;
    public MethodBuilder GetUint32
    {
        get => Require(_getUint32);
        internal set => Set(ref _getUint32, value);
    }

    private MethodBuilder? _getFloat32;
    public MethodBuilder GetFloat32
    {
        get => Require(_getFloat32);
        internal set => Set(ref _getFloat32, value);
    }

    private MethodBuilder? _getFloat64;
    public MethodBuilder GetFloat64
    {
        get => Require(_getFloat64);
        internal set => Set(ref _getFloat64, value);
    }

    private MethodBuilder? _setInt8;
    public MethodBuilder SetInt8
    {
        get => Require(_setInt8);
        internal set => Set(ref _setInt8, value);
    }

    private MethodBuilder? _setUint8;
    public MethodBuilder SetUint8
    {
        get => Require(_setUint8);
        internal set => Set(ref _setUint8, value);
    }

    private MethodBuilder? _setInt16;
    public MethodBuilder SetInt16
    {
        get => Require(_setInt16);
        internal set => Set(ref _setInt16, value);
    }

    private MethodBuilder? _setUint16;
    public MethodBuilder SetUint16
    {
        get => Require(_setUint16);
        internal set => Set(ref _setUint16, value);
    }

    private MethodBuilder? _setInt32;
    public MethodBuilder SetInt32
    {
        get => Require(_setInt32);
        internal set => Set(ref _setInt32, value);
    }

    private MethodBuilder? _setUint32;
    public MethodBuilder SetUint32
    {
        get => Require(_setUint32);
        internal set => Set(ref _setUint32, value);
    }

    private MethodBuilder? _setFloat32;
    public MethodBuilder SetFloat32
    {
        get => Require(_setFloat32);
        internal set => Set(ref _setFloat32, value);
    }

    private MethodBuilder? _setFloat64;
    public MethodBuilder SetFloat64
    {
        get => Require(_setFloat64);
        internal set => Set(ref _setFloat64, value);
    }

    private MethodBuilder? _getBigInt64;
    public MethodBuilder GetBigInt64
    {
        get => Require(_getBigInt64);
        internal set => Set(ref _getBigInt64, value);
    }

    private MethodBuilder? _getBigUint64;
    public MethodBuilder GetBigUint64
    {
        get => Require(_getBigUint64);
        internal set => Set(ref _getBigUint64, value);
    }

    private MethodBuilder? _setBigInt64;
    public MethodBuilder SetBigInt64
    {
        get => Require(_setBigInt64);
        internal set => Set(ref _setBigInt64, value);
    }

    private MethodBuilder? _setBigUint64;
    public MethodBuilder SetBigUint64
    {
        get => Require(_setBigUint64);
        internal set => Set(ref _setBigUint64, value);
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

    private MethodBuilder? _getByteOffset;
    public MethodBuilder GetByteOffset
    {
        get => Require(_getByteOffset);
        internal set => Set(ref _getByteOffset, value);
    }

    private MethodBuilder? _getBuffer;
    public MethodBuilder GetBuffer
    {
        get => Require(_getBuffer);
        internal set => Set(ref _getBuffer, value);
    }

    private MethodBuilder? _getInt8Object;
    public MethodBuilder GetInt8Object
    {
        get => Require(_getInt8Object);
        internal set => Set(ref _getInt8Object, value);
    }

    private MethodBuilder? _getUint8Object;
    public MethodBuilder GetUint8Object
    {
        get => Require(_getUint8Object);
        internal set => Set(ref _getUint8Object, value);
    }

    private MethodBuilder? _getInt16Object;
    public MethodBuilder GetInt16Object
    {
        get => Require(_getInt16Object);
        internal set => Set(ref _getInt16Object, value);
    }

    private MethodBuilder? _getUint16Object;
    public MethodBuilder GetUint16Object
    {
        get => Require(_getUint16Object);
        internal set => Set(ref _getUint16Object, value);
    }

    private MethodBuilder? _getInt32Object;
    public MethodBuilder GetInt32Object
    {
        get => Require(_getInt32Object);
        internal set => Set(ref _getInt32Object, value);
    }

    private MethodBuilder? _getUint32Object;
    public MethodBuilder GetUint32Object
    {
        get => Require(_getUint32Object);
        internal set => Set(ref _getUint32Object, value);
    }

    private MethodBuilder? _getFloat32Object;
    public MethodBuilder GetFloat32Object
    {
        get => Require(_getFloat32Object);
        internal set => Set(ref _getFloat32Object, value);
    }

    private MethodBuilder? _getFloat64Object;
    public MethodBuilder GetFloat64Object
    {
        get => Require(_getFloat64Object);
        internal set => Set(ref _getFloat64Object, value);
    }

    private MethodBuilder? _getBigInt64Object;
    public MethodBuilder GetBigInt64Object
    {
        get => Require(_getBigInt64Object);
        internal set => Set(ref _getBigInt64Object, value);
    }

    private MethodBuilder? _getBigUint64Object;
    public MethodBuilder GetBigUint64Object
    {
        get => Require(_getBigUint64Object);
        internal set => Set(ref _getBigUint64Object, value);
    }

    private MethodBuilder? _setInt8Object;
    public MethodBuilder SetInt8Object
    {
        get => Require(_setInt8Object);
        internal set => Set(ref _setInt8Object, value);
    }

    private MethodBuilder? _setUint8Object;
    public MethodBuilder SetUint8Object
    {
        get => Require(_setUint8Object);
        internal set => Set(ref _setUint8Object, value);
    }

    private MethodBuilder? _setInt16Object;
    public MethodBuilder SetInt16Object
    {
        get => Require(_setInt16Object);
        internal set => Set(ref _setInt16Object, value);
    }

    private MethodBuilder? _setUint16Object;
    public MethodBuilder SetUint16Object
    {
        get => Require(_setUint16Object);
        internal set => Set(ref _setUint16Object, value);
    }

    private MethodBuilder? _setInt32Object;
    public MethodBuilder SetInt32Object
    {
        get => Require(_setInt32Object);
        internal set => Set(ref _setInt32Object, value);
    }

    private MethodBuilder? _setUint32Object;
    public MethodBuilder SetUint32Object
    {
        get => Require(_setUint32Object);
        internal set => Set(ref _setUint32Object, value);
    }

    private MethodBuilder? _setFloat32Object;
    public MethodBuilder SetFloat32Object
    {
        get => Require(_setFloat32Object);
        internal set => Set(ref _setFloat32Object, value);
    }

    private MethodBuilder? _setFloat64Object;
    public MethodBuilder SetFloat64Object
    {
        get => Require(_setFloat64Object);
        internal set => Set(ref _setFloat64Object, value);
    }

    private MethodBuilder? _setBigInt64Object;
    public MethodBuilder SetBigInt64Object
    {
        get => Require(_setBigInt64Object);
        internal set => Set(ref _setBigInt64Object, value);
    }

    private MethodBuilder? _setBigUint64Object;
    public MethodBuilder SetBigUint64Object
    {
        get => Require(_setBigUint64Object);
        internal set => Set(ref _setBigUint64Object, value);
    }

    // The backing byte array can be shared with other views.
    private FieldBuilder? _bufferField;
    public FieldBuilder BufferField
    {
        get => Require(_bufferField);
        internal set => Set(ref _bufferField, value);
    }

    private FieldBuilder? _byteOffsetField;
    public FieldBuilder ByteOffsetField
    {
        get => Require(_byteOffsetField);
        internal set => Set(ref _byteOffsetField, value);
    }

    private FieldBuilder? _byteLengthField;
    public FieldBuilder ByteLengthField
    {
        get => Require(_byteLengthField);
        internal set => Set(ref _byteLengthField, value);
    }

    // Retains the original ArrayBuffer or SharedArrayBuffer object for view.buffer identity.
    private FieldBuilder? _arrayBufferField;
    public FieldBuilder ArrayBufferField
    {
        get => Require(_arrayBufferField);
        internal set => Set(ref _arrayBufferField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"DataView metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("DataView metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = ByteLengthGetter;
        _ = ByteOffsetGetter;
        _ = BufferGetter;
        _ = GetInt8;
        _ = GetUint8;
        _ = GetInt16;
        _ = GetUint16;
        _ = GetInt32;
        _ = GetUint32;
        _ = GetFloat32;
        _ = GetFloat64;
        _ = SetInt8;
        _ = SetUint8;
        _ = SetInt16;
        _ = SetUint16;
        _ = SetInt32;
        _ = SetUint32;
        _ = SetFloat32;
        _ = SetFloat64;
        _ = GetBigInt64;
        _ = GetBigUint64;
        _ = SetBigInt64;
        _ = SetBigUint64;
        _ = Create;
        _ = GetByteLength;
        _ = GetByteOffset;
        _ = GetBuffer;
        _ = GetInt8Object;
        _ = GetUint8Object;
        _ = GetInt16Object;
        _ = GetUint16Object;
        _ = GetInt32Object;
        _ = GetUint32Object;
        _ = GetFloat32Object;
        _ = GetFloat64Object;
        _ = GetBigInt64Object;
        _ = GetBigUint64Object;
        _ = SetInt8Object;
        _ = SetUint8Object;
        _ = SetInt16Object;
        _ = SetUint16Object;
        _ = SetInt32Object;
        _ = SetUint32Object;
        _ = SetFloat32Object;
        _ = SetFloat64Object;
        _ = SetBigInt64Object;
        _ = SetBigUint64Object;
        _ = BufferField;
        _ = ByteOffsetField;
        _ = ByteLengthField;
        _ = ArrayBufferField;
        IsComplete = true;
    }
}
