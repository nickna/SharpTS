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
/// Provides the Node.js KeyObject API:
/// - type: 'secret' | 'public' | 'private'
/// - asymmetricKeyType: 'rsa' | 'ec' | undefined (for secret keys)
/// - asymmetricKeyDetails: { modulusLength, publicExponent } for RSA, { namedCurve } for EC
/// - symmetricKeySize: number (for secret keys only)
/// - export(options?): Export the key as PEM string or Buffer
/// </remarks>
public class SharpTSKeyObject : ISharpTSPropertyAccessor
{
    private readonly KeyObjectType _type;
    private readonly AsymmetricKeyType _asymmetricKeyType;
    private readonly byte[]? _symmetricKey;
    private readonly RSA? _rsaKey;
    private readonly ECDsa? _ecdsaKey;
    private readonly string? _originalPem;

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

    /// <summary>
    /// Creates a public KeyObject from a PEM-encoded public key.
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
    /// Internal constructor for asymmetric keys.
    /// </summary>
    private SharpTSKeyObject(string pem, bool isPrivate)
    {
        _type = isPrivate ? KeyObjectType.Private : KeyObjectType.Public;
        _originalPem = pem;

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

    /// <summary>
    /// Exports the key in the requested format.
    /// Handles both options object style (compiled code) and direct string parameters.
    /// </summary>
    /// <param name="options">
    /// Either an options object with 'type' and 'format' properties,
    /// or can be called with no arguments for defaults.
    /// </param>
    /// <returns>PEM string or Buffer containing the exported key.</returns>
    public object Export(object? options = null)
    {
        string? type = null;
        string? format = null;

        // Extract type and format from options if provided
        if (options is SharpTSObject obj)
        {
            if (obj.Fields.TryGetValue("type", out var t) && t is string ts)
                type = ts;
            if (obj.Fields.TryGetValue("format", out var f) && f is string fs)
                format = fs;
        }
        else if (options is Dictionary<string, object?> dict)
        {
            if (dict.TryGetValue("type", out var t) && t is string ts)
                type = ts;
            if (dict.TryGetValue("format", out var f) && f is string fs)
                format = fs;
        }
        else if (options != null)
        {
            // Try reflection for compiled $Object
            var optionsType = options.GetType();
            var getPropertyMethod = optionsType.GetMethod("GetProperty", [typeof(string)]);
            if (getPropertyMethod != null)
            {
                var typeVal = getPropertyMethod.Invoke(options, ["type"]);
                if (typeVal is string ts) type = ts;
                var formatVal = getPropertyMethod.Invoke(options, ["format"]);
                if (formatVal is string fs) format = fs;
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
            _ => throw new ArgumentException($"Invalid export type '{type}' for {_type} EC key")
        };
    }

    private string ConvertToPem(byte[] keyBytes, string? type)
    {
        string label = (type?.ToLowerInvariant(), _type, _asymmetricKeyType) switch
        {
            ("pkcs1", KeyObjectType.Public, AsymmetricKeyType.Rsa) => "RSA PUBLIC KEY",
            ("pkcs1", KeyObjectType.Private, AsymmetricKeyType.Rsa) => "RSA PRIVATE KEY",
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
            // Public exponent as BigInt representation
            if (parameters.Exponent != null)
            {
                // Convert to number (typically 65537)
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
            var parameters = _ecdsaKey.ExportParameters(false);
            var curveName = parameters.Curve.Oid?.FriendlyName ?? "unknown";
            // Map .NET curve names to Node.js names
            curveName = curveName switch
            {
                "nistP256" or "ECDSA_P256" => "prime256v1",
                "nistP384" or "ECDSA_P384" => "secp384r1",
                "nistP521" or "ECDSA_P521" => "secp521r1",
                _ => curveName
            };
            details["namedCurve"] = curveName;
        }

        return new SharpTSObject(details);
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

            "export" => new BuiltInMethod("export", 0, 1, (interp, recv, args) =>
            {
                // For interpreter, pass the options object directly to Export
                return Export(args.Count > 0 ? args[0] : null);
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
        return name is "type" or "asymmetricKeyType" or "asymmetricKeyDetails" or "symmetricKeySize" or "export";
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
        }
    }

    #endregion
}
