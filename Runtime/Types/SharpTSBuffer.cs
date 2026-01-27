using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Buffer object for binary data handling.
/// </summary>
/// <remarks>
/// Provides the Node.js Buffer API for working with binary data:
/// - Buffer.from(data, encoding?) - create from string, array, or buffer
/// - Buffer.alloc(size, fill?) - create zero-filled buffer
/// - Buffer.allocUnsafe(size) - create uninitialized buffer
/// - Buffer.concat(buffers, totalLength?) - concatenate buffers
/// - Instance methods: toString, slice, copy, compare, equals, fill, write
/// </remarks>
public class SharpTSBuffer
{
    private readonly byte[] _data;

    /// <summary>
    /// Gets the length of this buffer in bytes.
    /// </summary>
    public int Length => _data.Length;

    /// <summary>
    /// Gets the underlying byte array.
    /// </summary>
    public byte[] Data => _data;

    /// <summary>
    /// Creates a new Buffer wrapping the specified byte array.
    /// </summary>
    /// <param name="data">The byte array to wrap.</param>
    public SharpTSBuffer(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Creates a new Buffer of the specified size.
    /// </summary>
    /// <param name="size">The size in bytes.</param>
    /// <param name="zero">Whether to zero-fill the buffer (default: true).</param>
    public SharpTSBuffer(int size, bool zero = true)
    {
        if (size < 0)
            throw new ArgumentException("Buffer size cannot be negative", nameof(size));

        _data = new byte[size];
        if (zero)
        {
            Array.Clear(_data, 0, size);
        }
    }

    #region Static Factory Methods

    /// <summary>
    /// Creates a Buffer from a string using the specified encoding.
    /// </summary>
    public static SharpTSBuffer FromString(string data, string encoding = "utf8")
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var bytes = EncodeString(data, encoding);
        return new SharpTSBuffer(bytes);
    }

    /// <summary>
    /// Creates a Buffer from an array of numbers.
    /// </summary>
    public static SharpTSBuffer FromArray(List<object?> array)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));

        var bytes = new byte[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            bytes[i] = array[i] switch
            {
                double d => (byte)((int)d & 0xFF),
                int n => (byte)(n & 0xFF),
                long l => (byte)(l & 0xFF),
                _ => 0
            };
        }
        return new SharpTSBuffer(bytes);
    }

    /// <summary>
    /// Creates a Buffer from another Buffer (copy).
    /// </summary>
    public static SharpTSBuffer FromBuffer(SharpTSBuffer buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        var bytes = new byte[buffer.Length];
        Array.Copy(buffer._data, bytes, buffer.Length);
        return new SharpTSBuffer(bytes);
    }

    /// <summary>
    /// Allocates a new Buffer of the specified size, optionally filled with a value.
    /// </summary>
    public static SharpTSBuffer Alloc(int size, object? fill = null, string encoding = "utf8")
    {
        if (size < 0)
            throw new ArgumentException("Buffer size cannot be negative", nameof(size));

        var buffer = new SharpTSBuffer(size, true);

        if (fill != null)
        {
            buffer.FillInternal(fill, 0, size, encoding);
        }

        return buffer;
    }

    /// <summary>
    /// Allocates a new Buffer of the specified size without initializing the memory.
    /// </summary>
    public static SharpTSBuffer AllocUnsafe(int size)
    {
        if (size < 0)
            throw new ArgumentException("Buffer size cannot be negative", nameof(size));

        // In .NET, new byte[size] is always zero-initialized, but we skip the explicit clear
        return new SharpTSBuffer(size, false);
    }

    /// <summary>
    /// Concatenates a list of Buffers into a single Buffer.
    /// </summary>
    public static SharpTSBuffer Concat(List<object?> buffers, int? totalLength = null)
    {
        if (buffers == null || buffers.Count == 0)
            return new SharpTSBuffer(0);

        // Calculate total length if not provided
        int calculatedLength = 0;
        var validBuffers = new List<SharpTSBuffer>();

        foreach (var item in buffers)
        {
            if (item is SharpTSBuffer buf)
            {
                validBuffers.Add(buf);
                calculatedLength += buf.Length;
            }
            else if (item is SharpTSArray arr)
            {
                // Convert array to buffer
                var converted = FromArray(arr.Elements);
                validBuffers.Add(converted);
                calculatedLength += converted.Length;
            }
        }

        int actualLength = totalLength ?? calculatedLength;
        var result = new byte[actualLength];
        int offset = 0;

        foreach (var buf in validBuffers)
        {
            int bytesToCopy = Math.Min(buf.Length, actualLength - offset);
            if (bytesToCopy <= 0) break;

            Array.Copy(buf._data, 0, result, offset, bytesToCopy);
            offset += bytesToCopy;
        }

        return new SharpTSBuffer(result);
    }

    /// <summary>
    /// Checks if the given object is a Buffer.
    /// </summary>
    public static bool IsBuffer(object? obj) => obj is SharpTSBuffer;

    #endregion

    #region Instance Methods

    /// <summary>
    /// Converts the buffer to a string using the specified encoding.
    /// </summary>
    public string ToString(string encoding)
    {
        return DecodeBytes(_data, encoding);
    }

    /// <summary>
    /// Returns a default string representation.
    /// </summary>
    public override string ToString()
    {
        return $"<Buffer {Convert.ToHexString(_data[..Math.Min(50, _data.Length)]).ToLowerInvariant()}{(_data.Length > 50 ? " ... " : "")}>";
    }

    /// <summary>
    /// Returns a new Buffer that references the same memory as the original, but cropped by start and end.
    /// Negative indices count from the end.
    /// </summary>
    public SharpTSBuffer Slice(int start = 0, int? end = null)
    {
        int len = _data.Length;

        // Handle negative indices
        int actualStart = start < 0 ? Math.Max(0, len + start) : Math.Min(start, len);
        int actualEnd = end switch
        {
            null => len,
            < 0 => Math.Max(0, len + end.Value),
            _ => Math.Min(end.Value, len)
        };

        if (actualStart >= actualEnd)
            return new SharpTSBuffer([]);

        var sliced = new byte[actualEnd - actualStart];
        Array.Copy(_data, actualStart, sliced, 0, sliced.Length);
        return new SharpTSBuffer(sliced);
    }

    /// <summary>
    /// Copies data from this buffer to target buffer.
    /// Returns the number of bytes copied.
    /// </summary>
    public int Copy(SharpTSBuffer target, int targetStart = 0, int sourceStart = 0, int? sourceEnd = null)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        int actualSourceEnd = sourceEnd ?? _data.Length;

        // Clamp values
        targetStart = Math.Max(0, Math.Min(targetStart, target.Length));
        sourceStart = Math.Max(0, Math.Min(sourceStart, _data.Length));
        actualSourceEnd = Math.Max(sourceStart, Math.Min(actualSourceEnd, _data.Length));

        int bytesToCopy = Math.Min(actualSourceEnd - sourceStart, target.Length - targetStart);
        if (bytesToCopy <= 0) return 0;

        Array.Copy(_data, sourceStart, target._data, targetStart, bytesToCopy);
        return bytesToCopy;
    }

    /// <summary>
    /// Compares this buffer to another buffer.
    /// Returns 0 if equal, -1 if this < other, 1 if this > other.
    /// </summary>
    public int Compare(SharpTSBuffer other)
    {
        if (other == null)
            throw new ArgumentNullException(nameof(other));

        int minLen = Math.Min(_data.Length, other._data.Length);

        for (int i = 0; i < minLen; i++)
        {
            if (_data[i] < other._data[i]) return -1;
            if (_data[i] > other._data[i]) return 1;
        }

        return _data.Length.CompareTo(other._data.Length);
    }

    /// <summary>
    /// Checks if this buffer equals another buffer.
    /// </summary>
    public bool Equals(SharpTSBuffer other)
    {
        if (other == null) return false;
        if (_data.Length != other._data.Length) return false;

        for (int i = 0; i < _data.Length; i++)
        {
            if (_data[i] != other._data[i]) return false;
        }

        return true;
    }

    /// <summary>
    /// Fills the buffer with the specified value.
    /// Returns this buffer for chaining.
    /// </summary>
    public SharpTSBuffer Fill(object value, int start = 0, int? end = null, string encoding = "utf8")
    {
        int actualEnd = end ?? _data.Length;
        FillInternal(value, start, actualEnd, encoding);
        return this;
    }

    private void FillInternal(object value, int start, int end, string encoding)
    {
        // Clamp values
        start = Math.Max(0, Math.Min(start, _data.Length));
        end = Math.Max(start, Math.Min(end, _data.Length));

        if (start >= end) return;

        byte[] fillBytes;

        if (value is double d)
        {
            fillBytes = [(byte)((int)d & 0xFF)];
        }
        else if (value is int n)
        {
            fillBytes = [(byte)(n & 0xFF)];
        }
        else if (value is string s)
        {
            fillBytes = EncodeString(s, encoding);
            if (fillBytes.Length == 0) return;
        }
        else if (value is SharpTSBuffer buf)
        {
            fillBytes = buf._data;
            if (fillBytes.Length == 0) return;
        }
        else
        {
            fillBytes = [0];
        }

        // Fill the buffer by repeating the fill bytes
        for (int i = start; i < end; i++)
        {
            _data[i] = fillBytes[(i - start) % fillBytes.Length];
        }
    }

    /// <summary>
    /// Writes string to buffer at offset.
    /// Returns the number of bytes written.
    /// </summary>
    public int Write(string data, int offset = 0, int? length = null, string encoding = "utf8")
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var bytes = EncodeString(data, encoding);
        int maxWrite = _data.Length - offset;
        if (maxWrite <= 0) return 0;

        int bytesToWrite = length.HasValue
            ? Math.Min(Math.Min(length.Value, bytes.Length), maxWrite)
            : Math.Min(bytes.Length, maxWrite);

        Array.Copy(bytes, 0, _data, offset, bytesToWrite);
        return bytesToWrite;
    }

    /// <summary>
    /// Reads an unsigned 8-bit integer from the buffer.
    /// </summary>
    public double ReadUInt8(int offset = 0)
    {
        if (offset < 0 || offset >= _data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return _data[offset];
    }

    /// <summary>
    /// Writes an unsigned 8-bit integer to the buffer.
    /// </summary>
    public int WriteUInt8(double value, int offset = 0)
    {
        if (offset < 0 || offset >= _data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _data[offset] = (byte)((int)value & 0xFF);
        return offset + 1;
    }

    /// <summary>
    /// Gets the byte at the specified index.
    /// </summary>
    public double this[int index]
    {
        get
        {
            if (index < 0 || index >= _data.Length)
                return double.NaN; // JavaScript undefined behavior
            return _data[index];
        }
        set
        {
            if (index >= 0 && index < _data.Length)
                _data[index] = (byte)((int)value & 0xFF);
        }
    }

    #endregion

    #region Member Access

    /// <summary>
    /// Gets a member of this buffer object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "length" => (double)Length,

            "toString" => new BuiltInMethod("toString", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() ?? "utf8" : "utf8";
                return ToString(encoding);
            }),

            "slice" => new BuiltInMethod("slice", 0, 2, (interp, recv, args) =>
            {
                int start = args.Count > 0 && args[0] is double s ? (int)s : 0;
                int? end = args.Count > 1 && args[1] is double e ? (int)e : null;
                return Slice(start, end);
            }),

            "copy" => new BuiltInMethod("copy", 1, 4, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not SharpTSBuffer target)
                    throw new Exception("Buffer.copy requires a target buffer");
                int targetStart = args.Count > 1 && args[1] is double ts ? (int)ts : 0;
                int sourceStart = args.Count > 2 && args[2] is double ss ? (int)ss : 0;
                int? sourceEnd = args.Count > 3 && args[3] is double se ? (int)se : null;
                return (double)Copy(target, targetStart, sourceStart, sourceEnd);
            }),

            "compare" => new BuiltInMethod("compare", 1, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not SharpTSBuffer other)
                    throw new Exception("Buffer.compare requires a buffer argument");
                return (double)Compare(other);
            }),

            "equals" => new BuiltInMethod("equals", 1, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not SharpTSBuffer other)
                    return false;
                return Equals(other);
            }),

            "fill" => new BuiltInMethod("fill", 1, 4, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("Buffer.fill requires a value");
                var value = args[0];
                int start = args.Count > 1 && args[1] is double s ? (int)s : 0;
                int? end = args.Count > 2 && args[2] is double e ? (int)e : null;
                string encoding = args.Count > 3 ? args[3]?.ToString() ?? "utf8" : "utf8";
                return Fill(value!, start, end, encoding);
            }),

            "write" => new BuiltInMethod("write", 1, 4, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not string data)
                    throw new Exception("Buffer.write requires a string argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                int? length = args.Count > 2 && args[2] is double l ? (int)l : null;
                string encoding = args.Count > 3 ? args[3]?.ToString() ?? "utf8" : "utf8";
                return (double)Write(data, offset, length, encoding);
            }),

            "readUInt8" => new BuiltInMethod("readUInt8", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadUInt8(offset);
            }),

            "writeUInt8" => new BuiltInMethod("writeUInt8", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeUInt8 requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return (double)WriteUInt8(value, offset);
            }),

            "toJSON" => new BuiltInMethod("toJSON", 0, (interp, recv, args) =>
            {
                var elements = new List<object?>(_data.Length);
                foreach (var b in _data)
                {
                    elements.Add((double)b);
                }
                return new SharpTSObject(new Dictionary<string, object?>
                {
                    ["type"] = "Buffer",
                    ["data"] = new SharpTSArray(elements)
                });
            }),

            // Multi-byte read methods
            "readInt8" => new BuiltInMethod("readInt8", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadInt8(offset);
            }),

            "readUInt16LE" => new BuiltInMethod("readUInt16LE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadUInt16LE(offset);
            }),

            "readUInt16BE" => new BuiltInMethod("readUInt16BE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadUInt16BE(offset);
            }),

            "readUInt32LE" => new BuiltInMethod("readUInt32LE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadUInt32LE(offset);
            }),

            "readUInt32BE" => new BuiltInMethod("readUInt32BE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadUInt32BE(offset);
            }),

            "readInt16LE" => new BuiltInMethod("readInt16LE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadInt16LE(offset);
            }),

            "readInt16BE" => new BuiltInMethod("readInt16BE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadInt16BE(offset);
            }),

            "readInt32LE" => new BuiltInMethod("readInt32LE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadInt32LE(offset);
            }),

            "readInt32BE" => new BuiltInMethod("readInt32BE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadInt32BE(offset);
            }),

            "readFloatLE" => new BuiltInMethod("readFloatLE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadFloatLE(offset);
            }),

            "readFloatBE" => new BuiltInMethod("readFloatBE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadFloatBE(offset);
            }),

            "readDoubleLE" => new BuiltInMethod("readDoubleLE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadDoubleLE(offset);
            }),

            "readDoubleBE" => new BuiltInMethod("readDoubleBE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadDoubleBE(offset);
            }),

            "readBigInt64LE" => new BuiltInMethod("readBigInt64LE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadBigInt64LE(offset);
            }),

            "readBigInt64BE" => new BuiltInMethod("readBigInt64BE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadBigInt64BE(offset);
            }),

            "readBigUInt64LE" => new BuiltInMethod("readBigUInt64LE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadBigUInt64LE(offset);
            }),

            "readBigUInt64BE" => new BuiltInMethod("readBigUInt64BE", 0, 1, (interp, recv, args) =>
            {
                int offset = args.Count > 0 && args[0] is double o ? (int)o : 0;
                return ReadBigUInt64BE(offset);
            }),

            // Multi-byte write methods
            "writeInt8" => new BuiltInMethod("writeInt8", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeInt8 requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteInt8(value, offset);
            }),

            "writeUInt16LE" => new BuiltInMethod("writeUInt16LE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeUInt16LE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteUInt16LE(value, offset);
            }),

            "writeUInt16BE" => new BuiltInMethod("writeUInt16BE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeUInt16BE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteUInt16BE(value, offset);
            }),

            "writeUInt32LE" => new BuiltInMethod("writeUInt32LE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeUInt32LE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteUInt32LE(value, offset);
            }),

            "writeUInt32BE" => new BuiltInMethod("writeUInt32BE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeUInt32BE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteUInt32BE(value, offset);
            }),

            "writeInt16LE" => new BuiltInMethod("writeInt16LE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeInt16LE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteInt16LE(value, offset);
            }),

            "writeInt16BE" => new BuiltInMethod("writeInt16BE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeInt16BE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteInt16BE(value, offset);
            }),

            "writeInt32LE" => new BuiltInMethod("writeInt32LE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeInt32LE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteInt32LE(value, offset);
            }),

            "writeInt32BE" => new BuiltInMethod("writeInt32BE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeInt32BE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteInt32BE(value, offset);
            }),

            "writeFloatLE" => new BuiltInMethod("writeFloatLE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeFloatLE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteFloatLE(value, offset);
            }),

            "writeFloatBE" => new BuiltInMethod("writeFloatBE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeFloatBE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteFloatBE(value, offset);
            }),

            "writeDoubleLE" => new BuiltInMethod("writeDoubleLE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeDoubleLE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteDoubleLE(value, offset);
            }),

            "writeDoubleBE" => new BuiltInMethod("writeDoubleBE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is not double value)
                    throw new Exception("Buffer.writeDoubleBE requires a number argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteDoubleBE(value, offset);
            }),

            "writeBigInt64LE" => new BuiltInMethod("writeBigInt64LE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is null)
                    throw new Exception("Buffer.writeBigInt64LE requires a BigInt argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteBigInt64LE(args[0]!, offset);
            }),

            "writeBigInt64BE" => new BuiltInMethod("writeBigInt64BE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is null)
                    throw new Exception("Buffer.writeBigInt64BE requires a BigInt argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteBigInt64BE(args[0]!, offset);
            }),

            "writeBigUInt64LE" => new BuiltInMethod("writeBigUInt64LE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is null)
                    throw new Exception("Buffer.writeBigUInt64LE requires a BigInt argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteBigUInt64LE(args[0]!, offset);
            }),

            "writeBigUInt64BE" => new BuiltInMethod("writeBigUInt64BE", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is null)
                    throw new Exception("Buffer.writeBigUInt64BE requires a BigInt argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                return WriteBigUInt64BE(args[0]!, offset);
            }),

            // Search methods
            "indexOf" => new BuiltInMethod("indexOf", 1, 3, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is null)
                    throw new Exception("Buffer.indexOf requires a value argument");
                int byteOffset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                string encoding = args.Count > 2 ? args[2]?.ToString() ?? "utf8" : "utf8";
                return IndexOf(args[0]!, byteOffset, encoding);
            }),

            "includes" => new BuiltInMethod("includes", 1, 3, (interp, recv, args) =>
            {
                if (args.Count == 0 || args[0] is null)
                    throw new Exception("Buffer.includes requires a value argument");
                int byteOffset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                string encoding = args.Count > 2 ? args[2]?.ToString() ?? "utf8" : "utf8";
                return Includes(args[0]!, byteOffset, encoding);
            }),

            // Swap methods
            "swap16" => new BuiltInMethod("swap16", 0, (interp, recv, args) => Swap16()),

            "swap32" => new BuiltInMethod("swap32", 0, (interp, recv, args) => Swap32()),

            "swap64" => new BuiltInMethod("swap64", 0, (interp, recv, args) => Swap64()),

            _ => null
        };
    }

    #endregion

    #region Bounds Validation

    /// <summary>
    /// Validates that the offset is within bounds for a read/write of the specified byte count.
    /// </summary>
    private void ValidateOffset(int offset, int byteCount)
    {
        int maxOffset = _data.Length - byteCount;
        if (offset < 0 || offset > maxOffset)
            throw new Exception($"The value of \"offset\" is out of range. " +
                $"It must be >= 0 and <= {maxOffset}. Received {offset}");
    }

    #endregion

    #region Multi-byte Reads

    /// <summary>
    /// Reads a signed 8-bit integer from the buffer.
    /// </summary>
    public double ReadInt8(int offset = 0)
    {
        ValidateOffset(offset, 1);
        return (sbyte)_data[offset];
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer (little-endian) from the buffer.
    /// </summary>
    public double ReadUInt16LE(int offset = 0)
    {
        ValidateOffset(offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer (big-endian) from the buffer.
    /// </summary>
    public double ReadUInt16BE(int offset = 0)
    {
        ValidateOffset(offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer (little-endian) from the buffer.
    /// </summary>
    public double ReadUInt32LE(int offset = 0)
    {
        ValidateOffset(offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer (big-endian) from the buffer.
    /// </summary>
    public double ReadUInt32BE(int offset = 0)
    {
        ValidateOffset(offset, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads a signed 16-bit integer (little-endian) from the buffer.
    /// </summary>
    public double ReadInt16LE(int offset = 0)
    {
        ValidateOffset(offset, 2);
        return BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads a signed 16-bit integer (big-endian) from the buffer.
    /// </summary>
    public double ReadInt16BE(int offset = 0)
    {
        ValidateOffset(offset, 2);
        return BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads a signed 32-bit integer (little-endian) from the buffer.
    /// </summary>
    public double ReadInt32LE(int offset = 0)
    {
        ValidateOffset(offset, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads a signed 32-bit integer (big-endian) from the buffer.
    /// </summary>
    public double ReadInt32BE(int offset = 0)
    {
        ValidateOffset(offset, 4);
        return BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads a 32-bit float (little-endian) from the buffer.
    /// </summary>
    public double ReadFloatLE(int offset = 0)
    {
        ValidateOffset(offset, 4);
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset)));
    }

    /// <summary>
    /// Reads a 32-bit float (big-endian) from the buffer.
    /// </summary>
    public double ReadFloatBE(int offset = 0)
    {
        ValidateOffset(offset, 4);
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(offset)));
    }

    /// <summary>
    /// Reads a 64-bit double (little-endian) from the buffer.
    /// </summary>
    public double ReadDoubleLE(int offset = 0)
    {
        ValidateOffset(offset, 8);
        return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(offset)));
    }

    /// <summary>
    /// Reads a 64-bit double (big-endian) from the buffer.
    /// </summary>
    public double ReadDoubleBE(int offset = 0)
    {
        ValidateOffset(offset, 8);
        return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(_data.AsSpan(offset)));
    }

    /// <summary>
    /// Reads a signed 64-bit BigInt (little-endian) from the buffer.
    /// </summary>
    public BigInteger ReadBigInt64LE(int offset = 0)
    {
        ValidateOffset(offset, 8);
        return BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads a signed 64-bit BigInt (big-endian) from the buffer.
    /// </summary>
    public BigInteger ReadBigInt64BE(int offset = 0)
    {
        ValidateOffset(offset, 8);
        return BinaryPrimitives.ReadInt64BigEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads an unsigned 64-bit BigInt (little-endian) from the buffer.
    /// </summary>
    public BigInteger ReadBigUInt64LE(int offset = 0)
    {
        ValidateOffset(offset, 8);
        return BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(offset));
    }

    /// <summary>
    /// Reads an unsigned 64-bit BigInt (big-endian) from the buffer.
    /// </summary>
    public BigInteger ReadBigUInt64BE(int offset = 0)
    {
        ValidateOffset(offset, 8);
        return BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(offset));
    }

    #endregion

    #region Multi-byte Writes

    /// <summary>
    /// Writes a signed 8-bit integer to the buffer. Returns offset + 1.
    /// </summary>
    public double WriteInt8(double value, int offset = 0)
    {
        ValidateOffset(offset, 1);
        _data[offset] = (byte)(sbyte)value;
        return offset + 1;
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer (little-endian) to the buffer. Returns offset + 2.
    /// </summary>
    public double WriteUInt16LE(double value, int offset = 0)
    {
        ValidateOffset(offset, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(offset), (ushort)value);
        return offset + 2;
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer (big-endian) to the buffer. Returns offset + 2.
    /// </summary>
    public double WriteUInt16BE(double value, int offset = 0)
    {
        ValidateOffset(offset, 2);
        BinaryPrimitives.WriteUInt16BigEndian(_data.AsSpan(offset), (ushort)value);
        return offset + 2;
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer (little-endian) to the buffer. Returns offset + 4.
    /// </summary>
    public double WriteUInt32LE(double value, int offset = 0)
    {
        ValidateOffset(offset, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(offset), (uint)value);
        return offset + 4;
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer (big-endian) to the buffer. Returns offset + 4.
    /// </summary>
    public double WriteUInt32BE(double value, int offset = 0)
    {
        ValidateOffset(offset, 4);
        BinaryPrimitives.WriteUInt32BigEndian(_data.AsSpan(offset), (uint)value);
        return offset + 4;
    }

    /// <summary>
    /// Writes a signed 16-bit integer (little-endian) to the buffer. Returns offset + 2.
    /// </summary>
    public double WriteInt16LE(double value, int offset = 0)
    {
        ValidateOffset(offset, 2);
        BinaryPrimitives.WriteInt16LittleEndian(_data.AsSpan(offset), (short)value);
        return offset + 2;
    }

    /// <summary>
    /// Writes a signed 16-bit integer (big-endian) to the buffer. Returns offset + 2.
    /// </summary>
    public double WriteInt16BE(double value, int offset = 0)
    {
        ValidateOffset(offset, 2);
        BinaryPrimitives.WriteInt16BigEndian(_data.AsSpan(offset), (short)value);
        return offset + 2;
    }

    /// <summary>
    /// Writes a signed 32-bit integer (little-endian) to the buffer. Returns offset + 4.
    /// </summary>
    public double WriteInt32LE(double value, int offset = 0)
    {
        ValidateOffset(offset, 4);
        BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(offset), (int)value);
        return offset + 4;
    }

    /// <summary>
    /// Writes a signed 32-bit integer (big-endian) to the buffer. Returns offset + 4.
    /// </summary>
    public double WriteInt32BE(double value, int offset = 0)
    {
        ValidateOffset(offset, 4);
        BinaryPrimitives.WriteInt32BigEndian(_data.AsSpan(offset), (int)value);
        return offset + 4;
    }

    /// <summary>
    /// Writes a 32-bit float (little-endian) to the buffer. Returns offset + 4.
    /// </summary>
    public double WriteFloatLE(double value, int offset = 0)
    {
        ValidateOffset(offset, 4);
        BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(offset), BitConverter.SingleToInt32Bits((float)value));
        return offset + 4;
    }

    /// <summary>
    /// Writes a 32-bit float (big-endian) to the buffer. Returns offset + 4.
    /// </summary>
    public double WriteFloatBE(double value, int offset = 0)
    {
        ValidateOffset(offset, 4);
        BinaryPrimitives.WriteInt32BigEndian(_data.AsSpan(offset), BitConverter.SingleToInt32Bits((float)value));
        return offset + 4;
    }

    /// <summary>
    /// Writes a 64-bit double (little-endian) to the buffer. Returns offset + 8.
    /// </summary>
    public double WriteDoubleLE(double value, int offset = 0)
    {
        ValidateOffset(offset, 8);
        BinaryPrimitives.WriteInt64LittleEndian(_data.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));
        return offset + 8;
    }

    /// <summary>
    /// Writes a 64-bit double (big-endian) to the buffer. Returns offset + 8.
    /// </summary>
    public double WriteDoubleBE(double value, int offset = 0)
    {
        ValidateOffset(offset, 8);
        BinaryPrimitives.WriteInt64BigEndian(_data.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));
        return offset + 8;
    }

    /// <summary>
    /// Writes a signed 64-bit BigInt (little-endian) to the buffer. Returns offset + 8.
    /// </summary>
    public double WriteBigInt64LE(object value, int offset = 0)
    {
        ValidateOffset(offset, 8);
        long longValue = value switch
        {
            BigInteger bi => (long)bi,
            double d => (long)d,
            _ => throw new Exception("Value must be a BigInt or number")
        };
        BinaryPrimitives.WriteInt64LittleEndian(_data.AsSpan(offset), longValue);
        return offset + 8;
    }

    /// <summary>
    /// Writes a signed 64-bit BigInt (big-endian) to the buffer. Returns offset + 8.
    /// </summary>
    public double WriteBigInt64BE(object value, int offset = 0)
    {
        ValidateOffset(offset, 8);
        long longValue = value switch
        {
            BigInteger bi => (long)bi,
            double d => (long)d,
            _ => throw new Exception("Value must be a BigInt or number")
        };
        BinaryPrimitives.WriteInt64BigEndian(_data.AsSpan(offset), longValue);
        return offset + 8;
    }

    /// <summary>
    /// Writes an unsigned 64-bit BigInt (little-endian) to the buffer. Returns offset + 8.
    /// </summary>
    public double WriteBigUInt64LE(object value, int offset = 0)
    {
        ValidateOffset(offset, 8);
        ulong ulongValue = value switch
        {
            BigInteger bi => (ulong)bi,
            double d => (ulong)d,
            _ => throw new Exception("Value must be a BigInt or number")
        };
        BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(offset), ulongValue);
        return offset + 8;
    }

    /// <summary>
    /// Writes an unsigned 64-bit BigInt (big-endian) to the buffer. Returns offset + 8.
    /// </summary>
    public double WriteBigUInt64BE(object value, int offset = 0)
    {
        ValidateOffset(offset, 8);
        ulong ulongValue = value switch
        {
            BigInteger bi => (ulong)bi,
            double d => (ulong)d,
            _ => throw new Exception("Value must be a BigInt or number")
        };
        BinaryPrimitives.WriteUInt64BigEndian(_data.AsSpan(offset), ulongValue);
        return offset + 8;
    }

    #endregion

    #region Search Methods

    /// <summary>
    /// Returns the index of the first occurrence of the value, or -1 if not found.
    /// </summary>
    public double IndexOf(object value, int byteOffset = 0, string encoding = "utf8")
    {
        if (byteOffset < 0) byteOffset = 0;
        if (byteOffset >= _data.Length) return -1;

        byte[] searchBytes = value switch
        {
            double d => [(byte)(int)d],
            int n => [(byte)n],
            string s => EncodeString(s, encoding),
            SharpTSBuffer buf => buf._data,
            _ => throw new Exception("Invalid search value type")
        };

        if (searchBytes.Length == 0) return byteOffset;
        if (searchBytes.Length > _data.Length - byteOffset) return -1;

        // Simple linear search
        for (int i = byteOffset; i <= _data.Length - searchBytes.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < searchBytes.Length; j++)
            {
                if (_data[i + j] != searchBytes[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns true if the buffer contains the specified value.
    /// </summary>
    public bool Includes(object value, int byteOffset = 0, string encoding = "utf8")
    {
        return IndexOf(value, byteOffset, encoding) != -1;
    }

    #endregion

    #region Swap Methods

    /// <summary>
    /// Swaps bytes in 16-bit words in place. Buffer length must be a multiple of 2.
    /// Returns this buffer for chaining.
    /// </summary>
    public SharpTSBuffer Swap16()
    {
        if (_data.Length % 2 != 0)
            throw new Exception("Buffer size must be a multiple of 16-bits");

        for (int i = 0; i < _data.Length; i += 2)
        {
            (_data[i], _data[i + 1]) = (_data[i + 1], _data[i]);
        }
        return this;
    }

    /// <summary>
    /// Swaps bytes in 32-bit words in place. Buffer length must be a multiple of 4.
    /// Returns this buffer for chaining.
    /// </summary>
    public SharpTSBuffer Swap32()
    {
        if (_data.Length % 4 != 0)
            throw new Exception("Buffer size must be a multiple of 32-bits");

        for (int i = 0; i < _data.Length; i += 4)
        {
            (_data[i], _data[i + 3]) = (_data[i + 3], _data[i]);
            (_data[i + 1], _data[i + 2]) = (_data[i + 2], _data[i + 1]);
        }
        return this;
    }

    /// <summary>
    /// Swaps bytes in 64-bit words in place. Buffer length must be a multiple of 8.
    /// Returns this buffer for chaining.
    /// </summary>
    public SharpTSBuffer Swap64()
    {
        if (_data.Length % 8 != 0)
            throw new Exception("Buffer size must be a multiple of 64-bits");

        for (int i = 0; i < _data.Length; i += 8)
        {
            (_data[i], _data[i + 7]) = (_data[i + 7], _data[i]);
            (_data[i + 1], _data[i + 6]) = (_data[i + 6], _data[i + 1]);
            (_data[i + 2], _data[i + 5]) = (_data[i + 5], _data[i + 2]);
            (_data[i + 3], _data[i + 4]) = (_data[i + 4], _data[i + 3]);
        }
        return this;
    }

    #endregion

    #region Encoding Helpers

    /// <summary>
    /// Encodes a string to bytes using the specified encoding.
    /// </summary>
    private static byte[] EncodeString(string data, string encoding)
    {
        return encoding.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => Encoding.UTF8.GetBytes(data),
            "ascii" => Encoding.ASCII.GetBytes(data),
            "base64" => Convert.FromBase64String(data),
            "hex" => ConvertHexToBytes(data),
            "latin1" or "binary" => Encoding.Latin1.GetBytes(data),
            "ucs2" or "ucs-2" or "utf16le" or "utf-16le" => Encoding.Unicode.GetBytes(data),
            _ => throw new ArgumentException($"Unknown encoding: {encoding}")
        };
    }

    /// <summary>
    /// Decodes bytes to string using the specified encoding.
    /// </summary>
    private static string DecodeBytes(byte[] data, string encoding)
    {
        return encoding.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => Encoding.UTF8.GetString(data),
            "ascii" => Encoding.ASCII.GetString(data),
            "base64" => Convert.ToBase64String(data),
            "hex" => Convert.ToHexString(data).ToLowerInvariant(),
            "latin1" or "binary" => Encoding.Latin1.GetString(data),
            "ucs2" or "ucs-2" or "utf16le" or "utf-16le" => Encoding.Unicode.GetString(data),
            _ => throw new ArgumentException($"Unknown encoding: {encoding}")
        };
    }

    /// <summary>
    /// Converts a hex string to byte array.
    /// </summary>
    private static byte[] ConvertHexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return [];

        // Remove any spaces or hyphens
        hex = hex.Replace(" ", "").Replace("-", "");

        // Handle odd-length strings by padding
        if (hex.Length % 2 != 0)
            hex = "0" + hex;

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    #endregion
}
