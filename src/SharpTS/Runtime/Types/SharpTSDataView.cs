using System.Buffers.Binary;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript's DataView.
/// Provides a low-level interface for reading and writing multiple number types
/// in a binary ArrayBuffer, with explicit control over endianness.
/// </summary>
public class SharpTSDataView : ITypeCategorized
{
    private readonly byte[] _buffer;
    private readonly int _byteOffset;
    private readonly int _byteLength;
    private readonly SharpTSArrayBuffer? _arrayBuffer;
    private readonly SharpTSSharedArrayBuffer? _sharedArrayBuffer;

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Buffer;

    /// <summary>
    /// Gets the size of the view in bytes.
    /// </summary>
    public int ByteLength => _byteLength;

    /// <summary>
    /// Gets the offset (in bytes) of this view from the start of its buffer.
    /// </summary>
    public int ByteOffset => _byteOffset;

    /// <summary>
    /// Gets the underlying buffer (ArrayBuffer or SharedArrayBuffer).
    /// </summary>
    public object Buffer => (object?)_arrayBuffer ?? _sharedArrayBuffer!;

    /// <summary>
    /// Creates a new DataView over an ArrayBuffer.
    /// </summary>
    /// <param name="buffer">The ArrayBuffer to view.</param>
    /// <param name="byteOffset">The offset in bytes from the start of the buffer.</param>
    /// <param name="byteLength">The number of bytes to include in the view. Defaults to the remainder of the buffer.</param>
    public SharpTSDataView(SharpTSArrayBuffer buffer, int byteOffset = 0, int? byteLength = null)
    {
        if (byteOffset < 0 || byteOffset > buffer.ByteLength)
        {
            throw new Exception("RangeError: Invalid DataView offset");
        }

        int maxLength = buffer.ByteLength - byteOffset;
        int actualLength = byteLength ?? maxLength;

        if (actualLength < 0 || actualLength > maxLength)
        {
            throw new Exception("RangeError: Invalid DataView length");
        }

        _arrayBuffer = buffer;
        _buffer = buffer.GetBackingArray();
        _byteOffset = byteOffset;
        _byteLength = actualLength;
    }

    /// <summary>
    /// Creates a new DataView over a SharedArrayBuffer.
    /// </summary>
    /// <param name="buffer">The SharedArrayBuffer to view.</param>
    /// <param name="byteOffset">The offset in bytes from the start of the buffer.</param>
    /// <param name="byteLength">The number of bytes to include in the view. Defaults to the remainder of the buffer.</param>
    public SharpTSDataView(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? byteLength = null)
    {
        if (byteOffset < 0 || byteOffset > buffer.ByteLength)
        {
            throw new Exception("RangeError: Invalid DataView offset");
        }

        int maxLength = buffer.ByteLength - byteOffset;
        int actualLength = byteLength ?? maxLength;

        if (actualLength < 0 || actualLength > maxLength)
        {
            throw new Exception("RangeError: Invalid DataView length");
        }

        _sharedArrayBuffer = buffer;
        _buffer = buffer.GetBackingArray();
        _byteOffset = byteOffset;
        _byteLength = actualLength;
    }

    #region Bounds Checking

    private void CheckBounds(int byteOffset, int size)
    {
        if (byteOffset < 0 || byteOffset + size > _byteLength)
        {
            throw new Exception("RangeError: Offset is outside the bounds of the DataView");
        }
    }

    #endregion

    #region Int8/Uint8 Methods (no endianness)

    /// <summary>
    /// Gets a signed 8-bit integer at the specified byte offset.
    /// </summary>
    public double GetInt8(int byteOffset)
    {
        CheckBounds(byteOffset, 1);
        return (sbyte)_buffer[_byteOffset + byteOffset];
    }

    /// <summary>
    /// Gets an unsigned 8-bit integer at the specified byte offset.
    /// </summary>
    public double GetUint8(int byteOffset)
    {
        CheckBounds(byteOffset, 1);
        return _buffer[_byteOffset + byteOffset];
    }

    /// <summary>
    /// Sets a signed 8-bit integer at the specified byte offset.
    /// </summary>
    public void SetInt8(int byteOffset, object? value)
    {
        CheckBounds(byteOffset, 1);
        _buffer[_byteOffset + byteOffset] = (byte)(sbyte)ToInt32(value);
    }

    /// <summary>
    /// Sets an unsigned 8-bit integer at the specified byte offset.
    /// </summary>
    public void SetUint8(int byteOffset, object? value)
    {
        CheckBounds(byteOffset, 1);
        _buffer[_byteOffset + byteOffset] = (byte)ToInt32(value);
    }

    #endregion

    #region Int16/Uint16 Methods

    /// <summary>
    /// Gets a signed 16-bit integer at the specified byte offset.
    /// </summary>
    /// <param name="byteOffset">The offset in bytes from the start of the view.</param>
    /// <param name="littleEndian">If true, read as little-endian; otherwise big-endian.</param>
    public double GetInt16(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadInt16LittleEndian(span)
            : BinaryPrimitives.ReadInt16BigEndian(span);
    }

    /// <summary>
    /// Gets an unsigned 16-bit integer at the specified byte offset.
    /// </summary>
    public double GetUint16(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(span)
            : BinaryPrimitives.ReadUInt16BigEndian(span);
    }

    /// <summary>
    /// Sets a signed 16-bit integer at the specified byte offset.
    /// </summary>
    public void SetInt16(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 2);
        short val = (short)ToInt32(value);
        if (littleEndian)
            BinaryPrimitives.WriteInt16LittleEndian(span, val);
        else
            BinaryPrimitives.WriteInt16BigEndian(span, val);
    }

    /// <summary>
    /// Sets an unsigned 16-bit integer at the specified byte offset.
    /// </summary>
    public void SetUint16(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 2);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 2);
        ushort val = (ushort)ToInt32(value);
        if (littleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(span, val);
        else
            BinaryPrimitives.WriteUInt16BigEndian(span, val);
    }

    #endregion

    #region Int32/Uint32 Methods

    /// <summary>
    /// Gets a signed 32-bit integer at the specified byte offset.
    /// </summary>
    public double GetInt32(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(span)
            : BinaryPrimitives.ReadInt32BigEndian(span);
    }

    /// <summary>
    /// Gets an unsigned 32-bit integer at the specified byte offset.
    /// </summary>
    public double GetUint32(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(span)
            : BinaryPrimitives.ReadUInt32BigEndian(span);
    }

    /// <summary>
    /// Sets a signed 32-bit integer at the specified byte offset.
    /// </summary>
    public void SetInt32(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 4);
        int val = ToInt32(value);
        if (littleEndian)
            BinaryPrimitives.WriteInt32LittleEndian(span, val);
        else
            BinaryPrimitives.WriteInt32BigEndian(span, val);
    }

    /// <summary>
    /// Sets an unsigned 32-bit integer at the specified byte offset.
    /// </summary>
    public void SetUint32(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 4);
        uint val = (uint)ToInt64(value);
        if (littleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(span, val);
        else
            BinaryPrimitives.WriteUInt32BigEndian(span, val);
    }

    #endregion

    #region Float32/Float64 Methods

    /// <summary>
    /// Gets a 32-bit float at the specified byte offset.
    /// </summary>
    public double GetFloat32(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadSingleLittleEndian(span)
            : BinaryPrimitives.ReadSingleBigEndian(span);
    }

    /// <summary>
    /// Gets a 64-bit float at the specified byte offset.
    /// </summary>
    public double GetFloat64(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    /// <summary>
    /// Sets a 32-bit float at the specified byte offset.
    /// </summary>
    public void SetFloat32(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 4);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 4);
        float val = (float)Interp.ToNumber(value);
        if (littleEndian)
            BinaryPrimitives.WriteSingleLittleEndian(span, val);
        else
            BinaryPrimitives.WriteSingleBigEndian(span, val);
    }

    /// <summary>
    /// Sets a 64-bit float at the specified byte offset.
    /// </summary>
    public void SetFloat64(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 8);
        double val = Interp.ToNumber(value);
        if (littleEndian)
            BinaryPrimitives.WriteDoubleLittleEndian(span, val);
        else
            BinaryPrimitives.WriteDoubleBigEndian(span, val);
    }

    #endregion

    #region BigInt64/BigUint64 Methods

    /// <summary>
    /// Gets a signed 64-bit BigInt at the specified byte offset.
    /// </summary>
    public System.Numerics.BigInteger GetBigInt64(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 8);
        long value = littleEndian
            ? BinaryPrimitives.ReadInt64LittleEndian(span)
            : BinaryPrimitives.ReadInt64BigEndian(span);
        return new System.Numerics.BigInteger(value);
    }

    /// <summary>
    /// Gets an unsigned 64-bit BigInt at the specified byte offset.
    /// </summary>
    public System.Numerics.BigInteger GetBigUint64(int byteOffset, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 8);
        ulong value = littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(span)
            : BinaryPrimitives.ReadUInt64BigEndian(span);
        return new System.Numerics.BigInteger(value);
    }

    /// <summary>
    /// Sets a signed 64-bit BigInt at the specified byte offset.
    /// </summary>
    public void SetBigInt64(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 8);
        long val = ToBigInt64(value);
        if (littleEndian)
            BinaryPrimitives.WriteInt64LittleEndian(span, val);
        else
            BinaryPrimitives.WriteInt64BigEndian(span, val);
    }

    /// <summary>
    /// Sets an unsigned 64-bit BigInt at the specified byte offset.
    /// </summary>
    public void SetBigUint64(int byteOffset, object? value, bool littleEndian = false)
    {
        CheckBounds(byteOffset, 8);
        var span = _buffer.AsSpan(_byteOffset + byteOffset, 8);
        ulong val = ToBigUint64(value);
        if (littleEndian)
            BinaryPrimitives.WriteUInt64LittleEndian(span, val);
        else
            BinaryPrimitives.WriteUInt64BigEndian(span, val);
    }

    #endregion

    #region Helper Methods

    private static int ToInt32(object? value)
    {
        return value switch
        {
            double d => (int)d,
            int i => i,
            long l => (int)l,
            float f => (int)f,
            System.Numerics.BigInteger bi => (int)bi,
            _ => 0
        };
    }

    private static long ToInt64(object? value)
    {
        return value switch
        {
            double d => (long)d,
            int i => i,
            long l => l,
            float f => (long)f,
            System.Numerics.BigInteger bi => (long)bi,
            _ => 0
        };
    }

    private static long ToBigInt64(object? value)
    {
        if (value == null)
            throw new Exception("TypeError: Cannot convert argument to BigInt");

        return value switch
        {
            SharpTSBigInt bi => (long)bi.Value,
            System.Numerics.BigInteger raw => (long)raw,
            double d => (long)d,
            int i => i,
            long l => l,
            _ => throw new Exception("TypeError: Cannot convert argument to BigInt")
        };
    }

    private static ulong ToBigUint64(object? value)
    {
        if (value == null)
            throw new Exception("TypeError: Cannot convert argument to BigInt");

        return value switch
        {
            SharpTSBigInt bi => (ulong)bi.Value,
            System.Numerics.BigInteger raw => (ulong)raw,
            double d => (ulong)d,
            int i => (ulong)i,
            long l => (ulong)l,
            _ => throw new Exception("TypeError: Cannot convert argument to BigInt")
        };
    }

    #endregion

    public override string ToString() => $"DataView {{ byteLength: {ByteLength}, byteOffset: {ByteOffset} }}";
}
