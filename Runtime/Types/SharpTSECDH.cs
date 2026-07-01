using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible ECDH object for Elliptic Curve Diffie-Hellman key exchange.
/// </summary>
/// <remarks>
/// Provides ECDH key exchange functionality:
/// - generateKeys() - generates public/private key pair
/// - computeSecret() - computes shared secret with another party's public key
/// - getPublicKey() / getPrivateKey() - returns the keys
/// - setPublicKey() / setPrivateKey() - sets the keys
/// </remarks>
public class SharpTSECDH
{
    private readonly ECDiffieHellman _ecdh;
    private readonly string _curveName;

    /// <summary>
    /// Creates an ECDH object with the specified curve.
    /// </summary>
    /// <param name="curveName">The curve name: prime256v1, secp384r1, secp521r1, p-256, p-384, p-521</param>
    public SharpTSECDH(string curveName)
    {
        _curveName = curveName;
        var curve = curveName.ToLowerInvariant() switch
        {
            "prime256v1" or "secp256r1" or "p-256" => ECCurve.NamedCurves.nistP256,
            "secp384r1" or "p-384" => ECCurve.NamedCurves.nistP384,
            "secp521r1" or "p-521" => ECCurve.NamedCurves.nistP521,
            _ => throw new ArgumentException($"Unsupported curve: {curveName}")
        };
        _ecdh = ECDiffieHellman.Create(curve);
    }

    /// <summary>
    /// Generates and returns the public key.
    /// </summary>
    public object GenerateKeys(string? encoding = null, string? format = null)
    {
        // Regenerate the key pair
        var curve = _ecdh.ExportParameters(false).Curve;
        _ecdh.GenerateKey(curve);
        return GetPublicKey(encoding, format);
    }

    /// <summary>
    /// Computes the shared secret using the other party's public key.
    /// </summary>
    public object ComputeSecret(object otherPublicKey, string? inputEncoding = null, string? outputEncoding = null)
    {
        var otherBytes = DecodeInput(otherPublicKey, inputEncoding);

        // Import the other party's public key
        using var otherEcdh = ECDiffieHellman.Create();
        otherEcdh.ImportSubjectPublicKeyInfo(otherBytes, out _);

        // Derive the shared secret
        var secret = _ecdh.DeriveKeyMaterial(otherEcdh.PublicKey);
        return EncodeResult(secret, outputEncoding);
    }

    /// <summary>
    /// Returns the public key.
    /// </summary>
    public object GetPublicKey(string? encoding = null, string? format = null)
    {
        // format: "uncompressed" (default) or "compressed"
        // For now, we always return uncompressed format (SPKI)
        var bytes = _ecdh.ExportSubjectPublicKeyInfo();
        return EncodeResult(bytes, encoding);
    }

    /// <summary>
    /// Returns the private key.
    /// </summary>
    public object GetPrivateKey(string? encoding = null)
    {
        var bytes = _ecdh.ExportPkcs8PrivateKey();
        return EncodeResult(bytes, encoding);
    }

    /// <summary>
    /// Sets the public key (imports from SPKI format).
    /// </summary>
    public void SetPublicKey(object key, string? encoding = null)
    {
        // Note: ECDiffieHellman doesn't have ImportSubjectPublicKeyInfo that sets the public key alone
        // This is a limitation - in Node.js, this would set the public key for the ECDH instance
        // For now, we throw as this isn't commonly used
        throw new NotSupportedException("setPublicKey is not supported for ECDH in this implementation");
    }

    /// <summary>
    /// Sets the private key (imports from PKCS8 format).
    /// </summary>
    public void SetPrivateKey(object key, string? encoding = null)
    {
        var keyBytes = DecodeInput(key, encoding);
        _ecdh.ImportPkcs8PrivateKey(keyBytes, out _);
    }

    private static object EncodeResult(byte[] bytes, string? encoding) =>
        CryptoEncoding.ToBufferOrString(bytes, encoding);

    private static byte[] DecodeInput(object input, string? encoding) =>
        CryptoEncoding.FromEncoded(input, encoding);

    /// <summary>
    /// Gets a member of this ECDH object.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "generateKeys" => BuiltInMethod.CreateV2("generateKeys", 0, 2, (_, _, args) =>
            {
                var encoding = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                var format = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(GenerateKeys(encoding, format));
            }),
            "computeSecret" => BuiltInMethod.CreateV2("computeSecret", 1, 3, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException("computeSecret requires a public key argument");
                var inputEncoding = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                var outputEncoding = args.Length > 2 ? args[2].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(ComputeSecret(args[0].ToObject()!, inputEncoding, outputEncoding));
            }),
            "getPublicKey" => BuiltInMethod.CreateV2("getPublicKey", 0, 2, (_, _, args) =>
            {
                var encoding = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                var format = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(GetPublicKey(encoding, format));
            }),
            "getPrivateKey" => BuiltInMethod.CreateV2("getPrivateKey", 0, 1, (_, _, args) =>
            {
                var encoding = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
                return RuntimeValue.FromBoxed(GetPrivateKey(encoding));
            }),
            "setPublicKey" => BuiltInMethod.CreateV2("setPublicKey", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException("setPublicKey requires a key argument");
                var encoding = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                SetPublicKey(args[0].ToObject()!, encoding);
                return RuntimeValue.Null;
            }),
            "setPrivateKey" => BuiltInMethod.CreateV2("setPrivateKey", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new ArgumentException("setPrivateKey requires a key argument");
                var encoding = args.Length > 1 ? args[1].ToObject()?.ToString() : null;
                SetPrivateKey(args[0].ToObject()!, encoding);
                return RuntimeValue.Null;
            }),
            _ => null
        };
    }
}
