using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Base class for TypedArray views over ArrayBuffer or SharedArrayBuffer.
/// </summary>
/// <remarks>
/// Provides typed access to binary data in a buffer. Views can be created over
/// SharedArrayBuffer for multi-threaded access with Atomics, or over regular
/// ArrayBuffer for single-threaded binary data manipulation.
/// </remarks>
public abstract class SharpTSTypedArray : ITypeCategorized
{
    protected readonly byte[] _buffer;
    protected readonly int _byteOffset;
    protected readonly int _length;  // In elements, not bytes
    protected readonly SharpTSSharedArrayBuffer? _sharedBuffer;
    protected readonly SharpTSArrayBuffer? _arrayBuffer;

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Array;

    /// <summary>
    /// Gets the size in bytes of each element in this typed array.
    /// </summary>
    public abstract int BytesPerElement { get; }

    /// <summary>
    /// Gets the name of this typed array type (e.g., "Int32Array").
    /// </summary>
    public abstract string TypeName { get; }

    /// <summary>
    /// Gets the number of elements in this typed array.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets the byte offset within the underlying buffer.
    /// </summary>
    public int ByteOffset => _byteOffset;

    /// <summary>
    /// Gets the length in bytes of this typed array.
    /// </summary>
    public int ByteLength => _length * BytesPerElement;

    /// <summary>
    /// Gets whether this typed array is backed by a SharedArrayBuffer.
    /// </summary>
    public bool IsShared => _sharedBuffer != null;

    /// <summary>
    /// Gets the SharedArrayBuffer backing this array, or null if not shared.
    /// </summary>
    public SharpTSSharedArrayBuffer? SharedBuffer => _sharedBuffer;

    /// <summary>
    /// Gets the ArrayBuffer backing this array, or null if not using ArrayBuffer.
    /// </summary>
    public SharpTSArrayBuffer? ArrayBuffer => _arrayBuffer;

    /// <summary>
    /// Gets the underlying buffer as a byte array.
    /// </summary>
    internal byte[] Buffer => _buffer;

    /// <summary>
    /// Creates a typed array over a SharedArrayBuffer.
    /// </summary>
    protected SharpTSTypedArray(SharpTSSharedArrayBuffer buffer, int byteOffset, int length)
    {
        _sharedBuffer = buffer;
        _arrayBuffer = null;
        _buffer = buffer.GetBackingArray();
        _byteOffset = byteOffset;
        _length = length;
        ValidateBounds();
    }

    /// <summary>
    /// Creates a typed array over an ArrayBuffer.
    /// </summary>
    protected SharpTSTypedArray(SharpTSArrayBuffer buffer, int byteOffset, int length)
    {
        _sharedBuffer = null;
        _arrayBuffer = buffer;
        _buffer = buffer.GetBackingArray();
        _byteOffset = byteOffset;
        _length = length;
        ValidateBounds();
    }

    /// <summary>
    /// Creates a typed array over a regular byte array.
    /// </summary>
    protected SharpTSTypedArray(byte[] buffer, int byteOffset, int length)
    {
        _buffer = buffer;
        _byteOffset = byteOffset;
        _length = length;
        _sharedBuffer = null;
        _arrayBuffer = null;
        ValidateBounds();
    }

    /// <summary>
    /// Creates a typed array with a new internal buffer.
    /// </summary>
    protected SharpTSTypedArray(int length)
    {
        _length = length;
        _byteOffset = 0;
        _buffer = new byte[length * BytesPerElement];
        _sharedBuffer = null;
        _arrayBuffer = null;
    }

    private void ValidateBounds()
    {
        if (_byteOffset < 0)
            throw new Exception("RangeError: byteOffset cannot be negative");
        if (_byteOffset % BytesPerElement != 0)
            throw new Exception($"RangeError: byteOffset must be a multiple of {BytesPerElement}");
        if (_byteOffset + _length * BytesPerElement > _buffer.Length)
            throw new Exception("RangeError: buffer too small for specified view");
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public abstract object? this[int index] { get; set; }

    /// <summary>
    /// Gets or sets an element using volatile semantics (for Atomics operations on shared buffers).
    /// </summary>
    public abstract object? GetVolatile(int index);

    /// <summary>
    /// Sets an element using volatile semantics (for Atomics operations on shared buffers).
    /// </summary>
    public abstract void SetVolatile(int index, object? value);

    /// <summary>
    /// Gets the byte offset for an element index.
    /// </summary>
    protected int GetByteIndex(int index)
    {
        if (index < 0 || index >= _length)
            throw new Exception("RangeError: Index out of bounds");
        return _byteOffset + index * BytesPerElement;
    }

    /// <summary>
    /// Allocates a new, zero-initialized typed array of this concrete element type.
    /// </summary>
    protected abstract SharpTSTypedArray Allocate(int length);

    /// <summary>
    /// Creates a view of this concrete element type over the given SharedArrayBuffer.
    /// </summary>
    protected abstract SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length);

    /// <summary>
    /// Creates a view of this concrete element type over the given ArrayBuffer.
    /// </summary>
    protected abstract SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length);

    /// <summary>
    /// Creates a view of this concrete element type over the given raw byte buffer.
    /// </summary>
    protected abstract SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length);

    /// <summary>
    /// Resolves JS slice/subarray bounds (negative-from-end, clamped to [0, length])
    /// to a concrete (start, count) pair.
    /// </summary>
    private (int start, int count) ClampRange(int begin, int? end)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        return (begin, count);
    }

    /// <summary>
    /// Creates a new typed array containing a copy of a portion of this array.
    /// </summary>
    public SharpTSTypedArray Slice(int begin, int? end = null)
    {
        var (start, count) = ClampRange(begin, end);
        var result = Allocate(count);
        BlockCopyElementsInto(result, start, count);
        return result;
    }

    /// <summary>
    /// Creates a new typed array view of the same buffer with specified bounds.
    /// </summary>
    public SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        var (start, count) = ClampRange(begin, end);
        int viewByteOffset = _byteOffset + start * BytesPerElement;
        if (_sharedBuffer != null)
            return CreateView(_sharedBuffer, viewByteOffset, count);
        if (_arrayBuffer != null)
            return CreateView(_arrayBuffer, viewByteOffset, count);
        return CreateView(_buffer, viewByteOffset, count);
    }

    /// <summary>
    /// Sets values from an array or typed array, starting at the specified offset.
    /// </summary>
    public void Set(object source, int offset = 0)
    {
        if (source is SharpTSTypedArray typedSource)
        {
            if (offset + typedSource.Length > _length)
                throw new Exception("RangeError: Source too large for target");

            // Same element type → bit-exact bulk copy on the byte[] backing, no per-element
            // boxing or double round-trip. (A different element type still needs the
            // value-converting element-wise path below; a negative offset falls through so the
            // element setter raises the same RangeError as before.)
            if (typedSource.GetType() == GetType() && offset >= 0)
            {
                int bpe = BytesPerElement;
                System.Buffer.BlockCopy(
                    typedSource._buffer, typedSource._byteOffset,
                    _buffer, _byteOffset + offset * bpe,
                    typedSource._length * bpe);
                return;
            }

            for (int i = 0; i < typedSource.Length; i++)
            {
                this[offset + i] = typedSource[i];
            }
        }
        else if (source is SharpTSArray array)
        {
            if (offset + array.Length > _length)
                throw new Exception("RangeError: Source too large for target");

            for (int i = 0; i < array.Length; i++)
            {
                this[offset + i] = array[i];
            }
        }
        else
        {
            throw new Exception("TypeError: Invalid source type for TypedArray.set");
        }
    }

    /// <summary>
    /// Fills all elements with a value.
    /// </summary>
    public SharpTSTypedArray Fill(object? value, int start = 0, int? end = null)
    {
        int actualEnd = end ?? _length;
        start = Math.Max(0, Math.Min(start, _length));
        actualEnd = Math.Max(start, Math.Min(actualEnd, _length));
        if (actualEnd <= start)
            return this;

        // Convert the value to the element representation exactly once (via the typed
        // setter, which applies the per-type coercion/clamping), then replicate its raw
        // bytes across the range with an exponential-doubling copy — no boxing or
        // re-conversion per element. Interop-safe: same byte[] backing.
        this[start] = value;
        int bpe = BytesPerElement;
        var region = _buffer.AsSpan(_byteOffset + start * bpe, (actualEnd - start) * bpe);
        int filled = bpe;
        while (filled < region.Length)
        {
            int chunk = Math.Min(filled, region.Length - filled);
            region.Slice(0, chunk).CopyTo(region.Slice(filled, chunk));
            filled += chunk;
        }

        return this;
    }

    /// <summary>
    /// Copies elements within the array.
    /// </summary>
    public SharpTSTypedArray CopyWithin(int target, int start, int? end = null)
    {
        int actualEnd = end ?? _length;
        target = Math.Max(0, Math.Min(target, _length));
        start = Math.Max(0, Math.Min(start, _length));
        actualEnd = Math.Max(start, Math.Min(actualEnd, _length));

        int count = Math.Min(actualEnd - start, _length - target);
        if (count <= 0)
            return this;

        // Byte-level memmove on the backing buffer. Span.CopyTo handles overlapping
        // source/destination regions correctly, so no boxed temporary is needed.
        int bpe = BytesPerElement;
        var buf = _buffer.AsSpan();
        buf.Slice(_byteOffset + start * bpe, count * bpe)
           .CopyTo(buf.Slice(_byteOffset + target * bpe, count * bpe));

        return this;
    }

    /// <summary>
    /// Reverses the array in place.
    /// </summary>
    public SharpTSTypedArray Reverse()
    {
        int bpe = BytesPerElement;
        var buf = _buffer.AsSpan();
        Span<byte> temp = stackalloc byte[8]; // max BytesPerElement (Float64/BigInt64) is 8
        int left = 0;
        int right = _length - 1;

        while (left < right)
        {
            // Swap the two elements' raw bytes — no boxing or double round-trip.
            var ls = buf.Slice(_byteOffset + left * bpe, bpe);
            var rs = buf.Slice(_byteOffset + right * bpe, bpe);
            ls.CopyTo(temp);
            rs.CopyTo(ls);
            temp.Slice(0, bpe).CopyTo(rs);
            left++;
            right--;
        }

        return this;
    }

    /// <summary>
    /// Bit-exact byte copy of <paramref name="count"/> elements starting at element
    /// <paramref name="begin"/> in this array into <paramref name="dest"/> at element 0.
    /// <paramref name="dest"/> must share this array's concrete element type (the bytes are
    /// copied verbatim). Used by the per-subclass <see cref="Slice"/> to avoid a boxed
    /// element-by-element copy.
    /// </summary>
    protected void BlockCopyElementsInto(SharpTSTypedArray dest, int begin, int count)
    {
        if (count <= 0)
            return;
        int bpe = BytesPerElement;
        System.Buffer.BlockCopy(
            _buffer, _byteOffset + begin * bpe,
            dest._buffer, dest._byteOffset, count * bpe);
    }

    /// <summary>
    /// Returns the index of the first matching element, or -1 if not found.
    /// </summary>
    public double IndexOf(object? value, int fromIndex = 0)
    {
        fromIndex = Math.Max(0, fromIndex);

        for (int i = fromIndex; i < _length; i++)
        {
            if (ElementEquals(this[i], value))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns the index of the last matching element, or -1 if not found.
    /// </summary>
    public double LastIndexOf(object? value, int? fromIndex = null)
    {
        int start = fromIndex ?? _length - 1;
        start = Math.Min(start, _length - 1);

        for (int i = start; i >= 0; i--)
        {
            if (ElementEquals(this[i], value))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns whether the array includes the specified element.
    /// </summary>
    public bool Includes(object? value, int fromIndex = 0)
    {
        return IndexOf(value, fromIndex) >= 0;
    }

    /// <summary>
    /// Converts the typed array to a regular array.
    /// </summary>
    public SharpTSArray ToArray()
    {
        var elements = new List<object?>(_length);
        for (int i = 0; i < _length; i++)
        {
            elements.Add(this[i]);
        }
        return new SharpTSArray(elements);
    }

    private static bool ElementEquals(object? a, object? b)
    {
        if (a is double d1 && b is double d2)
            return d1 == d2 || (double.IsNaN(d1) && double.IsNaN(d2));
        return Equals(a, b);
    }

    /// <summary>
    /// Gets a member of this typed array (for property access).
    /// </summary>
    public virtual object? GetMember(string name)
    {
        return name switch
        {
            "length" => (double)Length,
            "byteLength" => (double)ByteLength,
            "byteOffset" => (double)ByteOffset,
            "BYTES_PER_ELEMENT" => (double)BytesPerElement,
            "buffer" => _sharedBuffer ?? _arrayBuffer ?? (object?)new SharpTSBuffer(_buffer),

            "set" => BuiltInMethod.CreateV2("set", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("TypedArray.set requires a source argument");
                int offset = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                Set(args[0].ToObject()!, offset);
                return RuntimeValue.Null;
            }),

            "slice" => BuiltInMethod.CreateV2("slice", 0, 2, (_, _, args) =>
            {
                int begin = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                return RuntimeValue.FromObject(Slice(begin, end));
            }),

            "subarray" => BuiltInMethod.CreateV2("subarray", 0, 2, (_, _, args) =>
            {
                int begin = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                return RuntimeValue.FromObject(Subarray(begin, end));
            }),

            "fill" => BuiltInMethod.CreateV2("fill", 1, 3, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("TypedArray.fill requires a value argument");
                int start = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                int? end = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : null;
                return RuntimeValue.FromObject(Fill(args[0].ToObject(), start, end));
            }),

            "copyWithin" => BuiltInMethod.CreateV2("copyWithin", 2, 3, (_, _, args) =>
            {
                if (args.Length < 2)
                    throw new Exception("TypedArray.copyWithin requires target and start arguments");
                int target = args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
                int start = args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                int? end = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : null;
                return RuntimeValue.FromObject(CopyWithin(target, start, end));
            }),

            "reverse" => BuiltInMethod.CreateV2("reverse", 0, (_, _, _) => RuntimeValue.FromObject(Reverse())),

            "indexOf" => BuiltInMethod.CreateV2("indexOf", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("TypedArray.indexOf requires a search element");
                int fromIndex = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                return RuntimeValue.FromNumber(IndexOf(args[0].ToObject(), fromIndex));
            }),

            "lastIndexOf" => BuiltInMethod.CreateV2("lastIndexOf", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("TypedArray.lastIndexOf requires a search element");
                int? fromIndex = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                return RuntimeValue.FromNumber(LastIndexOf(args[0].ToObject(), fromIndex));
            }),

            "includes" => BuiltInMethod.CreateV2("includes", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("TypedArray.includes requires a search element");
                int fromIndex = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
                return RuntimeValue.FromBoolean(Includes(args[0].ToObject(), fromIndex));
            }),

            "join" => BuiltInMethod.CreateV2("join", 0, 1, (_, _, args) =>
            {
                string separator = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "," : ",";
                var parts = new string[_length];
                for (int i = 0; i < _length; i++)
                {
                    parts[i] = this[i]?.ToString() ?? "";
                }
                return RuntimeValue.FromString(string.Join(separator, parts));
            }),

            "toString" => BuiltInMethod.CreateV2("toString", 0, (_, _, _) =>
            {
                var parts = new string[_length];
                for (int i = 0; i < _length; i++)
                {
                    parts[i] = this[i]?.ToString() ?? "";
                }
                return RuntimeValue.FromString(string.Join(",", parts));
            }),

            _ => null
        };
    }

    public override string ToString()
    {
        var elements = new string[Math.Min(_length, 10)];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = this[i]?.ToString() ?? "0";
        }
        string suffix = _length > 10 ? $", ... {_length - 10} more items" : "";
        return $"{TypeName}({_length}) [{string.Join(", ", elements)}{suffix}]";
    }
}

/// <summary>
/// 8-bit signed integer array.
/// </summary>
public class SharpTSInt8Array : SharpTSTypedArray
{
    public override int BytesPerElement => 1;
    public override string TypeName => "Int8Array";

    public SharpTSInt8Array(int length) : base(length) { }
    public SharpTSInt8Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset)) { }
    public SharpTSInt8Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset)) { }
    public SharpTSInt8Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset)) { }

    public override object? this[int index]
    {
        get => (double)(sbyte)_buffer[GetByteIndex(index)];
        set => _buffer[GetByteIndex(index)] = (byte)(sbyte)Convert.ToDouble(value);
    }

    public override object? GetVolatile(int index) =>
        (double)(sbyte)Volatile.Read(ref _buffer[GetByteIndex(index)]);

    public override void SetVolatile(int index, object? value) =>
        Volatile.Write(ref _buffer[GetByteIndex(index)], (byte)(sbyte)Convert.ToDouble(value));

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSInt8Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSInt8Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSInt8Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSInt8Array(buffer, byteOffset, length);
}

/// <summary>
/// 8-bit unsigned integer array.
/// </summary>
public class SharpTSUint8Array : SharpTSTypedArray
{
    public override int BytesPerElement => 1;
    public override string TypeName => "Uint8Array";

    public SharpTSUint8Array(int length) : base(length) { }
    public SharpTSUint8Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset)) { }
    public SharpTSUint8Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset)) { }
    public SharpTSUint8Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset)) { }

    public override object? this[int index]
    {
        get => (double)_buffer[GetByteIndex(index)];
        set => _buffer[GetByteIndex(index)] = (byte)Convert.ToDouble(value);
    }

    public override object? GetVolatile(int index) =>
        (double)Volatile.Read(ref _buffer[GetByteIndex(index)]);

    public override void SetVolatile(int index, object? value) =>
        Volatile.Write(ref _buffer[GetByteIndex(index)], (byte)Convert.ToDouble(value));

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSUint8Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint8Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint8Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSUint8Array(buffer, byteOffset, length);
}

/// <summary>
/// 8-bit unsigned clamped integer array.
/// </summary>
public class SharpTSUint8ClampedArray : SharpTSTypedArray
{
    public override int BytesPerElement => 1;
    public override string TypeName => "Uint8ClampedArray";

    public SharpTSUint8ClampedArray(int length) : base(length) { }
    public SharpTSUint8ClampedArray(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset)) { }
    public SharpTSUint8ClampedArray(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset)) { }
    public SharpTSUint8ClampedArray(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset)) { }

    public override object? this[int index]
    {
        get => (double)_buffer[GetByteIndex(index)];
        set
        {
            double val = Convert.ToDouble(value);
            _buffer[GetByteIndex(index)] = (byte)Math.Max(0, Math.Min(255, Math.Round(val)));
        }
    }

    public override object? GetVolatile(int index) =>
        (double)Volatile.Read(ref _buffer[GetByteIndex(index)]);

    public override void SetVolatile(int index, object? value)
    {
        double val = Convert.ToDouble(value);
        Volatile.Write(ref _buffer[GetByteIndex(index)], (byte)Math.Max(0, Math.Min(255, Math.Round(val))));
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSUint8ClampedArray(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint8ClampedArray(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint8ClampedArray(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSUint8ClampedArray(buffer, byteOffset, length);
}

/// <summary>
/// 16-bit signed integer array.
/// </summary>
public class SharpTSInt16Array : SharpTSTypedArray
{
    public override int BytesPerElement => 2;
    public override string TypeName => "Int16Array";

    public SharpTSInt16Array(int length) : base(length) { }
    public SharpTSInt16Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 2) { }
    public SharpTSInt16Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 2) { }
    public SharpTSInt16Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 2) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return (double)BitConverter.ToInt16(_buffer, byteIdx);
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], (short)Convert.ToDouble(value));
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref short slot = ref Unsafe.As<byte, short>(ref _buffer[byteIdx]);
        return (double)Volatile.Read(ref slot);
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref short slot = ref Unsafe.As<byte, short>(ref _buffer[byteIdx]);
        Volatile.Write(ref slot, (short)Convert.ToDouble(value));
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSInt16Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSInt16Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSInt16Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSInt16Array(buffer, byteOffset, length);
}

/// <summary>
/// 16-bit unsigned integer array.
/// </summary>
public class SharpTSUint16Array : SharpTSTypedArray
{
    public override int BytesPerElement => 2;
    public override string TypeName => "Uint16Array";

    public SharpTSUint16Array(int length) : base(length) { }
    public SharpTSUint16Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 2) { }
    public SharpTSUint16Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 2) { }
    public SharpTSUint16Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 2) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return (double)BitConverter.ToUInt16(_buffer, byteIdx);
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], (ushort)Convert.ToDouble(value));
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref ushort slot = ref Unsafe.As<byte, ushort>(ref _buffer[byteIdx]);
        return (double)Volatile.Read(ref slot);
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref ushort slot = ref Unsafe.As<byte, ushort>(ref _buffer[byteIdx]);
        Volatile.Write(ref slot, (ushort)Convert.ToDouble(value));
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSUint16Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint16Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint16Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSUint16Array(buffer, byteOffset, length);
}

/// <summary>
/// 32-bit signed integer array. Used by Atomics.wait/notify.
/// </summary>
public class SharpTSInt32Array : SharpTSTypedArray
{
    public override int BytesPerElement => 4;
    public override string TypeName => "Int32Array";

    public SharpTSInt32Array(int length) : base(length) { }
    public SharpTSInt32Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 4) { }
    public SharpTSInt32Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 4) { }
    public SharpTSInt32Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 4) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return (double)BitConverter.ToInt32(_buffer, byteIdx);
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], (int)Convert.ToDouble(value));
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref int slot = ref Unsafe.As<byte, int>(ref _buffer[byteIdx]);
        return (double)Volatile.Read(ref slot);
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref int slot = ref Unsafe.As<byte, int>(ref _buffer[byteIdx]);
        Volatile.Write(ref slot, (int)Convert.ToDouble(value));
    }

    /// <summary>
    /// Gets a reference to the int at the specified index (for Interlocked operations).
    /// </summary>
    internal ref int GetRef(int index)
    {
        int byteIdx = GetByteIndex(index);
        return ref Unsafe.As<byte, int>(ref _buffer[byteIdx]);
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSInt32Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSInt32Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSInt32Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSInt32Array(buffer, byteOffset, length);
}

/// <summary>
/// 32-bit unsigned integer array.
/// </summary>
public class SharpTSUint32Array : SharpTSTypedArray
{
    public override int BytesPerElement => 4;
    public override string TypeName => "Uint32Array";

    public SharpTSUint32Array(int length) : base(length) { }
    public SharpTSUint32Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 4) { }
    public SharpTSUint32Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 4) { }
    public SharpTSUint32Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 4) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return (double)BitConverter.ToUInt32(_buffer, byteIdx);
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], (uint)Convert.ToDouble(value));
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref uint slot = ref Unsafe.As<byte, uint>(ref _buffer[byteIdx]);
        return (double)Volatile.Read(ref slot);
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref uint slot = ref Unsafe.As<byte, uint>(ref _buffer[byteIdx]);
        Volatile.Write(ref slot, (uint)Convert.ToDouble(value));
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSUint32Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint32Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSUint32Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSUint32Array(buffer, byteOffset, length);
}

/// <summary>
/// 32-bit floating point array.
/// </summary>
public class SharpTSFloat32Array : SharpTSTypedArray
{
    public override int BytesPerElement => 4;
    public override string TypeName => "Float32Array";

    public SharpTSFloat32Array(int length) : base(length) { }
    public SharpTSFloat32Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 4) { }
    public SharpTSFloat32Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 4) { }
    public SharpTSFloat32Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 4) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return (double)BitConverter.ToSingle(_buffer, byteIdx);
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], (float)Convert.ToDouble(value));
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref int slot = ref Unsafe.As<byte, int>(ref _buffer[byteIdx]);
        return (double)BitConverter.Int32BitsToSingle(Volatile.Read(ref slot));
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref int slot = ref Unsafe.As<byte, int>(ref _buffer[byteIdx]);
        Volatile.Write(ref slot, BitConverter.SingleToInt32Bits((float)Convert.ToDouble(value)));
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSFloat32Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSFloat32Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSFloat32Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSFloat32Array(buffer, byteOffset, length);
}

/// <summary>
/// 64-bit floating point array.
/// </summary>
public class SharpTSFloat64Array : SharpTSTypedArray
{
    public override int BytesPerElement => 8;
    public override string TypeName => "Float64Array";

    public SharpTSFloat64Array(int length) : base(length) { }
    public SharpTSFloat64Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 8) { }
    public SharpTSFloat64Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 8) { }
    public SharpTSFloat64Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 8) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return BitConverter.ToDouble(_buffer, byteIdx);
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], Convert.ToDouble(value));
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref long slot = ref Unsafe.As<byte, long>(ref _buffer[byteIdx]);
        return BitConverter.Int64BitsToDouble(Volatile.Read(ref slot));
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref long slot = ref Unsafe.As<byte, long>(ref _buffer[byteIdx]);
        Volatile.Write(ref slot, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)));
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSFloat64Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSFloat64Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSFloat64Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSFloat64Array(buffer, byteOffset, length);
}

/// <summary>
/// 64-bit signed BigInt array.
/// </summary>
public class SharpTSBigInt64Array : SharpTSTypedArray
{
    public override int BytesPerElement => 8;
    public override string TypeName => "BigInt64Array";

    public SharpTSBigInt64Array(int length) : base(length) { }
    public SharpTSBigInt64Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 8) { }
    public SharpTSBigInt64Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 8) { }
    public SharpTSBigInt64Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 8) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return new System.Numerics.BigInteger(BitConverter.ToInt64(_buffer, byteIdx));
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            long val = value switch
            {
                System.Numerics.BigInteger bi => (long)bi,
                double d => (long)d,
                _ => Convert.ToInt64(value)
            };
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], val);
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref long slot = ref Unsafe.As<byte, long>(ref _buffer[byteIdx]);
        return new System.Numerics.BigInteger(Volatile.Read(ref slot));
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref long slot = ref Unsafe.As<byte, long>(ref _buffer[byteIdx]);
        long val = value switch
        {
            System.Numerics.BigInteger bi => (long)bi,
            double d => (long)d,
            _ => Convert.ToInt64(value)
        };
        Volatile.Write(ref slot, val);
    }

    /// <summary>
    /// Gets a reference to the long at the specified index (for Interlocked operations).
    /// </summary>
    internal ref long GetRef(int index)
    {
        int byteIdx = GetByteIndex(index);
        return ref Unsafe.As<byte, long>(ref _buffer[byteIdx]);
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSBigInt64Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSBigInt64Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSBigInt64Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSBigInt64Array(buffer, byteOffset, length);
}

/// <summary>
/// 64-bit unsigned BigInt array.
/// </summary>
public class SharpTSBigUint64Array : SharpTSTypedArray
{
    public override int BytesPerElement => 8;
    public override string TypeName => "BigUint64Array";

    public SharpTSBigUint64Array(int length) : base(length) { }
    public SharpTSBigUint64Array(SharpTSSharedArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 8) { }
    public SharpTSBigUint64Array(SharpTSArrayBuffer buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.ByteLength - byteOffset) / 8) { }
    public SharpTSBigUint64Array(byte[] buffer, int byteOffset = 0, int? length = null)
        : base(buffer, byteOffset, length ?? (buffer.Length - byteOffset) / 8) { }

    public override object? this[int index]
    {
        get
        {
            int byteIdx = GetByteIndex(index);
            return new System.Numerics.BigInteger(BitConverter.ToUInt64(_buffer, byteIdx));
        }
        set
        {
            int byteIdx = GetByteIndex(index);
            ulong val = value switch
            {
                System.Numerics.BigInteger bi => (ulong)bi,
                double d => (ulong)d,
                _ => Convert.ToUInt64(value)
            };
            Unsafe.WriteUnaligned(ref _buffer[byteIdx], val);
        }
    }

    public override object? GetVolatile(int index)
    {
        int byteIdx = GetByteIndex(index);
        ref ulong slot = ref Unsafe.As<byte, ulong>(ref _buffer[byteIdx]);
        return new System.Numerics.BigInteger(Volatile.Read(ref slot));
    }

    public override void SetVolatile(int index, object? value)
    {
        int byteIdx = GetByteIndex(index);
        ref ulong slot = ref Unsafe.As<byte, ulong>(ref _buffer[byteIdx]);
        ulong val = value switch
        {
            System.Numerics.BigInteger bi => (ulong)bi,
            double d => (ulong)d,
            _ => Convert.ToUInt64(value)
        };
        Volatile.Write(ref slot, val);
    }

    protected override SharpTSTypedArray Allocate(int length) =>
        new SharpTSBigUint64Array(length);

    protected override SharpTSTypedArray CreateView(SharpTSSharedArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSBigUint64Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(SharpTSArrayBuffer buffer, int byteOffset, int length) =>
        new SharpTSBigUint64Array(buffer, byteOffset, length);

    protected override SharpTSTypedArray CreateView(byte[] buffer, int byteOffset, int length) =>
        new SharpTSBigUint64Array(buffer, byteOffset, length);
}
