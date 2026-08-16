using System.Text;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the WHATWG/Node.js <c>Blob</c> — an immutable bag of
/// bytes with a MIME <c>type</c>. Backed by a flat managed byte[] (pure BCL,
/// standalone). Reused by <c>buffer.Blob</c>, the global <c>Blob</c>, and (future)
/// <c>fs.openAsBlob</c> / <c>Response.body</c>.
/// </summary>
public class SharpTSBlob
{
    private readonly byte[] _data;
    private readonly string _type;

    /// <summary>The blob's bytes (internal; callers must not mutate).</summary>
    public byte[] Data => _data;

    /// <summary>Size of the blob in bytes.</summary>
    public int Size => _data.Length;

    /// <summary>The blob's MIME type (lowercased), or "" if none.</summary>
    public string Type => _type;

    public SharpTSBlob(byte[] data, string type)
    {
        _data = data ?? [];
        _type = type ?? "";
    }

    /// <summary>
    /// Builds a Blob from a <c>parts</c> sequence (strings, Buffers, TypedArrays,
    /// ArrayBuffers, DataViews, or other Blobs) honoring the <c>type</c> and
    /// <c>endings</c> options.
    /// </summary>
    public static SharpTSBlob FromParts(IEnumerable<object?>? parts, string type, string endings)
    {
        var ms = new MemoryStream();
        if (parts != null)
        {
            bool native = string.Equals(endings, "native", StringComparison.OrdinalIgnoreCase);
            foreach (var part in parts)
                AppendPart(ms, part, native);
        }
        // The Blob type is stored lowercased; an invalid type (non-ASCII-printable)
        // becomes "" per spec, but we keep it lenient and just lowercase.
        return new SharpTSBlob(ms.ToArray(), NormalizeType(type));
    }

    private static void AppendPart(MemoryStream ms, object? part, bool nativeEndings)
    {
        switch (part)
        {
            case null:
                AppendString(ms, "null", nativeEndings);
                break;
            case string s:
                AppendString(ms, s, nativeEndings);
                break;
            case SharpTSBlob blob:
                ms.Write(blob._data, 0, blob._data.Length);
                break;
            case SharpTSBuffer buf:
                ms.Write(buf.Data, 0, buf.Data.Length);
                break;
            case SharpTSTypedArray ta:
                ms.Write(ta.Buffer, ta.ByteOffset, ta.ByteLength);
                break;
            case SharpTSArrayBuffer ab:
                var abBytes = ab.GetBackingArray();
                ms.Write(abBytes, 0, abBytes.Length);
                break;
            default:
                AppendString(ms, part.ToString() ?? "", nativeEndings);
                break;
        }
    }

    private static void AppendString(MemoryStream ms, string s, bool nativeEndings)
    {
        if (nativeEndings)
            s = s.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        var bytes = Encoding.UTF8.GetBytes(s);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static string NormalizeType(string? type)
        => string.IsNullOrEmpty(type) ? "" : type.ToLowerInvariant();

    /// <summary>
    /// slice([start][, end][, contentType]) — a new Blob over a byte window
    /// (negative indices count from the end), with an optionally different type.
    /// </summary>
    public SharpTSBlob Slice(int? start, int? end, string? contentType)
    {
        int len = _data.Length;
        int s = start ?? 0;
        int e = end ?? len;
        int actualStart = s < 0 ? Math.Max(0, len + s) : Math.Min(s, len);
        int actualEnd = e < 0 ? Math.Max(0, len + e) : Math.Min(e, len);
        if (actualStart >= actualEnd)
            return new SharpTSBlob([], contentType ?? "");

        var sliced = new byte[actualEnd - actualStart];
        Array.Copy(_data, actualStart, sliced, 0, sliced.Length);
        return new SharpTSBlob(sliced, contentType ?? "");
    }

    /// <summary>Member dispatch for interpreter property access.</summary>
    public virtual object? GetMember(string name)
    {
        switch (name)
        {
            case "size": return (double)Size;
            case "type": return Type;

            case "arrayBuffer":
                return new BuiltInAsyncMethod("arrayBuffer", 0,
                    (interp, receiver, args) => Task.FromResult<object?>(ToArrayBuffer())).Bind(this);

            case "bytes":
                return new BuiltInAsyncMethod("bytes", 0,
                    (interp, receiver, args) => Task.FromResult<object?>(new SharpTSUint8Array(CopyData()))).Bind(this);

            case "text":
                return new BuiltInAsyncMethod("text", 0,
                    (interp, receiver, args) => Task.FromResult<object?>(Encoding.UTF8.GetString(_data))).Bind(this);

            case "slice":
                return BuiltInMethod.CreateV2("slice", 0, 3, (_, _, args) =>
                {
                    int? start = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : null;
                    int? end = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : null;
                    string? contentType = args.Length > 2 && args[2].IsString ? args[2].AsStringUnsafe() : null;
                    return RuntimeValue.FromObject(Slice(start, end, contentType));
                });

            case "stream":
                return BuiltInMethod.CreateV2("stream", 0, (interp, _, _) =>
                    RuntimeValue.FromObject(CreateStream(interp)));

            default:
                return null;
        }
    }

    private SharpTSArrayBuffer ToArrayBuffer()
    {
        var ab = new SharpTSArrayBuffer(_data.Length);
        _data.CopyTo(ab.AsSpan());
        return ab;
    }

    private byte[] CopyData()
    {
        var copy = new byte[_data.Length];
        Array.Copy(_data, copy, _data.Length);
        return copy;
    }

    /// <summary>
    /// stream() — a WHATWG ReadableStream that emits the blob's bytes as a single
    /// Uint8Array chunk then closes. Supports <c>for await</c>.
    /// </summary>
    private SharpTSReadableStream CreateStream(Interp interpreter)
    {
        var stream = new SharpTSReadableStream(interpreter, underlyingSource: null, strategy: null);
        if (_data.Length > 0)
            stream.EnqueueInternal(new SharpTSUint8Array(CopyData()));
        stream.CloseInternal();
        return stream;
    }

    public override string ToString() => $"[object Blob]";
}

/// <summary>
/// Runtime representation of the WHATWG/Node.js <c>File</c> — a <c>Blob</c> with a
/// <c>name</c> and <c>lastModified</c> timestamp.
/// </summary>
public sealed class SharpTSFile : SharpTSBlob
{
    public string Name { get; }
    public double LastModified { get; }
    public string WebkitRelativePath { get; }

    public SharpTSFile(byte[] data, string name, string type, double lastModified, string webkitRelativePath = "")
        : base(data, type)
    {
        Name = name ?? "";
        LastModified = lastModified;
        WebkitRelativePath = webkitRelativePath ?? "";
    }

    public static SharpTSFile FromParts(IEnumerable<object?>? parts, string name, string type, double lastModified, string endings)
    {
        var blob = SharpTSBlob.FromParts(parts, type, endings);
        return new SharpTSFile(blob.Data, name, blob.Type, lastModified);
    }

    public override object? GetMember(string name)
    {
        return name switch
        {
            "name" => Name,
            "lastModified" => LastModified,
            "webkitRelativePath" => WebkitRelativePath,
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => $"[object File]";
}

/// <summary>
/// Value-position marker for the <c>Blob</c> constructor (so <c>import {{ Blob }} from
/// 'buffer'</c> binds and bare <c>Blob</c> references resolve). Actual construction
/// goes through <c>BuiltInConstructorFactory</c> by the syntactic name <c>new Blob()</c>.
/// </summary>
public sealed class SharpTSBlobConstructor
{
    public static readonly SharpTSBlobConstructor Instance = new();
    private SharpTSBlobConstructor() { }
    public override string ToString() => "[Function: Blob]";
}

/// <summary>Value-position marker for the <c>File</c> constructor. See <see cref="SharpTSBlobConstructor"/>.</summary>
public sealed class SharpTSFileConstructor
{
    public static readonly SharpTSFileConstructor Instance = new();
    private SharpTSFileConstructor() { }
    public override string ToString() => "[Function: File]";
}
