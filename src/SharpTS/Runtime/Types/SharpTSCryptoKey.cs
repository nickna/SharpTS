namespace SharpTS.Runtime.Types;

/// <summary>
/// WebCrypto CryptoKey (#1063): { type, extractable, algorithm, usages }.
/// </summary>
/// <remarks>
/// Key material is held as raw bytes: the secret itself for symmetric keys,
/// PKCS#8 DER for private keys, SPKI DER for public keys. Operations re-import
/// the material per call — the same model the compiled $CryptoKey uses, so the
/// two modes share one shape.
/// NOTE: Must stay in sync with the emitted $CryptoKey (Compilation/RuntimeEmitter.WebCrypto.cs).
/// </remarks>
public sealed class SharpTSCryptoKey
{
    /// <summary>'secret' | 'public' | 'private'.</summary>
    public string Type { get; }

    /// <summary>Whether exportKey is allowed on this key.</summary>
    public bool Extractable { get; }

    /// <summary>The normalized algorithm object ({ name, ... }).</summary>
    public SharpTSObject Algorithm { get; }

    /// <summary>The usages array granted at creation.</summary>
    public SharpTSArray Usages { get; }

    /// <summary>Raw secret bytes, PKCS#8 DER (private), or SPKI DER (public).</summary>
    internal byte[] Material { get; }

    /// <summary>Uppercase algorithm name ("AES-GCM", "HMAC", "ECDSA", ...).</summary>
    internal string AlgorithmName { get; }

    /// <summary>Digest name for HMAC/RSA keys ("SHA-256" form), or null.</summary>
    internal string? HashName { get; }

    /// <summary>Named curve for EC keys ("P-256" form), or null.</summary>
    internal string? NamedCurve { get; }

    internal SharpTSCryptoKey(
        string type, bool extractable, SharpTSObject algorithm, SharpTSArray usages,
        byte[] material, string algorithmName, string? hashName, string? namedCurve)
    {
        Type = type;
        Extractable = extractable;
        Algorithm = algorithm;
        Usages = usages;
        Material = material;
        AlgorithmName = algorithmName;
        HashName = hashName;
        NamedCurve = namedCurve;
    }

    /// <summary>Property access (crypto key objects are read-only).</summary>
    public object? GetMember(string name) => name switch
    {
        "type" => Type,
        "extractable" => Extractable,
        "algorithm" => Algorithm,
        "usages" => Usages,
        _ => null
    };
}
