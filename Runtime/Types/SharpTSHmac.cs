using System.Security.Cryptography;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Hmac object for keyed-hash message authentication.
/// </summary>
/// <remarks>
/// Wraps .NET's IncrementalHash (HMAC mode) to provide the Node.js Hmac API:
/// - hmac.update(data) - adds data to the HMAC
/// - hmac.digest(encoding?) - returns the HMAC value
/// </remarks>
public class SharpTSHmac
{
    private readonly IncrementalHash _hmac;
    private bool _finalized;

    /// <summary>
    /// Creates a new HMAC object using the specified algorithm and key.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: md5, sha1, sha256, sha384, sha512</param>
    /// <param name="key">The secret key as a string (UTF-8 encoded) or byte array</param>
    public SharpTSHmac(string algorithm, object key)
    {
        var hashName = CryptoAlgorithms.ParseHashName(algorithm, context: "HMAC");

        byte[] keyBytes = ConvertToBytes(key);
        _hmac = IncrementalHash.CreateHMAC(hashName, keyBytes);
        _finalized = false;
    }

    /// <summary>
    /// Converts the key to a byte array.
    /// </summary>
    private static byte[] ConvertToBytes(object key)
    {
        return key switch
        {
            string s => Encoding.UTF8.GetBytes(s),
            SharpTSBuffer buf => buf.Data,
            SharpTSArray arr => ConvertArrayToBytes(arr),
            byte[] bytes => bytes,
            _ => throw new ArgumentException("Key must be a string or Buffer (array of bytes)")
        };
    }

    /// <summary>
    /// Converts a SharpTSArray (Buffer-like) to a byte array.
    /// </summary>
    private static byte[] ConvertArrayToBytes(SharpTSArray arr)
    {
        var bytes = new byte[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            var element = arr.Get(i);
            bytes[i] = element switch
            {
                double d => (byte)d,
                int n => (byte)n,
                _ => throw new ArgumentException("Buffer elements must be numbers")
            };
        }
        return bytes;
    }

    /// <summary>
    /// Updates the HMAC with the given data.
    /// </summary>
    /// <param name="data">The data to add to the HMAC.</param>
    /// <returns>This HMAC object for chaining.</returns>
    public SharpTSHmac Update(string data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update HMAC after digest() has been called");

        var bytes = Encoding.UTF8.GetBytes(data);
        _hmac.AppendData(bytes);
        return this;
    }

    /// <summary>
    /// Finalizes the HMAC and returns the digest.
    /// </summary>
    /// <param name="encoding">The output encoding: "hex", "base64", or null for raw bytes.</param>
    /// <returns>The HMAC digest as a string or byte array.</returns>
    public object Digest(string? encoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("digest() has already been called");

        _finalized = true;
        var hmacBytes = _hmac.GetHashAndReset();

        return CryptoEncoding.ToBufferOrString(hmacBytes, encoding);
    }

    /// <summary>
    /// Gets a member of this HMAC object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "update" => BuiltInMethod.CreateV2("update", 1, (_, _, args) =>
            {
                if (args.Length > 0 && args[0].IsString)
                    return RuntimeValue.FromBoxed(Update(args[0].AsStringUnsafe()));
                return RuntimeValue.FromObject(this);
            }),
            "digest" => BuiltInMethod.CreateV2("digest", 0, 1, (_, _, args) =>
            {
                var encoding = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(Digest(encoding));
            }),
            _ => null
        };
    }
}
