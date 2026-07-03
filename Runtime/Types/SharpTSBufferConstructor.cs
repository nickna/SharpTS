using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the global Buffer constructor/namespace object.
/// Provides static methods like Buffer.from(), Buffer.alloc(), etc.
/// </summary>
public sealed class SharpTSBufferConstructor
{
    /// <summary>
    /// The singleton instance of the Buffer constructor.
    /// </summary>
    public static readonly SharpTSBufferConstructor Instance = new();

    private SharpTSBufferConstructor() { }

    /// <summary>
    /// Buffer.poolSize — the internal pre-allocation pool size. SharpTS doesn't pool
    /// (every Buffer owns its byte[]), so this is purely informational; the default
    /// matches Node (8 KiB) and a write updates the stored value.
    /// </summary>
    public static double PoolSize { get; set; } = 8192;

    /// <summary>
    /// Gets a property from the Buffer namespace.
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "from" => BuiltInMethod.CreateV2("from", 1, 2, BufferFrom),
            "of" => BuiltInMethod.CreateV2("of", 0, int.MaxValue, BufferOf),
            "alloc" => BuiltInMethod.CreateV2("alloc", 1, 3, BufferAlloc),
            "allocUnsafe" => BuiltInMethod.CreateV2("allocUnsafe", 1, BufferAllocUnsafe),
            "allocUnsafeSlow" => BuiltInMethod.CreateV2("allocUnsafeSlow", 1, BufferAllocUnsafe), // Same as allocUnsafe
            "concat" => BuiltInMethod.CreateV2("concat", 1, 2, BufferConcat),
            "copyBytesFrom" => BuiltInMethod.CreateV2("copyBytesFrom", 1, 3, BufferCopyBytesFrom),
            "isBuffer" => BuiltInMethod.CreateV2("isBuffer", 1, BufferIsBuffer),
            "byteLength" => BuiltInMethod.CreateV2("byteLength", 1, 2, BufferByteLength),
            "compare" => BuiltInMethod.CreateV2("compare", 2, BufferCompare),
            "isEncoding" => BuiltInMethod.CreateV2("isEncoding", 1, BufferIsEncoding),
            "poolSize" => PoolSize,
            _ => null
        };
    }

    /// <summary>Sets a property on the Buffer namespace (currently only poolSize).</summary>
    public bool SetProperty(string name, object? value)
    {
        if (name == "poolSize" && value is double d)
        {
            PoolSize = d;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Buffer.of(...bytes) — creates a Buffer from the given byte values.
    /// </summary>
    private static RuntimeValue BufferOf(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var items = new List<object?>(args.Length);
        foreach (var a in args)
            items.Add(a.ToObject());
        return RuntimeValue.FromObject(SharpTSBuffer.FromArray(items));
    }

    /// <summary>
    /// Buffer.copyBytesFrom(view[, offset[, length]]) (Node 19+) — copies the underlying
    /// bytes of a TypedArray view (offset/length are in view elements) into a new Buffer.
    /// </summary>
    private static RuntimeValue BufferCopyBytesFrom(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not SharpTSTypedArray view)
            throw new Exception("Buffer.copyBytesFrom requires a TypedArray argument");

        int elementSize = view.BytesPerElement;
        int offset = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
        int length = args.Length > 2 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : view.Length - offset;

        if (offset < 0) offset = 0;
        if (length < 0) length = 0;
        if (offset + length > view.Length) length = Math.Max(0, view.Length - offset);

        int byteStart = view.ByteOffset + offset * elementSize;
        int byteLen = length * elementSize;
        var bytes = new byte[byteLen];
        Array.Copy(view.Buffer, byteStart, bytes, 0, byteLen);
        return RuntimeValue.FromObject(new SharpTSBuffer(bytes));
    }

    /// <summary>
    /// Buffer.from(data, encoding?)
    /// Creates a Buffer from string, array, or another buffer.
    /// </summary>
    private static RuntimeValue BufferFrom(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].IsNull)
            throw new Exception("Buffer.from requires an argument");

        var data = args[0].ToObject()!;  // Null check is above
        var encoding = args.Length > 1 ? args[1].ToObject()?.ToString() ?? "utf8" : "utf8";

        return RuntimeValue.FromObject(data switch
        {
            string s => SharpTSBuffer.FromString(s, encoding),
            SharpTSBuffer buf => SharpTSBuffer.FromBuffer(buf),
            SharpTSArray arr => SharpTSBuffer.FromArray(arr),
            List<object?> list => SharpTSBuffer.FromArray(list),
            // Buffer.from(arrayBuffer) / Buffer.from(typedArray) (#1063) — copies
            // (Node copies for TypedArray; for ArrayBuffer Node shares memory — we copy).
            SharpTSArrayBuffer ab => new SharpTSBuffer(ab.AsSpan().ToArray()),
            SharpTSTypedArray typed => new SharpTSBuffer(typed.Buffer.AsSpan(typed.ByteOffset, typed.ByteLength).ToArray()),
            _ => throw new Exception($"Buffer.from: unsupported data type: {data.GetType().Name}")
        });
    }

    /// <summary>
    /// Buffer.alloc(size, fill?, encoding?)
    /// Allocates a new zero-filled Buffer of the specified size.
    /// </summary>
    private static RuntimeValue BufferAlloc(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsNumber)
            throw new Exception("Buffer.alloc requires a size argument");

        var fill = args.Length > 1 ? args[1].ToObject() : null;
        var encoding = args.Length > 2 && args[2].IsString ? args[2].AsStringUnsafe() : "utf8";

        return RuntimeValue.FromObject(SharpTSBuffer.Alloc((int)args[0].AsNumberUnsafe(), fill, encoding));
    }

    /// <summary>
    /// Buffer.allocUnsafe(size)
    /// Allocates a new uninitialized Buffer of the specified size.
    /// </summary>
    private static RuntimeValue BufferAllocUnsafe(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsNumber)
            throw new Exception("Buffer.allocUnsafe requires a size argument");

        return RuntimeValue.FromObject(SharpTSBuffer.AllocUnsafe((int)args[0].AsNumberUnsafe()));
    }

    /// <summary>
    /// Buffer.concat(list, totalLength?)
    /// Concatenates a list of Buffers.
    /// </summary>
    private static RuntimeValue BufferConcat(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            return RuntimeValue.FromObject(new SharpTSBuffer(0));

        IReadOnlyCollection<object?> buffers;
        if (args[0].ToObject() is SharpTSArray arr)
        {
            buffers = arr;
        }
        else if (args[0].ToObject() is IReadOnlyCollection<object?> coll)
        {
            buffers = coll;
        }
        else
        {
            return RuntimeValue.FromObject(new SharpTSBuffer(0));
        }

        int? totalLength = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;

        return RuntimeValue.FromObject(SharpTSBuffer.Concat(buffers, totalLength));
    }

    /// <summary>
    /// Buffer.isBuffer(obj)
    /// Checks if the given object is a Buffer.
    /// </summary>
    private static RuntimeValue BufferIsBuffer(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            return RuntimeValue.False;

        return RuntimeValue.FromBoolean(args[0].ToObject() is SharpTSBuffer);
    }

    /// <summary>
    /// Buffer.byteLength(string, encoding?)
    /// Returns the byte length of a string when encoded.
    /// </summary>
    private static RuntimeValue BufferByteLength(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("Buffer.byteLength requires an argument");

        if (args[0].ToObject() is SharpTSBuffer buf)
            return RuntimeValue.FromNumber(buf.Length);

        if (!args[0].IsString)
            throw new Exception("Buffer.byteLength requires a string or Buffer argument");

        var encoding = args.Length > 1 ? args[1].ToObject()?.ToString() ?? "utf8" : "utf8";

        // Create a temporary buffer to get the byte length
        var temp = SharpTSBuffer.FromString(args[0].AsStringUnsafe(), encoding);
        return RuntimeValue.FromNumber(temp.Length);
    }

    /// <summary>
    /// Buffer.compare(buf1, buf2)
    /// Compares two Buffers.
    /// </summary>
    private static RuntimeValue BufferCompare(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2 || args[0].ToObject() is not SharpTSBuffer buf1 || args[1].ToObject() is not SharpTSBuffer buf2)
            throw new Exception("Buffer.compare requires two Buffer arguments");

        return RuntimeValue.FromNumber(buf1.Compare(buf2));
    }

    /// <summary>
    /// Buffer.isEncoding(encoding)
    /// Checks if the encoding is supported.
    /// </summary>
    private static RuntimeValue BufferIsEncoding(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            return RuntimeValue.False;

        return RuntimeValue.FromBoolean(args[0].AsStringUnsafe().ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => true,
            "ascii" => true,
            "base64" => true,
            "hex" => true,
            "latin1" or "binary" => true,
            "ucs2" or "ucs-2" or "utf16le" or "utf-16le" => true,
            _ => false
        });
    }

    public override string ToString() => "[Function: Buffer]";
}
