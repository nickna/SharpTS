using System.Text;

namespace SharpTS.Parsing;

/// <summary>
/// Source-driven scanning and cooking of JSX text runs and attribute string values.
/// The upfront <see cref="Lexer"/> applies TypeScript string/comment/operator rules inside
/// JSX text (an apostrophe starts a string literal, <c>//</c> starts a comment, …), so the
/// parser reads these regions directly from the source text via <see cref="Token.Start"/>
/// offsets instead of trusting the token stream. See <c>Parser.Jsx.cs</c> for the consumer.
/// </summary>
internal static class JsxText
{
    /// <summary>A bare '&gt;' or '}' inside JSX text — a grammar error tsc recovers from (TS1382/TS1381).</summary>
    public readonly record struct TextError(char Character, int Line);

    /// <summary>Result of <see cref="ScanText"/>.</summary>
    /// <param name="Raw">The raw text run, verbatim from source.</param>
    /// <param name="EndOffset">Offset of the terminator ('&lt;' or '{'), or source length at EOF.</param>
    /// <param name="Terminator">'&lt;', '{', or '\0' when the file ended inside the run.</param>
    /// <param name="EndLine">Line number at <paramref name="EndOffset"/>.</param>
    /// <param name="Errors">Bare '&gt;'/'}' occurrences inside the run, in order.</param>
    public readonly record struct TextScan(
        string Raw, int EndOffset, char Terminator, int EndLine, IReadOnlyList<TextError>? Errors);

    /// <summary>
    /// Scans a raw JSX text run starting at <paramref name="start"/> until the next '&lt;' or
    /// '{'. Bare '&gt;' and '}' are collected as recoverable errors, not terminators (tsc's
    /// behavior — the text continues through them).
    /// </summary>
    public static TextScan ScanText(string source, int start, int line)
    {
        List<TextError>? errors = null;
        int i = start;
        for (; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '<' || c == '{')
                return new TextScan(source[start..i], i, c, line, errors);
            if (c == '\n')
                line++;
            else if (c == '>' || c == '}')
                (errors ??= []).Add(new TextError(c, line));
        }
        return new TextScan(source[start..i], i, '\0', line, errors);
    }

    /// <summary>
    /// Applies JSX whitespace semantics to a raw text run and decodes entities. Returns null
    /// when the run contributes no child. Rules (matching tsc/Babel): a line's leading
    /// whitespace is trimmed unless it is the first line, its trailing whitespace is trimmed
    /// unless it is the last line; lines that become empty are dropped; survivors are joined
    /// with a single space. A run that is whitespace-only with a newline therefore vanishes,
    /// while same-line whitespace (<c>&lt;b&gt; &lt;/b&gt;</c>) is preserved verbatim.
    /// Entities are decoded after trimming so <c>&amp;nbsp;</c>/<c>&amp;#32;</c> survive.
    /// </summary>
    public static string? CookChildText(string raw)
    {
        string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string segment = lines[i];
            if (i > 0) segment = segment.TrimStart(' ', '\t');
            if (i < lines.Length - 1) segment = segment.TrimEnd(' ', '\t');
            if (segment.Length == 0) continue;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(segment);
        }
        return builder.Length == 0 ? null : DecodeEntities(builder.ToString());
    }

    /// <summary>
    /// Result of <see cref="CookAttributeValue"/>.
    /// </summary>
    /// <param name="Value">The decoded attribute value.</param>
    /// <param name="EndOffset">Offset of the closing quote character.</param>
    /// <param name="EndLine">Line number at the closing quote.</param>
    public readonly record struct AttributeScan(string Value, int EndOffset, int EndLine);

    /// <summary>
    /// Scans a JSX attribute string starting at the quote character at
    /// <paramref name="quoteOffset"/>. JSX strings end at the same unescaped quote character —
    /// backslash is a literal character, not an escape — may span newlines, and have their
    /// entities decoded. Throws <see cref="ParseError"/> (TS1002) when unterminated.
    /// </summary>
    public static AttributeScan CookAttributeValue(string source, int quoteOffset, int line)
    {
        char quote = source[quoteOffset];
        for (int i = quoteOffset + 1; i < source.Length; i++)
        {
            char c = source[i];
            if (c == quote)
                return new AttributeScan(DecodeEntities(source[(quoteOffset + 1)..i]), i, line);
            if (c == '\n')
                line++;
        }
        throw new ParseError("Unterminated string literal.", "TS1002");
    }

    /// <summary>
    /// Decodes HTML character references: a curated named set (the ones that appear in real
    /// JSX) plus decimal (<c>&amp;#160;</c>) and hex (<c>&amp;#xA0;</c>) forms. An
    /// unrecognized or malformed reference is left verbatim (tsc's behavior).
    /// </summary>
    public static string DecodeEntities(string text)
    {
        int amp = text.IndexOf('&');
        if (amp < 0) return text;

        var builder = new StringBuilder(text.Length);
        builder.Append(text, 0, amp);
        for (int i = amp; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '&')
            {
                builder.Append(c);
                continue;
            }

            int semi = text.IndexOf(';', i + 1);
            if (semi < 0 || semi == i + 1 || semi - i > 12)
            {
                builder.Append(c);
                continue;
            }

            string name = text[(i + 1)..semi];
            if (TryDecodeEntityName(name, out string decoded))
            {
                builder.Append(decoded);
                i = semi;
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static bool TryDecodeEntityName(string name, out string decoded)
    {
        decoded = "";
        if (name.Length >= 2 && name[0] == '#')
        {
            bool hex = name[1] is 'x' or 'X';
            string digits = name[(hex ? 2 : 1)..];
            if (digits.Length == 0) return false;
            if (!int.TryParse(digits,
                    hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int code))
                return false;
            if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) return false;
            decoded = char.ConvertFromUtf32(code);
            return true;
        }

        if (NamedEntities.TryGetValue(name, out string? value))
        {
            decoded = value;
            return true;
        }
        return false;
    }

    // Case-sensitive, as in HTML ("&Amp;" is not an entity).
    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["quot"] = "\"",
        ["apos"] = "'",
        ["nbsp"] = " ",
        ["copy"] = "©",
        ["reg"] = "®",
        ["trade"] = "™",
        ["hellip"] = "…",
        ["mdash"] = "—",
        ["ndash"] = "–",
        ["lsquo"] = "‘",
        ["rsquo"] = "’",
        ["ldquo"] = "“",
        ["rdquo"] = "”",
        ["bull"] = "•",
        ["middot"] = "·",
        ["sect"] = "§",
        ["para"] = "¶",
        ["deg"] = "°",
        ["plusmn"] = "±",
        ["times"] = "×",
        ["divide"] = "÷",
        ["frac12"] = "½",
        ["frac14"] = "¼",
        ["frac34"] = "¾",
        ["larr"] = "←",
        ["uarr"] = "↑",
        ["rarr"] = "→",
        ["darr"] = "↓",
        ["harr"] = "↔",
        ["laquo"] = "«",
        ["raquo"] = "»",
        ["iexcl"] = "¡",
        ["iquest"] = "¿",
        ["szlig"] = "ß",
        ["euro"] = "€",
        ["pound"] = "£",
        ["yen"] = "¥",
        ["cent"] = "¢",
        ["rbrace"] = "}",
        ["lbrace"] = "{",
    };
}
