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
    /// Gets the underlying byte array (internal access for performance).
    /// </summary>
    internal byte[] Data => _data;

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

            _ => null
        };
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
