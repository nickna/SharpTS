using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits zlib module helper methods.
    /// All compression logic is emitted inline using BCL compression streams.
    /// </summary>
    private void EmitZlibMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit helper methods FIRST (they are used by compression methods)
        EmitGetZlibInputBytes(typeBuilder, runtime);
        EmitGetZlibCompressionLevel(typeBuilder, runtime);

        // Compression methods
        EmitZlibGzipSync(typeBuilder, runtime);
        EmitZlibGunzipSync(typeBuilder, runtime);
        EmitZlibDeflateSync(typeBuilder, runtime);
        EmitZlibInflateSync(typeBuilder, runtime);
        EmitZlibDeflateRawSync(typeBuilder, runtime);
        EmitZlibInflateRawSync(typeBuilder, runtime);
        EmitZlibBrotliCompressSync(typeBuilder, runtime);
        EmitZlibBrotliDecompressSync(typeBuilder, runtime);
        EmitZlibZstdCompressSync(typeBuilder, runtime);
        EmitZlibZstdDecompressSync(typeBuilder, runtime);
        EmitZlibUnzipSync(typeBuilder, runtime);
        EmitZlibGetConstants(typeBuilder, runtime);

        // Emit wrapper methods for named imports
        EmitZlibMethodWrappers(typeBuilder, runtime);
    }

    #region Input/Options Helpers

    private MethodBuilder? _getZlibInputBytes;
    private MethodBuilder? _getZlibCompressionLevel;

    /// <summary>
    /// Emits: public static byte[] GetZlibInputBytes(object input)
    /// Extracts bytes from Buffer or string.
    /// </summary>
    private void EmitGetZlibInputBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetZlibInputBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte),
            [_types.Object]);
        _getZlibInputBytes = method;

        var il = method.GetILGenerator();

        var isStringLabel = il.DefineLabel();
        var isBufferLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check for null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // Check if string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        // Check if $Buffer (using isinst with the buffer type)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, isBufferLabel);

        // Unknown type - throw
        il.Emit(OpCodes.Br, throwLabel);

        // String path: UTF8.GetBytes(str)
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Br, endLabel);

        // Buffer path: call GetData() method
        il.MarkLabel(isBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
        il.Emit(OpCodes.Br, endLabel);

        // Throw error
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "zlib requires a Buffer or string argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static int GetZlibCompressionLevel(object options)
    /// Extracts compression level from options object, returns CompressionLevel enum value.
    /// For now, returns Optimal (2) as the default. Options parsing can be enhanced later.
    /// </summary>
    private void EmitGetZlibCompressionLevel(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetZlibCompressionLevel",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object]);
        _getZlibCompressionLevel = method;

        var il = method.GetILGenerator();

        var hasOptionsLabel = il.DefineLabel();
        var checkValueLabel = il.DefineLabel();
        var level0Label = il.DefineLabel();
        var level1Label = il.DefineLabel();
        var level9Label = il.DefineLabel();
        var defaultLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // If options is null, return Optimal (2)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        // Check if options is a $Object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        // Call GetProperty("level") on the $Object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "level");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        var levelLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, levelLocal);

        // Check if level is null or undefined
        il.Emit(OpCodes.Ldloc, levelLocal);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        // Check if it's undefined
        il.Emit(OpCodes.Ldloc, levelLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, defaultLabel);

        // Unbox level as double and convert to int
        il.Emit(OpCodes.Ldloc, levelLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        var levelIntLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, levelIntLocal);

        // Map zlib level to .NET CompressionLevel
        // .NET CompressionLevel enum values:
        //   Optimal = 0
        //   Fastest = 1
        //   NoCompression = 2
        //   SmallestSize = 3
        // zlib levels: 0=none, 1=fastest, 9=best, -1=default

        il.Emit(OpCodes.Ldloc, levelIntLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, level0Label);

        il.Emit(OpCodes.Ldloc, levelIntLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, level1Label);

        il.Emit(OpCodes.Ldloc, levelIntLocal);
        il.Emit(OpCodes.Ldc_I4, 9);
        il.Emit(OpCodes.Beq, level9Label);

        il.Emit(OpCodes.Br, defaultLabel);

        il.MarkLabel(level0Label);
        il.Emit(OpCodes.Ldc_I4_2); // zlib 0 -> NoCompression (enum value 2)
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(level1Label);
        il.Emit(OpCodes.Ldc_I4_1); // zlib 1 -> Fastest (enum value 1)
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(level9Label);
        il.Emit(OpCodes.Ldc_I4_3); // zlib 9 -> SmallestSize (enum value 3)
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldc_I4_0); // Default -> Optimal (enum value 0)

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Gzip

    /// <summary>
    /// Emits: public static object ZlibGzipSync(object input, object options)
    /// Uses GZipStream for compression.
    /// </summary>
    private void EmitZlibGzipSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibGzipSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibGzipSync = method;

        EmitCompressMethod(method.GetILGenerator(), runtime, typeof(GZipStream), true);
    }

    /// <summary>
    /// Emits: public static object ZlibGunzipSync(object input, object options)
    /// Uses GZipStream for decompression.
    /// </summary>
    private void EmitZlibGunzipSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibGunzipSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibGunzipSync = method;

        EmitDecompressMethod(method.GetILGenerator(), runtime, typeof(GZipStream));
    }

    #endregion

    #region Deflate (with zlib header)

    /// <summary>
    /// Emits: public static object ZlibDeflateSync(object input, object options)
    /// Uses ZLibStream for compression (includes zlib header).
    /// </summary>
    private void EmitZlibDeflateSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibDeflateSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibDeflateSync = method;

        EmitCompressMethod(method.GetILGenerator(), runtime, typeof(ZLibStream), true);
    }

    /// <summary>
    /// Emits: public static object ZlibInflateSync(object input, object options)
    /// Uses ZLibStream for decompression.
    /// </summary>
    private void EmitZlibInflateSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibInflateSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibInflateSync = method;

        EmitDecompressMethod(method.GetILGenerator(), runtime, typeof(ZLibStream));
    }

    #endregion

    #region DeflateRaw (no header)

    /// <summary>
    /// Emits: public static object ZlibDeflateRawSync(object input, object options)
    /// Uses DeflateStream for compression (no header).
    /// </summary>
    private void EmitZlibDeflateRawSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibDeflateRawSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibDeflateRawSync = method;

        EmitCompressMethod(method.GetILGenerator(), runtime, typeof(DeflateStream), true);
    }

    /// <summary>
    /// Emits: public static object ZlibInflateRawSync(object input, object options)
    /// Uses DeflateStream for decompression.
    /// </summary>
    private void EmitZlibInflateRawSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibInflateRawSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibInflateRawSync = method;

        EmitDecompressMethod(method.GetILGenerator(), runtime, typeof(DeflateStream));
    }

    #endregion

    #region Brotli

    /// <summary>
    /// Emits: public static object ZlibBrotliCompressSync(object input, object options)
    /// Uses BrotliStream for compression.
    /// </summary>
    private void EmitZlibBrotliCompressSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibBrotliCompressSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibBrotliCompressSync = method;

        EmitCompressMethod(method.GetILGenerator(), runtime, typeof(BrotliStream), true);
    }

    /// <summary>
    /// Emits: public static object ZlibBrotliDecompressSync(object input, object options)
    /// Uses BrotliStream for decompression.
    /// </summary>
    private void EmitZlibBrotliDecompressSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibBrotliDecompressSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibBrotliDecompressSync = method;

        EmitDecompressMethod(method.GetILGenerator(), runtime, typeof(BrotliStream));
    }

    #endregion

    #region Zstd

    /// <summary>
    /// Emits: public static object ZlibZstdCompressSync(object input, object options)
    /// Note: Zstd is not supported in compiled mode because it requires ZstdSharp.dll
    /// to be deployed alongside the compiled output. Use interpreter mode for Zstd.
    /// </summary>
    private void EmitZlibZstdCompressSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibZstdCompressSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibZstdCompressSync = method;

        var il = method.GetILGenerator();

        // Zstd requires ZstdSharp.dll which would need to be deployed alongside the compiled output
        // Throw a clear error message explaining this limitation
        il.Emit(OpCodes.Ldstr, "zstdCompressSync is not available in compiled mode. " +
            "Zstd requires the ZstdSharp.dll library to be deployed alongside the compiled output. " +
            "Use interpreter mode or deploy ZstdSharp.dll manually.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static object ZlibZstdDecompressSync(object input, object options)
    /// Note: Zstd is not supported in compiled mode because it requires ZstdSharp.dll
    /// to be deployed alongside the compiled output. Use interpreter mode for Zstd.
    /// </summary>
    private void EmitZlibZstdDecompressSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibZstdDecompressSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibZstdDecompressSync = method;

        var il = method.GetILGenerator();

        // Zstd requires ZstdSharp.dll which would need to be deployed alongside the compiled output
        // Throw a clear error message explaining this limitation
        il.Emit(OpCodes.Ldstr, "zstdDecompressSync is not available in compiled mode. " +
            "Zstd requires the ZstdSharp.dll library to be deployed alongside the compiled output. " +
            "Use interpreter mode or deploy ZstdSharp.dll manually.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    #endregion

    #region Unzip (Auto-detect)

    /// <summary>
    /// Emits: public static object ZlibUnzipSync(object input, object options)
    /// Auto-detects gzip/deflate format based on magic bytes.
    /// </summary>
    private void EmitZlibUnzipSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibUnzipSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.ZlibUnzipSync = method;

        var il = method.GetILGenerator();

        // Get input bytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _getZlibInputBytes!);
        var inputLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, inputLocal);

        var isGzipLabel = il.DefineLabel();
        var isZlibLabel = il.DefineLabel();
        var tryDeflateLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Check if length >= 2
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, tryDeflateLabel);

        // Check for gzip magic: 0x1f 0x8b
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x1f);
        il.Emit(OpCodes.Bne_Un, isZlibLabel);

        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x8b);
        il.Emit(OpCodes.Beq, isGzipLabel);

        // Check for zlib header: first byte 0x78
        il.MarkLabel(isZlibLabel);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x78);
        il.Emit(OpCodes.Bne_Un, tryDeflateLabel);

        // It's zlib - call ZlibInflateSync
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ZlibInflateSync);
        il.Emit(OpCodes.Ret);

        // It's gzip - call ZlibGunzipSync
        il.MarkLabel(isGzipLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ZlibGunzipSync);
        il.Emit(OpCodes.Ret);

        // Try raw deflate
        il.MarkLabel(tryDeflateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ZlibInflateRawSync);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Constants

    /// <summary>
    /// Emits: public static object ZlibGetConstants()
    /// Creates a $Object with all zlib constants.
    /// </summary>
    private void EmitZlibGetConstants(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ZlibGetConstants",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.ZlibGetConstants = method;

        var il = method.GetILGenerator();

        // Create new $Object with empty dictionary: new $Object(new Dictionary<string, object?>())
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        var objLocal = il.DeclareLocal(runtime.TSObjectType);
        il.Emit(OpCodes.Stloc, objLocal);

        // Helper to add constant
        void AddConstant(string name, double value)
        {
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_R8, value);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        }

        // Compression levels
        AddConstant("Z_NO_COMPRESSION", 0);
        AddConstant("Z_BEST_SPEED", 1);
        AddConstant("Z_BEST_COMPRESSION", 9);
        AddConstant("Z_DEFAULT_COMPRESSION", -1);

        // Strategies
        AddConstant("Z_FILTERED", 1);
        AddConstant("Z_HUFFMAN_ONLY", 2);
        AddConstant("Z_RLE", 3);
        AddConstant("Z_FIXED", 4);
        AddConstant("Z_DEFAULT_STRATEGY", 0);

        // Flush modes
        AddConstant("Z_NO_FLUSH", 0);
        AddConstant("Z_PARTIAL_FLUSH", 1);
        AddConstant("Z_SYNC_FLUSH", 2);
        AddConstant("Z_FULL_FLUSH", 3);
        AddConstant("Z_FINISH", 4);
        AddConstant("Z_BLOCK", 5);
        AddConstant("Z_TREES", 6);

        // Return codes
        AddConstant("Z_OK", 0);
        AddConstant("Z_STREAM_END", 1);
        AddConstant("Z_NEED_DICT", 2);
        AddConstant("Z_ERRNO", -1);
        AddConstant("Z_STREAM_ERROR", -2);
        AddConstant("Z_DATA_ERROR", -3);
        AddConstant("Z_MEM_ERROR", -4);
        AddConstant("Z_BUF_ERROR", -5);
        AddConstant("Z_VERSION_ERROR", -6);

        // Window/memory defaults
        AddConstant("Z_DEFAULT_WINDOWBITS", 15);
        AddConstant("Z_DEFAULT_MEMLEVEL", 8);
        AddConstant("Z_MIN_WINDOWBITS", 8);
        AddConstant("Z_MAX_WINDOWBITS", 15);
        AddConstant("Z_MIN_MEMLEVEL", 1);
        AddConstant("Z_MAX_MEMLEVEL", 9);
        AddConstant("Z_DEFAULT_CHUNK", 16384);

        // Brotli constants
        AddConstant("BROTLI_OPERATION_PROCESS", 0);
        AddConstant("BROTLI_OPERATION_FLUSH", 1);
        AddConstant("BROTLI_OPERATION_FINISH", 2);
        AddConstant("BROTLI_OPERATION_EMIT_METADATA", 3);
        AddConstant("BROTLI_PARAM_MODE", 0);
        AddConstant("BROTLI_PARAM_QUALITY", 1);
        AddConstant("BROTLI_PARAM_LGWIN", 2);
        AddConstant("BROTLI_PARAM_LGBLOCK", 3);
        AddConstant("BROTLI_PARAM_DISABLE_LITERAL_CONTEXT_MODELING", 4);
        AddConstant("BROTLI_PARAM_SIZE_HINT", 5);
        AddConstant("BROTLI_PARAM_LARGE_WINDOW", 6);
        AddConstant("BROTLI_PARAM_NPOSTFIX", 7);
        AddConstant("BROTLI_PARAM_NDIRECT", 8);
        AddConstant("BROTLI_MODE_GENERIC", 0);
        AddConstant("BROTLI_MODE_TEXT", 1);
        AddConstant("BROTLI_MODE_FONT", 2);
        AddConstant("BROTLI_MIN_QUALITY", 0);
        AddConstant("BROTLI_MAX_QUALITY", 11);
        AddConstant("BROTLI_DEFAULT_QUALITY", 11);
        AddConstant("BROTLI_MIN_WINDOW_BITS", 10);
        AddConstant("BROTLI_MAX_WINDOW_BITS", 24);
        AddConstant("BROTLI_LARGE_MAX_WINDOW_BITS", 30);
        AddConstant("BROTLI_DEFAULT_WINDOW", 22);
        AddConstant("BROTLI_DECODER_PARAM_DISABLE_RING_BUFFER_REALLOCATION", 0);
        AddConstant("BROTLI_DECODER_PARAM_LARGE_WINDOW", 1);

        // Zstd constants
        AddConstant("ZSTD_c_compressionLevel", 100);
        AddConstant("ZSTD_c_windowLog", 101);
        AddConstant("ZSTD_c_hashLog", 102);
        AddConstant("ZSTD_c_chainLog", 103);
        AddConstant("ZSTD_c_searchLog", 104);
        AddConstant("ZSTD_c_minMatch", 105);
        AddConstant("ZSTD_c_targetLength", 106);
        AddConstant("ZSTD_c_strategy", 107);
        AddConstant("ZSTD_c_checksumFlag", 201);
        AddConstant("ZSTD_c_contentSizeFlag", 200);
        AddConstant("ZSTD_c_dictIDFlag", 202);
        AddConstant("ZSTD_c_nbWorkers", 400);
        AddConstant("ZSTD_c_jobSize", 401);
        AddConstant("ZSTD_c_overlapLog", 402);
        AddConstant("ZSTD_minCLevel", -131072);
        AddConstant("ZSTD_maxCLevel", 22);
        AddConstant("ZSTD_defaultCLevel", 3);

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Method Wrappers for Named Imports

    /// <summary>
    /// Emits wrapper methods for zlib module functions to support named imports.
    /// </summary>
    private void EmitZlibMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var methodNames = new[]
        {
            ("gzipSync", runtime.ZlibGzipSync),
            ("gunzipSync", runtime.ZlibGunzipSync),
            ("deflateSync", runtime.ZlibDeflateSync),
            ("inflateSync", runtime.ZlibInflateSync),
            ("deflateRawSync", runtime.ZlibDeflateRawSync),
            ("inflateRawSync", runtime.ZlibInflateRawSync),
            ("brotliCompressSync", runtime.ZlibBrotliCompressSync),
            ("brotliDecompressSync", runtime.ZlibBrotliDecompressSync),
            ("zstdCompressSync", runtime.ZlibZstdCompressSync),
            ("zstdDecompressSync", runtime.ZlibZstdDecompressSync),
            ("unzipSync", runtime.ZlibUnzipSync)
        };

        foreach (var (name, targetMethod) in methodNames)
        {
            var wrapper = typeBuilder.DefineMethod(
                "ZlibWrapper_" + name,
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]);

            var il = wrapper.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, targetMethod);
            il.Emit(OpCodes.Ret);

            runtime.RegisterBuiltInModuleMethod("zlib", name, wrapper);
        }
    }

    #endregion

    #region Compression/Decompression Emission Helpers

    /// <summary>
    /// Emits compression using a BCL compression stream type.
    /// </summary>
    private void EmitCompressMethod(ILGenerator il, EmittedRuntime runtime, Type streamType, bool useLevel)
    {
        // Get input bytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _getZlibInputBytes!);
        var inputLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, inputLocal);

        // Get compression level
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _getZlibCompressionLevel!);
        var levelLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, levelLocal);

        // Create output MemoryStream
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        var outputLocal = il.DeclareLocal(typeof(MemoryStream));
        il.Emit(OpCodes.Stloc, outputLocal);

        // Create compression stream
        // Constructor: (Stream stream, CompressionLevel level, bool leaveOpen)
        var streamCtor = streamType.GetConstructor([typeof(Stream), typeof(CompressionLevel), typeof(bool)]);
        if (streamCtor == null)
        {
            // Fallback for older APIs
            streamCtor = streamType.GetConstructor([typeof(Stream), typeof(CompressionLevel)])!;
            il.Emit(OpCodes.Ldloc, outputLocal);
            il.Emit(OpCodes.Ldloc, levelLocal);
            il.Emit(OpCodes.Newobj, streamCtor);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, outputLocal);
            il.Emit(OpCodes.Ldloc, levelLocal);
            il.Emit(OpCodes.Ldc_I4_1); // leaveOpen = true
            il.Emit(OpCodes.Newobj, streamCtor);
        }
        var compressLocal = il.DeclareLocal(streamType);
        il.Emit(OpCodes.Stloc, compressLocal);

        // Write input to compression stream
        il.Emit(OpCodes.Ldloc, compressLocal);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "Write", _types.MakeArrayType(_types.Byte), _types.Int32, _types.Int32));

        // Dispose compression stream (flushes final data)
        il.Emit(OpCodes.Ldloc, compressLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        // Get result from output stream
        il.Emit(OpCodes.Ldloc, outputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(MemoryStream), "ToArray"));
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Dispose output stream
        il.Emit(OpCodes.Ldloc, outputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        // Create $Buffer from result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits decompression using a BCL compression stream type.
    /// </summary>
    private void EmitDecompressMethod(ILGenerator il, EmittedRuntime runtime, Type streamType)
    {
        // Get input bytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _getZlibInputBytes!);
        var inputLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, inputLocal);

        // Create input MemoryStream
        il.Emit(OpCodes.Ldloc, inputLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(MemoryStream), _types.MakeArrayType(_types.Byte)));
        var inputStreamLocal = il.DeclareLocal(typeof(MemoryStream));
        il.Emit(OpCodes.Stloc, inputStreamLocal);

        // Create decompression stream
        // Constructor: (Stream stream, CompressionMode mode)
        var streamCtor = streamType.GetConstructor([typeof(Stream), typeof(CompressionMode)])!;
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Ldc_I4_0); // CompressionMode.Decompress = 0
        il.Emit(OpCodes.Newobj, streamCtor);
        var decompressLocal = il.DeclareLocal(streamType);
        il.Emit(OpCodes.Stloc, decompressLocal);

        // Create output MemoryStream
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        var outputLocal = il.DeclareLocal(typeof(MemoryStream));
        il.Emit(OpCodes.Stloc, outputLocal);

        // Copy from decompression stream to output
        il.Emit(OpCodes.Ldloc, decompressLocal);
        il.Emit(OpCodes.Ldloc, outputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "CopyTo", _types.Stream));

        // Dispose streams
        il.Emit(OpCodes.Ldloc, decompressLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        // Get result
        il.Emit(OpCodes.Ldloc, outputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(MemoryStream), "ToArray"));
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Dispose output stream
        il.Emit(OpCodes.Ldloc, outputLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        // Create $Buffer from result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
