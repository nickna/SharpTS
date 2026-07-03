using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for <c>primitive:zlib</c> — the narrow compiled surface behind
/// the stdlib/node/zlib.ts facade.
/// </summary>
/// <remarks>
/// Provides the compression cores and stateful streams TypeScript cannot express:
/// sync one-shots (gzip/deflate/deflateRaw/brotli/zstd/unzip), the streaming
/// create* Transform factories, and crc32. The Node-shape glue (constants/codes
/// objects, async callback forms) lives in the TS facade.
/// </remarks>
public sealed class ZlibModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:zlib";

    private static readonly string[] _exportedMembers =
    [
        "gzipSync", "gunzipSync",
        "deflateSync", "inflateSync",
        "deflateRawSync", "inflateRawSync",
        "brotliCompressSync", "brotliDecompressSync",
        "zstdCompressSync", "zstdDecompressSync",
        "unzipSync",
        // Streaming APIs
        "createGzip", "createGunzip",
        "createDeflate", "createInflate",
        "createDeflateRaw", "createInflateRaw",
        "createBrotliCompress", "createBrotliDecompress",
        "createZstdCompress", "createZstdDecompress",
        "createUnzip",
        // Checksums
        "crc32"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "gzipSync" => EmitCompressionMethod(emitter, arguments, "ZlibGzipSync"),
            "gunzipSync" => EmitCompressionMethod(emitter, arguments, "ZlibGunzipSync"),
            "deflateSync" => EmitCompressionMethod(emitter, arguments, "ZlibDeflateSync"),
            "inflateSync" => EmitCompressionMethod(emitter, arguments, "ZlibInflateSync"),
            "deflateRawSync" => EmitCompressionMethod(emitter, arguments, "ZlibDeflateRawSync"),
            "inflateRawSync" => EmitCompressionMethod(emitter, arguments, "ZlibInflateRawSync"),
            "brotliCompressSync" => EmitCompressionMethod(emitter, arguments, "ZlibBrotliCompressSync"),
            "brotliDecompressSync" => EmitCompressionMethod(emitter, arguments, "ZlibBrotliDecompressSync"),
            "zstdCompressSync" => EmitCompressionMethod(emitter, arguments, "ZlibZstdCompressSync"),
            "zstdDecompressSync" => EmitCompressionMethod(emitter, arguments, "ZlibZstdDecompressSync"),
            "unzipSync" => EmitCompressionMethod(emitter, arguments, "ZlibUnzipSync"),
            // Streaming APIs
            "createGzip" => EmitCreateStream(emitter, arguments, "ZlibCreateGzip"),
            "createGunzip" => EmitCreateStream(emitter, arguments, "ZlibCreateGunzip"),
            "createDeflate" => EmitCreateStream(emitter, arguments, "ZlibCreateDeflate"),
            "createInflate" => EmitCreateStream(emitter, arguments, "ZlibCreateInflate"),
            "createDeflateRaw" => EmitCreateStream(emitter, arguments, "ZlibCreateDeflateRaw"),
            "createInflateRaw" => EmitCreateStream(emitter, arguments, "ZlibCreateInflateRaw"),
            "createBrotliCompress" => EmitCreateStream(emitter, arguments, "ZlibCreateBrotliCompress"),
            "createBrotliDecompress" => EmitCreateStream(emitter, arguments, "ZlibCreateBrotliDecompress"),
            "createZstdCompress" => EmitCreateStream(emitter, arguments, "ZlibCreateZstdCompress"),
            "createZstdDecompress" => EmitCreateStream(emitter, arguments, "ZlibCreateZstdDecompress"),
            "createUnzip" => EmitCreateStream(emitter, arguments, "ZlibCreateUnzip"),
            // Checksums (Node 22+): crc32(data[, value])
            "crc32" => EmitCrc32(emitter, arguments),
            _ => false
        };
    }

    /// <summary>
    /// Emits crc32(data[, value]) -> number. Routes to $Runtime.ZlibCrc32(object, object).
    /// </summary>
    private static bool EmitCrc32(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // data argument
        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        // optional running value
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.ZlibCrc32);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // constants/codes are TS object literals in the stdlib/node/zlib.ts facade.
        return false;
    }

    /// <summary>
    /// Emits a createXxx() streaming method call.
    /// Pattern: CreateMethod(object? options) -> object (ZlibTransform)
    /// </summary>
    private static bool EmitCreateStream(IEmitterContext emitter, List<Expr> arguments, string runtimeMethodName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit options argument (null if not provided)
        if (arguments.Count >= 1)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Get the runtime method
        var method = runtimeMethodName switch
        {
            "ZlibCreateGzip" => ctx.Runtime!.ZlibCreateGzip,
            "ZlibCreateGunzip" => ctx.Runtime!.ZlibCreateGunzip,
            "ZlibCreateDeflate" => ctx.Runtime!.ZlibCreateDeflate,
            "ZlibCreateInflate" => ctx.Runtime!.ZlibCreateInflate,
            "ZlibCreateDeflateRaw" => ctx.Runtime!.ZlibCreateDeflateRaw,
            "ZlibCreateInflateRaw" => ctx.Runtime!.ZlibCreateInflateRaw,
            "ZlibCreateBrotliCompress" => ctx.Runtime!.ZlibCreateBrotliCompress,
            "ZlibCreateBrotliDecompress" => ctx.Runtime!.ZlibCreateBrotliDecompress,
            "ZlibCreateZstdCompress" => ctx.Runtime!.ZlibCreateZstdCompress,
            "ZlibCreateZstdDecompress" => ctx.Runtime!.ZlibCreateZstdDecompress,
            "ZlibCreateUnzip" => ctx.Runtime!.ZlibCreateUnzip,
            _ => throw new CompileException($"Unknown zlib streaming method: {runtimeMethodName}")
        };

        il.Emit(OpCodes.Call, method);
        return true;
    }

    /// <summary>
    /// Emits a compression/decompression method call.
    /// All methods follow the pattern: MethodName(object input, object? options) -> object (Buffer)
    /// </summary>
    private static bool EmitCompressionMethod(IEmitterContext emitter, List<Expr> arguments, string runtimeMethodName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit input argument
        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        // Emit options argument (null if not provided)
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Get the appropriate runtime method
        var method = runtimeMethodName switch
        {
            "ZlibGzipSync" => ctx.Runtime!.ZlibGzipSync,
            "ZlibGunzipSync" => ctx.Runtime!.ZlibGunzipSync,
            "ZlibDeflateSync" => ctx.Runtime!.ZlibDeflateSync,
            "ZlibInflateSync" => ctx.Runtime!.ZlibInflateSync,
            "ZlibDeflateRawSync" => ctx.Runtime!.ZlibDeflateRawSync,
            "ZlibInflateRawSync" => ctx.Runtime!.ZlibInflateRawSync,
            "ZlibBrotliCompressSync" => ctx.Runtime!.ZlibBrotliCompressSync,
            "ZlibBrotliDecompressSync" => ctx.Runtime!.ZlibBrotliDecompressSync,
            "ZlibZstdCompressSync" => ctx.Runtime!.ZlibZstdCompressSync,
            "ZlibZstdDecompressSync" => ctx.Runtime!.ZlibZstdDecompressSync,
            "ZlibUnzipSync" => ctx.Runtime!.ZlibUnzipSync,
            _ => throw new CompileException($"Unknown zlib method: {runtimeMethodName}")
        };

        il.Emit(OpCodes.Call, method);
        return true;
    }
}
