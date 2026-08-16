using System.Text;

namespace SharpTS.Runtime.Types;

/// <summary>
/// The single source of truth for Node.js Buffer/string encoding conversions in
/// interpreter mode. Both <see cref="SharpTSBuffer"/> and the <c>fs</c> module use
/// this so a given encoding name behaves identically across Buffer and file I/O.
/// </summary>
/// <remarks>
/// BCL-only by design. The compiled pipeline emits an IL twin
/// (<c>$Runtime.BufferEncode</c>/<c>BufferDecode</c>) that MUST stay byte-for-byte
/// equivalent to these tables so interpreter and compiled output agree.
/// Supported names (Node 24): utf8/utf-8, ascii, latin1/binary, base64, base64url,
/// hex, utf16le/ucs2/ucs-2/utf-16le.
/// </remarks>
public static class BufferEncoding
{
    /// <summary>Default encoding when none is supplied (Node uses utf8).</summary>
    public const string Default = "utf8";

    /// <summary>Encodes a string to bytes using the given Node encoding name.</summary>
    public static byte[] Encode(string data, string? encoding)
    {
        switch (Normalize(encoding))
        {
            case "utf8": return Encoding.UTF8.GetBytes(data);
            case "ascii": return Encoding.ASCII.GetBytes(data);
            case "latin1": return Encoding.Latin1.GetBytes(data);
            case "base64": return DecodeBase64(data, urlSafe: false);
            case "base64url": return DecodeBase64(data, urlSafe: true);
            case "hex": return HexToBytes(data);
            case "utf16le": return Encoding.Unicode.GetBytes(data);
            default: throw new ArgumentException($"Unknown encoding: {encoding}");
        }
    }

    /// <summary>Decodes bytes to a string using the given Node encoding name.</summary>
    public static string Decode(byte[] data, string? encoding)
    {
        switch (Normalize(encoding))
        {
            case "utf8": return Encoding.UTF8.GetString(data);
            case "ascii": return Encoding.ASCII.GetString(data);
            case "latin1": return Encoding.Latin1.GetString(data);
            case "base64": return Convert.ToBase64String(data);
            case "base64url": return Convert.ToBase64String(data)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            case "hex": return Convert.ToHexString(data).ToLowerInvariant();
            case "utf16le": return Encoding.Unicode.GetString(data);
            default: throw new ArgumentException($"Unknown encoding: {encoding}");
        }
    }

    /// <summary>
    /// Converts a write payload to raw bytes: a Buffer or TypedArray view contributes
    /// its bytes verbatim (offset/length aware); a string is encoded with
    /// <paramref name="encoding"/> (default utf8). This is what fs write/append and
    /// stream writes use so binary data is persisted byte-exact (never ToString'd).
    /// </summary>
    public static byte[] ToBytes(object? data, string? encoding)
    {
        switch (data)
        {
            case SharpTSBuffer buf:
                return buf.Data;
            case SharpTSTypedArray ta:
            {
                var bytes = new byte[ta.ByteLength];
                Array.Copy(ta.Buffer, ta.ByteOffset, bytes, 0, ta.ByteLength);
                return bytes;
            }
            case string s:
                return Encode(s, encoding);
            default:
                return Encode(data?.ToString() ?? "", encoding);
        }
    }

    /// <summary>Whether the name is a Buffer encoding this helper supports.</summary>
    public static bool IsSupported(string? encoding)
    {
        switch (Normalize(encoding))
        {
            case "utf8":
            case "ascii":
            case "latin1":
            case "base64":
            case "base64url":
            case "hex":
            case "utf16le":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Canonicalizes a Node encoding alias to its representative name.</summary>
    private static string Normalize(string? encoding)
    {
        if (string.IsNullOrEmpty(encoding)) return "utf8";
        return encoding.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => "utf8",
            "ascii" => "ascii",
            "latin1" or "binary" => "latin1",
            "base64" => "base64",
            "base64url" => "base64url",
            "hex" => "hex",
            "utf16le" or "utf-16le" or "ucs2" or "ucs-2" => "utf16le",
            _ => encoding.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Decodes a base64 (or base64url) string to bytes, tolerating missing padding
    /// and the URL-safe alphabet (matching Node's lenient base64 decoder).
    /// </summary>
    private static byte[] DecodeBase64(string data, bool urlSafe)
    {
        // Node ignores whitespace and accepts both alphabets; normalize to standard.
        var sb = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }
        var s = sb.ToString();
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    /// <summary>Converts a hex string to bytes, padding an odd length like Node.</summary>
    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return [];
        hex = hex.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
