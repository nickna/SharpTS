using System.Runtime.InteropServices;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript's SharedArrayBuffer.
/// </summary>
/// <remarks>
/// Provides shared memory that can be accessed from multiple threads.
/// Uses a pinned byte array to ensure the memory address remains stable
/// for cross-thread access and Atomics operations.
/// Unlike regular ArrayBuffer, SharedArrayBuffer is shared by reference
/// across threads (not cloned during postMessage).
/// </remarks>
public class SharpTSSharedArrayBuffer : ITypeCategorized, IDisposable
{
    private readonly byte[] _data;
    private readonly GCHandle _handle;
    private bool _disposed;

    /// <summary>
    /// Unique identifier for this buffer, used for Atomics.wait/notify waiter tracking.
    /// </summary>
    public Guid BufferId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Buffer;

    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    public int ByteLength { get; }

    /// <summary>
    /// Creates a new SharedArrayBuffer with the specified byte length.
    /// </summary>
    /// <param name="byteLength">The size of the buffer in bytes.</param>
    public SharpTSSharedArrayBuffer(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new Exception("RangeError: Invalid SharedArrayBuffer length");
        }

        ByteLength = byteLength;
        _data = new byte[byteLength];
        // Pin the array so it doesn't move during GC - required for safe cross-thread access
        _handle = GCHandle.Alloc(_data, GCHandleType.Pinned);
    }

    /// <summary>
    /// Private constructor for creating sliced views that share the same backing array.
    /// </summary>
    private SharpTSSharedArrayBuffer(byte[] data, GCHandle handle, int byteLength, Guid bufferId)
    {
        _data = data;
        _handle = handle;
        ByteLength = byteLength;
        BufferId = bufferId;
        // Note: We don't own the handle in this case, so we won't free it
        _disposed = true; // Mark as disposed so finalizer doesn't try to free
    }

    /// <summary>
    /// Gets a span over the entire buffer for direct memory access.
    /// </summary>
    public Span<byte> AsSpan()
    {
        ThrowIfDisposed();
        return _data.AsSpan();
    }

    /// <summary>
    /// Gets a span over a portion of the buffer.
    /// </summary>
    /// <param name="start">The starting byte offset.</param>
    /// <param name="length">The number of bytes.</param>
    public Span<byte> AsSpan(int start, int length)
    {
        ThrowIfDisposed();
        return _data.AsSpan(start, length);
    }

    /// <summary>
    /// Gets the underlying byte array for direct access.
    /// Use with caution - prefer AsSpan() for bounds-checked access.
    /// </summary>
    internal byte[] GetBackingArray()
    {
        ThrowIfDisposed();
        return _data;
    }

    /// <summary>
    /// Creates a new SharedArrayBuffer containing a copy of a portion of this buffer.
    /// </summary>
    /// <param name="begin">The beginning index (inclusive). Negative values count from end.</param>
    /// <param name="end">The ending index (exclusive). Negative values count from end. Defaults to ByteLength.</param>
    /// <returns>A new SharedArrayBuffer containing the copied bytes.</returns>
    public SharpTSSharedArrayBuffer Slice(int begin, int? end = null)
    {
        ThrowIfDisposed();

        // Handle negative indices
        int actualBegin = begin < 0 ? Math.Max(ByteLength + begin, 0) : Math.Min(begin, ByteLength);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(ByteLength + end.Value, 0) : Math.Min(end.Value, ByteLength))
            : ByteLength;

        int length = Math.Max(actualEnd - actualBegin, 0);

        var result = new SharpTSSharedArrayBuffer(length);
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
            ThrowIfDisposed();
            if (index < 0 || index >= ByteLength)
            {
                throw new Exception("RangeError: Index out of bounds");
            }
            return _data[index];
        }
        set
        {
            ThrowIfDisposed();
            if (index < 0 || index >= ByteLength)
            {
                throw new Exception("RangeError: Index out of bounds");
            }
            _data[index] = value;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed && !_handle.IsAllocated)
        {
            throw new ObjectDisposedException(nameof(SharpTSSharedArrayBuffer));
        }
    }

    /// <summary>
    /// Releases the pinned memory handle.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
            GC.SuppressFinalize(this);
        }
    }

    ~SharpTSSharedArrayBuffer()
    {
        if (!_disposed && _handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    public override string ToString() => $"SharedArrayBuffer {{ byteLength: {ByteLength} }}";
}
