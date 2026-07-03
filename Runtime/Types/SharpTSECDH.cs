using System.Numerics;
using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible ECDH object for Elliptic Curve Diffie-Hellman key exchange.
/// </summary>
/// <remarks>
/// Provides ECDH key exchange functionality (#1060, Node-faithful):
/// - generateKeys() / getPublicKey() - raw EC point (uncompressed 04||X||Y by default,
///   'compressed' 02/03||X supported)
/// - computeSecret() - raw shared secret (the X coordinate), accepting raw points
///   (or SPKI DER for backward compatibility)
/// - getPrivateKey() - raw private scalar
/// - setPrivateKey() - accepts PKCS#8 DER (legacy deviation; raw scalars need
///   public-point recovery the BCL can't do)
/// NOTE: Must stay in sync with the emitted $ECDH (Compilation/RuntimeEmitter.TSECDH.cs).
/// </remarks>
public class SharpTSECDH
{
    private readonly ECDiffieHellman _ecdh;
    private readonly int _fieldByteLength;

    /// <summary>
    /// Creates an ECDH object with the specified curve.
    /// </summary>
    /// <param name="curveName">The curve name: prime256v1, secp384r1, secp521r1, p-256, p-384, p-521</param>
    public SharpTSECDH(string curveName)
    {
        var (curve, byteLen) = ResolveCurve(curveName);
        _fieldByteLength = byteLen;
        _ecdh = ECDiffieHellman.Create(curve);
    }

    internal static (ECCurve Curve, int FieldByteLength) ResolveCurve(string curveName)
    {
        return curveName.ToLowerInvariant() switch
        {
            "prime256v1" or "secp256r1" or "p-256" => (ECCurve.NamedCurves.nistP256, 32),
            "secp384r1" or "p-384" => (ECCurve.NamedCurves.nistP384, 48),
            "secp521r1" or "p-521" => (ECCurve.NamedCurves.nistP521, 66),
            _ => throw new ArgumentException($"Unsupported curve: {curveName}")
        };
    }

    /// <summary>
    /// Generates a fresh key pair and returns the public key.
    /// </summary>
    public object GenerateKeys(string? encoding = null, string? format = null)
    {
        // Regenerate the key pair
        var curve = _ecdh.ExportParameters(false).Curve;
        _ecdh.GenerateKey(curve);
        return GetPublicKey(encoding, format);
    }

    /// <summary>
    /// Computes the shared secret using the other party's public key
    /// (raw EC point or SPKI DER). Returns the raw X coordinate, like Node.
    /// </summary>
    public object ComputeSecret(object otherPublicKey, string? inputEncoding = null, string? outputEncoding = null)
    {
        var otherBytes = DecodeInput(otherPublicKey, inputEncoding);

        using var otherEcdh = ECDiffieHellman.Create();
        if (otherBytes.Length > 0 && otherBytes[0] == 0x30)
        {
            // Backward compatibility: SPKI DER
            otherEcdh.ImportSubjectPublicKeyInfo(otherBytes, out _);
        }
        else
        {
            var curve = _ecdh.ExportParameters(false).Curve;
            otherEcdh.ImportParameters(new ECParameters
            {
                Curve = curve,
                Q = DecodePoint(otherBytes, _fieldByteLength)
            });
        }

        var secret = _ecdh.DeriveRawSecretAgreement(otherEcdh.PublicKey);
        return EncodeResult(secret, outputEncoding);
    }

    /// <summary>
    /// Returns the public key as a raw EC point.
    /// </summary>
    /// <param name="encoding">Output encoding: hex/base64/null (Buffer).</param>
    /// <param name="format">"uncompressed" (default), "compressed", or "hybrid".</param>
    public object GetPublicKey(string? encoding = null, string? format = null)
    {
        var p = _ecdh.ExportParameters(false);
        var bytes = EncodePoint(p.Q, _fieldByteLength, format ?? "uncompressed");
        return EncodeResult(bytes, encoding);
    }

    /// <summary>
    /// Returns the private key as the raw scalar (Node behavior).
    /// </summary>
    public object GetPrivateKey(string? encoding = null)
    {
        var p = _ecdh.ExportParameters(true);
        return EncodeResult(p.D!, encoding);
    }

    /// <summary>
    /// Sets the public key. Deprecated in Node; not supported here.
    /// </summary>
    public void SetPublicKey(object key, string? encoding = null)
    {
        throw new NotSupportedException("setPublicKey is not supported for ECDH in this implementation");
    }

    /// <summary>
    /// Sets the private key (imports from PKCS8 format — deviation: Node takes the raw
    /// scalar, but the BCL cannot recover the public point from a bare scalar).
    /// </summary>
    public void SetPrivateKey(object key, string? encoding = null)
    {
        var keyBytes = DecodeInput(key, encoding);
        _ecdh.ImportPkcs8PrivateKey(keyBytes, out _);
    }

    /// <summary>Encodes an EC point per the requested Node point-conversion format.</summary>
    internal static byte[] EncodePoint(ECPoint q, int fieldLen, string format)
    {
        var x = PadTo(q.X!, fieldLen);
        var y = PadTo(q.Y!, fieldLen);
        switch (format.ToLowerInvariant())
        {
            case "uncompressed":
            {
                var result = new byte[1 + 2 * fieldLen];
                result[0] = 0x04;
                x.CopyTo(result, 1);
                y.CopyTo(result, 1 + fieldLen);
                return result;
            }
            case "compressed":
            {
                var result = new byte[1 + fieldLen];
                result[0] = (byte)((y[^1] & 1) == 0 ? 0x02 : 0x03);
                x.CopyTo(result, 1);
                return result;
            }
            case "hybrid":
            {
                var result = new byte[1 + 2 * fieldLen];
                result[0] = (byte)((y[^1] & 1) == 0 ? 0x06 : 0x07);
                x.CopyTo(result, 1);
                y.CopyTo(result, 1 + fieldLen);
                return result;
            }
            default:
                throw new ArgumentException($"Invalid point format '{format}' (expected 'uncompressed', 'compressed', or 'hybrid')");
        }
    }

    /// <summary>Decodes a raw EC point (uncompressed/hybrid/compressed) into an ECPoint.</summary>
    internal static ECPoint DecodePoint(byte[] bytes, int fieldLen)
    {
        if (bytes.Length == 1 + 2 * fieldLen && bytes[0] is 0x04 or 0x06 or 0x07)
        {
            return new ECPoint
            {
                X = bytes[1..(1 + fieldLen)],
                Y = bytes[(1 + fieldLen)..]
            };
        }
        if (bytes.Length == 1 + fieldLen && bytes[0] is 0x02 or 0x03)
        {
            var x = bytes[1..];
            var y = DecompressY(x, bytes[0] == 0x03, fieldLen);
            return new ECPoint { X = x, Y = y };
        }
        throw new ArgumentException("Invalid EC public key point");
    }

    /// <summary>
    /// Recovers Y from a compressed point. All three NIST primes are ≡ 3 (mod 4), so
    /// sqrt(c) = c^((p+1)/4) mod p.
    /// </summary>
    internal static byte[] DecompressY(byte[] xBytes, bool odd, int fieldLen)
    {
        var (p, b) = CurveParamsForFieldLength(fieldLen);
        var x = new BigInteger(xBytes, isUnsigned: true, isBigEndian: true);

        // y² = x³ - 3x + b (mod p)
        var rhs = (BigInteger.ModPow(x, 3, p) - 3 * x + b) % p;
        if (rhs < 0) rhs += p;

        var y = BigInteger.ModPow(rhs, (p + 1) / 4, p);
        if (((y % 2 == 1) ? !odd : odd))
            y = p - y;

        var yBytes = y.ToByteArray(isUnsigned: true, isBigEndian: true);
        return PadTo(yBytes, fieldLen);
    }

    /// <summary>Prime modulus and b coefficient for the NIST P-curves, keyed by field byte length.</summary>
    private static (BigInteger P, BigInteger B) CurveParamsForFieldLength(int fieldLen)
    {
        return fieldLen switch
        {
            // Primes are computed from their generalized-Mersenne forms (typo-proof);
            // b coefficients are the FIPS 186-4 constants.
            32 => (
                (BigInteger.One << 256) - (BigInteger.One << 224) + (BigInteger.One << 192) + (BigInteger.One << 96) - 1,
                BigInteger.Parse("05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", System.Globalization.NumberStyles.HexNumber)),
            48 => (
                (BigInteger.One << 384) - (BigInteger.One << 128) - (BigInteger.One << 96) + (BigInteger.One << 32) - 1,
                BigInteger.Parse("0B3312FA7E23EE7E4988E056BE3F82D19181D9C6EFE8141120314088F5013875AC656398D8A2ED19D2A85C8EDD3EC2AEF", System.Globalization.NumberStyles.HexNumber)),
            66 => (
                (BigInteger.One << 521) - 1,
                BigInteger.Parse("051953EB9618E1C9A1F929A21A0B68540EEA2DA725B99B315F3B8B489918EF109E156193951EC7E937B1652C0BD3BB1BF073573DF883D2C34F1EF451FD46B503F00", System.Globalization.NumberStyles.HexNumber)),
            _ => throw new ArgumentException("Unsupported curve field length")
        };
    }

    /// <summary>
    /// ECDH.convertKey(key, curve[, inputEncoding[, outputEncoding[, format]]]) —
    /// re-encodes a raw EC public key between point-conversion formats.
    /// </summary>
    internal static object ConvertKey(object key, string curveName, string? inputEncoding, string? outputEncoding, string? format)
    {
        var (_, fieldLen) = ResolveCurve(curveName);
        var bytes = CryptoEncoding.FromEncoded(key, inputEncoding);
        var point = DecodePoint(bytes, fieldLen);
        var result = EncodePoint(point, fieldLen, format ?? "uncompressed");
        return CryptoEncoding.ToBufferOrString(result, outputEncoding);
    }

    private static byte[] PadTo(byte[] bytes, int length)
    {
        if (bytes.Length == length) return bytes;
        if (bytes.Length > length)
            return bytes[(bytes.Length - length)..];
        var padded = new byte[length];
        bytes.CopyTo(padded, length - bytes.Length);
        return padded;
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
