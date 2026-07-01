using System.Security.Cryptography;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared algorithm-name parsing for the Node <c>crypto</c> value-type wrappers.
/// </summary>
/// <remarks>
/// Hash, Hmac, Sign, and Verify each carried their own identical
/// <see cref="HashAlgorithmName"/> switch (#1135); this centralizes them.
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
            _ => throw new ArgumentException($"Unsupported {context} algorithm: {algorithm}")
        };
    }
}
