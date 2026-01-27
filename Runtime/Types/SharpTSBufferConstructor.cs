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
    /// Gets a property from the Buffer namespace.
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "from" => new BuiltInMethod("from", 1, 2, BufferFrom),
            "alloc" => new BuiltInMethod("alloc", 1, 3, BufferAlloc),
            "allocUnsafe" => new BuiltInMethod("allocUnsafe", 1, BufferAllocUnsafe),
            "allocUnsafeSlow" => new BuiltInMethod("allocUnsafeSlow", 1, BufferAllocUnsafe), // Same as allocUnsafe
            "concat" => new BuiltInMethod("concat", 1, 2, BufferConcat),
            "isBuffer" => new BuiltInMethod("isBuffer", 1, BufferIsBuffer),
            "byteLength" => new BuiltInMethod("byteLength", 1, 2, BufferByteLength),
            "compare" => new BuiltInMethod("compare", 2, BufferCompare),
            "isEncoding" => new BuiltInMethod("isEncoding", 1, BufferIsEncoding),
            _ => null
        };
    }

    /// <summary>
    /// Buffer.from(data, encoding?)
    /// Creates a Buffer from string, array, or another buffer.
    /// </summary>
    private static object? BufferFrom(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
            throw new Exception("Buffer.from requires an argument");

        var data = args[0]!;  // Null check is above
        var encoding = args.Count > 1 ? args[1]?.ToString() ?? "utf8" : "utf8";

        return data switch
        {
            string s => SharpTSBuffer.FromString(s, encoding),
            SharpTSBuffer buf => SharpTSBuffer.FromBuffer(buf),
            SharpTSArray arr => SharpTSBuffer.FromArray(arr.Elements),
            List<object?> list => SharpTSBuffer.FromArray(list),
            _ => throw new Exception($"Buffer.from: unsupported data type: {data.GetType().Name}")
        };
    }

    /// <summary>
    /// Buffer.alloc(size, fill?, encoding?)
    /// Allocates a new zero-filled Buffer of the specified size.
    /// </summary>
    private static object? BufferAlloc(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not double size)
            throw new Exception("Buffer.alloc requires a size argument");

        var fill = args.Count > 1 ? args[1] : null;
        var encoding = args.Count > 2 && args[2] is string enc ? enc : "utf8";

        return SharpTSBuffer.Alloc((int)size, fill, encoding);
    }

    /// <summary>
    /// Buffer.allocUnsafe(size)
    /// Allocates a new uninitialized Buffer of the specified size.
    /// </summary>
    private static object? BufferAllocUnsafe(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not double size)
            throw new Exception("Buffer.allocUnsafe requires a size argument");

        return SharpTSBuffer.AllocUnsafe((int)size);
    }

    /// <summary>
    /// Buffer.concat(list, totalLength?)
    /// Concatenates a list of Buffers.
    /// </summary>
    private static object? BufferConcat(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return new SharpTSBuffer(0);

        List<object?> buffers;
        if (args[0] is SharpTSArray arr)
        {
            buffers = arr.Elements;
        }
        else if (args[0] is List<object?> list)
        {
            buffers = list;
        }
        else
        {
            return new SharpTSBuffer(0);
        }

        int? totalLength = args.Count > 1 && args[1] is double tl ? (int)tl : null;

        return SharpTSBuffer.Concat(buffers, totalLength);
    }

    /// <summary>
    /// Buffer.isBuffer(obj)
    /// Checks if the given object is a Buffer.
    /// </summary>
    private static object? BufferIsBuffer(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return false;

        return args[0] is SharpTSBuffer;
    }

    /// <summary>
    /// Buffer.byteLength(string, encoding?)
    /// Returns the byte length of a string when encoded.
    /// </summary>
    private static object? BufferByteLength(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Buffer.byteLength requires an argument");

        if (args[0] is SharpTSBuffer buf)
            return (double)buf.Length;

        if (args[0] is not string str)
            throw new Exception("Buffer.byteLength requires a string or Buffer argument");

        var encoding = args.Count > 1 ? args[1]?.ToString() ?? "utf8" : "utf8";

        // Create a temporary buffer to get the byte length
        var temp = SharpTSBuffer.FromString(str, encoding);
        return (double)temp.Length;
    }

    /// <summary>
    /// Buffer.compare(buf1, buf2)
    /// Compares two Buffers.
    /// </summary>
    private static object? BufferCompare(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2 || args[0] is not SharpTSBuffer buf1 || args[1] is not SharpTSBuffer buf2)
            throw new Exception("Buffer.compare requires two Buffer arguments");

        return (double)buf1.Compare(buf2);
    }

    /// <summary>
    /// Buffer.isEncoding(encoding)
    /// Checks if the encoding is supported.
    /// </summary>
    private static object? BufferIsEncoding(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string encoding)
            return false;

        return encoding.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => true,
            "ascii" => true,
            "base64" => true,
            "hex" => true,
            "latin1" or "binary" => true,
            "ucs2" or "ucs-2" or "utf16le" or "utf-16le" => true,
            _ => false
        };
    }

    public override string ToString() => "[Function: Buffer]";
}
