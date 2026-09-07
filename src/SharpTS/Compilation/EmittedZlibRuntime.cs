using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Zlib metadata for one compilation. Declared handles support forward references;
/// completion validates all handles and freezes the component for consumers.
/// </summary>
public sealed class EmittedZlibRuntime
{
    internal EmittedZlibRuntime() { }

    public bool IsComplete { get; private set; }

    private ConstructorBuilder? _transformCtor;
    public ConstructorBuilder TransformCtor
    {
        get => Require(_transformCtor);
        internal set => Set(ref _transformCtor, value);
    }

    private MethodBuilder? _gzipSync;
    public MethodBuilder GzipSync
    {
        get => Require(_gzipSync);
        internal set => Set(ref _gzipSync, value);
    }

    private MethodBuilder? _gunzipSync;
    public MethodBuilder GunzipSync
    {
        get => Require(_gunzipSync);
        internal set => Set(ref _gunzipSync, value);
    }

    private MethodBuilder? _deflateSync;
    public MethodBuilder DeflateSync
    {
        get => Require(_deflateSync);
        internal set => Set(ref _deflateSync, value);
    }

    private MethodBuilder? _inflateSync;
    public MethodBuilder InflateSync
    {
        get => Require(_inflateSync);
        internal set => Set(ref _inflateSync, value);
    }

    private MethodBuilder? _deflateRawSync;
    public MethodBuilder DeflateRawSync
    {
        get => Require(_deflateRawSync);
        internal set => Set(ref _deflateRawSync, value);
    }

    private MethodBuilder? _inflateRawSync;
    public MethodBuilder InflateRawSync
    {
        get => Require(_inflateRawSync);
        internal set => Set(ref _inflateRawSync, value);
    }

    private MethodBuilder? _brotliCompressSync;
    public MethodBuilder BrotliCompressSync
    {
        get => Require(_brotliCompressSync);
        internal set => Set(ref _brotliCompressSync, value);
    }

    private MethodBuilder? _brotliDecompressSync;
    public MethodBuilder BrotliDecompressSync
    {
        get => Require(_brotliDecompressSync);
        internal set => Set(ref _brotliDecompressSync, value);
    }

    private MethodBuilder? _zstdCompressSync;
    public MethodBuilder ZstdCompressSync
    {
        get => Require(_zstdCompressSync);
        internal set => Set(ref _zstdCompressSync, value);
    }

    private MethodBuilder? _zstdDecompressSync;
    public MethodBuilder ZstdDecompressSync
    {
        get => Require(_zstdDecompressSync);
        internal set => Set(ref _zstdDecompressSync, value);
    }

    private MethodBuilder? _unzipSync;
    public MethodBuilder UnzipSync
    {
        get => Require(_unzipSync);
        internal set => Set(ref _unzipSync, value);
    }

    private MethodBuilder? _crc32;
    public MethodBuilder Crc32
    {
        get => Require(_crc32);
        internal set => Set(ref _crc32, value);
    }

    private MethodBuilder? _createGzip;
    public MethodBuilder CreateGzip
    {
        get => Require(_createGzip);
        internal set => Set(ref _createGzip, value);
    }

    private MethodBuilder? _createGunzip;
    public MethodBuilder CreateGunzip
    {
        get => Require(_createGunzip);
        internal set => Set(ref _createGunzip, value);
    }

    private MethodBuilder? _createDeflate;
    public MethodBuilder CreateDeflate
    {
        get => Require(_createDeflate);
        internal set => Set(ref _createDeflate, value);
    }

    private MethodBuilder? _createInflate;
    public MethodBuilder CreateInflate
    {
        get => Require(_createInflate);
        internal set => Set(ref _createInflate, value);
    }

    private MethodBuilder? _createDeflateRaw;
    public MethodBuilder CreateDeflateRaw
    {
        get => Require(_createDeflateRaw);
        internal set => Set(ref _createDeflateRaw, value);
    }

    private MethodBuilder? _createInflateRaw;
    public MethodBuilder CreateInflateRaw
    {
        get => Require(_createInflateRaw);
        internal set => Set(ref _createInflateRaw, value);
    }

    private MethodBuilder? _createBrotliCompress;
    public MethodBuilder CreateBrotliCompress
    {
        get => Require(_createBrotliCompress);
        internal set => Set(ref _createBrotliCompress, value);
    }

    private MethodBuilder? _createBrotliDecompress;
    public MethodBuilder CreateBrotliDecompress
    {
        get => Require(_createBrotliDecompress);
        internal set => Set(ref _createBrotliDecompress, value);
    }

    private MethodBuilder? _createZstdCompress;
    public MethodBuilder CreateZstdCompress
    {
        get => Require(_createZstdCompress);
        internal set => Set(ref _createZstdCompress, value);
    }

    private MethodBuilder? _createZstdDecompress;
    public MethodBuilder CreateZstdDecompress
    {
        get => Require(_createZstdDecompress);
        internal set => Set(ref _createZstdDecompress, value);
    }

    private MethodBuilder? _createUnzip;
    public MethodBuilder CreateUnzip
    {
        get => Require(_createUnzip);
        internal set => Set(ref _createUnzip, value);
    }

    private static T Require<T>(T? method, [CallerMemberName] string name = "") where T : class =>
        method ?? throw new InvalidOperationException($"Zlib metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Zlib metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = GzipSync;
        _ = GunzipSync;
        _ = DeflateSync;
        _ = InflateSync;
        _ = DeflateRawSync;
        _ = InflateRawSync;
        _ = BrotliCompressSync;
        _ = BrotliDecompressSync;
        _ = ZstdCompressSync;
        _ = ZstdDecompressSync;
        _ = UnzipSync;
        _ = Crc32;
        _ = CreateGzip;
        _ = CreateGunzip;
        _ = CreateDeflate;
        _ = CreateInflate;
        _ = CreateDeflateRaw;
        _ = CreateInflateRaw;
        _ = CreateBrotliCompress;
        _ = CreateBrotliDecompress;
        _ = CreateZstdCompress;
        _ = CreateZstdDecompress;
        _ = CreateUnzip;
        _ = TransformCtor;
        IsComplete = true;
    }
}
