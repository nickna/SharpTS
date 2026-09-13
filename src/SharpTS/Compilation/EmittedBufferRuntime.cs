using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Node Buffer metadata. Declarations are readable before bodies are emitted; completion
/// validates and freezes them after runtime finalization. Typed-array copy has its own feature gate.
/// </summary>
public sealed class EmittedBufferRuntime
{
    internal EmittedBufferRuntime(bool hasTypedArrayCopy) => HasTypedArrayCopy = hasTypedArrayCopy;

    public bool HasTypedArrayCopy { get; }
    public bool IsComplete { get; private set; }

    private Type? _type;
    public Type Type
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

    private ConstructorBuilder? _ctorSize;
    public ConstructorBuilder CtorSize
    {
        get => Require(_ctorSize);
        internal set => Set(ref _ctorSize, value);
    }

    private MethodBuilder? _lengthGetter;
    public MethodBuilder LengthGetter
    {
        get => Require(_lengthGetter);
        internal set => Set(ref _lengthGetter, value);
    }

    private MethodBuilder? _fromString;
    public MethodBuilder FromString
    {
        get => Require(_fromString);
        internal set => Set(ref _fromString, value);
    }

    private MethodBuilder? _fromArray;
    public MethodBuilder FromArray
    {
        get => Require(_fromArray);
        internal set => Set(ref _fromArray, value);
    }

    private MethodBuilder? _fromBuffer;
    public MethodBuilder FromBuffer
    {
        get => Require(_fromBuffer);
        internal set => Set(ref _fromBuffer, value);
    }

    private MethodBuilder? _alloc;
    public MethodBuilder Alloc
    {
        get => Require(_alloc);
        internal set => Set(ref _alloc, value);
    }

    private MethodBuilder? _allocUnsafe;
    public MethodBuilder AllocUnsafe
    {
        get => Require(_allocUnsafe);
        internal set => Set(ref _allocUnsafe, value);
    }

    private MethodBuilder? _concat;
    public MethodBuilder Concat
    {
        get => Require(_concat);
        internal set => Set(ref _concat, value);
    }

    private MethodBuilder? _calculateTotalLength;
    public MethodBuilder CalculateTotalLength
    {
        get => Require(_calculateTotalLength);
        internal set => Set(ref _calculateTotalLength, value);
    }

    private MethodBuilder? _isBuffer;
    public MethodBuilder IsBuffer
    {
        get => Require(_isBuffer);
        internal set => Set(ref _isBuffer, value);
    }

    private MethodBuilder? _toString;
    public MethodBuilder ToStringMethod
    {
        get => Require(_toString);
        internal set => Set(ref _toString, value);
    }

    private MethodBuilder? _slice;
    public MethodBuilder Slice
    {
        get => Require(_slice);
        internal set => Set(ref _slice, value);
    }

    private MethodBuilder? _getData;
    public MethodBuilder GetData
    {
        get => Require(_getData);
        internal set => Set(ref _getData, value);
    }

    private MethodBuilder? _atob;
    public MethodBuilder Atob
    {
        get => Require(_atob);
        internal set => Set(ref _atob, value);
    }

    private MethodBuilder? _btoa;
    public MethodBuilder Btoa
    {
        get => Require(_btoa);
        internal set => Set(ref _btoa, value);
    }

    private MethodBuilder? _isUtf8;
    public MethodBuilder IsUtf8
    {
        get => Require(_isUtf8);
        internal set => Set(ref _isUtf8, value);
    }

    private MethodBuilder? _isAscii;
    public MethodBuilder IsAscii
    {
        get => Require(_isAscii);
        internal set => Set(ref _isAscii, value);
    }

    private MethodBuilder? _transcode;
    public MethodBuilder Transcode
    {
        get => Require(_transcode);
        internal set => Set(ref _transcode, value);
    }

    private MethodBuilder? _slowBuffer;
    public MethodBuilder SlowBuffer
    {
        get => Require(_slowBuffer);
        internal set => Set(ref _slowBuffer, value);
    }

    private MethodBuilder? _moduleConstants;
    public MethodBuilder ModuleConstants
    {
        get => Require(_moduleConstants);
        internal set => Set(ref _moduleConstants, value);
    }

    private MethodBuilder? _copyBytesFrom;
    public MethodBuilder CopyBytesFrom
    {
        get
        {
            RequireTypedArrayCopy();
            return Require(_copyBytesFrom);
        }
        internal set
        {
            EnsureMutable();
            RequireTypedArrayCopy();
            Set(ref _copyBytesFrom, value);
        }
    }

    private MethodBuilder? _copy;
    public MethodBuilder Copy
    {
        get => Require(_copy);
        internal set => Set(ref _copy, value);
    }

    private MethodBuilder? _compare;
    public MethodBuilder Compare
    {
        get => Require(_compare);
        internal set => Set(ref _compare, value);
    }

    private MethodBuilder? _equals;
    public MethodBuilder EqualsMethod
    {
        get => Require(_equals);
        internal set => Set(ref _equals, value);
    }

    private MethodBuilder? _fill;
    public MethodBuilder Fill
    {
        get => Require(_fill);
        internal set => Set(ref _fill, value);
    }

    private MethodBuilder? _write;
    public MethodBuilder Write
    {
        get => Require(_write);
        internal set => Set(ref _write, value);
    }

    private MethodBuilder? _readUInt8;
    public MethodBuilder ReadUInt8
    {
        get => Require(_readUInt8);
        internal set => Set(ref _readUInt8, value);
    }

    private MethodBuilder? _writeUInt8;
    public MethodBuilder WriteUInt8
    {
        get => Require(_writeUInt8);
        internal set => Set(ref _writeUInt8, value);
    }

    private MethodBuilder? _toJSON;
    public MethodBuilder ToJSON
    {
        get => Require(_toJSON);
        internal set => Set(ref _toJSON, value);
    }

    private MethodBuilder? _readInt8;
    public MethodBuilder ReadInt8
    {
        get => Require(_readInt8);
        internal set => Set(ref _readInt8, value);
    }

    private MethodBuilder? _readUInt16LE;
    public MethodBuilder ReadUInt16LE
    {
        get => Require(_readUInt16LE);
        internal set => Set(ref _readUInt16LE, value);
    }

    private MethodBuilder? _readUIntLE;
    public MethodBuilder ReadUIntLE
    {
        get => Require(_readUIntLE);
        internal set => Set(ref _readUIntLE, value);
    }

    private MethodBuilder? _readUIntBE;
    public MethodBuilder ReadUIntBE
    {
        get => Require(_readUIntBE);
        internal set => Set(ref _readUIntBE, value);
    }

    private MethodBuilder? _readIntLE;
    public MethodBuilder ReadIntLE
    {
        get => Require(_readIntLE);
        internal set => Set(ref _readIntLE, value);
    }

    private MethodBuilder? _readIntBE;
    public MethodBuilder ReadIntBE
    {
        get => Require(_readIntBE);
        internal set => Set(ref _readIntBE, value);
    }

    private MethodBuilder? _writeUIntLE;
    public MethodBuilder WriteUIntLE
    {
        get => Require(_writeUIntLE);
        internal set => Set(ref _writeUIntLE, value);
    }

    private MethodBuilder? _writeUIntBE;
    public MethodBuilder WriteUIntBE
    {
        get => Require(_writeUIntBE);
        internal set => Set(ref _writeUIntBE, value);
    }

    private MethodBuilder? _readUInt16BE;
    public MethodBuilder ReadUInt16BE
    {
        get => Require(_readUInt16BE);
        internal set => Set(ref _readUInt16BE, value);
    }

    private MethodBuilder? _readUInt32LE;
    public MethodBuilder ReadUInt32LE
    {
        get => Require(_readUInt32LE);
        internal set => Set(ref _readUInt32LE, value);
    }

    private MethodBuilder? _readUInt32BE;
    public MethodBuilder ReadUInt32BE
    {
        get => Require(_readUInt32BE);
        internal set => Set(ref _readUInt32BE, value);
    }

    private MethodBuilder? _readInt16LE;
    public MethodBuilder ReadInt16LE
    {
        get => Require(_readInt16LE);
        internal set => Set(ref _readInt16LE, value);
    }

    private MethodBuilder? _readInt16BE;
    public MethodBuilder ReadInt16BE
    {
        get => Require(_readInt16BE);
        internal set => Set(ref _readInt16BE, value);
    }

    private MethodBuilder? _readInt32LE;
    public MethodBuilder ReadInt32LE
    {
        get => Require(_readInt32LE);
        internal set => Set(ref _readInt32LE, value);
    }

    private MethodBuilder? _readInt32BE;
    public MethodBuilder ReadInt32BE
    {
        get => Require(_readInt32BE);
        internal set => Set(ref _readInt32BE, value);
    }

    private MethodBuilder? _readFloatLE;
    public MethodBuilder ReadFloatLE
    {
        get => Require(_readFloatLE);
        internal set => Set(ref _readFloatLE, value);
    }

    private MethodBuilder? _readFloatBE;
    public MethodBuilder ReadFloatBE
    {
        get => Require(_readFloatBE);
        internal set => Set(ref _readFloatBE, value);
    }

    private MethodBuilder? _readDoubleLE;
    public MethodBuilder ReadDoubleLE
    {
        get => Require(_readDoubleLE);
        internal set => Set(ref _readDoubleLE, value);
    }

    private MethodBuilder? _readDoubleBE;
    public MethodBuilder ReadDoubleBE
    {
        get => Require(_readDoubleBE);
        internal set => Set(ref _readDoubleBE, value);
    }

    private MethodBuilder? _readBigInt64LE;
    public MethodBuilder ReadBigInt64LE
    {
        get => Require(_readBigInt64LE);
        internal set => Set(ref _readBigInt64LE, value);
    }

    private MethodBuilder? _readBigInt64BE;
    public MethodBuilder ReadBigInt64BE
    {
        get => Require(_readBigInt64BE);
        internal set => Set(ref _readBigInt64BE, value);
    }

    private MethodBuilder? _readBigUInt64LE;
    public MethodBuilder ReadBigUInt64LE
    {
        get => Require(_readBigUInt64LE);
        internal set => Set(ref _readBigUInt64LE, value);
    }

    private MethodBuilder? _readBigUInt64BE;
    public MethodBuilder ReadBigUInt64BE
    {
        get => Require(_readBigUInt64BE);
        internal set => Set(ref _readBigUInt64BE, value);
    }

    private MethodBuilder? _writeInt8;
    public MethodBuilder WriteInt8
    {
        get => Require(_writeInt8);
        internal set => Set(ref _writeInt8, value);
    }

    private MethodBuilder? _writeUInt16LE;
    public MethodBuilder WriteUInt16LE
    {
        get => Require(_writeUInt16LE);
        internal set => Set(ref _writeUInt16LE, value);
    }

    private MethodBuilder? _writeUInt16BE;
    public MethodBuilder WriteUInt16BE
    {
        get => Require(_writeUInt16BE);
        internal set => Set(ref _writeUInt16BE, value);
    }

    private MethodBuilder? _writeUInt32LE;
    public MethodBuilder WriteUInt32LE
    {
        get => Require(_writeUInt32LE);
        internal set => Set(ref _writeUInt32LE, value);
    }

    private MethodBuilder? _writeUInt32BE;
    public MethodBuilder WriteUInt32BE
    {
        get => Require(_writeUInt32BE);
        internal set => Set(ref _writeUInt32BE, value);
    }

    private MethodBuilder? _writeInt16LE;
    public MethodBuilder WriteInt16LE
    {
        get => Require(_writeInt16LE);
        internal set => Set(ref _writeInt16LE, value);
    }

    private MethodBuilder? _writeInt16BE;
    public MethodBuilder WriteInt16BE
    {
        get => Require(_writeInt16BE);
        internal set => Set(ref _writeInt16BE, value);
    }

    private MethodBuilder? _writeInt32LE;
    public MethodBuilder WriteInt32LE
    {
        get => Require(_writeInt32LE);
        internal set => Set(ref _writeInt32LE, value);
    }

    private MethodBuilder? _writeInt32BE;
    public MethodBuilder WriteInt32BE
    {
        get => Require(_writeInt32BE);
        internal set => Set(ref _writeInt32BE, value);
    }

    private MethodBuilder? _writeFloatLE;
    public MethodBuilder WriteFloatLE
    {
        get => Require(_writeFloatLE);
        internal set => Set(ref _writeFloatLE, value);
    }

    private MethodBuilder? _writeFloatBE;
    public MethodBuilder WriteFloatBE
    {
        get => Require(_writeFloatBE);
        internal set => Set(ref _writeFloatBE, value);
    }

    private MethodBuilder? _writeDoubleLE;
    public MethodBuilder WriteDoubleLE
    {
        get => Require(_writeDoubleLE);
        internal set => Set(ref _writeDoubleLE, value);
    }

    private MethodBuilder? _writeDoubleBE;
    public MethodBuilder WriteDoubleBE
    {
        get => Require(_writeDoubleBE);
        internal set => Set(ref _writeDoubleBE, value);
    }

    private MethodBuilder? _writeBigInt64LE;
    public MethodBuilder WriteBigInt64LE
    {
        get => Require(_writeBigInt64LE);
        internal set => Set(ref _writeBigInt64LE, value);
    }

    private MethodBuilder? _writeBigInt64BE;
    public MethodBuilder WriteBigInt64BE
    {
        get => Require(_writeBigInt64BE);
        internal set => Set(ref _writeBigInt64BE, value);
    }

    private MethodBuilder? _writeBigUInt64LE;
    public MethodBuilder WriteBigUInt64LE
    {
        get => Require(_writeBigUInt64LE);
        internal set => Set(ref _writeBigUInt64LE, value);
    }

    private MethodBuilder? _writeBigUInt64BE;
    public MethodBuilder WriteBigUInt64BE
    {
        get => Require(_writeBigUInt64BE);
        internal set => Set(ref _writeBigUInt64BE, value);
    }

    private MethodBuilder? _indexOf;
    public MethodBuilder IndexOf
    {
        get => Require(_indexOf);
        internal set => Set(ref _indexOf, value);
    }

    private MethodBuilder? _includes;
    public MethodBuilder Includes
    {
        get => Require(_includes);
        internal set => Set(ref _includes, value);
    }

    private MethodBuilder? _swap16;
    public MethodBuilder Swap16
    {
        get => Require(_swap16);
        internal set => Set(ref _swap16, value);
    }

    private MethodBuilder? _swap32;
    public MethodBuilder Swap32
    {
        get => Require(_swap32);
        internal set => Set(ref _swap32, value);
    }

    private MethodBuilder? _swap64;
    public MethodBuilder Swap64
    {
        get => Require(_swap64);
        internal set => Set(ref _swap64, value);
    }

    private FieldBuilder? _dataField;
    public FieldBuilder DataField
    {
        get => Require(_dataField);
        internal set => Set(ref _dataField, value);
    }

    private MethodBuilder? _coerceString;
    public MethodBuilder CoerceString
    {
        get => Require(_coerceString);
        internal set => Set(ref _coerceString, value);
    }

    private MethodBuilder? _bytesOf;
    public MethodBuilder BytesOf
    {
        get => Require(_bytesOf);
        internal set => Set(ref _bytesOf, value);
    }

    private void RequireTypedArrayCopy()
    {
        if (!HasTypedArrayCopy)
            throw new InvalidOperationException("Buffer typed-array copy was not enabled for this compilation.");
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Buffer metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Buffer metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = CtorSize;
        _ = LengthGetter;
        _ = FromString;
        _ = FromArray;
        _ = FromBuffer;
        _ = Alloc;
        _ = AllocUnsafe;
        _ = Concat;
        _ = CalculateTotalLength;
        _ = IsBuffer;
        _ = ToStringMethod;
        _ = Slice;
        _ = GetData;
        _ = Atob;
        _ = Btoa;
        _ = IsUtf8;
        _ = IsAscii;
        _ = Transcode;
        _ = SlowBuffer;
        _ = ModuleConstants;
        if (HasTypedArrayCopy)
            _ = CopyBytesFrom;
        _ = Copy;
        _ = Compare;
        _ = EqualsMethod;
        _ = Fill;
        _ = Write;
        _ = ReadUInt8;
        _ = WriteUInt8;
        _ = ToJSON;
        _ = ReadInt8;
        _ = ReadUInt16LE;
        _ = ReadUIntLE;
        _ = ReadUIntBE;
        _ = ReadIntLE;
        _ = ReadIntBE;
        _ = WriteUIntLE;
        _ = WriteUIntBE;
        _ = ReadUInt16BE;
        _ = ReadUInt32LE;
        _ = ReadUInt32BE;
        _ = ReadInt16LE;
        _ = ReadInt16BE;
        _ = ReadInt32LE;
        _ = ReadInt32BE;
        _ = ReadFloatLE;
        _ = ReadFloatBE;
        _ = ReadDoubleLE;
        _ = ReadDoubleBE;
        _ = ReadBigInt64LE;
        _ = ReadBigInt64BE;
        _ = ReadBigUInt64LE;
        _ = ReadBigUInt64BE;
        _ = WriteInt8;
        _ = WriteUInt16LE;
        _ = WriteUInt16BE;
        _ = WriteUInt32LE;
        _ = WriteUInt32BE;
        _ = WriteInt16LE;
        _ = WriteInt16BE;
        _ = WriteInt32LE;
        _ = WriteInt32BE;
        _ = WriteFloatLE;
        _ = WriteFloatBE;
        _ = WriteDoubleLE;
        _ = WriteDoubleBE;
        _ = WriteBigInt64LE;
        _ = WriteBigInt64BE;
        _ = WriteBigUInt64LE;
        _ = WriteBigUInt64BE;
        _ = IndexOf;
        _ = Includes;
        _ = Swap16;
        _ = Swap32;
        _ = Swap64;
        _ = DataField;
        _ = CoerceString;
        _ = BytesOf;
        IsComplete = true;
    }
}
