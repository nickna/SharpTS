using System.Security.Cryptography;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared algorithm-name parsing for the Node <c>crypto</c> value-type wrappers.
/// </summary>
/// <remarks>
/// Hash, Hmac, Sign, and Verify each carried their own identical
/// <see cref="HashAlgorithmName"/> switch (#1135); this centralizes them.
/// The one-shot digest table (incl. SHA-3/SHAKE, #1062) lives here too so
/// Hash, <c>crypto.hash()</c>, and <c>getHashes()</c> agree on one algorithm set.
/// </remarks>
internal static class CryptoAlgorithms
{
    /// <summary>
    /// Parses a digest name (<c>md5</c>/<c>sha1</c>/<c>sha256</c>/<c>sha384</c>/
    /// <c>sha512</c>) into a <see cref="HashAlgorithmName"/>.
    /// </summary>
    /// <param name="algorithm">The algorithm name (case-insensitive).</param>
    /// <param name="stripSignaturePrefix">
    /// When <c>true</c>, a leading <c>"rsa-"</c>/<c>"ecdsa-"</c> is removed first so
    /// Sign/Verify accept Node-style names like <c>"RSA-SHA256"</c>.
    /// </param>
    /// <param name="context">
    /// Noun used in the "Unsupported … algorithm" error message (e.g. "hash",
    /// "HMAC", "signing", "verification").
    /// </param>
    public static HashAlgorithmName ParseHashName(
        string algorithm, bool stripSignaturePrefix = false, string context = "hash")
    {
        var normalized = algorithm.ToLowerInvariant();
        if (stripSignaturePrefix)
        {
            if (normalized.StartsWith("rsa-"))
                normalized = normalized[4..];
            else if (normalized.StartsWith("ecdsa-"))
                normalized = normalized[6..];
        }

        return normalized switch
        {
            "md5" => HashAlgorithmName.MD5,
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            "sha3-256" => HashAlgorithmName.SHA3_256,
            "sha3-384" => HashAlgorithmName.SHA3_384,
            "sha3-512" => HashAlgorithmName.SHA3_512,
            _ => throw new ArgumentException($"Unsupported {context} algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// Default digest length (bytes) for the XOF hashes when no
    /// <c>outputLength</c> option is given, matching Node (shake128 → 16,
    /// shake256 → 32).
    /// </summary>
    public static int DefaultXofLength(string normalized) => normalized switch
    {
        "shake128" => 16,
        "shake256" => 32,
        _ => throw new ArgumentException($"Not an XOF hash: {normalized}")
    };

    /// <summary>Whether the (normalized) algorithm is an extendable-output hash.</summary>
    public static bool IsXofHash(string normalized) => normalized is "shake128" or "shake256";

    /// <summary>
    /// Validates and normalizes a hash algorithm name for <c>createHash</c>/<c>crypto.hash</c>.
    /// Throws for unknown names and for SHA-3/SHAKE on platforms whose crypto
    /// primitives don't support them (BCL <c>IsSupported</c> gates).
    /// </summary>
    public static string ValidateHashName(string algorithm, string context = "hash")
    {
        var normalized = algorithm.ToLowerInvariant();
        switch (normalized)
        {
            case "md5" or "sha1" or "sha256" or "sha384" or "sha512":
                return normalized;
            case "sha3-256" when SHA3_256.IsSupported:
            case "sha3-384" when SHA3_384.IsSupported:
            case "sha3-512" when SHA3_512.IsSupported:
                return normalized;
            case "shake128" when Shake128.IsSupported:
            case "shake256" when Shake256.IsSupported:
                return normalized;
            case "sha3-256" or "sha3-384" or "sha3-512" or "shake128" or "shake256":
                throw new ArgumentException($"Unsupported {context} algorithm: {algorithm} (not supported on this platform)");
            default:
                throw new ArgumentException($"Unsupported {context} algorithm: {algorithm}");
        }
    }

    /// <summary>
    /// One-shot digest over the full algorithm table. <paramref name="outputLength"/>
    /// applies to the XOF hashes only (ignored otherwise).
    /// </summary>
    public static byte[] OneShotHash(string algorithm, byte[] data, int outputLength = -1)
    {
        var normalized = ValidateHashName(algorithm);
        return normalized switch
        {
            "md5" => MD5.HashData(data),
            "sha1" => SHA1.HashData(data),
            "sha256" => SHA256.HashData(data),
            "sha384" => SHA384.HashData(data),
            "sha512" => SHA512.HashData(data),
            "sha3-256" => SHA3_256.HashData(data),
            "sha3-384" => SHA3_384.HashData(data),
            "sha3-512" => SHA3_512.HashData(data),
            "shake128" => Shake128.HashData(data, outputLength > 0 ? outputLength : DefaultXofLength(normalized)),
            "shake256" => Shake256.HashData(data, outputLength > 0 ? outputLength : DefaultXofLength(normalized)),
            _ => throw new ArgumentException($"Unsupported hash algorithm: {algorithm}")
        };
    }

    /// <summary>
    /// The hash names reported by <c>crypto.getHashes()</c> — the static family
    /// plus whichever SHA-3/SHAKE members this platform supports.
    /// </summary>
    public static List<object?> SupportedHashNames()
    {
        var names = new List<object?> { "md5", "sha1", "sha256", "sha384", "sha512" };
        if (SHA3_256.IsSupported) names.Add("sha3-256");
        if (SHA3_384.IsSupported) names.Add("sha3-384");
        if (SHA3_512.IsSupported) names.Add("sha3-512");
        if (Shake128.IsSupported) names.Add("shake128");
        if (Shake256.IsSupported) names.Add("shake256");
        return names;
    }
}
