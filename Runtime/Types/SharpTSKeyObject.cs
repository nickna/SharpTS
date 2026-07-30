using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// The type of cryptographic key.
/// </summary>
public enum KeyObjectType
{
    Secret,
    Public,
    Private
}

/// <summary>
/// The type of asymmetric key algorithm.
/// </summary>
public enum AsymmetricKeyType
{
    None,
    Rsa,
    Ec
}

/// <summary>
/// Represents a Node.js-compatible KeyObject for cryptographic keys.
/// </summary>
/// <remarks>
/// Provides the Node.js KeyObject API (#1059):
/// - type: 'secret' | 'public' | 'private'
/// - asymmetricKeyType: 'rsa' | 'ec' | undefined (for secret keys)
/// - asymmetricKeyDetails: { modulusLength, publicExponent } for RSA, { namedCurve } for EC
///   (publicExponent is a number here; Node uses bigint — documented deviation)
/// - symmetricKeySize: number (for secret keys only)
/// - export(options?): PEM string, DER Buffer, or JWK object
/// - equals(other): key-material comparison
/// </remarks>
public class SharpTSKeyObject : ISharpTSPropertyAccessor
{
    private readonly KeyObjectType _type;
    private readonly AsymmetricKeyType _asymmetricKeyType;
    private readonly byte[]? _symmetricKey;
    private readonly RSA? _rsaKey;
    private readonly ECDsa? _ecdsaKey;

    /// <summary>
    /// Gets the key type ('secret', 'public', or 'private').
    /// </summary>
    public KeyObjectType Type => _type;

    /// <summary>
    /// Gets the asymmetric key type for public/private keys (None for secret keys).
    /// </summary>
    public AsymmetricKeyType AsymmetricKeyAlgorithm => _asymmetricKeyType;

    /// <summary>
    /// Gets the symmetric key data (for secret keys only).
    /// </summary>
    internal byte[]? SymmetricKey => _symmetricKey;

    /// <summary>
    /// Gets the RSA key (for RSA public/private keys).
    /// </summary>
    internal RSA? RsaKey => _rsaKey;

    /// <summary>
    /// Gets the ECDsa key (for EC public/private keys).
    /// </summary>
    internal ECDsa? EcdsaKey => _ecdsaKey;

    /// <summary>
    /// Creates a secret (symmetric) KeyObject from raw key bytes.
    /// </summary>
    public SharpTSKeyObject(byte[] key)
    {
        _type = KeyObjectType.Secret;
        _asymmetricKeyType = AsymmetricKeyType.None;
        _symmetricKey = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <summary>Wraps an already-imported RSA key.</summary>
    internal SharpTSKeyObject(RSA rsa, bool isPrivate)
    {
        _type = isPrivate ? KeyObjectType.Private : KeyObjectType.Public;
        _asymmetricKeyType = AsymmetricKeyType.Rsa;
        _rsaKey = rsa;
    }

    /// <summary>Wraps an already-imported EC key.</summary>
    internal SharpTSKeyObject(ECDsa ecdsa, bool isPrivate)
    {
        _type = isPrivate ? KeyObjectType.Private : KeyObjectType.Public;
        _asymmetricKeyType = AsymmetricKeyType.Ec;
        _ecdsaKey = ecdsa;
    }

    /// <summary>
    /// Creates a public KeyObject from a PEM-encoded public key (a private PEM
    /// yields the corresponding public key, matching Node's createPublicKey).
    /// </summary>
    public static SharpTSKeyObject CreatePublicKey(string pem)
    {
        if (string.IsNullOrEmpty(pem))
            throw new ArgumentNullException(nameof(pem));

        return new SharpTSKeyObject(pem, isPrivate: false);
    }

    /// <summary>
    /// Creates a private KeyObject from a PEM-encoded private key.
    /// </summary>
    public static SharpTSKeyObject CreatePrivateKey(string pem)
    {
        if (string.IsNullOrEmpty(pem))
            throw new ArgumentNullException(nameof(pem));

        return new SharpTSKeyObject(pem, isPrivate: true);
    }

    /// <summary>
    /// Creates a public/private KeyObject from DER bytes
    /// ('spki'/'pkcs1' for public; 'pkcs8'/'pkcs1'/'sec1' for private).
    /// </summary>
    public static SharpTSKeyObject CreateFromDer(byte[] der, string? type, bool isPrivate)
    {
        ThrowIfEdKey(der);
        switch ((type ?? (isPrivate ? "pkcs8" : "spki")).ToLowerInvariant())
        {
            case "spki":
            {
                // SPKI can hold RSA or EC — probe RSA first, fall back to EC.
                try
                {
                    var rsa = RSA.Create();
                    rsa.ImportSubjectPublicKeyInfo(der, out _);
                    return new SharpTSKeyObject(rsa, isPrivate: false);
                }
                catch (CryptographicException)
                {
                    var ec = ECDsa.Create();
                    ec.ImportSubjectPublicKeyInfo(der, out _);
                    return new SharpTSKeyObject(ec, isPrivate: false);
                }
            }
            case "pkcs1":
            {
                var rsa = RSA.Create();
                if (isPrivate)
                    rsa.ImportRSAPrivateKey(der, out _);
                else
                    rsa.ImportRSAPublicKey(der, out _);
                return new SharpTSKeyObject(rsa, isPrivate);
            }
            case "pkcs8":
            {
                try
                {
                    var rsa = RSA.Create();
                    rsa.ImportPkcs8PrivateKey(der, out _);
                    return new SharpTSKeyObject(rsa, isPrivate: true);
                }
                catch (CryptographicException)
                {
                    var ec = ECDsa.Create();
                    ec.ImportPkcs8PrivateKey(der, out _);
                    return new SharpTSKeyObject(ec, isPrivate: true);
                }
            }
            case "sec1":
            {
                var ec = ECDsa.Create();
                ec.ImportECPrivateKey(der, out _);
                return new SharpTSKeyObject(ec, isPrivate: true);
            }
            default:
                throw new ArgumentException($"Unsupported DER key type '{type}'");
        }
    }

    /// <summary>
    /// Creates a KeyObject from a JWK object ({ kty: 'RSA' | 'EC' | 'oct' }).
    /// </summary>
    public static SharpTSKeyObject CreateFromJwk(SharpTSObject jwk, bool isPrivate)
    {
        var kty = jwk.Fields.TryGetValue("kty", out var k) ? k as string : null;
        switch (kty)
        {
            case "oct":
            {
                if (!jwk.Fields.TryGetValue("k", out var kk) || kk is not string kVal)
                    throw new ArgumentException("JWK 'oct' key requires a 'k' member");
                return new SharpTSKeyObject(FromBase64Url(kVal));
            }
            case "RSA":
            {
                var p = new RSAParameters
                {
                    Modulus = GetJwkBytes(jwk, "n") ?? throw new ArgumentException("JWK RSA key requires 'n'"),
                    Exponent = GetJwkBytes(jwk, "e") ?? throw new ArgumentException("JWK RSA key requires 'e'")
                };
                if (isPrivate)
                {
                    int half = (p.Modulus.Length + 1) / 2;
                    p.D = PadTo(GetJwkBytes(jwk, "d") ?? throw new ArgumentException("JWK RSA private key requires 'd'"), p.Modulus.Length);
                    p.P = PadTo(GetJwkBytes(jwk, "p") ?? throw new ArgumentException("JWK RSA private key requires 'p'"), half);
                    p.Q = PadTo(GetJwkBytes(jwk, "q") ?? throw new ArgumentException("JWK RSA private key requires 'q'"), half);
                    p.DP = PadTo(GetJwkBytes(jwk, "dp") ?? throw new ArgumentException("JWK RSA private key requires 'dp'"), half);
                    p.DQ = PadTo(GetJwkBytes(jwk, "dq") ?? throw new ArgumentException("JWK RSA private key requires 'dq'"), half);
                    p.InverseQ = PadTo(GetJwkBytes(jwk, "qi") ?? throw new ArgumentException("JWK RSA private key requires 'qi'"), half);
                }
                var rsa = RSA.Create();
                rsa.ImportParameters(p);
                return new SharpTSKeyObject(rsa, isPrivate);
            }
            case "EC":
            {
                var crv = jwk.Fields.TryGetValue("crv", out var c) ? c as string : null;
                var (curve, byteLen) = crv switch
                {
                    "P-256" => (ECCurve.NamedCurves.nistP256, 32),
                    "P-384" => (ECCurve.NamedCurves.nistP384, 48),
                    "P-521" => (ECCurve.NamedCurves.nistP521, 66),
                    _ => throw new ArgumentException($"Unsupported JWK EC curve '{crv}'")
                };
                var p = new ECParameters
                {
                    Curve = curve,
                    Q = new ECPoint
                    {
                        X = PadTo(GetJwkBytes(jwk, "x") ?? throw new ArgumentException("JWK EC key requires 'x'"), byteLen),
                        Y = PadTo(GetJwkBytes(jwk, "y") ?? throw new ArgumentException("JWK EC key requires 'y'"), byteLen)
                    }
                };
                if (isPrivate)
                    p.D = PadTo(GetJwkBytes(jwk, "d") ?? throw new ArgumentException("JWK EC private key requires 'd'"), byteLen);
                var ec = ECDsa.Create();
                ec.ImportParameters(p);
                return new SharpTSKeyObject(ec, isPrivate);
            }
            case "OKP":
                throw new NotSupportedException("JWK 'OKP' keys (Ed25519/Ed448/X25519/X448) are not supported on this runtime (.NET BCL has no EdDSA/X-curve support)");
            default:
                throw new ArgumentException($"Unsupported JWK key type '{kty}'");
        }
    }

    private static byte[]? GetJwkBytes(SharpTSObject jwk, string name)
        => jwk.Fields.TryGetValue(name, out var v) && v is string s ? FromBase64Url(s) : null;

    private static byte[] FromBase64Url(string s)
        => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Left-pads (or trims leading zeros of) a big-endian magnitude to an exact length.</summary>
    private static byte[] PadTo(byte[] bytes, int length)
    {
        if (bytes.Length == length) return bytes;
        if (bytes.Length > length)
        {
            int skip = bytes.Length - length;
            for (int i = 0; i < skip; i++)
                if (bytes[i] != 0) throw new ArgumentException("JWK field longer than expected for the key size");
            return bytes[skip..];
        }
        var padded = new byte[length];
        bytes.CopyTo(padded, length - bytes.Length);
        return padded;
    }

    /// <summary>
    /// Rejects Ed25519/Ed448/X25519/X448 keys (DER OIDs 1.3.101.110–113) with a clear
    /// ceiling error instead of an opaque import failure (#1061).
    /// </summary>
    internal static void ThrowIfEdKey(byte[] der)
    {
        // OID encodings: 06 03 2B 65 6E..71 (X25519, X448, Ed25519, Ed448)
        for (int i = 0; i + 4 < der.Length; i++)
        {
            if (der[i] == 0x06 && der[i + 1] == 0x03 && der[i + 2] == 0x2B && der[i + 3] == 0x65 &&
                der[i + 4] is >= 0x6E and <= 0x71)
            {
                throw new NotSupportedException(
                    "Ed25519/Ed448/X25519/X448 keys are not supported on this runtime (.NET BCL has no EdDSA/X-curve support)");
            }
        }
    }

    /// <summary>
    /// Internal constructor for asymmetric keys.
    /// </summary>
    private SharpTSKeyObject(string pem, bool isPrivate)
    {
        _type = isPrivate ? KeyObjectType.Private : KeyObjectType.Public;

        // Surface the EdDSA ceiling clearly before the generic import fails (#1061).
        if (TryGetPemBody(pem) is { } body)
            ThrowIfEdKey(body);

        // Try to detect key type from PEM header or by attempting imports
        // EC keys typically have "EC" in the header, RSA doesn't
        bool isEc = pem.Contains("EC PRIVATE KEY") || pem.Contains("EC PUBLIC KEY");

        if (isEc)
        {
            _ecdsaKey = ECDsa.Create();
            _ecdsaKey.ImportFromPem(pem);
            _asymmetricKeyType = AsymmetricKeyType.Ec;
        }
        else
        {
            // Try RSA first
            try
            {
                var rsaKey = RSA.Create();
                rsaKey.ImportFromPem(pem);
                _rsaKey = rsaKey;  // Only assign if import succeeds
                _asymmetricKeyType = AsymmetricKeyType.Rsa;
            }
            catch
            {
                // Fall back to EC (for generic PKCS#8/SPKI keys)
                try
                {
                    _ecdsaKey = ECDsa.Create();
                    _ecdsaKey.ImportFromPem(pem);
                    _asymmetricKeyType = AsymmetricKeyType.Ec;
                }
                catch
                {
                    throw new ArgumentException("Unable to parse key from PEM. Unsupported key format.");
                }
            }
        }
    }

    private static byte[]? TryGetPemBody(string pem)
    {
        var start = pem.IndexOf("-----BEGIN", StringComparison.Ordinal);
        if (start < 0) return null;
        var afterHeader = pem.IndexOf("-----", start + 10, StringComparison.Ordinal);
        if (afterHeader < 0) return null;
        var end = pem.IndexOf("-----END", afterHeader, StringComparison.Ordinal);
        if (end < 0) return null;
        var body = pem[(afterHeader + 5)..end].Replace("\r", "").Replace("\n", "").Trim();
        try { return Convert.FromBase64String(body); }
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// Exports the key in the requested format.
    /// Handles both options object style (compiled code) and direct string parameters.
    /// </summary>
    /// <param name="options">
    /// Either an options object with 'type' and 'format' properties,
    /// or can be called with no arguments for defaults.
    /// </param>
    /// <returns>PEM string, DER Buffer, or JWK object.</returns>
    public object Export(object? options = null)
    {
        string? type = null;
        string? format = null;

        // StreamFields is the shared adapter for interpreter objects,
        // dictionaries, and compiler-emitted $Object values.
        if (StreamFields.TryGet(options, "type", out var typeValue) &&
            typeValue is string typeString)
            type = typeString;
        if (StreamFields.TryGet(options, "format", out var formatValue) &&
            formatValue is string formatString)
            format = formatString;

        // Preserve the managed .NET interop fallback for custom options
        // objects exposing GetProperty(string). Compiler-emitted $Object
        // values have already gone through the guarded StreamFields path.
        if (options != null &&
            !ManagedEmittedShapeReflection.IsShape(
                options.GetType(), ManagedEmittedShape.HasFields) &&
            options is not SharpTSObject &&
            options is not IDictionary<string, object?>)
        {
            var getPropertyMethod = options.GetType().GetMethod("GetProperty", [typeof(string)]);
            if (getPropertyMethod != null)
            {
                if (getPropertyMethod.Invoke(options, ["type"]) is string reflectedType)
                    type = reflectedType;
                if (getPropertyMethod.Invoke(options, ["format"]) is string reflectedFormat)
                    format = reflectedFormat;
            }
        }

        return ExportInternal(type, format);
    }

    /// <summary>
    /// Internal export implementation.
    /// </summary>
    private object ExportInternal(string? type, string? format)
    {
        format ??= "pem";

        if (format.Equals("jwk", StringComparison.OrdinalIgnoreCase))
            return ExportJwk();

        if (_type == KeyObjectType.Secret)
        {
            // For secret keys, return the raw bytes as a Buffer
            return new SharpTSBuffer(_symmetricKey!);
        }

        // Asymmetric key export
        byte[] keyBytes;

        if (_rsaKey != null)
        {
            keyBytes = ExportRsaKey(type);
        }
        else if (_ecdsaKey != null)
        {
            keyBytes = ExportEcKey(type);
        }
        else
        {
            throw new InvalidOperationException("No key available for export");
        }

        if (format.Equals("der", StringComparison.OrdinalIgnoreCase))
        {
            return new SharpTSBuffer(keyBytes);
        }

        // PEM format
        return ConvertToPem(keyBytes, type);
    }

    /// <summary>Exports the key as a JWK object.</summary>
    public SharpTSObject ExportJwk()
    {
        var fields = new Dictionary<string, object?>();
        if (_type == KeyObjectType.Secret)
        {
            fields["kty"] = "oct";
            fields["k"] = ToBase64Url(_symmetricKey!);
            return new SharpTSObject(fields);
        }

        if (_rsaKey != null)
        {
            var p = _rsaKey.ExportParameters(_type == KeyObjectType.Private);
            fields["kty"] = "RSA";
            fields["n"] = ToBase64Url(p.Modulus!);
            fields["e"] = ToBase64Url(p.Exponent!);
            if (_type == KeyObjectType.Private)
            {
                fields["d"] = ToBase64Url(p.D!);
                fields["p"] = ToBase64Url(p.P!);
                fields["q"] = ToBase64Url(p.Q!);
                fields["dp"] = ToBase64Url(p.DP!);
                fields["dq"] = ToBase64Url(p.DQ!);
                fields["qi"] = ToBase64Url(p.InverseQ!);
            }
            return new SharpTSObject(fields);
        }

        if (_ecdsaKey != null)
        {
            var p = _ecdsaKey.ExportParameters(_type == KeyObjectType.Private);
            fields["kty"] = "EC";
            fields["crv"] = NodeCurveName(p.Curve) switch
            {
                "prime256v1" => "P-256",
                "secp384r1" => "P-384",
                "secp521r1" => "P-521",
                var other => other
            };
            fields["x"] = ToBase64Url(p.Q.X!);
            fields["y"] = ToBase64Url(p.Q.Y!);
            if (_type == KeyObjectType.Private && p.D != null)
                fields["d"] = ToBase64Url(p.D);
            return new SharpTSObject(fields);
        }

        throw new InvalidOperationException("No key available for export");
    }

    private byte[] ExportRsaKey(string? type)
    {
        if (_rsaKey == null)
            throw new InvalidOperationException("Not an RSA key");

        return (type?.ToLowerInvariant(), _type) switch
        {
            ("pkcs1", KeyObjectType.Public) => _rsaKey.ExportRSAPublicKey(),
            ("pkcs1", KeyObjectType.Private) => _rsaKey.ExportRSAPrivateKey(),
            ("spki", KeyObjectType.Public) or (null, KeyObjectType.Public) => _rsaKey.ExportSubjectPublicKeyInfo(),
            ("pkcs8", KeyObjectType.Private) or (null, KeyObjectType.Private) => _rsaKey.ExportPkcs8PrivateKey(),
            _ => throw new ArgumentException($"Invalid export type '{type}' for {_type} RSA key")
        };
    }

    private byte[] ExportEcKey(string? type)
    {
        if (_ecdsaKey == null)
            throw new InvalidOperationException("Not an EC key");

        return (type?.ToLowerInvariant(), _type) switch
        {
            ("spki", KeyObjectType.Public) or (null, KeyObjectType.Public) => _ecdsaKey.ExportSubjectPublicKeyInfo(),
            ("pkcs8", KeyObjectType.Private) or (null, KeyObjectType.Private) => _ecdsaKey.ExportPkcs8PrivateKey(),
            ("sec1", KeyObjectType.Private) => _ecdsaKey.ExportECPrivateKey(),
            _ => throw new ArgumentException($"Invalid export type '{type}' for {_type} EC key")
        };
    }

    private string ConvertToPem(byte[] keyBytes, string? type)
    {
        string label = (type?.ToLowerInvariant(), _type, _asymmetricKeyType) switch
        {
            ("pkcs1", KeyObjectType.Public, AsymmetricKeyType.Rsa) => "RSA PUBLIC KEY",
            ("pkcs1", KeyObjectType.Private, AsymmetricKeyType.Rsa) => "RSA PRIVATE KEY",
            ("sec1", KeyObjectType.Private, AsymmetricKeyType.Ec) => "EC PRIVATE KEY",
            (_, KeyObjectType.Public, _) => "PUBLIC KEY",
            (_, KeyObjectType.Private, _) => "PRIVATE KEY",
            _ => "PRIVATE KEY"
        };

        var base64 = Convert.ToBase64String(keyBytes);
        var lines = new List<string> { $"-----BEGIN {label}-----" };

        // Split into 64-character lines
        for (int i = 0; i < base64.Length; i += 64)
        {
            lines.Add(base64.Substring(i, Math.Min(64, base64.Length - i)));
        }

        lines.Add($"-----END {label}-----");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Gets asymmetric key details (modulusLength, publicExponent for RSA; namedCurve for EC).
    /// </summary>
    public SharpTSObject? GetAsymmetricKeyDetails()
    {
        if (_type == KeyObjectType.Secret)
            return null;

        var details = new Dictionary<string, object?>();

        if (_rsaKey != null)
        {
            var parameters = _rsaKey.ExportParameters(false);
            details["modulusLength"] = (double)(parameters.Modulus?.Length * 8 ?? 0);
            // Public exponent as a number (typically 65537). Node returns a bigint;
            // number keeps === comparisons working and is a documented deviation.
            if (parameters.Exponent != null)
            {
                long exp = 0;
                foreach (var b in parameters.Exponent)
                {
                    exp = (exp << 8) | b;
                }
                details["publicExponent"] = (double)exp;
            }
        }
        else if (_ecdsaKey != null)
        {
            details["namedCurve"] = NodeCurveName(_ecdsaKey.ExportParameters(false).Curve);
        }

        return new SharpTSObject(details);
    }

    /// <summary>Maps a BCL curve to its Node (OpenSSL) name.</summary>
    private static string NodeCurveName(ECCurve curve)
    {
        var curveName = curve.Oid?.FriendlyName ?? "unknown";
        return curveName switch
        {
            "nistP256" or "ECDSA_P256" => "prime256v1",
            "nistP384" or "ECDSA_P384" => "secp384r1",
            "nistP521" or "ECDSA_P521" => "secp521r1",
            _ => curveName
        };
    }

    /// <summary>
    /// Node's keyObject.equals(other): same type and same key material.
    /// </summary>
    public bool KeyEquals(SharpTSKeyObject? other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_type != other._type || _asymmetricKeyType != other._asymmetricKeyType) return false;

        return _type switch
        {
            KeyObjectType.Secret => _symmetricKey!.AsSpan().SequenceEqual(other._symmetricKey),
            KeyObjectType.Public => ExportMaterial(false).AsSpan().SequenceEqual(other.ExportMaterial(false)),
            KeyObjectType.Private => ExportMaterial(true).AsSpan().SequenceEqual(other.ExportMaterial(true)),
            _ => false
        };
    }

    private byte[] ExportMaterial(bool isPrivate)
    {
        if (_rsaKey != null)
            return isPrivate ? _rsaKey.ExportPkcs8PrivateKey() : _rsaKey.ExportSubjectPublicKeyInfo();
        if (_ecdsaKey != null)
            return isPrivate ? _ecdsaKey.ExportPkcs8PrivateKey() : _ecdsaKey.ExportSubjectPublicKeyInfo();
        throw new InvalidOperationException("No key available");
    }

    /// <summary>
    /// Gets a member of this KeyObject (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "type" => _type switch
            {
                KeyObjectType.Secret => "secret",
                KeyObjectType.Public => "public",
                KeyObjectType.Private => "private",
                _ => throw new InvalidOperationException("Unknown key type")
            },

            "asymmetricKeyType" => _asymmetricKeyType switch
            {
                AsymmetricKeyType.None => null,
                AsymmetricKeyType.Rsa => "rsa",
                AsymmetricKeyType.Ec => "ec",
                _ => null
            },

            "asymmetricKeyDetails" => GetAsymmetricKeyDetails(),

            "symmetricKeySize" => _type == KeyObjectType.Secret
                ? (double?)_symmetricKey!.Length
                : null,

            "export" => BuiltInMethod.CreateV2("export", 0, 1, (_, _, args) =>
            {
                // For interpreter, pass the options object directly to Export
                return RuntimeValue.FromBoxed(Export(args.Length > 0 ? args[0].ToObject() : null));
            }),

            "equals" => BuiltInMethod.CreateV2("equals", 1, (_, _, args) =>
            {
                var other = args.Length > 0 ? args[0].ToObject() as SharpTSKeyObject : null;
                return RuntimeValue.FromBoolean(KeyEquals(other));
            }),

            _ => null
        };
    }

    #region ISharpTSPropertyAccessor implementation

    /// <inheritdoc />
    public object? GetProperty(string name) => GetMember(name);

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        throw new InvalidOperationException("KeyObject properties are read-only");
    }

    /// <inheritdoc />
    public bool HasProperty(string name)
    {
        return name is "type" or "asymmetricKeyType" or "asymmetricKeyDetails" or "symmetricKeySize" or "export" or "equals";
    }

    /// <inheritdoc />
    public IEnumerable<string> PropertyNames
    {
        get
        {
            yield return "type";
            if (_type != KeyObjectType.Secret)
            {
                yield return "asymmetricKeyType";
                yield return "asymmetricKeyDetails";
            }
            else
            {
                yield return "symmetricKeySize";
            }
            yield return "export";
            yield return "equals";
        }
    }

    #endregion
}
