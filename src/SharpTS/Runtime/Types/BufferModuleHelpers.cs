using System.Text;
using SharpTS.Runtime.Exceptions;

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

    internal const string AtobWhitespace = " \t\n\f\r";
    internal const string InvalidCharacterErrorName = "InvalidCharacterError";
    internal const string InvalidCharacterMessage = "Invalid character";
    internal const string MissingInputCode = "ERR_MISSING_ARGS";
    internal const string MissingInputMessage = "The \"input\" argument must be specified";
    internal const string DetachedValidationCode = "ERR_INVALID_STATE";
    internal const string DetachedValidationMessage = "Cannot validate on a detached buffer";
    internal const string InvalidValidationInputCode = "ERR_INVALID_ARG_TYPE";
    internal const string InvalidValidationInputMessage = "The \"input\" argument must be an instance of ArrayBuffer, Buffer, or TypedArray.";

    /// <summary>Decodes forgiving Base64 into a Latin-1 binary string.</summary>
    public static string Atob(string data)
    {
        foreach (var whitespace in AtobWhitespace)
            data = data.Replace(whitespace.ToString(), "");

        // Convert validates the standard alphabet and padding. Supply missing padding only
        // for unpadded input with two or three characters in its last group.
        int remainder = data.Length % 4;
        if (remainder >= 2 && !data.Contains('='))
            data = data.PadRight(data.Length + 4 - remainder, '=');

        try
        {
            return Encoding.Latin1.GetString(Convert.FromBase64String(data));
        }
        catch (FormatException)
        {
            throw CreateInvalidCharacterError();
        }
    }

    private static ThrowException CreateInvalidCharacterError()
        => new(new SharpTSDomException(InvalidCharacterMessage, InvalidCharacterErrorName));

    /// <summary>Encodes a Latin-1 binary string to Base64.</summary>
    public static string Btoa(string data)
    {
        foreach (char character in data)
            if (character > byte.MaxValue)
                throw CreateInvalidCharacterError();

        return Convert.ToBase64String(Encoding.Latin1.GetBytes(data));
    }

    /// <summary>Whether <paramref name="bytes"/> is well-formed UTF-8.</summary>
    public static bool IsUtf8(byte[] bytes) => IsUtf8(bytes.AsSpan());

    internal static bool IsUtf8(ReadOnlySpan<byte> bytes) => System.Text.Unicode.Utf8.IsValid(bytes);

    /// <summary>Whether every byte of <paramref name="bytes"/> is in the ASCII range.</summary>
    public static bool IsAscii(byte[] bytes) => IsAscii(bytes.AsSpan());

    internal static bool IsAscii(ReadOnlySpan<byte> bytes) => System.Text.Ascii.IsValid(bytes);

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
