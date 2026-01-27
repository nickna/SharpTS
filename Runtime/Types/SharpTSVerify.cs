using System.Security.Cryptography;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible Verify object for cryptographic signature verification.
/// </summary>
/// <remarks>
/// Wraps .NET's RSA/ECDsa APIs to provide the Node.js Verify API:
/// - verify.update(data) - adds data to be verified
/// - verify.verify(publicKey, signature, encoding?) - verifies the signature
/// </remarks>
public class SharpTSVerify
{
    private readonly HashAlgorithmName _hashAlgorithm;
    private readonly List<byte> _data = new();
    private bool _finalized;

    /// <summary>
    /// Creates a new Verify object using the specified algorithm.
    /// </summary>
    /// <param name="algorithm">The hash algorithm name: sha1, sha256, sha384, sha512, or RSA-SHA256 style names</param>
    public SharpTSVerify(string algorithm)
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
            _ => throw new ArgumentException($"Unsupported verification algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Updates the verifier with the given data.
    /// </summary>
    /// <param name="data">The data to add for verification.</param>
    /// <returns>This Verify object for chaining.</returns>
    public SharpTSVerify Update(string data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update Verify after verify() has been called");

        var bytes = Encoding.UTF8.GetBytes(data);
        _data.AddRange(bytes);
        return this;
    }

    /// <summary>
    /// Updates the verifier with binary data.
    /// </summary>
    public SharpTSVerify Update(byte[] data)
    {
        if (_finalized)
            throw new InvalidOperationException("Cannot update Verify after verify() has been called");

        _data.AddRange(data);
        return this;
    }

    /// <summary>
    /// Verifies the signature against the accumulated data using the provided public key.
    /// </summary>
    /// <param name="publicKeyPem">PEM-encoded public key (RSA or EC)</param>
    /// <param name="signature">The signature to verify</param>
    /// <param name="signatureEncoding">Input encoding of the signature: "hex", "base64", or null for Buffer</param>
    /// <returns>True if the signature is valid, false otherwise.</returns>
    public bool Verify(string publicKeyPem, object signature, string? signatureEncoding = null)
    {
        if (_finalized)
            throw new InvalidOperationException("verify() has already been called");

        _finalized = true;
        var dataBytes = _data.ToArray();

        // Convert signature to bytes
        byte[] signatureBytes = signature switch
        {
            string sigStr when signatureEncoding?.ToLowerInvariant() == "hex" =>
                Convert.FromHexString(sigStr),
            string sigStr when signatureEncoding?.ToLowerInvariant() == "base64" =>
                Convert.FromBase64String(sigStr),
            string sigStr => Encoding.UTF8.GetBytes(sigStr),
            SharpTSBuffer buf => buf.Data,
            byte[] bytes => bytes,
            _ => throw new ArgumentException("Signature must be a string or Buffer")
        };

        // Detect key type from PEM header
        if (publicKeyPem.Contains("EC PUBLIC KEY") || publicKeyPem.Contains("-----BEGIN PUBLIC KEY-----"))
        {
            // Try EC first, fall back to RSA
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicKeyPem);
                return ecdsa.VerifyData(dataBytes, signatureBytes, _hashAlgorithm);
            }
            catch
            {
                // Fall back to RSA
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                return rsa.VerifyData(dataBytes, signatureBytes, _hashAlgorithm, RSASignaturePadding.Pkcs1);
            }
        }
        else
        {
            // Assume RSA
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(dataBytes, signatureBytes, _hashAlgorithm, RSASignaturePadding.Pkcs1);
        }
    }

    /// <summary>
    /// Verifies using a key object.
    /// </summary>
    public bool Verify(SharpTSObject keyObject, object signature, string? signatureEncoding = null)
    {
        // Extract the key from the object
        if (!keyObject.Fields.TryGetValue("key", out var keyValue))
            throw new ArgumentException("Key object must have a 'key' property");

        var keyPem = keyValue?.ToString() ?? throw new ArgumentException("Key must be a string");
        return Verify(keyPem, signature, signatureEncoding);
    }

    /// <summary>
    /// Gets a member of this Verify object (for property access).
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
            "verify" => new BuiltInMethod("verify", 2, 3, (interp, recv, args) =>
            {
                if (args.Count < 2)
                    throw new ArgumentException("verify() requires public key and signature arguments");

                var signatureEncoding = args.Count > 2 ? args[2]?.ToString() : null;

                if (args[0] is string keyPem)
                    return Verify(keyPem, args[1]!, signatureEncoding);
                if (args[0] is SharpTSObject keyObj)
                    return Verify(keyObj, args[1]!, signatureEncoding);

                throw new ArgumentException("verify() key must be a string or object");
            }),
            _ => null
        };
    }
}
