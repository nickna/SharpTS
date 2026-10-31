using System.Globalization;
using System.Text;

namespace SharpTS.Test262;

internal static class Test262TraceWriter
{
    internal static void WriteLine(TextWriter? writer, string format, params object[] args)
    {
        if (writer is null) return;
        string text = $"{DateTime.UtcNow:HH:mm:ss.fff} {string.Format(format, args)}";
        writer.WriteLine(EscapeInvalidUtf16(text));
    }

    private static string EscapeInvalidUtf16(string text)
    {
        StringBuilder? escaped = null;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            if (!char.IsSurrogate(value)) continue;
            if (char.IsHighSurrogate(value) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            // A strict UTF-8 trace cannot encode an unpaired code unit. Retain
            // its exact value in readable text instead of replacing it or aborting.
            escaped ??= new StringBuilder(text.Length + 6);
            escaped.Append(text, start, i - start);
            escaped.Append("\\u").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
            start = i + 1;
        }
        return escaped is null ? text : escaped.Append(text, start, text.Length - start).ToString();
    }
}