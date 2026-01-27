using System.Security.Cryptography;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Sign object for cryptographic signing.
/// </summary>
/// <remarks>
/// Wraps .NET's RSA/ECDsa APIs to provide the Node.js Sign API:
/// - sign.update(data) - adds data to be signed
/// - sign.sign(privateKey, encoding?) - signs the data and returns the signature
/// </remarks>
public class SharpTSSign
{
    private readonly HashAlgorithmName _hashAlgorithm;
    private readonly List<byte> _data = new();
    private bool _finalized;

    /// <summary>
    /// Creates a new Sign object using the specified algorithm.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: sha1, sha256, sha384, sha512, or RSA-SHA256 style names</param>
    public SharpTSSign(string algorithm)
    {
        _hashAlgorithm = ParseAlgorithm(algorithm);
        _finalized = false;
    }

    /// <summary>
    /// Parses the algorithm string into a HashAlgorithmName.
    /// Supports both simple names (sha256) and prefixed names (RSA-SHA256).
    /// </summary>
    private static HashAlgorithmName ParseAlgorithm(string algorithm)
    {
        // Normalize: remove prefix like "RSA-" or "ECDSA-" and lowercase
        var normalized = algorithm.ToLowerInvariant();
        if (normalized.StartsWith("rsa-"))
            normalized = normalized[4..];
        else if (normalized.StartsWith("ecdsa-"))
            normalized = normalized[6..];

        return normalized switch
        {
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException($"Unsupported signing algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Updates the signer with the given data.
    /// </summary>
    /// <param name="data">The data to add for signing.</param>
    /// <returns>This Sign object for chaining.</returns>
    public SharpTSSign Update(string data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update Sign after sign() has been called");

        var bytes = Encoding.UTF8.GetBytes(data);
        _data.AddRange(bytes);
        return this;
    }

    /// <summary>
    /// Updates the signer with binary data.
    /// </summary>
    public SharpTSSign Update(byte[] data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update Sign after sign() has been called");

        _data.AddRange(data);
        return this;
    }

    /// <summary>
    /// Signs the accumulated data using the provided private key.
    /// </summary>
    /// <param name="privateKeyPem">PEM-encoded private key (RSA or EC)</param>
    /// <param name="encoding">Output encoding: "hex", "base64", or null for Buffer</param>
    /// <returns>The signature as a string or Buffer.</returns>
    public object Sign(string privateKeyPem, string? encoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("sign() has already been called");

        _finalized = true;
        var dataBytes = _data.ToArray();

        byte[] signature;

        // Detect key type from PEM header
        if (privateKeyPem.Contains("EC PRIVATE KEY") || privateKeyPem.Contains("-----BEGIN PRIVATE KEY-----"))
        {
            // Try EC first, fall back to RSA
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(privateKeyPem);
                signature = ecdsa.SignData(dataBytes, _hashAlgorithm);
            }
            catch
            {
                // Fall back to RSA
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                signature = rsa.SignData(dataBytes, _hashAlgorithm, RSASignaturePadding.Pkcs1);
            }
        }
        else
        {
            // Assume RSA
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            signature = rsa.SignData(dataBytes, _hashAlgorithm, RSASignaturePadding.Pkcs1);
        }

        return encoding?.ToLowerInvariant() switch
        {
            "hex" => Convert.ToHexString(signature).ToLowerInvariant(),
            "base64" => Convert.ToBase64String(signature),
            _ => new SharpTSBuffer(signature)
        };
    }

    /// <summary>
    /// Signs the accumulated data using a key object.
    /// </summary>
    public object Sign(SharpTSObject keyObject, string? encoding = null)
    {
        // Extract the key from the object
        if (!keyObject.Fields.TryGetValue("key", out var keyValue))
            throw new ArgumentException("Key object must have a 'key' property");

        var keyPem = keyValue?.ToString() ?? throw new ArgumentException("Key must be a string");
        return Sign(keyPem, encoding);
    }

    /// <summary>
    /// Gets a member of this Sign object (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "update" => new BuiltInMethod("update", 1, (interp, recv, args) =>
            {
                if (args.Count > 0)
                {
                    if (args[0] is string data)
                        return Update(data);
                    if (args[0] is SharpTSBuffer buf)
                        return Update(buf.Data);
                }
                return this;
            }),
            "sign" => new BuiltInMethod("sign", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new ArgumentException("sign() requires a private key argument");

                var encoding = args.Count > 1 ? args[1]?.ToString() : null;

                if (args[0] is string keyPem)
                    return Sign(keyPem, encoding);
                if (args[0] is SharpTSObject keyObj)
                    return Sign(keyObj, encoding);

                throw new ArgumentException("sign() key must be a string or object");
            }),
            _ => null
        };
    }
}
