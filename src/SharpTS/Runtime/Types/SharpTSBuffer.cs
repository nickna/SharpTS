using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

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
public class SharpTSBuffer : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Buffer;

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
    public static SharpTSBuffer FromArray(IReadOnlyList<object?> array)
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
    public static SharpTSBuffer Concat(IReadOnlyCollection<object?> buffers, int? totalLength = null)
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
                var converted = FromArray(arr);
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
        // Node exposes both the `UInt` and lowercase `Uint` spellings for every
        // unsigned read/write (readUInt8/readUint8, writeBigUInt64LE/writeBigUint64LE,
        // readUIntLE/readUintLE, …). Normalize to the canonical `UInt` spelling so a
        // single set of cases serves both — matching the compiled path, whose
        // reflection dispatch already resolves either spelling case-insensitively.
        if (name.Contains("Uint"))
            name = name.Replace("Uint", "UInt");

        return name switch
        {
            "length" => (double)Length,

            "toString" => BuiltInMethod.CreateV2("toString", 0, 1, (_, _, args) =>
            {
                var encoding = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "utf8" : "utf8";
                return RuntimeValue.FromString(ToString(encoding));
            }),

            "slice" => BuiltInMethod.CreateV2("slice", 0, 2, (_, _, args) =>
            {
                int start = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                return RuntimeValue.FromObject(Slice(start, end));
            }),

            // subarray: same windowing arithmetic as slice. DEVIATION: SharpTS Buffers
            // wrap a flat managed byte[] (not a view over a shared ArrayBuffer), so the
            // result is an independent copy rather than sharing memory with the parent —
            // mirroring this Buffer's existing copying `slice`. (Shared-memory views would
            // require an offset/length backing refactor of both the interpreter and the
            // compiled $Buffer.)
            "subarray" => BuiltInMethod.CreateV2("subarray", 0, 2, (_, _, args) =>
            {
                int start = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                return RuntimeValue.FromObject(Slice(start, end));
            }),

            // Variable-length integer reads/writes (1-6 bytes).
            "readUIntLE" or "readUintLE" => BuiltInMethod.CreateV2("readUIntLE", 2, (_, _, args) =>
                RuntimeValue.FromNumber(ReadUIntLE(OffsetArg(args, 0), OffsetArg(args, 1)))),
            "readUIntBE" or "readUintBE" => BuiltInMethod.CreateV2("readUIntBE", 2, (_, _, args) =>
                RuntimeValue.FromNumber(ReadUIntBE(OffsetArg(args, 0), OffsetArg(args, 1)))),
            "readIntLE" => BuiltInMethod.CreateV2("readIntLE", 2, (_, _, args) =>
                RuntimeValue.FromNumber(ReadIntLE(OffsetArg(args, 0), OffsetArg(args, 1)))),
            "readIntBE" => BuiltInMethod.CreateV2("readIntBE", 2, (_, _, args) =>
                RuntimeValue.FromNumber(ReadIntBE(OffsetArg(args, 0), OffsetArg(args, 1)))),
            "writeUIntLE" or "writeUintLE" => BuiltInMethod.CreateV2("writeUIntLE", 3, (_, _, args) =>
                RuntimeValue.FromNumber(WriteUIntLE(NumberArg(args, "writeUIntLE"), OffsetArg(args, 1), OffsetArg(args, 2)))),
            "writeUIntBE" or "writeUintBE" => BuiltInMethod.CreateV2("writeUIntBE", 3, (_, _, args) =>
                RuntimeValue.FromNumber(WriteUIntBE(NumberArg(args, "writeUIntBE"), OffsetArg(args, 1), OffsetArg(args, 2)))),
            "writeIntLE" => BuiltInMethod.CreateV2("writeIntLE", 3, (_, _, args) =>
                RuntimeValue.FromNumber(WriteIntLE(NumberArg(args, "writeIntLE"), OffsetArg(args, 1), OffsetArg(args, 2)))),
            "writeIntBE" => BuiltInMethod.CreateV2("writeIntBE", 3, (_, _, args) =>
                RuntimeValue.FromNumber(WriteIntBE(NumberArg(args, "writeIntBE"), OffsetArg(args, 1), OffsetArg(args, 2)))),

            "copy" => BuiltInMethod.CreateV2("copy", 1, 4, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].ToObject() is not SharpTSBuffer target)
                    throw new Exception("Buffer.copy requires a target buffer");
                int targetStart = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                int sourceStart = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : 0;
                int? sourceEnd = args.Length > 3 && args[3].IsNumber ? (int)args[3].AsNumberUnsafe() : null;
                return RuntimeValue.FromNumber(Copy(target, targetStart, sourceStart, sourceEnd));
            }),

            "compare" => BuiltInMethod.CreateV2("compare", 1, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].ToObject() is not SharpTSBuffer other)
                    throw new Exception("Buffer.compare requires a buffer argument");
                return RuntimeValue.FromNumber(Compare(other));
            }),

            "equals" => BuiltInMethod.CreateV2("equals", 1, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].ToObject() is not SharpTSBuffer other)
                    return RuntimeValue.False;
                return RuntimeValue.FromBoolean(Equals(other));
            }),

            "fill" => BuiltInMethod.CreateV2("fill", 1, 4, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("Buffer.fill requires a value");
                var value = args[0].ToObject();
                int start = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                int? end = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : null;
                string encoding = args.Length > 3 ? args[3].ToObject()?.ToString() ?? "utf8" : "utf8";
                return RuntimeValue.FromObject(Fill(value!, start, end, encoding));
            }),

            "write" => BuiltInMethod.CreateV2("write", 1, 4, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].Kind != ValueKind.String)
                    throw new Exception("Buffer.write requires a string argument");
                var data = args[0].AsStringUnsafe();
                int offset = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                int? length = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : null;
                string encoding = args.Length > 3 ? args[3].ToObject()?.ToString() ?? "utf8" : "utf8";
                return RuntimeValue.FromNumber(Write(data, offset, length, encoding));
            }),

            "readUInt8" => BuiltInMethod.CreateV2("readUInt8", 0, 1, (_, _, args) =>
            {
                int offset = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                return RuntimeValue.FromNumber(ReadUInt8(offset));
            }),

            "writeUInt8" => BuiltInMethod.CreateV2("writeUInt8", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0 || !args[0].IsNumber)
                    throw new Exception("Buffer.writeUInt8 requires a number argument");
                int offset = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                return RuntimeValue.FromNumber(WriteUInt8(args[0].AsNumberUnsafe(), offset));
            }),

            "toJSON" => BuiltInMethod.CreateV2("toJSON", 0, (_, _, _) =>
            {
                var elements = new List<object?>(_data.Length);
                foreach (var b in _data)
                {
                    elements.Add((double)b);
                }
                return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
                {
                    ["type"] = "Buffer",
                    ["data"] = new SharpTSArray(elements)
                }));
            }),

            // Multi-byte read methods
            "readInt8" => BuiltInMethod.CreateV2("readInt8", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadInt8(OffsetArg(args, 0)))),

            "readUInt16LE" => BuiltInMethod.CreateV2("readUInt16LE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadUInt16LE(OffsetArg(args, 0)))),

            "readUInt16BE" => BuiltInMethod.CreateV2("readUInt16BE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadUInt16BE(OffsetArg(args, 0)))),

            "readUInt32LE" => BuiltInMethod.CreateV2("readUInt32LE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadUInt32LE(OffsetArg(args, 0)))),

            "readUInt32BE" => BuiltInMethod.CreateV2("readUInt32BE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadUInt32BE(OffsetArg(args, 0)))),

            "readInt16LE" => BuiltInMethod.CreateV2("readInt16LE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadInt16LE(OffsetArg(args, 0)))),

            "readInt16BE" => BuiltInMethod.CreateV2("readInt16BE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadInt16BE(OffsetArg(args, 0)))),

            "readInt32LE" => BuiltInMethod.CreateV2("readInt32LE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadInt32LE(OffsetArg(args, 0)))),

            "readInt32BE" => BuiltInMethod.CreateV2("readInt32BE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadInt32BE(OffsetArg(args, 0)))),

            "readFloatLE" => BuiltInMethod.CreateV2("readFloatLE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadFloatLE(OffsetArg(args, 0)))),

            "readFloatBE" => BuiltInMethod.CreateV2("readFloatBE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadFloatBE(OffsetArg(args, 0)))),

            "readDoubleLE" => BuiltInMethod.CreateV2("readDoubleLE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadDoubleLE(OffsetArg(args, 0)))),

            "readDoubleBE" => BuiltInMethod.CreateV2("readDoubleBE", 0, 1, (_, _, args) =>
                RuntimeValue.FromNumber(ReadDoubleBE(OffsetArg(args, 0)))),

            "readBigInt64LE" => BuiltInMethod.CreateV2("readBigInt64LE", 0, 1, (_, _, args) =>
                RuntimeValue.FromBigInt(new SharpTSBigInt(ReadBigInt64LE(OffsetArg(args, 0))))),

            "readBigInt64BE" => BuiltInMethod.CreateV2("readBigInt64BE", 0, 1, (_, _, args) =>
                RuntimeValue.FromBigInt(new SharpTSBigInt(ReadBigInt64BE(OffsetArg(args, 0))))),

            "readBigUInt64LE" => BuiltInMethod.CreateV2("readBigUInt64LE", 0, 1, (_, _, args) =>
                RuntimeValue.FromBigInt(new SharpTSBigInt(ReadBigUInt64LE(OffsetArg(args, 0))))),

            "readBigUInt64BE" => BuiltInMethod.CreateV2("readBigUInt64BE", 0, 1, (_, _, args) =>
                RuntimeValue.FromBigInt(new SharpTSBigInt(ReadBigUInt64BE(OffsetArg(args, 0))))),

            // Multi-byte write methods
            "writeInt8" => BuiltInMethod.CreateV2("writeInt8", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteInt8(NumberArg(args, "writeInt8"), OffsetArg(args, 1)))),

            "writeUInt16LE" => BuiltInMethod.CreateV2("writeUInt16LE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteUInt16LE(NumberArg(args, "writeUInt16LE"), OffsetArg(args, 1)))),

            "writeUInt16BE" => BuiltInMethod.CreateV2("writeUInt16BE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteUInt16BE(NumberArg(args, "writeUInt16BE"), OffsetArg(args, 1)))),

            "writeUInt32LE" => BuiltInMethod.CreateV2("writeUInt32LE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteUInt32LE(NumberArg(args, "writeUInt32LE"), OffsetArg(args, 1)))),

            "writeUInt32BE" => BuiltInMethod.CreateV2("writeUInt32BE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteUInt32BE(NumberArg(args, "writeUInt32BE"), OffsetArg(args, 1)))),

            "writeInt16LE" => BuiltInMethod.CreateV2("writeInt16LE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteInt16LE(NumberArg(args, "writeInt16LE"), OffsetArg(args, 1)))),

            "writeInt16BE" => BuiltInMethod.CreateV2("writeInt16BE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteInt16BE(NumberArg(args, "writeInt16BE"), OffsetArg(args, 1)))),

            "writeInt32LE" => BuiltInMethod.CreateV2("writeInt32LE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteInt32LE(NumberArg(args, "writeInt32LE"), OffsetArg(args, 1)))),

            "writeInt32BE" => BuiltInMethod.CreateV2("writeInt32BE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteInt32BE(NumberArg(args, "writeInt32BE"), OffsetArg(args, 1)))),

            "writeFloatLE" => BuiltInMethod.CreateV2("writeFloatLE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteFloatLE(NumberArg(args, "writeFloatLE"), OffsetArg(args, 1)))),

            "writeFloatBE" => BuiltInMethod.CreateV2("writeFloatBE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteFloatBE(NumberArg(args, "writeFloatBE"), OffsetArg(args, 1)))),

            "writeDoubleLE" => BuiltInMethod.CreateV2("writeDoubleLE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteDoubleLE(NumberArg(args, "writeDoubleLE"), OffsetArg(args, 1)))),

            "writeDoubleBE" => BuiltInMethod.CreateV2("writeDoubleBE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteDoubleBE(NumberArg(args, "writeDoubleBE"), OffsetArg(args, 1)))),

            "writeBigInt64LE" => BuiltInMethod.CreateV2("writeBigInt64LE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteBigInt64LE(BigIntArg(args, "writeBigInt64LE"), OffsetArg(args, 1)))),

            "writeBigInt64BE" => BuiltInMethod.CreateV2("writeBigInt64BE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteBigInt64BE(BigIntArg(args, "writeBigInt64BE"), OffsetArg(args, 1)))),

            "writeBigUInt64LE" => BuiltInMethod.CreateV2("writeBigUInt64LE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteBigUInt64LE(BigIntArg(args, "writeBigUInt64LE"), OffsetArg(args, 1)))),

            "writeBigUInt64BE" => BuiltInMethod.CreateV2("writeBigUInt64BE", 1, 2, (_, _, args) =>
                RuntimeValue.FromNumber(WriteBigUInt64BE(BigIntArg(args, "writeBigUInt64BE"), OffsetArg(args, 1)))),

            // Search methods
            "indexOf" => BuiltInMethod.CreateV2("indexOf", 1, 3, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].IsNull)
                    throw new Exception("Buffer.indexOf requires a value argument");
                int byteOffset = OffsetArg(args, 1);
                string encoding = args.Length > 2 ? args[2].ToObject()?.ToString() ?? "utf8" : "utf8";
                return RuntimeValue.FromNumber(IndexOf(args[0].ToObject()!, byteOffset, encoding));
            }),

            "includes" => BuiltInMethod.CreateV2("includes", 1, 3, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].IsNull)
                    throw new Exception("Buffer.includes requires a value argument");
                int byteOffset = OffsetArg(args, 1);
                string encoding = args.Length > 2 ? args[2].ToObject()?.ToString() ?? "utf8" : "utf8";
                return RuntimeValue.FromBoolean(Includes(args[0].ToObject()!, byteOffset, encoding));
            }),

            // Swap methods
            "swap16" => BuiltInMethod.CreateV2("swap16", 0, (_, _, _) => RuntimeValue.FromObject(Swap16())),

            "swap32" => BuiltInMethod.CreateV2("swap32", 0, (_, _, _) => RuntimeValue.FromObject(Swap32())),

            "swap64" => BuiltInMethod.CreateV2("swap64", 0, (_, _, _) => RuntimeValue.FromObject(Swap64())),

            _ => null
        };
    }

    /// <summary>
    /// Reads an optional numeric argument at <paramref name="index"/>;
    /// missing or non-number args coerce to 0 (matches the legacy
    /// <c>args[i] is double o ? (int)o : 0</c> pattern).
    /// </summary>
    private static int OffsetArg(ReadOnlySpan<RuntimeValue> args, int index)
        => args.Length > index && args[index].IsNumber ? (int)args[index].AsNumberUnsafe() : 0;

    /// <summary>
    /// Reads the required numeric value argument at index 0 for write* methods,
    /// throwing the same error the legacy bodies threw on a missing/non-number arg.
    /// </summary>
    private static double NumberArg(ReadOnlySpan<RuntimeValue> args, string methodName)
    {
        if (args.Length == 0 || !args[0].IsNumber)
            throw new Exception($"Buffer.{methodName} requires a number argument");
        return args[0].AsNumberUnsafe();
    }

    /// <summary>
    /// Reads the required BigInt-ish value argument at index 0 for writeBig* methods.
    /// The legacy bodies only rejected missing/null args (the conversion itself
    /// validates the value), so this does the same.
    /// </summary>
    private static object BigIntArg(ReadOnlySpan<RuntimeValue> args, string methodName)
    {
        if (args.Length == 0 || args[0].IsNull)
            throw new Exception($"Buffer.{methodName} requires a BigInt argument");
        return args[0].ToObject()!;
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
            SharpTSBigInt sbi => (long)sbi.Value,
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
            SharpTSBigInt sbi => (long)sbi.Value,
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
            SharpTSBigInt sbi => (ulong)sbi.Value,
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
            SharpTSBigInt sbi => (ulong)sbi.Value,
            BigInteger bi => (ulong)bi,
            double d => (ulong)d,
            _ => throw new Exception("Value must be a BigInt or number")
        };
        BinaryPrimitives.WriteUInt64BigEndian(_data.AsSpan(offset), ulongValue);
        return offset + 8;
    }

    #endregion

    #region Variable-length Reads/Writes

    /// <summary>Reads an unsigned little-endian integer of <paramref name="byteLength"/> (1-6) bytes.</summary>
    public double ReadUIntLE(int offset, int byteLength)
    {
        ValidateVarByteLength(byteLength);
        ValidateOffset(offset, byteLength);
        long val = 0;
        for (int i = 0; i < byteLength; i++)
            val |= (long)_data[offset + i] << (8 * i);
        return val;
    }

    /// <summary>Reads an unsigned big-endian integer of <paramref name="byteLength"/> (1-6) bytes.</summary>
    public double ReadUIntBE(int offset, int byteLength)
    {
        ValidateVarByteLength(byteLength);
        ValidateOffset(offset, byteLength);
        long val = 0;
        for (int i = 0; i < byteLength; i++)
            val = (val << 8) | _data[offset + i];
        return val;
    }

    /// <summary>Reads a signed little-endian integer of <paramref name="byteLength"/> (1-6) bytes.</summary>
    public double ReadIntLE(int offset, int byteLength)
        => SignExtend((long)ReadUIntLE(offset, byteLength), byteLength);

    /// <summary>Reads a signed big-endian integer of <paramref name="byteLength"/> (1-6) bytes.</summary>
    public double ReadIntBE(int offset, int byteLength)
        => SignExtend((long)ReadUIntBE(offset, byteLength), byteLength);

    /// <summary>Writes a little-endian integer of <paramref name="byteLength"/> (1-6) bytes. Returns offset + byteLength.</summary>
    public double WriteUIntLE(double value, int offset, int byteLength)
    {
        ValidateVarByteLength(byteLength);
        ValidateOffset(offset, byteLength);
        long v = (long)value;
        for (int i = 0; i < byteLength; i++)
        {
            _data[offset + i] = (byte)(v & 0xFF);
            v >>= 8;
        }
        return offset + byteLength;
    }

    /// <summary>Writes a big-endian integer of <paramref name="byteLength"/> (1-6) bytes. Returns offset + byteLength.</summary>
    public double WriteUIntBE(double value, int offset, int byteLength)
    {
        ValidateVarByteLength(byteLength);
        ValidateOffset(offset, byteLength);
        long v = (long)value;
        for (int i = byteLength - 1; i >= 0; i--)
        {
            _data[offset + i] = (byte)(v & 0xFF);
            v >>= 8;
        }
        return offset + byteLength;
    }

    // Signed writes are byte-identical to unsigned (two's-complement representation).
    // Private so the SharpTSBuffer↔$Buffer public-method-parity check (RuntimeTypeSyncTests)
    // isn't tripped — the compiled BufferEmitter routes writeInt*LE/BE to WriteUInt*LE/BE.
    private double WriteIntLE(double value, int offset, int byteLength) => WriteUIntLE(value, offset, byteLength);
    private double WriteIntBE(double value, int offset, int byteLength) => WriteUIntBE(value, offset, byteLength);

    private static void ValidateVarByteLength(int byteLength)
    {
        if (byteLength < 1 || byteLength > 6)
            throw new Exception($"The value of \"byteLength\" is out of range. It must be >= 1 and <= 6. Received {byteLength}");
    }

    private static double SignExtend(long val, int byteLength)
    {
        long signBit = 1L << (byteLength * 8 - 1);
        if ((val & signBit) != 0)
            val -= 1L << (byteLength * 8);
        return val;
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

    // Encoding conversions delegate to the shared BufferEncoding table so Buffer
    // and fs agree on every encoding name (and the compiled IL twin stays in sync).
    private static byte[] EncodeString(string data, string encoding) => BufferEncoding.Encode(data, encoding);

    private static string DecodeBytes(byte[] data, string encoding) => BufferEncoding.Decode(data, encoding);

    #endregion
}
