using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript's ArrayBuffer.
/// </summary>
/// <remarks>
/// Provides fixed-length raw binary data buffer for single-threaded use.
/// Unlike SharedArrayBuffer, ArrayBuffer is not shared across threads and
/// does not require pinned memory or IDisposable.
/// </remarks>
public class SharpTSArrayBuffer : ITypeCategorized
{
    private readonly byte[] _data;
    private bool _detached;

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Buffer;

    /// <summary>
    /// Gets the size of the buffer in bytes. A detached buffer (transferred via
    /// <c>postMessage</c>'s transfer list) reports 0, matching Node neutering.
    /// </summary>
    public int ByteLength => _detached ? 0 : _data.Length;

    /// <summary>
    /// Whether this buffer has been detached (its memory transferred away).
    /// </summary>
    public bool IsDetached => _detached;

    /// <summary>
    /// Marks this buffer as detached after its contents were transferred to another
    /// agent. Subsequent access throws and <see cref="ByteLength"/> reports 0 (Node
    /// neuters the source ArrayBuffer on transfer).
    /// </summary>
    public void Detach() => _detached = true;

    /// <summary>
    /// Creates a new ArrayBuffer with the specified byte length.
    /// </summary>
    /// <param name="byteLength">The size of the buffer in bytes.</param>
    public SharpTSArrayBuffer(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new Exception("RangeError: Invalid ArrayBuffer length");
        }

        _data = new byte[byteLength];
    }

    /// <summary>
    /// Gets a span over the entire buffer for direct memory access.
    /// </summary>
    public Span<byte> AsSpan()
    {
        ThrowIfDetached();
        return _data.AsSpan();
    }

    /// <summary>
    /// Gets a span over a portion of the buffer.
    /// </summary>
    /// <param name="start">The starting byte offset.</param>
    /// <param name="length">The number of bytes.</param>
    public Span<byte> AsSpan(int start, int length)
    {
        ThrowIfDetached();
        return _data.AsSpan(start, length);
    }

    /// <summary>
    /// Gets the underlying byte array for direct access.
    /// Use with caution - prefer AsSpan() for bounds-checked access.
    /// </summary>
    internal byte[] GetBackingArray()
    {
        ThrowIfDetached();
        return _data;
    }

    private void ThrowIfDetached()
    {
        if (_detached)
            throw new Exception("TypeError: Cannot perform operation on a detached ArrayBuffer");
    }

    /// <summary>
    /// Creates a new ArrayBuffer containing a copy of a portion of this buffer.
    /// </summary>
    /// <param name="begin">The beginning index (inclusive). Negative values count from end.</param>
    /// <param name="end">The ending index (exclusive). Negative values count from end. Defaults to ByteLength.</param>
    /// <returns>A new ArrayBuffer containing the copied bytes.</returns>
    public SharpTSArrayBuffer Slice(int begin, int? end = null)
    {
        ThrowIfDetached();
        // Handle negative indices
        int actualBegin = begin < 0 ? Math.Max(ByteLength + begin, 0) : Math.Min(begin, ByteLength);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(ByteLength + end.Value, 0) : Math.Min(end.Value, ByteLength))
            : ByteLength;

        int length = Math.Max(actualEnd - actualBegin, 0);

        var result = new SharpTSArrayBuffer(length);
        if (length > 0)
        {
            Array.Copy(_data, actualBegin, result._data, 0, length);
        }

        return result;
    }

    /// <summary>
    /// Gets or sets a byte at the specified index.
    /// </summary>
    public byte this[int index]
    {
        get
        {
            if (index < 0 || index >= ByteLength)
            {
                throw new Exception("RangeError: Index out of bounds");
            }
            return _data[index];
        }
        set
        {
            if (index < 0 || index >= ByteLength)
            {
                throw new Exception("RangeError: Index out of bounds");
            }
            _data[index] = value;
        }
    }

    /// <summary>
    /// Checks if the argument is a TypedArray or DataView (a view over an ArrayBuffer).
    /// </summary>
    /// <param name="arg">The value to check.</param>
    /// <returns>True if arg is a TypedArray or DataView instance.</returns>
    public static bool IsView(object? arg)
    {
        // TypedArray instances are views
        if (arg is SharpTSTypedArray)
            return true;

        // DataView instances are views
        if (arg is SharpTSDataView)
            return true;

        return false;
    }

    public override string ToString() => $"ArrayBuffer {{ byteLength: {ByteLength} }}";
}
