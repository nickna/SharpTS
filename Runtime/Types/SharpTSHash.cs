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
        var hashName = CryptoAlgorithms.ParseHashName(algorithm, context: "hash");

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

        return CryptoEncoding.ToBufferOrString(hashBytes, encoding);
    }

    /// <summary>
    /// Gets a member of this hash object (for property access).
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
