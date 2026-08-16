using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible X509Certificate (#1064), backed by the BCL
/// <see cref="X509Certificate2"/>.
/// </summary>
/// <remarks>
/// Node shapes preserved: OpenSSL-style multi-line subject/issuer, "MMM ​d HH:mm:ss yyyy GMT"
/// validity strings, colon-separated uppercase fingerprints, "DNS:a, IP Address:b"
/// subjectAltName. NOTE: Must stay in sync with the emitted $X509Certificate
/// (Compilation/RuntimeEmitter.TSX509.cs) — compiled defers toLegacyObject/infoAccess/checkEmail.
/// </remarks>
public class SharpTSX509Certificate
{
    private readonly X509Certificate2 _cert;

    // Parsed SubjectAltName GeneralNames: (asn1 context tag, rendered value)
    private readonly List<(int Tag, string Value)> _san = new();

    private const int SanRfc822Name = 1;
    private const int SanDnsName = 2;
    private const int SanUri = 6;
    private const int SanIpAddress = 7;

    /// <summary>
    /// Creates an X509Certificate from a PEM string or a Buffer holding PEM or DER.
    /// </summary>
    public SharpTSX509Certificate(object source)
    {
        switch (source)
        {
            case string pem:
                _cert = X509Certificate2.CreateFromPem(pem);
                break;
            case SharpTSBuffer buf:
            {
                var data = buf.Data;
                // PEM buffers start with "-----BEGIN"; anything else is DER.
                if (data.Length > 10 && data[0] == (byte)'-')
                    _cert = X509Certificate2.CreateFromPem(System.Text.Encoding.UTF8.GetString(data));
                else
                    _cert = X509CertificateLoader.LoadCertificate(data);
                break;
            }
            case SharpTSX509Certificate other:
                _cert = other._cert;
                break;
            default:
                throw new ArgumentException("X509Certificate: argument must be a PEM string or Buffer");
        }

        ParseSubjectAltName();
    }

    internal X509Certificate2 Certificate => _cert;

    #region Property values

    /// <summary>OpenSSL/Node-style multi-line name (cert order, one RDN per line).</summary>
    internal static string FormatName(System.Security.Cryptography.X509Certificates.X500DistinguishedName name)
    {
        var lines = name.Format(true)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim('\r', ' '))
            .Where(l => l.Length > 0)
            .Reverse();
        return string.Join("\n", lines);
    }

    /// <summary>OpenSSL-style validity timestamp: "Jan  1 00:00:00 2020 GMT".</summary>
    internal static string FormatValidity(DateTime local)
    {
        var utc = local.ToUniversalTime();
        return string.Format(CultureInfo.InvariantCulture, "{0} {1,2} {2} GMT",
            utc.ToString("MMM", CultureInfo.InvariantCulture),
            utc.Day,
            utc.ToString("HH:mm:ss yyyy", CultureInfo.InvariantCulture));
    }

    /// <summary>Colon-separated uppercase hex, e.g. "AB:0C:…".</summary>
    internal static string ColonHex(byte[] bytes)
        => string.Join(":", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private string Fingerprint(HashAlgorithmName alg) => ColonHex(_cert.GetCertHash(alg));

    private void ParseSubjectAltName()
    {
        var ext = _cert.Extensions["2.5.29.17"];
        if (ext == null)
            return;

        var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
        var seq = reader.ReadSequence();
        while (seq.HasData)
        {
            var tag = seq.PeekTag();
            if (tag.TagClass != TagClass.ContextSpecific)
            {
                seq.ReadEncodedValue();
                continue;
            }

            switch (tag.TagValue)
            {
                case SanRfc822Name:
                case SanDnsName:
                case SanUri:
                {
                    var value = seq.ReadCharacterString(UniversalTagNumber.IA5String,
                        new Asn1Tag(TagClass.ContextSpecific, tag.TagValue));
                    _san.Add((tag.TagValue, value));
                    break;
                }
                case SanIpAddress:
                {
                    var bytes = seq.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, SanIpAddress));
                    _san.Add((SanIpAddress, new IPAddress(bytes).ToString()));
                    break;
                }
                default:
                    seq.ReadEncodedValue();
                    break;
            }
        }
    }

    private string? SubjectAltNameString()
    {
        if (_san.Count == 0)
            return null;

        return string.Join(", ", _san.Select(e => e.Tag switch
        {
            SanRfc822Name => $"email:{e.Value}",
            SanDnsName => $"DNS:{e.Value}",
            SanUri => $"URI:{e.Value}",
            SanIpAddress => $"IP Address:{e.Value}",
            _ => e.Value
        }));
    }

    private bool IsCa()
    {
        foreach (var ext in _cert.Extensions)
        {
            if (ext is X509BasicConstraintsExtension bc)
                return bc.CertificateAuthority;
        }
        return false;
    }

    private SharpTSArray? KeyUsageArray()
    {
        foreach (var ext in _cert.Extensions)
        {
            if (ext is X509KeyUsageExtension ku)
            {
                var names = new List<object?>();
                var flags = ku.KeyUsages;
                if (flags.HasFlag(X509KeyUsageFlags.DigitalSignature)) names.Add("Digital Signature");
                if (flags.HasFlag(X509KeyUsageFlags.NonRepudiation)) names.Add("Non Repudiation");
                if (flags.HasFlag(X509KeyUsageFlags.KeyEncipherment)) names.Add("Key Encipherment");
                if (flags.HasFlag(X509KeyUsageFlags.DataEncipherment)) names.Add("Data Encipherment");
                if (flags.HasFlag(X509KeyUsageFlags.KeyAgreement)) names.Add("Key Agreement");
                if (flags.HasFlag(X509KeyUsageFlags.KeyCertSign)) names.Add("Certificate Sign");
                if (flags.HasFlag(X509KeyUsageFlags.CrlSign)) names.Add("CRL Sign");
                if (flags.HasFlag(X509KeyUsageFlags.EncipherOnly)) names.Add("Encipher Only");
                if (flags.HasFlag(X509KeyUsageFlags.DecipherOnly)) names.Add("Decipher Only");
                return new SharpTSArray(names);
            }
        }
        return null;
    }

    private SharpTSArray? ExtKeyUsageArray()
    {
        foreach (var ext in _cert.Extensions)
        {
            if (ext is X509EnhancedKeyUsageExtension eku)
            {
                var oids = new List<object?>();
                foreach (var oid in eku.EnhancedKeyUsages)
                    oids.Add(oid.Value);
                return new SharpTSArray(oids);
            }
        }
        return null;
    }

    private string? InfoAccessString()
    {
        var ext = _cert.Extensions["1.3.6.1.5.5.7.1.1"];
        if (ext == null)
            return null;

        var lines = new List<string>();
        var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
        var seq = reader.ReadSequence();
        while (seq.HasData)
        {
            var access = seq.ReadSequence();
            var methodOid = access.ReadObjectIdentifier();
            var label = methodOid switch
            {
                "1.3.6.1.5.5.7.48.1" => "OCSP",
                "1.3.6.1.5.5.7.48.2" => "CA Issuers",
                _ => methodOid
            };

            var tag = access.PeekTag();
            if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == SanUri)
            {
                var uri = access.ReadCharacterString(UniversalTagNumber.IA5String,
                    new Asn1Tag(TagClass.ContextSpecific, SanUri));
                lines.Add($"{label} - URI:{uri}");
            }
            else
            {
                access.ReadEncodedValue();
                lines.Add(label);
            }
        }

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    private SharpTSKeyObject PublicKeyObject()
        => SharpTSKeyObject.CreateFromDer(_cert.PublicKey.ExportSubjectPublicKeyInfo(), "spki", isPrivate: false);

    #endregion

    #region Signature verification

    /// <summary>
    /// Splits a DER certificate into (tbsCertificate, signature-hash OID, signature bytes).
    /// </summary>
    internal static (byte[] Tbs, string SigOid, byte[] Signature) SplitSignedData(byte[] certDer)
    {
        var reader = new AsnReader(certDer, AsnEncodingRules.DER);
        var cert = reader.ReadSequence();
        var tbs = cert.ReadEncodedValue().ToArray();
        var sigAlg = cert.ReadSequence();
        var oid = sigAlg.ReadObjectIdentifier();
        var signature = cert.ReadBitString(out _);
        return (tbs, oid, signature);
    }

    internal static HashAlgorithmName HashForSignatureOid(string oid) => oid switch
    {
        "1.2.840.113549.1.1.5" => HashAlgorithmName.SHA1,     // sha1WithRSAEncryption
        "1.2.840.113549.1.1.11" => HashAlgorithmName.SHA256,  // sha256WithRSAEncryption
        "1.2.840.113549.1.1.12" => HashAlgorithmName.SHA384,
        "1.2.840.113549.1.1.13" => HashAlgorithmName.SHA512,
        "1.2.840.10045.4.1" => HashAlgorithmName.SHA1,        // ecdsa-with-SHA1
        "1.2.840.10045.4.3.2" => HashAlgorithmName.SHA256,
        "1.2.840.10045.4.3.3" => HashAlgorithmName.SHA384,
        "1.2.840.10045.4.3.4" => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException($"Unsupported certificate signature algorithm OID {oid}")
    };

    /// <summary>
    /// Checks whether this certificate's signature was produced by the given key.
    /// </summary>
    public bool VerifySignedBy(SharpTSKeyObject key)
    {
        var (tbs, oid, signature) = SplitSignedData(_cert.RawData);
        var hash = HashForSignatureOid(oid);

        if (key.RsaKey != null)
            return key.RsaKey.VerifyData(tbs, signature, hash, RSASignaturePadding.Pkcs1);
        if (key.EcdsaKey != null)
            return key.EcdsaKey.VerifyData(tbs, signature, hash, DSASignatureFormat.Rfc3279DerSequence);

        throw new ArgumentException("X509Certificate.verify: key must be an RSA or EC KeyObject");
    }

    #endregion

    #region Host / email / IP checks

    /// <summary>Wildcard-aware single-label match: "*.wild.example" matches "a.wild.example".</summary>
    internal static bool HostMatches(string pattern, string name)
    {
        pattern = pattern.TrimEnd('.').ToLowerInvariant();
        name = name.TrimEnd('.').ToLowerInvariant();

        if (!pattern.Contains('*'))
            return pattern == name;

        var patternLabels = pattern.Split('.');
        var nameLabels = name.Split('.');
        if (patternLabels.Length != nameLabels.Length)
            return false;

        for (int i = 0; i < patternLabels.Length; i++)
        {
            if (patternLabels[i] == "*")
            {
                if (i != 0 || nameLabels[i].Length == 0)
                    return false; // only a leading full-label wildcard is honored
                continue;
            }
            if (patternLabels[i] != nameLabels[i])
                return false;
        }
        return true;
    }

    private string? SubjectCommonName()
    {
        foreach (var line in FormatName(_cert.SubjectName).Split('\n'))
        {
            if (line.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return line[3..];
        }
        return null;
    }

    /// <summary>Node's checkHost: returns the input name on match, undefined otherwise.</summary>
    public string? CheckHost(string name)
    {
        var dnsNames = _san.Where(e => e.Tag == SanDnsName).Select(e => e.Value).ToList();
        if (dnsNames.Count > 0)
            return dnsNames.Any(p => HostMatches(p, name)) ? name : null;

        // No DNS SANs: fall back to the subject CN.
        var cn = SubjectCommonName();
        return cn != null && HostMatches(cn, name) ? name : null;
    }

    /// <summary>Node's checkEmail: exact match against rfc822 SAN entries (or subject emailAddress).</summary>
    public string? CheckEmail(string email)
    {
        return _san.Any(e => e.Tag == SanRfc822Name && string.Equals(e.Value, email, StringComparison.OrdinalIgnoreCase))
            ? email : null;
    }

    /// <summary>Node's checkIP: exact match against IP SAN entries.</summary>
    public string? CheckIP(string ip)
    {
        if (!IPAddress.TryParse(ip, out var parsed))
            return null;
        var normalized = parsed.ToString();
        return _san.Any(e => e.Tag == SanIpAddress && e.Value == normalized) ? normalized : null;
    }

    #endregion

    private SharpTSObject ToLegacyObject()
    {
        static SharpTSObject NameToObject(string formatted)
        {
            var fields = new Dictionary<string, object?>();
            foreach (var line in formatted.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf('=');
                if (idx > 0)
                    fields[line[..idx]] = line[(idx + 1)..];
            }
            return new SharpTSObject(fields);
        }

        var legacy = new Dictionary<string, object?>
        {
            ["subject"] = NameToObject(FormatName(_cert.SubjectName)),
            ["issuer"] = NameToObject(FormatName(_cert.IssuerName)),
            ["valid_from"] = FormatValidity(_cert.NotBefore),
            ["valid_to"] = FormatValidity(_cert.NotAfter),
            ["fingerprint"] = Fingerprint(HashAlgorithmName.SHA1),
            ["fingerprint256"] = Fingerprint(HashAlgorithmName.SHA256),
            ["fingerprint512"] = Fingerprint(HashAlgorithmName.SHA512),
            ["serialNumber"] = _cert.SerialNumber,
            ["raw"] = new SharpTSBuffer(_cert.RawData),
            ["ca"] = IsCa(),
        };

        var san = SubjectAltNameString();
        if (san != null)
            legacy["subjectaltname"] = san;

        if (_cert.GetRSAPublicKey() is { } rsa)
        {
            using (rsa)
                legacy["bits"] = (double)rsa.KeySize;
        }
        else if (_cert.GetECDsaPublicKey() is { } ec)
        {
            using (ec)
                legacy["bits"] = (double)ec.KeySize;
        }

        return new SharpTSObject(legacy);
    }

    /// <summary>
    /// Gets a member of this certificate (for property access).
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "subject" => FormatName(_cert.SubjectName),
            "issuer" => FormatName(_cert.IssuerName),
            "validFrom" => FormatValidity(_cert.NotBefore),
            "validTo" => FormatValidity(_cert.NotAfter),
            "validFromDate" => new SharpTSDate((double)new DateTimeOffset(_cert.NotBefore.ToUniversalTime()).ToUnixTimeMilliseconds()),
            "validToDate" => new SharpTSDate((double)new DateTimeOffset(_cert.NotAfter.ToUniversalTime()).ToUnixTimeMilliseconds()),
            "fingerprint" => Fingerprint(HashAlgorithmName.SHA1),
            "fingerprint256" => Fingerprint(HashAlgorithmName.SHA256),
            "fingerprint512" => Fingerprint(HashAlgorithmName.SHA512),
            "serialNumber" => _cert.SerialNumber,
            "subjectAltName" => SubjectAltNameString(),
            "infoAccess" => InfoAccessString(),
            "keyUsage" => KeyUsageArray(),
            "extKeyUsage" => ExtKeyUsageArray(),
            "ca" => IsCa(),
            "raw" => new SharpTSBuffer(_cert.RawData),
            "publicKey" => PublicKeyObject(),

            "verify" => BuiltInMethod.CreateV2("verify", 1, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].ToObject() is not SharpTSKeyObject key)
                    throw new ArgumentException("X509Certificate.verify requires a KeyObject argument");
                return RuntimeValue.FromBoolean(VerifySignedBy(key));
            }),
            "checkHost" => BuiltInMethod.CreateV2("checkHost", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0 || !args[0].IsString)
                    throw new ArgumentException("X509Certificate.checkHost requires a name argument");
                // Node returns undefined (not null) on a failed match
                return CheckHost(args[0].AsStringUnsafe()) is { } match
                    ? RuntimeValue.FromString(match) : RuntimeValue.Undefined;
            }),
            "checkEmail" => BuiltInMethod.CreateV2("checkEmail", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0 || !args[0].IsString)
                    throw new ArgumentException("X509Certificate.checkEmail requires an email argument");
                return CheckEmail(args[0].AsStringUnsafe()) is { } match
                    ? RuntimeValue.FromString(match) : RuntimeValue.Undefined;
            }),
            "checkIP" => BuiltInMethod.CreateV2("checkIP", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0 || !args[0].IsString)
                    throw new ArgumentException("X509Certificate.checkIP requires an ip argument");
                return CheckIP(args[0].AsStringUnsafe()) is { } match
                    ? RuntimeValue.FromString(match) : RuntimeValue.Undefined;
            }),
            "checkIssued" => BuiltInMethod.CreateV2("checkIssued", 1, (_, _, args) =>
            {
                if (args.Length == 0 || args[0].ToObject() is not SharpTSX509Certificate issuer)
                    throw new ArgumentException("X509Certificate.checkIssued requires an X509Certificate argument");
                var issuedByName = FormatName(_cert.IssuerName) == FormatName(issuer._cert.SubjectName);
                return RuntimeValue.FromBoolean(issuedByName && VerifySignedBy(issuer.PublicKeyObject()));
            }),
            "toString" => BuiltInMethod.CreateV2("toString", 0, (_, _, _) =>
                RuntimeValue.FromString(_cert.ExportCertificatePem() + "\n")),
            "toJSON" => BuiltInMethod.CreateV2("toJSON", 0, (_, _, _) =>
                RuntimeValue.FromString(_cert.ExportCertificatePem() + "\n")),
            "toLegacyObject" => BuiltInMethod.CreateV2("toLegacyObject", 0, (_, _, _) =>
                RuntimeValue.FromObject(ToLegacyObject())),

            _ => null
        };
    }
}
