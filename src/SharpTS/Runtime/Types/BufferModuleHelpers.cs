using System.Text;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Pure-BCL helpers backing the Node.js <c>buffer</c> module's standalone exports
/// (atob/btoa, isUtf8/isAscii, transcode) and the module constants. This is the
/// single source of truth for interpreter mode; the compiled pipeline emits an IL
/// twin that must stay behaviourally identical.
/// </summary>
/// <remarks>
/// BCL-only by design (System.Text / System.Convert) so compiled output stays
/// standalone — no SharpTS.dll dependency, no RequireSharpTSRuntime.
/// </remarks>
public static class BufferModuleHelpers
{
    // Node 24 on 64-bit: buffer.constants.MAX_LENGTH = 2^32, MAX_STRING_LENGTH per V8.
    // SharpTS buffers are backed by a managed byte[] (~2GB ceiling), so these are
    // advisory limits matching Node's reported values.
    public const double MaxLength = 4294967296.0;       // buffer.constants.MAX_LENGTH / kMaxLength
    public const double MaxStringLength = 536870888.0;  // buffer.constants.MAX_STRING_LENGTH / kStringMaxLength
    public const double InspectMaxBytes = 50.0;         // buffer.INSPECT_MAX_BYTES default

    /// <summary>
    /// atob(data): decodes a base64 string to a Latin-1 "binary string" (each output
    /// char is one decoded byte). Malformed input is checked by Convert.
    /// </summary>
    public static string Atob(string data)
    {
        var normalized = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            if (char.IsWhiteSpace(c)) continue;
            normalized.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }
        switch (normalized.Length % 4)
        {
            case 2: normalized.Append("=="); break;
            case 3: normalized.Append('='); break;
        }
        return Encoding.Latin1.GetString(Convert.FromBase64String(normalized.ToString()));
    }

    /// <summary>
    /// btoa(data): encodes a Latin-1 "binary string" to base64. Composed from
    /// <see cref="BufferEncoding"/> so it mirrors the compiled twin
    /// (Buffer.from(data,'latin1').toString('base64')).
    /// </summary>
    public static string Btoa(string data)
        => BufferEncoding.Decode(BufferEncoding.Encode(data, "latin1"), "base64");

    /// <summary>Whether <paramref name="bytes"/> is well-formed UTF-8.</summary>
    public static bool IsUtf8(byte[] bytes) => System.Text.Unicode.Utf8.IsValid(bytes);

    /// <summary>Whether every byte of <paramref name="bytes"/> is in the ASCII range.</summary>
    public static bool IsAscii(byte[] bytes) => System.Text.Ascii.IsValid(bytes);

    /// <summary>
    /// transcode(source, fromEnc, toEnc): re-encodes bytes from one supported Buffer
    /// encoding to another (limited to the encodings <see cref="BufferEncoding"/>
    /// supports — a documented BCL ceiling vs. Node's ICU-backed set).
    /// </summary>
    public static byte[] Transcode(byte[] source, string fromEncoding, string toEncoding)
    {
        var decoded = BufferEncoding.Decode(source, fromEncoding);
        return BufferEncoding.Encode(decoded, toEncoding);
    }
}
