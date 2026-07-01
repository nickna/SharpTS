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
    private static HashAlgorithmName ParseAlgorithm(string algorithm) =>
        CryptoAlgorithms.ParseHashName(algorithm, stripSignaturePrefix: true, context: "verification");

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
        byte[] signatureBytes = CryptoEncoding.FromEncoded(signature, signatureEncoding);

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
            "update" => BuiltInMethod.CreateV2("update", 1, (_, _, args) =>
            {
                if (args.Length > 0)
                {
                    if (args[0].IsString)
                        return RuntimeValue.FromBoxed(Update(args[0].AsStringUnsafe()));
                    if (args[0].ToObject() is SharpTSBuffer buf)
                        return RuntimeValue.FromBoxed(Update(buf.Data));
                }
                return RuntimeValue.FromObject(this);
            }),
            "verify" => BuiltInMethod.CreateV2("verify", 2, 3, (_, _, args) =>
            {
                if (args.Length < 2)
                    throw new ArgumentException("verify() requires public key and signature arguments");

                var signatureEncoding = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

                if (args[0].IsString)
                    return RuntimeValue.FromBoxed(Verify(args[0].AsStringUnsafe(), args[1].ToObject()!, signatureEncoding));
                if (args[0].ToObject() is SharpTSObject keyObj)
                    return RuntimeValue.FromBoxed(Verify(keyObj, args[1].ToObject()!, signatureEncoding));

                throw new ArgumentException("verify() key must be a string or object");
            }),
            _ => null
        };
    }
}
