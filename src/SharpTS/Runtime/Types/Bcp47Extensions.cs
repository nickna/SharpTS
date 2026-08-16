namespace SharpTS.Runtime.Types;

/// <summary>
/// Parses BCP 47 Unicode extensions (-u-...) from locale strings.
/// Extracts calendar (ca), numbering system (nu), and hour cycle (hc) extensions.
/// </summary>
public record Bcp47Extensions(
    string BaseLocale,
    string? Calendar,
    string? NumberingSystem,
    string? HourCycle
)
{
    /// <summary>
    /// Parses a locale string and extracts BCP 47 Unicode extensions.
    /// E.g. "ja-JP-u-ca-japanese-nu-latn-hc-h23" → BaseLocale="ja-JP", Calendar="japanese", NumberingSystem="latn", HourCycle="h23"
    /// </summary>
    public static Bcp47Extensions Parse(string locale)
    {
        var idx = locale.IndexOf("-u-", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return new Bcp47Extensions(locale, null, null, null);

        var baseLocale = locale[..idx];
        var extensionPart = locale[(idx + 3)..]; // skip "-u-"

        string? calendar = null;
        string? numberingSystem = null;
        string? hourCycle = null;

        // Parse key-value pairs: 2-char key followed by value(s) until next 2-char key
        var parts = extensionPart.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 2 && i + 1 < parts.Length)
            {
                var key = parts[i].ToLowerInvariant();
                // Collect value parts (a value can contain hyphens, so take until next 2-char key)
                var valueParts = new List<string>();
                for (int j = i + 1; j < parts.Length; j++)
                {
                    if (parts[j].Length == 2 && j + 1 < parts.Length && IsExtensionKey(parts[j]))
                        break;
                    valueParts.Add(parts[j]);
                }
                var value = string.Join("-", valueParts);
                i += valueParts.Count; // skip value parts

                switch (key)
                {
                    case "ca": calendar = value; break;
                    case "nu": numberingSystem = value; break;
                    case "hc": hourCycle = value; break;
                }
            }
        }

        return new Bcp47Extensions(baseLocale, calendar, numberingSystem, hourCycle);
    }

    private static bool IsExtensionKey(string part) =>
        part.Length == 2 && char.IsLetter(part[0]) && char.IsLetterOrDigit(part[1]);
}
