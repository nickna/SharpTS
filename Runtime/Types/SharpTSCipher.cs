using System.Security.Cryptography;
using System.Text;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Cipher object for symmetric encryption.
/// </summary>
/// <remarks>
/// Supports AES encryption in CBC and GCM modes:
/// - cipher.update(data, inputEncoding?, outputEncoding?) - encrypts data
/// - cipher.final(outputEncoding?) - finalizes encryption
/// - cipher.setAutoPadding(autoPadding) - sets padding mode (CBC only)
/// - cipher.getAuthTag() - returns auth tag (GCM only)
/// - cipher.setAAD(buffer) - sets additional authenticated data (GCM only)
/// </remarks>
public class SharpTSCipher : IDisposable
{
    private readonly string _algorithm;
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly bool _isGcm;
    private readonly int _keySize;

    // CBC mode
    private Aes? _aes;
    private ICryptoTransform? _encryptor;
    private readonly List<byte> _outputBuffer = new();

    // GCM mode
    private AesGcm? _aesGcm;
    private readonly List<byte> _plaintextBuffer = new();
    private byte[]? _authTag;
    private byte[]? _aad;

    private bool _finalized;
    private bool _autoPadding = true;
    private bool _disposed;

    /// <summary>
    /// Creates a new Cipher object for the specified algorithm.
    /// </summary>
    /// <param name="algorithm">Algorithm name: aes-128-cbc, aes-192-cbc, aes-256-cbc, aes-128-gcm, aes-192-gcm, aes-256-gcm</param>
    /// <param name="key">Encryption key as byte array</param>
    /// <param name="iv">Initialization vector as byte array</param>
    public SharpTSCipher(string algorithm, byte[] key, byte[] iv)
    {
        _algorithm = algorithm.ToLowerInvariant();

        // Parse algorithm to determine mode and key size
        var (keySize, isGcm) = ParseAlgorithm(_algorithm);
        _keySize = keySize;
        _isGcm = isGcm;

        // Validate key size
        if (key.Length != keySize / 8)
            throw new ArgumentException($"Invalid key length for {_algorithm}. Expected {keySize / 8} bytes, got {key.Length} bytes.");

        // Validate IV size
        var expectedIvSize = isGcm ? 12 : 16;
        if (iv.Length != expectedIvSize)
            throw new ArgumentException($"Invalid IV length for {_algorithm}. Expected {expectedIvSize} bytes, got {iv.Length} bytes.");

        _key = key;
        _iv = iv;
        _finalized = false;

        if (isGcm)
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
            _encryptor = _aes.CreateEncryptor();
        }
    }

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

    /// <summary>
    /// Encrypts data and returns the ciphertext.
    /// </summary>
    /// <param name="data">Data to encrypt (string or Buffer)</param>
    /// <param name="inputEncoding">Input encoding for string data: utf8, hex, base64</param>
    /// <param name="outputEncoding">Output encoding: hex, base64, or null for Buffer</param>
    /// <returns>Encrypted data as string or Buffer</returns>
    public object Update(object data, string? inputEncoding = null, string? outputEncoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("Cipher has already been finalized");

        var inputBytes = ConvertToBytes(data, inputEncoding);

        if (_isGcm)
        {
            // For GCM, accumulate plaintext until final()
            _plaintextBuffer.AddRange(inputBytes);
            // Return empty result (encryption happens in final)
            return FormatOutput([], outputEncoding);
        }
        else
        {
            // For CBC, we need to buffer data and only process complete blocks
            _outputBuffer.AddRange(inputBytes);

            // Process complete blocks (each block is 16 bytes for AES)
            const int blockSize = 16;
            var completeBlocks = _outputBuffer.Count / blockSize;
            if (completeBlocks == 0)
            {
                // Not enough data for a complete block
                return FormatOutput([], outputEncoding);
            }

            var bytesToProcess = completeBlocks * blockSize;
            var dataToProcess = _outputBuffer.Take(bytesToProcess).ToArray();
            _outputBuffer.RemoveRange(0, bytesToProcess);

            var outputBytes = new byte[bytesToProcess];
            var bytesWritten = _encryptor!.TransformBlock(dataToProcess, 0, dataToProcess.Length, outputBytes, 0);

            if (bytesWritten > 0)
            {
                var result = new byte[bytesWritten];
                Array.Copy(outputBytes, result, bytesWritten);
                return FormatOutput(result, outputEncoding);
            }

            return FormatOutput([], outputEncoding);
        }
    }

    /// <summary>
    /// Finalizes encryption and returns any remaining ciphertext.
    /// </summary>
    /// <param name="outputEncoding">Output encoding: hex, base64, or null for Buffer</param>
    /// <returns>Final encrypted data as string or Buffer</returns>
    public object Final(string? outputEncoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("Cipher has already been finalized");

        _finalized = true;

        if (_isGcm)
        {
            // Perform GCM encryption
            var plaintext = _plaintextBuffer.ToArray();
            var ciphertext = new byte[plaintext.Length];
            _authTag = new byte[AesGcm.TagByteSizes.MaxSize];

            if (_aad != null)
            {
                _aesGcm!.Encrypt(_iv, plaintext, ciphertext, _authTag, _aad);
            }
            else
            {
                _aesGcm!.Encrypt(_iv, plaintext, ciphertext, _authTag);
            }

            return FormatOutput(ciphertext, outputEncoding);
        }
        else
        {
            // Finalize CBC encryption with any remaining buffered data
            var remainingData = _outputBuffer.ToArray();
            var finalBlock = _encryptor!.TransformFinalBlock(remainingData, 0, remainingData.Length);
            return FormatOutput(finalBlock, outputEncoding);
        }
    }

    /// <summary>
    /// Sets whether to use auto-padding (CBC mode only).
    /// </summary>
    /// <param name="autoPadding">True to use PKCS7 padding, false for no padding</param>
    /// <returns>This cipher for chaining</returns>
    public SharpTSCipher SetAutoPadding(bool autoPadding)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot set auto padding after cipher has been finalized");

        if (_isGcm)
            return this; // GCM doesn't use padding

        _autoPadding = autoPadding;

        // Recreate the encryptor with new padding
        _aes!.Padding = autoPadding ? PaddingMode.PKCS7 : PaddingMode.None;
        _encryptor?.Dispose();
        _encryptor = _aes.CreateEncryptor();

        return this;
    }

    /// <summary>
    /// Gets the authentication tag (GCM mode only, after final()).
    /// </summary>
    /// <returns>Authentication tag as Buffer</returns>
    public SharpTSBuffer GetAuthTag()
    {
        if (!_isGcm)
            throw new InvalidOperationException("getAuthTag is only available for GCM mode ciphers");

        if (!_finalized || _authTag == null)
            throw new InvalidOperationException("getAuthTag must be called after final()");

        return new SharpTSBuffer(_authTag);
    }

    /// <summary>
    /// Sets additional authenticated data (GCM mode only, before final()).
    /// </summary>
    /// <param name="aad">Additional authenticated data as Buffer</param>
    /// <returns>This cipher for chaining</returns>
    public SharpTSCipher SetAAD(object aad)
    {
        if (!_isGcm)
            throw new InvalidOperationException("setAAD is only available for GCM mode ciphers");

        if (_finalized)
            throw new InvalidOperationException("setAAD must be called before final()");

        _aad = ConvertToBytes(aad, null);
        return this;
    }

    /// <summary>
    /// Converts input data to byte array.
    /// </summary>
    private static byte[] ConvertToBytes(object data, string? encoding)
    {
        return data switch
        {
            string s => encoding?.ToLowerInvariant() switch
            {
                "hex" => Convert.FromHexString(s),
                "base64" => Convert.FromBase64String(s),
                _ => Encoding.UTF8.GetBytes(s) // utf8 default
            },
            SharpTSBuffer buf => buf.Data,
            byte[] bytes => bytes,
            _ => throw new ArgumentException("Data must be a string or Buffer")
        };
    }

    /// <summary>
    /// Formats output bytes according to encoding.
    /// </summary>
    private static object FormatOutput(byte[] bytes, string? encoding)
    {
        return encoding?.ToLowerInvariant() switch
        {
            "hex" => Convert.ToHexString(bytes).ToLowerInvariant(),
            "base64" => Convert.ToBase64String(bytes),
            _ => new SharpTSBuffer(bytes)
        };
    }

    /// <summary>
    /// Gets a member of this cipher object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "update" => new BuiltInMethod("update", 1, 3, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new ArgumentException("cipher.update requires data argument");

                var inputEncoding = args.Count > 1 ? args[1]?.ToString() : null;
                var outputEncoding = args.Count > 2 ? args[2]?.ToString() : null;
                return Update(args[0]!, inputEncoding, outputEncoding);
            }),
            "final" => new BuiltInMethod("final", 0, 1, (interp, recv, args) =>
            {
                var outputEncoding = args.Count > 0 ? args[0]?.ToString() : null;
                return Final(outputEncoding);
            }),
            "setAutoPadding" => new BuiltInMethod("setAutoPadding", 0, 1, (interp, recv, args) =>
            {
                var autoPadding = args.Count == 0 || (args[0] is bool b ? b : args[0] is double d ? d != 0 : true);
                return SetAutoPadding(autoPadding);
            }),
            "getAuthTag" => new BuiltInMethod("getAuthTag", 0, (interp, recv, args) =>
            {
                return GetAuthTag();
            }),
            "setAAD" => new BuiltInMethod("setAAD", 1, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new ArgumentException("cipher.setAAD requires buffer argument");
                return SetAAD(args[0]!);
            }),
            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _encryptor?.Dispose();
        _aes?.Dispose();
        _aesGcm?.Dispose();
    }
}
