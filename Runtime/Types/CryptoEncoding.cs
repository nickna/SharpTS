using System.Text;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared byte ⇄ encoded-value conversions for the Node <c>crypto</c> value-type
/// wrappers (Cipher, Decipher, Hash, Hmac, Sign, Verify, DiffieHellman, ECDH).
/// </summary>
/// <remarks>
/// Before #1135 each wrapper re-rolled its own copy of these conversions (the
/// byte→hex/base64/Buffer encoder appeared ~7 times, the encoded-input decoder
/// ~4 times). The copies had already drifted — <c>Decipher.FormatOutput</c> grew
/// a <c>utf8</c> case that <c>Cipher</c> lacked. Centralizing the logic here makes
/// that one remaining difference an explicit, documented parameter
/// (<paramref name="decodeUtf8"/>) instead of a silent copy-paste divergence.
/// </remarks>
internal static class CryptoEncoding
{
    /// <summary>
    /// Formats raw bytes according to a Node output encoding: <c>"hex"</c> and
    /// <c>"base64"</c> always yield a string; <c>null</c> (and any unrecognized
    /// encoding) yields a <see cref="SharpTSBuffer"/>.
    /// </summary>
    /// <param name="decodeUtf8">
    /// When <c>true</c>, <c>"utf8"</c>/<c>"utf-8"</c> decode the bytes to a string.
    /// Decryption output (plaintext) sets this; encryption output (ciphertext) does
    /// not, matching the historical Cipher/Decipher behavior.
    /// </param>
    public static object ToBufferOrString(byte[] bytes, string? encoding, bool decodeUtf8 = false)
    {
        switch (encoding?.ToLowerInvariant())
        {
            case "hex":
                return Convert.ToHexString(bytes).ToLowerInvariant();
            case "base64":
                return Convert.ToBase64String(bytes);
            case "utf8" or "utf-8" when decodeUtf8:
                return Encoding.UTF8.GetString(bytes);
            default:
                return new SharpTSBuffer(bytes);
        }
    }

    /// <summary>
    /// Decodes input data to raw bytes. A string is interpreted via the given input
    /// encoding (<c>"hex"</c>/<c>"base64"</c>, otherwise UTF-8); a
    /// <see cref="SharpTSBuffer"/> or <c>byte[]</c> passes through unchanged.
    /// </summary>
    public static byte[] FromEncoded(object? data, string? encoding)
    {
        return data switch
        {
            string s => encoding?.ToLowerInvariant() switch
            {
                "hex" => Convert.FromHexString(s),
                "base64" => Convert.FromBase64String(s),
                _ => Encoding.UTF8.GetBytes(s)
            },
            SharpTSBuffer buf => buf.Data,
            byte[] bytes => bytes,
            _ => throw new ArgumentException("Data must be a string or Buffer")
        };
    }
}
