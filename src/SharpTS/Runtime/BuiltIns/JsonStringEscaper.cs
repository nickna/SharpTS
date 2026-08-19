using System.Text;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// ECMA-262 §25.5.2.2 <c>QuoteJSONString</c>: the string quoting JSON.stringify specifies.
/// C# twin of the IL the compiler emits into standalone output
/// (<c>RuntimeEmitter.Json.Stringify.cs</c> <c>EmitEscapeJsonStringHelper</c>) — keep the two in
/// sync; cross-backend stdout parity depends on it.
/// </summary>
/// <remarks>
/// This replaces <c>JsonSerializer.Serialize(string)</c> at the interpreter/RuntimeTypes call
/// sites (#1324 Phase 1): the reflection serializer needs type metadata Native AOT doesn't keep,
/// and its default encoder diverges from JavaScript — it escapes non-ASCII and HTML-sensitive
/// characters (<c>é</c> → <c>é</c>, <c>&lt;</c> → <c><</c>) and replaces lone
/// surrogates with U+FFFD, where ES2019 well-formed JSON.stringify emits non-ASCII literally and
/// escapes lone surrogates as <c>\uXXXX</c>.
/// </remarks>
internal static class JsonStringEscaper
{
    /// <summary>Appends <paramref name="s"/> quoted and escaped per QuoteJSONString.</summary>
    internal static void AppendQuoted(StringBuilder sb, string s)
    {
        // Identifiers and ordinary application strings overwhelmingly need no
        // escaping. Append that common case in three chunks instead of one
        // virtual StringBuilder call per UTF-16 code unit. Keep surrogate code
        // units on the complete path below so lone surrogates remain well-formed.
        bool needsEscaping = false;
        foreach (char c in s)
        {
            if (c is '"' or '\\' || c < 0x20 || char.IsSurrogate(c))
            {
                needsEscaping = true;
                break;
            }
        }

        if (!needsEscaping)
        {
            sb.Append('"').Append(s).Append('"');
            return;
        }

        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        AppendUnicodeEscape(sb, c);
                    }
                    else if (char.IsHighSurrogate(c))
                    {
                        // Well-formed stringify (ES2019): a valid pair passes through as-is;
                        // a lone high surrogate is escaped.
                        if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                        {
                            sb.Append(c).Append(s[i + 1]);
                            i++;
                        }
                        else
                        {
                            AppendUnicodeEscape(sb, c);
                        }
                    }
                    else if (char.IsLowSurrogate(c))
                    {
                        AppendUnicodeEscape(sb, c); // lone low surrogate
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Returns <paramref name="s"/> quoted and escaped per QuoteJSONString.</summary>
    internal static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        AppendQuoted(sb, s);
        return sb.ToString();
    }

    private static void AppendUnicodeEscape(StringBuilder sb, char c) =>
        sb.Append("\\u").Append(((int)c).ToString("x4")); // lowercase hex, matching JS engines and the emitted IL
}
