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
    private static HashAlgorithmName ParseAlgorithm(string algorithm) =>
        CryptoAlgorithms.ParseHashName(algorithm, stripSignaturePrefix: true, context: "signing");

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

        return CryptoEncoding.ToBufferOrString(signature, encoding);
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
            "sign" => BuiltInMethod.CreateV2("sign", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException("sign() requires a private key argument");

                var encoding = args.Length > 1 ? args[1].ToObject()?.ToString() : null;

                if (args[0].IsString)
                    return RuntimeValue.FromBoxed(Sign(args[0].AsStringUnsafe(), encoding));
                if (args[0].ToObject() is SharpTSObject keyObj)
                    return RuntimeValue.FromBoxed(Sign(keyObj, encoding));

                throw new ArgumentException("sign() key must be a string or object");
            }),
            _ => null
        };
    }
}
