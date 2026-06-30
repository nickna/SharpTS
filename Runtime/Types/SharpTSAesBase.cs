using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared implementation for the Node-compatible AES <c>Cipher</c>/<c>Decipher</c>
/// value-type wrappers, which were previously line-for-line parallel (#1135).
/// </summary>
/// <remarks>
/// Both directions support CBC (with PKCS7/no padding) and GCM modes. The
/// direction-specific pieces are the few abstract members below:
/// <list type="bullet">
/// <item><see cref="Noun"/> — "Cipher"/"Decipher", used in error messages.</item>
/// <item><see cref="ReserveFinalBlock"/> — decryption holds back the last CBC block
///   for <c>final()</c>; encryption does not.</item>
/// <item><see cref="DecodeOutputAsUtf8"/> — decryption may render its plaintext output
///   as a UTF-8 string; encryption output (ciphertext) is never decoded as UTF-8.</item>
/// <item><see cref="FinalizeGcm"/> — encrypt vs. authenticated-decrypt at <c>final()</c>.</item>
/// </list>
/// </remarks>
public abstract class SharpTSAesBase : IDisposable
{
    protected readonly string _algorithm;
    protected readonly byte[] _key;
    protected readonly byte[] _iv;
    protected readonly bool _isGcm;
    protected readonly int _keySize;
    private readonly bool _forEncryption;

    // CBC mode
    protected Aes? _aes;
    protected ICryptoTransform? _transform;
    protected readonly List<byte> _cbcBuffer = new();

    // GCM mode
    protected AesGcm? _aesGcm;
    protected readonly List<byte> _gcmBuffer = new();
    protected byte[]? _authTag;
    protected byte[]? _aad;

    protected bool _finalized;
    protected bool _autoPadding = true;
    private bool _disposed;

    /// <param name="forEncryption">True for a Cipher, false for a Decipher.</param>
    protected SharpTSAesBase(string algorithm, byte[] key, byte[] iv, bool forEncryption)
    {
        _forEncryption = forEncryption;
        _algorithm = algorithm.ToLowerInvariant();

        (_keySize, _isGcm) = ParseAlgorithm(_algorithm);

        // Validate key size
        if (key.Length != _keySize / 8)
            throw new ArgumentException($"Invalid key length for {_algorithm}. Expected {_keySize / 8} bytes, got {key.Length} bytes.");

        // Validate IV size
        var expectedIvSize = _isGcm ? 12 : 16;
        if (iv.Length != expectedIvSize)
            throw new ArgumentException($"Invalid IV length for {_algorithm}. Expected {expectedIvSize} bytes, got {iv.Length} bytes.");

        _key = key;
        _iv = iv;
        _finalized = false;

        if (_isGcm)
        {
            _aesGcm = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        }
        else
        {
            _aes = Aes.Create();
            _aes.Key = _key;
            _aes.IV = _iv;
            _aes.Mode = CipherMode.CBC;
            _aes.Padding = PaddingMode.PKCS7;
            _transform = CreateTransform();
        }
    }

    /// <summary>Lowercase "cipher"/"decipher" message stem (e.g. "Cipher").</summary>
    protected abstract string Noun { get; }

    /// <summary>Decryption holds back the final CBC block for <c>final()</c>.</summary>
    protected abstract bool ReserveFinalBlock { get; }

    /// <summary>Decryption renders UTF-8 output; encryption (ciphertext) does not.</summary>
    protected abstract bool DecodeOutputAsUtf8 { get; }

    /// <summary>Performs the GCM transform at <c>final()</c> and returns the result bytes.</summary>
    protected abstract byte[] FinalizeGcm();

    /// <summary>
    /// Parses the algorithm string to extract key size and mode.
    /// </summary>
    private static (int keySize, bool isGcm) ParseAlgorithm(string algorithm)
    {
        return algorithm switch
        {
            "aes-128-cbc" => (128, false),
            "aes-192-cbc" => (192, false),
            "aes-256-cbc" => (256, false),
            "aes-128-gcm" => (128, true),
            "aes-192-gcm" => (192, true),
            "aes-256-gcm" => (256, true),
            _ => throw new ArgumentException($"Unsupported cipher algorithm: {algorithm}")
        };
    }

    private ICryptoTransform CreateTransform() =>
        _forEncryption ? _aes!.CreateEncryptor() : _aes!.CreateDecryptor();

    /// <summary>
    /// Transforms data and returns the result.
    /// </summary>
    /// <param name="data">Data to transform (string or Buffer).</param>
    /// <param name="inputEncoding">Input encoding for string data: utf8 (default), hex, base64.</param>
    /// <param name="outputEncoding">Output encoding: hex, base64, (utf8 for decipher) or null for Buffer.</param>
    public object Update(object data, string? inputEncoding = null, string? outputEncoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException($"{Noun} has already been finalized");

        var inputBytes = CryptoEncoding.FromEncoded(data, inputEncoding);

        if (_isGcm)
        {
            // Accumulate until final(); the transform happens there.
            _gcmBuffer.AddRange(inputBytes);
            return FormatOutput([], outputEncoding);
        }

        // CBC: buffer data and only process complete 16-byte blocks. Decryption keeps
        // the last block buffered so final() can strip padding.
        _cbcBuffer.AddRange(inputBytes);

        const int blockSize = 16;
        var completeBlocks = (_cbcBuffer.Count / blockSize) - (ReserveFinalBlock ? 1 : 0);
        if (completeBlocks <= 0)
            return FormatOutput([], outputEncoding);

        var bytesToProcess = completeBlocks * blockSize;
        var dataToProcess = _cbcBuffer.Take(bytesToProcess).ToArray();
        _cbcBuffer.RemoveRange(0, bytesToProcess);

        var outputBytes = new byte[bytesToProcess];
        var bytesWritten = _transform!.TransformBlock(dataToProcess, 0, dataToProcess.Length, outputBytes, 0);

        if (bytesWritten > 0)
        {
            var result = new byte[bytesWritten];
            Array.Copy(outputBytes, result, bytesWritten);
            return FormatOutput(result, outputEncoding);
        }

        return FormatOutput([], outputEncoding);
    }

    /// <summary>
    /// Finalizes the transform and returns any remaining output.
    /// </summary>
    public object Final(string? outputEncoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException($"{Noun} has already been finalized");

        _finalized = true;

        byte[] result;
        if (_isGcm)
        {
            result = FinalizeGcm();
        }
        else
        {
            var remainingData = _cbcBuffer.ToArray();
            result = _transform!.TransformFinalBlock(remainingData, 0, remainingData.Length);
        }

        return FormatOutput(result, outputEncoding);
    }

    /// <summary>
    /// Sets whether to use PKCS7 auto-padding (CBC mode only); a no-op for GCM.
    /// </summary>
    protected void SetAutoPaddingCore(bool autoPadding)
    {
        if (_finalized)
            throw new InvalidOperationException($"Cannot set auto padding after {Noun.ToLowerInvariant()} has been finalized");

        if (_isGcm)
            return; // GCM doesn't use padding

        _autoPadding = autoPadding;

        // Recreate the transform with the new padding mode.
        _aes!.Padding = autoPadding ? PaddingMode.PKCS7 : PaddingMode.None;
        _transform?.Dispose();
        _transform = CreateTransform();
    }

    /// <summary>
    /// Sets additional authenticated data (GCM mode only, before final()).
    /// </summary>
    protected void SetAADCore(object aad)
    {
        if (!_isGcm)
            throw new InvalidOperationException("setAAD is only available for GCM mode ciphers");

        if (_finalized)
            throw new InvalidOperationException("setAAD must be called before final()");

        _aad = CryptoEncoding.FromEncoded(aad, null);
    }

    protected object FormatOutput(byte[] bytes, string? encoding) =>
        CryptoEncoding.ToBufferOrString(bytes, encoding, DecodeOutputAsUtf8);

    /// <summary>
    /// Gets a member of this object (for property access). Subclasses override to add
    /// their direction-specific members (getAuthTag / setAuthTag) and chain to base.
    /// </summary>
    public virtual object? GetMember(string name)
    {
        return name switch
        {
            "update" => BuiltInMethod.CreateV2("update", 1, 3, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException($"{Noun.ToLowerInvariant()}.update requires data argument");

                var inputEncoding = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                var outputEncoding = args.Length > 2 ? args[2].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(Update(args[0].ToObject()!, inputEncoding, outputEncoding));
            }),
            "final" => BuiltInMethod.CreateV2("final", 0, 1, (_, _, args) =>
            {
                var outputEncoding = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(Final(outputEncoding));
            }),
            "setAutoPadding" => BuiltInMethod.CreateV2("setAutoPadding", 0, 1, (_, _, args) =>
            {
                var autoPadding = args.Length == 0
                    || (args[0].IsBoolean ? args[0].AsBooleanUnsafe()
                        : !args[0].IsNumber || args[0].AsNumberUnsafe() != 0);
                SetAutoPaddingCore(autoPadding);
                return RuntimeValue.FromObject(this);
            }),
            "setAAD" => BuiltInMethod.CreateV2("setAAD", 1, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException($"{Noun.ToLowerInvariant()}.setAAD requires buffer argument");
                SetAADCore(args[0].ToObject()!);
                return RuntimeValue.FromObject(this);
            }),
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _transform?.Dispose();
        _aes?.Dispose();
        _aesGcm?.Dispose();
    }
}
