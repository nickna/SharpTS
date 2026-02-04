using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'zlib' module.
/// </summary>
/// <remarks>
/// Provides compression/decompression functionality:
/// - gzipSync/gunzipSync: Gzip format
/// - deflateSync/inflateSync: Deflate with zlib header
/// - deflateRawSync/inflateRawSync: Raw deflate (no header)
/// - brotliCompressSync/brotliDecompressSync: Brotli format
/// - zstdCompressSync/zstdDecompressSync: Zstandard format
/// - constants: Compression constants object
/// </remarks>
public sealed class ZlibModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "zlib";

    private static readonly string[] _exportedMembers =
    [
        "gzipSync", "gunzipSync",
        "deflateSync", "inflateSync",
        "deflateRawSync", "inflateRawSync",
        "brotliCompressSync", "brotliDecompressSync",
        "zstdCompressSync", "zstdDecompressSync",
        "unzipSync",
        "constants"
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
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "constants")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: ZlibGetConstants() -> object
        il.Emit(OpCodes.Call, ctx.Runtime!.ZlibGetConstants);
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
