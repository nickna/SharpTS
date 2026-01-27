using System.Security.Cryptography;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Hash object for cryptographic hashing.
/// </summary>
/// <remarks>
/// Wraps .NET's IncrementalHash to provide the Node.js Hash API:
/// - hash.update(data) - adds data to the hash
/// - hash.digest(encoding?) - returns the hash value
/// </remarks>
public class SharpTSHash
{
    private readonly IncrementalHash _hash;
    private bool _finalized;

    /// <summary>
    /// Creates a new hash object using the specified algorithm.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: md5, sha1, sha256, sha512</param>
    public SharpTSHash(string algorithm)
    {
        var hashName = algorithm.ToLowerInvariant() switch
        {
            "md5" => HashAlgorithmName.MD5,
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException($"Unsupported hash algorithm: {algorithm}")
        };

        _hash = IncrementalHash.CreateHash(hashName);
        _finalized = false;
    }

    /// <summary>
    /// Updates the hash with the given data.
    /// </summary>
    /// <param name="data">The data to add to the hash.</param>
    /// <returns>This hash object for chaining.</returns>
    public SharpTSHash Update(string data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update hash after digest() has been called");

        var bytes = Encoding.UTF8.GetBytes(data);
        _hash.AppendData(bytes);
        return this;
    }

    /// <summary>
    /// Finalizes the hash and returns the digest.
    /// </summary>
    /// <param name="encoding">The output encoding: "hex", "base64", or null for raw bytes.</param>
    /// <returns>The hash digest as a string or byte array.</returns>
    public object Digest(string? encoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("digest() has already been called");

        _finalized = true;
        var hashBytes = _hash.GetHashAndReset();

        return encoding?.ToLowerInvariant() switch
        {
            "hex" => Convert.ToHexString(hashBytes).ToLowerInvariant(),
            "base64" => Convert.ToBase64String(hashBytes),
            _ => new SharpTSBuffer(hashBytes)
        };
    }

    /// <summary>
    /// Gets a member of this hash object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "update" => new BuiltInMethod("update", 1, (interp, recv, args) =>
            {
                if (args.Count > 0 && args[0] is string data)
                    return Update(data);
                return this;
            }),
            "digest" => new BuiltInMethod("digest", 0, 1, (interp, recv, args) =>
            {
                var encoding = args.Count > 0 ? args[0]?.ToString() : null;
                return Digest(encoding);
            }),
            _ => null
        };
    }
}
