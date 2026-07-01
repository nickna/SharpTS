using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $ZlibTransform class for standalone zlib streaming support.
/// Extends $Transform with inline BCL compression/decompression logic.
/// No reflection back to SharpTS.dll — all compression uses BCL types directly.
/// </summary>
public partial class RuntimeEmitter
{
    // $ZlibTransform fields
    private FieldBuilder _tsZlibKindField = null!;        // int: ZlibTransformKind ordinal
    private FieldBuilder _tsZlibOutputMsField = null!;     // MemoryStream: compression output buffer
    private FieldBuilder _tsZlibCompressField = null!;     // Stream: the BCL compression stream
    private FieldBuilder _tsZlibInputMsField = null!;      // MemoryStream: decompression accumulation buffer
    private FieldBuilder _tsZlibLevelField = null!;        // int (CompressionLevel): compression level
    private FieldBuilder _tsZlibBytesWrittenField = null!; // long: total bytes written into the engine

    // Kind constants matching ZlibTransformKind enum
    private const int KindGzip = 0;
    private const int KindGunzip = 1;
    private const int KindDeflate = 2;
    private const int KindInflate = 3;
    private const int KindDeflateRaw = 4;
    private const int KindInflateRaw = 5;
    private const int KindBrotliCompress = 6;
    private const int KindBrotliDecompress = 7;
    private const int KindUnzip = 8;
    private const int KindZstdCompress = 9;
    private const int KindZstdDecompress = 10;

    /// <summary>
    /// Emits the $ZlibTransform class extending $Transform.
    /// Must be called AFTER EmitTSTransformClass and BEFORE the runtime class is finalized.
    /// </summary>
    private void EmitTSZlibTransformClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$ZlibTransform",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            runtime.TSTransformType
        );
        _ = typeBuilder;

        // Fields
        _tsZlibKindField = typeBuilder.DefineField("_kind", _types.Int32, FieldAttributes.Private);
        _tsZlibOutputMsField = typeBuilder.DefineField("_outputMs", typeof(MemoryStream), FieldAttributes.Private);
        _tsZlibCompressField = typeBuilder.DefineField("_compressStream", _types.Stream, FieldAttributes.Private);
        _tsZlibInputMsField = typeBuilder.DefineField("_inputMs", typeof(MemoryStream), FieldAttributes.Private);
        _tsZlibLevelField = typeBuilder.DefineField("_level", _types.Int32, FieldAttributes.Private);
        _tsZlibBytesWrittenField = typeBuilder.DefineField("_bytesWritten", _types.Int64, FieldAttributes.Private);

        // Emit ChunkToBytes as a static method on $ZlibTransform itself
        EmitTSZlibChunkToBytesOnType(typeBuilder, runtime);
        EmitTSZlibTransformCtor(typeBuilder, runtime);
        EmitTSZlibTransformWrite(typeBuilder, runtime);
        EmitTSZlibTransformEnd(typeBuilder, runtime);
        // Node zlib stream control surface (#1164). Reflection dispatch resolves the
        // camelCase JS names to these PascalCase members via ToPascalCase + IgnoreCase.
        EmitTSZlibBytesProperties(typeBuilder, runtime);
        EmitTSZlibTransformFlush(typeBuilder, runtime);
        EmitTSZlibTransformParams(typeBuilder, runtime);
        EmitTSZlibTransformReset(typeBuilder, runtime);
        EmitTSZlibTransformClose(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Constructor: public $ZlibTransform(int kind, int compressionLevel)
    /// Sets up compression or decompression state based on kind.
    /// </summary>
    private void EmitTSZlibTransformCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32, _types.Int32]  // kind, compressionLevel
        );
        runtime.TSZlibTransformCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base ($Transform) constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSTransformCtor);

        // this._kind = kind
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsZlibKindField);

        // this._level = compressionLevel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _tsZlibLevelField);

        // if (IsCompression(kind)) setup compression, else setup decompression
        var isDecompLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        var compLabel = il.DefineLabel();

        // Compression kinds: 0=Gzip, 2=Deflate, 4=DeflateRaw, 6=BrotliCompress, 9=ZstdCompress
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, KindGzip);
        il.Emit(OpCodes.Beq, compLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, KindDeflate);
        il.Emit(OpCodes.Beq, compLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, KindDeflateRaw);
        il.Emit(OpCodes.Beq, compLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, KindBrotliCompress);
        il.Emit(OpCodes.Beq, compLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, KindZstdCompress);
        il.Emit(OpCodes.Beq, compLabel);

        // Fall through to decompression setup
        il.Emit(OpCodes.Br, isDecompLabel);

        // Compression setup: _outputMs = new MemoryStream(); _compressStream = CreateCompressionStream(_outputMs, _level, kind)
        il.MarkLabel(compLabel);

        // _outputMs = new MemoryStream()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        il.Emit(OpCodes.Stfld, _tsZlibOutputMsField);

        // Create the appropriate compression stream
        // Switch on kind to create correct compression stream
        EmitCreateCompressionStreamSwitch(il, runtime);
        il.Emit(OpCodes.Br, doneLabel);

        // Decompression setup: _inputMs = new MemoryStream()
        il.MarkLabel(isDecompLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        il.Emit(OpCodes.Stfld, _tsZlibInputMsField);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a switch on this._kind to create the appropriate BCL compression stream.
    /// Stores result in this._compressStream.
    /// </summary>
    private void EmitCreateCompressionStreamSwitch(ILGenerator il, EmittedRuntime runtime)
    {
        var gzipLabel = il.DefineLabel();
        var deflateLabel = il.DefineLabel();
        var deflateRawLabel = il.DefineLabel();
        var brotliLabel = il.DefineLabel();
        var zstdLabel = il.DefineLabel();
        var storeLabel = il.DefineLabel();

        // switch (_kind)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);

        il.Emit(OpCodes.Ldc_I4, KindGzip);
        il.Emit(OpCodes.Beq, gzipLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindDeflate);
        il.Emit(OpCodes.Beq, deflateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindDeflateRaw);
        il.Emit(OpCodes.Beq, deflateRawLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindZstdCompress);
        il.Emit(OpCodes.Beq, zstdLabel);
        // Default: brotli
        il.Emit(OpCodes.Br, brotliLabel);

        // GZipStream(outputMs, level, leaveOpen: true)
        il.MarkLabel(gzipLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibLevelField);
        il.Emit(OpCodes.Ldc_I4_1); // leaveOpen = true
        il.Emit(OpCodes.Newobj, typeof(GZipStream).GetConstructor([typeof(Stream), typeof(CompressionLevel), typeof(bool)])!);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);
        il.Emit(OpCodes.Br, storeLabel);

        // ZLibStream(outputMs, level, leaveOpen: true)
        il.MarkLabel(deflateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibLevelField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(ZLibStream).GetConstructor([typeof(Stream), typeof(CompressionLevel), typeof(bool)])!);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);
        il.Emit(OpCodes.Br, storeLabel);

        // DeflateStream(outputMs, level, leaveOpen: true)
        il.MarkLabel(deflateRawLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibLevelField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(DeflateStream).GetConstructor([typeof(Stream), typeof(CompressionLevel), typeof(bool)])!);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);
        il.Emit(OpCodes.Br, storeLabel);

        // BrotliStream(outputMs, level, leaveOpen: true)
        il.MarkLabel(brotliLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibLevelField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(BrotliStream).GetConstructor([typeof(Stream), typeof(CompressionLevel), typeof(bool)])!);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);
        il.Emit(OpCodes.Br, storeLabel);

        // ZstdSharp.CompressionStream(outputMs, level=3, bufferSize=0, leaveOpen=true)
        il.MarkLabel(zstdLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Ldc_I4_3);  // default zstd level
        il.Emit(OpCodes.Ldc_I4_0);  // bufferSize = 0 (default)
        il.Emit(OpCodes.Ldc_I4_1);  // leaveOpen = true
        il.Emit(OpCodes.Newobj, typeof(ZstdSharp.CompressionStream).GetConstructor([typeof(Stream), typeof(int), typeof(int), typeof(bool)])!);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);

        il.MarkLabel(storeLabel);
    }

    /// <summary>
    /// Override Write: for compression, write bytes to compression stream and push output.
    /// For decompression, accumulate in input buffer.
    /// </summary>
    private void EmitTSZlibTransformWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Boolean,
            [_types.Object, _types.Object, _types.Object]  // chunk, encoding, callback
        );

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var isDecompLabel = il.DefineLabel();
        var afterWriteLabel = il.DefineLabel();

        // if (_destroyed || _writeEnded) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // Convert chunk to byte[] via helper
        // var bytes = ChunkToBytes(chunk)
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Call, _tsZlibChunkToBytesMethod!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // if (bytes.Length == 0) goto afterWrite
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, afterWriteLabel);

        // _bytesWritten += bytes.Length (Node's bytesWritten counts engine input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibBytesWrittenField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsZlibBytesWrittenField);

        // if (_compressStream != null) -> compression path, else -> decompression path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, isDecompLabel);

        // --- Compression path ---
        // _compressStream.Write(bytes, 0, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "Write", _types.MakeArrayType(_types.Byte), _types.Int32, _types.Int32));

        // _compressStream.Flush()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Stream, "Flush"));

        // Push output buffer contents to readable side
        // var data = _outputMs.ToArray(); _outputMs.SetLength(0);
        EmitPushOutputBuffer(il, runtime);
        il.Emit(OpCodes.Br, afterWriteLabel);

        // --- Decompression path ---
        il.MarkLabel(isDecompLabel);
        // _inputMs.Write(bytes, 0, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "Write", _types.MakeArrayType(_types.Byte), _types.Int32, _types.Int32));

        il.MarkLabel(afterWriteLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to push whatever is in _outputMs to the readable side as a $Buffer,
    /// then resets _outputMs.
    /// </summary>
    private void EmitPushOutputBuffer(ILGenerator il, EmittedRuntime runtime)
    {
        var noDataLabel = il.DefineLabel();

        // if (_outputMs == null || _outputMs.Position == 0) skip
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Brfalse, noDataLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Callvirt, typeof(MemoryStream).GetProperty("Position")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Beq, noDataLabel);

        // var data = _outputMs.ToArray()
        var dataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(MemoryStream), "ToArray"));
        il.Emit(OpCodes.Stloc, dataLocal);

        // _outputMs.SetLength(0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Callvirt, typeof(MemoryStream).GetMethod("SetLength", [typeof(long)])!);

        // if (data.Length > 0) Push(new $Buffer(data))
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, noDataLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noDataLabel);
    }

    /// <summary>
    /// Override End: for compression, dispose the compression stream and push final output.
    /// For decompression, decompress accumulated input and push result.
    /// Then signal end of stream.
    /// </summary>
    private void EmitTSZlibTransformEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            typeBuilder,
            [_types.Object, _types.Object, _types.Object]  // chunk, encoding, callback
        );

        var il = method.GetILGenerator();
        var alreadyEndedLabel = il.DefineLabel();
        var isDecompLabel = il.DefineLabel();
        var emitEventsLabel = il.DefineLabel();

        // if (_writeEnded) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, alreadyEndedLabel);

        // _writeEnded = true; _writable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDuplexWriteEndedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDuplexWritableField);

        // Write final chunk if provided
        var noChunkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noChunkLabel);

        // Convert chunk to bytes and write
        var chunkBytes = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _tsZlibChunkToBytesMethod!);
        il.Emit(OpCodes.Stloc, chunkBytes);

        // _bytesWritten += chunk.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibBytesWrittenField);
        il.Emit(OpCodes.Ldloc, chunkBytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsZlibBytesWrittenField);

        // if (_compressStream != null) -> write to compress stream
        var chunkDecompLabel = il.DefineLabel();
        var afterChunkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, chunkDecompLabel);

        // compression: write to compression stream
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Ldloc, chunkBytes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, chunkBytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "Write", _types.MakeArrayType(_types.Byte), _types.Int32, _types.Int32));
        il.Emit(OpCodes.Br, afterChunkLabel);

        // decompression: accumulate
        il.MarkLabel(chunkDecompLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Ldloc, chunkBytes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, chunkBytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "Write", _types.MakeArrayType(_types.Byte), _types.Int32, _types.Int32));

        il.MarkLabel(afterChunkLabel);
        il.MarkLabel(noChunkLabel);

        // Check if compression or decompression
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, isDecompLabel);

        // --- Compression flush ---
        // Dispose compression stream (writes final data)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));
        // null out the field so we know it's disposed
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);

        // Push remaining output buffer
        EmitPushOutputBuffer(il, runtime);
        il.Emit(OpCodes.Br, emitEventsLabel);

        // --- Decompression flush ---
        il.MarkLabel(isDecompLabel);
        EmitDecompressionFlush(il, runtime);

        il.MarkLabel(emitEventsLabel);

        // Push null to signal end of readable side
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        // _writeFinished = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDuplexWriteFinishedField);

        // emit 'finish' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "finish");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyEndedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits decompression flush: read accumulated bytes from _inputMs, decompress via
    /// the appropriate BCL stream, push result as $Buffer.
    /// </summary>
    private void EmitDecompressionFlush(ILGenerator il, EmittedRuntime runtime)
    {
        // var inputBytes = _inputMs.ToArray()
        var inputBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var skipDecompLabel = il.DefineLabel();

        // if (_inputMs == null) skip
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Brfalse, skipDecompLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(MemoryStream), "ToArray"));
        il.Emit(OpCodes.Stloc, inputBytesLocal);

        // Dispose _inputMs
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsZlibInputMsField);

        // if (inputBytes.Length == 0) skip
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, skipDecompLabel);

        // Decompress: need to switch on kind to use correct BCL stream
        // Create input MemoryStream from bytes
        var inputStreamLocal = il.DeclareLocal(typeof(MemoryStream));
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(MemoryStream), _types.MakeArrayType(_types.Byte)));
        il.Emit(OpCodes.Stloc, inputStreamLocal);

        // Create output MemoryStream
        var outputStreamLocal = il.DeclareLocal(typeof(MemoryStream));
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        il.Emit(OpCodes.Stloc, outputStreamLocal);

        // Create decompression stream based on kind
        var decompStreamLocal = il.DeclareLocal(_types.Stream);
        EmitCreateDecompressionStream(il, inputStreamLocal, inputBytesLocal, decompStreamLocal);

        // CopyTo output
        il.Emit(OpCodes.Ldloc, decompStreamLocal);
        il.Emit(OpCodes.Ldloc, outputStreamLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Stream, "CopyTo", _types.Stream));

        // Dispose decompression stream and input stream
        il.Emit(OpCodes.Ldloc, decompStreamLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        // Get result bytes and push as $Buffer
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldloc, outputStreamLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(MemoryStream), "ToArray"));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Dispose output stream
        il.Emit(OpCodes.Ldloc, outputStreamLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));

        // Push result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, skipDecompLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipDecompLabel);
    }

    /// <summary>
    /// Emits a switch on this._kind to create the appropriate BCL decompression stream.
    /// For Unzip, auto-detects format from magic bytes.
    /// </summary>
    private void EmitCreateDecompressionStream(ILGenerator il, LocalBuilder inputStreamLocal,
        LocalBuilder inputBytesLocal, LocalBuilder decompStreamLocal)
    {
        var gunzipLabel = il.DefineLabel();
        var inflateLabel = il.DefineLabel();
        var inflateRawLabel = il.DefineLabel();
        var brotliLabel = il.DefineLabel();
        var zstdDecompLabel = il.DefineLabel();
        var unzipLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // For Unzip (kind == 8), detect format from magic bytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindUnzip);
        il.Emit(OpCodes.Beq, unzipLabel);

        // Normal dispatch
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindGunzip);
        il.Emit(OpCodes.Beq, gunzipLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindInflate);
        il.Emit(OpCodes.Beq, inflateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindInflateRaw);
        il.Emit(OpCodes.Beq, inflateRawLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibKindField);
        il.Emit(OpCodes.Ldc_I4, KindZstdDecompress);
        il.Emit(OpCodes.Beq, zstdDecompLabel);
        // Default to brotli decompress
        il.Emit(OpCodes.Br, brotliLabel);

        // --- Unzip: auto-detect from magic bytes ---
        il.MarkLabel(unzipLabel);
        // if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) -> gzip
        var unzipNotGzipLabel = il.DefineLabel();
        var unzipDeflateCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, unzipNotGzipLabel);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x1f);
        il.Emit(OpCodes.Bne_Un, unzipNotGzipLabel);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x8b);
        il.Emit(OpCodes.Bne_Un, unzipNotGzipLabel);
        il.Emit(OpCodes.Br, gunzipLabel);

        il.MarkLabel(unzipNotGzipLabel);
        // if (bytes[0] == 0x78) -> deflate (zlib header)
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Blt, inflateRawLabel);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x78);
        il.Emit(OpCodes.Beq, inflateLabel);
        // Default: raw deflate
        il.Emit(OpCodes.Br, inflateRawLabel);

        // GZipStream(inputMs, Decompress)
        il.MarkLabel(gunzipLabel);
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Ldc_I4_0); // CompressionMode.Decompress
        il.Emit(OpCodes.Newobj, typeof(GZipStream).GetConstructor([typeof(Stream), typeof(CompressionMode)])!);
        il.Emit(OpCodes.Stloc, decompStreamLocal);
        il.Emit(OpCodes.Br, doneLabel);

        // ZLibStream(inputMs, Decompress)
        il.MarkLabel(inflateLabel);
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(ZLibStream).GetConstructor([typeof(Stream), typeof(CompressionMode)])!);
        il.Emit(OpCodes.Stloc, decompStreamLocal);
        il.Emit(OpCodes.Br, doneLabel);

        // DeflateStream(inputMs, Decompress)
        il.MarkLabel(inflateRawLabel);
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(DeflateStream).GetConstructor([typeof(Stream), typeof(CompressionMode)])!);
        il.Emit(OpCodes.Stloc, decompStreamLocal);
        il.Emit(OpCodes.Br, doneLabel);

        // BrotliStream(inputMs, Decompress)
        il.MarkLabel(brotliLabel);
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(BrotliStream).GetConstructor([typeof(Stream), typeof(CompressionMode)])!);
        il.Emit(OpCodes.Stloc, decompStreamLocal);
        il.Emit(OpCodes.Br, doneLabel);

        // ZstdSharp.DecompressionStream(inputMs, bufferSize=0, checkEndOfStream=false, leaveOpen=false)
        il.MarkLabel(zstdDecompLabel);
        il.Emit(OpCodes.Ldloc, inputStreamLocal);
        il.Emit(OpCodes.Ldc_I4_0);  // bufferSize = 0
        il.Emit(OpCodes.Ldc_I4_0);  // checkEndOfStream = false
        il.Emit(OpCodes.Ldc_I4_0);  // leaveOpen = false
        il.Emit(OpCodes.Newobj, typeof(ZstdSharp.DecompressionStream).GetConstructor([typeof(Stream), typeof(int), typeof(bool), typeof(bool)])!);
        il.Emit(OpCodes.Stloc, decompStreamLocal);

        il.MarkLabel(doneLabel);
    }

    // ── Stream control surface (#1164) ───────────────────────────────────────────
    // These PascalCase members are resolved from the camelCase JS names by the
    // runtime's reflection dispatch (ToPascalCase + IgnoreCase GetProperty/GetMethod).

    /// <summary>
    /// Emits the bytesWritten / bytesRead read-only properties (both surface the
    /// engine-input byte count; Node's bytesRead is a deprecated alias).
    /// </summary>
    private void EmitTSZlibBytesProperties(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        foreach (var name in new[] { "BytesWritten", "BytesRead" })
        {
            var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, _types.Double, null);
            var getter = typeBuilder.DefineMethod(
                "get_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                _types.Double,
                Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsZlibBytesWrittenField);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ret);
            prop.SetGetMethod(getter);
        }
    }

    /// <summary>
    /// Invokes the first of the given argument positions that is a $TSFunction, with no
    /// arguments (the Node zlib control callbacks take none).
    /// </summary>
    private void EmitInvokeFirstCallback(ILGenerator il, EmittedRuntime runtime, params int[] argIndices)
    {
        var done = il.DefineLabel();
        foreach (var idx in argIndices)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, idx);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg, idx);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg, idx);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }
        il.MarkLabel(done);
    }

    /// <summary>
    /// flush([kind][, callback]): pushes buffered compressor output and invokes the
    /// callback. (BCL streams cannot emit a true zlib flush boundary — documented.)
    /// </summary>
    private void EmitTSZlibTransformFlush(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Flush",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Object, _types.Object]);  // kindOrCallback, callback
        var il = method.GetILGenerator();

        var notComp = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, notComp);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Stream, "Flush"));
        EmitPushOutputBuffer(il, runtime);
        il.MarkLabel(notComp);

        EmitInvokeFirstCallback(il, runtime, 2, 1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// params(level, strategy[, callback]): BCL ceiling — an in-flight stream cannot be
    /// retuned, so we flush and invoke the callback; the values are not applied to
    /// already-written data.
    /// </summary>
    private void EmitTSZlibTransformParams(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Params",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);  // level, strategy, callback
        var il = method.GetILGenerator();

        var notComp = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, notComp);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Stream, "Flush"));
        EmitPushOutputBuffer(il, runtime);
        il.MarkLabel(notComp);

        EmitInvokeFirstCallback(il, runtime, 3);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// reset(): restores the stream to its initial state for reuse and zeroes the counter.
    /// </summary>
    private void EmitTSZlibTransformReset(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Reset",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        var il = method.GetILGenerator();

        var decomp = il.DefineLabel();
        var done = il.DefineLabel();

        // Compression streams keep _outputMs; decompression streams keep _inputMs.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibOutputMsField);
        il.Emit(OpCodes.Brfalse, decomp);

        // dispose old compression stream if present
        var skipDispose = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, skipDispose);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));
        il.MarkLabel(skipDispose);

        // _outputMs = new MemoryStream(); recreate _compressStream
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        il.Emit(OpCodes.Stfld, _tsZlibOutputMsField);
        EmitCreateCompressionStreamSwitch(il, runtime);
        il.Emit(OpCodes.Br, done);

        // decompression: _inputMs = new MemoryStream()
        il.MarkLabel(decomp);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(typeof(MemoryStream)));
        il.Emit(OpCodes.Stfld, _tsZlibInputMsField);

        il.MarkLabel(done);
        // _bytesWritten = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stfld, _tsZlibBytesWrittenField);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// close([callback]): releases the streams, emits 'close', and invokes the callback.
    /// </summary>
    private void EmitTSZlibTransformClose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Object]);  // callback
        var il = method.GetILGenerator();

        var skipComp = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Brfalse, skipComp);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibCompressField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsZlibCompressField);
        il.MarkLabel(skipComp);

        var skipInput = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Brfalse, skipInput);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsZlibInputMsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(IDisposable), "Dispose"));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsZlibInputMsField);
        il.MarkLabel(skipInput);

        // emit 'close'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        EmitInvokeFirstCallback(il, runtime, 1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    // Static helper method for converting chunk to byte[]
    private MethodBuilder? _tsZlibChunkToBytesMethod;

    /// <summary>
    /// Emits: public static byte[] ChunkToBytes(object? chunk)
    /// Converts Buffer or string to byte[]. Emitted on the $ZlibTransform type.
    /// </summary>
    private void EmitTSZlibChunkToBytesOnType(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ChunkToBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte),
            [_types.Object]);
        _tsZlibChunkToBytesMethod = method;

        var il = method.GetILGenerator();
        var isStringLabel = il.DefineLabel();
        var emptyLabel = il.DefineLabel();

        // if (chunk == null) return empty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // if (chunk is $Buffer buf) return buf.GetData()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, isStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
        il.Emit(OpCodes.Ret);

        // if (chunk is string s) return UTF8.GetBytes(s)
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, emptyLabel);
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        // default: return empty byte[]
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Ret);
    }
}
