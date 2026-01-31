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
    /// Gets the underlying buffer as a byte array.
    /// </summary>
    internal byte[] Buffer => _buffer;

    /// <summary>
    /// Creates a typed array over a SharedArrayBuffer.
    /// </summary>
    protected SharpTSTypedArray(SharpTSSharedArrayBuffer buffer, int byteOffset, int length)
    {
        _sharedBuffer = buffer;
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
    /// Creates a new typed array containing a copy of a portion of this array.
    /// </summary>
    public abstract SharpTSTypedArray Slice(int begin, int? end = null);

    /// <summary>
    /// Creates a new typed array view of the same buffer with specified bounds.
    /// </summary>
    public abstract SharpTSTypedArray Subarray(int begin, int? end = null);

    /// <summary>
    /// Sets values from an array or typed array, starting at the specified offset.
    /// </summary>
    public void Set(object source, int offset = 0)
    {
        if (source is SharpTSTypedArray typedSource)
        {
            if (offset + typedSource.Length > _length)
                throw new Exception("RangeError: Source too large for target");

            for (int i = 0; i < typedSource.Length; i++)
            {
                this[offset + i] = typedSource[i];
            }
        }
        else if (source is SharpTSArray array)
        {
            if (offset + array.Elements.Count > _length)
                throw new Exception("RangeError: Source too large for target");

            for (int i = 0; i < array.Elements.Count; i++)
            {
                this[offset + i] = array.Elements[i];
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

        for (int i = start; i < actualEnd; i++)
        {
            this[i] = value;
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

        // Create temporary copy to handle overlapping regions
        var temp = new object?[count];
        for (int i = 0; i < count; i++)
        {
            temp[i] = this[start + i];
        }

        for (int i = 0; i < count; i++)
        {
            this[target + i] = temp[i];
        }

        return this;
    }

    /// <summary>
    /// Reverses the array in place.
    /// </summary>
    public SharpTSTypedArray Reverse()
    {
        int left = 0;
        int right = _length - 1;

        while (left < right)
        {
            var temp = this[left];
            this[left] = this[right];
            this[right] = temp;
            left++;
            right--;
        }

        return this;
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
            "buffer" => _sharedBuffer ?? (object?)new SharpTSBuffer(_buffer),

            "set" => new BuiltInMethod("set", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("TypedArray.set requires a source argument");
                int offset = args.Count > 1 && args[1] is double o ? (int)o : 0;
                Set(args[0]!, offset);
                return null;
            }),

            "slice" => new BuiltInMethod("slice", 0, 2, (interp, recv, args) =>
            {
                int begin = args.Count > 0 && args[0] is double b ? (int)b : 0;
                int? end = args.Count > 1 && args[1] is double e ? (int)e : null;
                return Slice(begin, end);
            }),

            "subarray" => new BuiltInMethod("subarray", 0, 2, (interp, recv, args) =>
            {
                int begin = args.Count > 0 && args[0] is double b ? (int)b : 0;
                int? end = args.Count > 1 && args[1] is double e ? (int)e : null;
                return Subarray(begin, end);
            }),

            "fill" => new BuiltInMethod("fill", 1, 3, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("TypedArray.fill requires a value argument");
                int start = args.Count > 1 && args[1] is double s ? (int)s : 0;
                int? end = args.Count > 2 && args[2] is double e ? (int)e : null;
                return Fill(args[0], start, end);
            }),

            "copyWithin" => new BuiltInMethod("copyWithin", 2, 3, (interp, recv, args) =>
            {
                if (args.Count < 2)
                    throw new Exception("TypedArray.copyWithin requires target and start arguments");
                int target = args[0] is double t ? (int)t : 0;
                int start = args[1] is double s ? (int)s : 0;
                int? end = args.Count > 2 && args[2] is double e ? (int)e : null;
                return CopyWithin(target, start, end);
            }),

            "reverse" => new BuiltInMethod("reverse", 0, (interp, recv, args) => Reverse()),

            "indexOf" => new BuiltInMethod("indexOf", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("TypedArray.indexOf requires a search element");
                int fromIndex = args.Count > 1 && args[1] is double f ? (int)f : 0;
                return IndexOf(args[0], fromIndex);
            }),

            "lastIndexOf" => new BuiltInMethod("lastIndexOf", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("TypedArray.lastIndexOf requires a search element");
                int? fromIndex = args.Count > 1 && args[1] is double f ? (int)f : null;
                return LastIndexOf(args[0], fromIndex);
            }),

            "includes" => new BuiltInMethod("includes", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("TypedArray.includes requires a search element");
                int fromIndex = args.Count > 1 && args[1] is double f ? (int)f : 0;
                return Includes(args[0], fromIndex);
            }),

            "join" => new BuiltInMethod("join", 0, 1, (interp, recv, args) =>
            {
                string separator = args.Count > 0 ? args[0]?.ToString() ?? "," : ",";
                var parts = new string[_length];
                for (int i = 0; i < _length; i++)
                {
                    parts[i] = this[i]?.ToString() ?? "";
                }
                return string.Join(separator, parts);
            }),

            "toString" => new BuiltInMethod("toString", 0, (interp, recv, args) =>
            {
                var parts = new string[_length];
                for (int i = 0; i < _length; i++)
                {
                    parts[i] = this[i]?.ToString() ?? "";
                }
                return string.Join(",", parts);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSInt8Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSInt8Array(_sharedBuffer, _byteOffset + begin, count);
        return new SharpTSInt8Array(_buffer, _byteOffset + begin, count);
    }
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSUint8Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSUint8Array(_sharedBuffer, _byteOffset + begin, count);
        return new SharpTSUint8Array(_buffer, _byteOffset + begin, count);
    }
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSUint8ClampedArray(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSUint8ClampedArray(_sharedBuffer, _byteOffset + begin, count);
        return new SharpTSUint8ClampedArray(_buffer, _byteOffset + begin, count);
    }
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
            var bytes = BitConverter.GetBytes((short)Convert.ToDouble(value));
            _buffer[byteIdx] = bytes[0];
            _buffer[byteIdx + 1] = bytes[1];
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSInt16Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSInt16Array(_sharedBuffer, _byteOffset + begin * 2, count);
        return new SharpTSInt16Array(_buffer, _byteOffset + begin * 2, count);
    }
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
            var bytes = BitConverter.GetBytes((ushort)Convert.ToDouble(value));
            _buffer[byteIdx] = bytes[0];
            _buffer[byteIdx + 1] = bytes[1];
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSUint16Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSUint16Array(_sharedBuffer, _byteOffset + begin * 2, count);
        return new SharpTSUint16Array(_buffer, _byteOffset + begin * 2, count);
    }
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
            var bytes = BitConverter.GetBytes((int)Convert.ToDouble(value));
            Array.Copy(bytes, 0, _buffer, byteIdx, 4);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSInt32Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSInt32Array(_sharedBuffer, _byteOffset + begin * 4, count);
        return new SharpTSInt32Array(_buffer, _byteOffset + begin * 4, count);
    }
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
            var bytes = BitConverter.GetBytes((uint)Convert.ToDouble(value));
            Array.Copy(bytes, 0, _buffer, byteIdx, 4);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSUint32Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSUint32Array(_sharedBuffer, _byteOffset + begin * 4, count);
        return new SharpTSUint32Array(_buffer, _byteOffset + begin * 4, count);
    }
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
            var bytes = BitConverter.GetBytes((float)Convert.ToDouble(value));
            Array.Copy(bytes, 0, _buffer, byteIdx, 4);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSFloat32Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSFloat32Array(_sharedBuffer, _byteOffset + begin * 4, count);
        return new SharpTSFloat32Array(_buffer, _byteOffset + begin * 4, count);
    }
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
            var bytes = BitConverter.GetBytes(Convert.ToDouble(value));
            Array.Copy(bytes, 0, _buffer, byteIdx, 8);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSFloat64Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSFloat64Array(_sharedBuffer, _byteOffset + begin * 8, count);
        return new SharpTSFloat64Array(_buffer, _byteOffset + begin * 8, count);
    }
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
            var bytes = BitConverter.GetBytes(val);
            Array.Copy(bytes, 0, _buffer, byteIdx, 8);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSBigInt64Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSBigInt64Array(_sharedBuffer, _byteOffset + begin * 8, count);
        return new SharpTSBigInt64Array(_buffer, _byteOffset + begin * 8, count);
    }
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
            var bytes = BitConverter.GetBytes(val);
            Array.Copy(bytes, 0, _buffer, byteIdx, 8);
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

    public override SharpTSTypedArray Slice(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        var result = new SharpTSBigUint64Array(count);
        for (int i = 0; i < count; i++) result[i] = this[begin + i];
        return result;
    }

    public override SharpTSTypedArray Subarray(int begin, int? end = null)
    {
        begin = begin < 0 ? Math.Max(_length + begin, 0) : Math.Min(begin, _length);
        int actualEnd = end.HasValue
            ? (end.Value < 0 ? Math.Max(_length + end.Value, 0) : Math.Min(end.Value, _length))
            : _length;
        int count = Math.Max(0, actualEnd - begin);
        if (_sharedBuffer != null)
            return new SharpTSBigUint64Array(_sharedBuffer, _byteOffset + begin * 8, count);
        return new SharpTSBigUint64Array(_buffer, _byteOffset + begin * 8, count);
    }
}
