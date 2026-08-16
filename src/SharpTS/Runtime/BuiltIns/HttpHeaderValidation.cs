namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Shared HTTP header-token/value validation (#1052), used by both http.validateHeaderName/
/// validateHeaderValue (interpreter) and the compiled emitter so the rules match by construction.
/// </summary>
public static class HttpHeaderValidation
{
    /// <summary>Node's default http.maxHeaderSize (16 KiB).</summary>
    public const int MaxHeaderSize = 16384;

    /// <summary>
    /// True if <paramref name="s"/> is a valid HTTP token (RFC 7230 §3.2.6): non-empty and every
    /// char is an alpha/digit or one of !#$%&amp;'*+-.^_`|~.
    /// </summary>
    public static bool IsValidHttpToken(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (!IsTokenChar(c)) return false;
        return true;
    }

    private static bool IsTokenChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
        c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.'
          or '^' or '_' or '`' or '|' or '~';

    /// <summary>
    /// True if a header value contains an invalid character (Node's checkInvalidHeaderChar):
    /// anything outside {tab, 0x20–0x7e, 0x80–0xff}.
    /// </summary>
    public static bool HasInvalidHeaderChar(string value)
    {
        foreach (char c in value)
        {
            if (c == '\t') continue;
            if (c < 0x20 || c == 0x7f) return true;
        }
        return false;
    }
}
