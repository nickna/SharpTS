using System.Globalization;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.DisplayNames.
/// Provides locale-aware display names for languages, regions, scripts, currencies, etc.
/// </summary>
public class SharpTSIntlDisplayNames : SharpTSIntlFormatterBase
{
    private string _type;
    private string _style;
    private string _fallback;
    private string _languageDisplay;

    // Script code → display name
    private static readonly Dictionary<string, string> ScriptNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Latn"] = "Latin",
        ["Cyrl"] = "Cyrillic",
        ["Arab"] = "Arabic",
        ["Deva"] = "Devanagari",
        ["Hans"] = "Simplified Han",
        ["Hant"] = "Traditional Han",
        ["Hang"] = "Hangul",
        ["Kana"] = "Katakana",
        ["Hira"] = "Hiragana",
        ["Grek"] = "Greek",
        ["Hebr"] = "Hebrew",
        ["Thai"] = "Thai",
        ["Geor"] = "Georgian",
        ["Armn"] = "Armenian",
        ["Beng"] = "Bengali",
        ["Guru"] = "Gurmukhi",
        ["Gujr"] = "Gujarati",
        ["Orya"] = "Odia",
        ["Taml"] = "Tamil",
        ["Telu"] = "Telugu",
        ["Knda"] = "Kannada",
        ["Mlym"] = "Malayalam",
        ["Sinh"] = "Sinhala",
        ["Mymr"] = "Myanmar",
        ["Khmr"] = "Khmer",
        ["Laoo"] = "Lao",
        ["Tibt"] = "Tibetan",
        ["Ethi"] = "Ethiopic",
    };

    // Currency code → display name
    private static readonly Dictionary<string, string> CurrencyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = "US Dollar",
        ["EUR"] = "Euro",
        ["GBP"] = "British Pound",
        ["JPY"] = "Japanese Yen",
        ["CNY"] = "Chinese Yuan",
        ["KRW"] = "South Korean Won",
        ["INR"] = "Indian Rupee",
        ["RUB"] = "Russian Ruble",
        ["BRL"] = "Brazilian Real",
        ["CAD"] = "Canadian Dollar",
        ["AUD"] = "Australian Dollar",
        ["CHF"] = "Swiss Franc",
        ["SEK"] = "Swedish Krona",
        ["NOK"] = "Norwegian Krone",
        ["DKK"] = "Danish Krone",
        ["NZD"] = "New Zealand Dollar",
        ["SGD"] = "Singapore Dollar",
        ["HKD"] = "Hong Kong Dollar",
        ["MXN"] = "Mexican Peso",
        ["ZAR"] = "South African Rand",
        ["TRY"] = "Turkish Lira",
        ["PLN"] = "Polish Zloty",
        ["THB"] = "Thai Baht",
        ["TWD"] = "New Taiwan Dollar",
        ["ILS"] = "Israeli New Shekel",
        ["AED"] = "United Arab Emirates Dirham",
        ["SAR"] = "Saudi Riyal",
        ["PHP"] = "Philippine Peso",
        ["MYR"] = "Malaysian Ringgit",
        ["IDR"] = "Indonesian Rupiah",
        ["CZK"] = "Czech Koruna",
        ["HUF"] = "Hungarian Forint",
        ["CLP"] = "Chilean Peso",
        ["ARS"] = "Argentine Peso",
        ["COP"] = "Colombian Peso",
        ["PEN"] = "Peruvian Sol",
        ["EGP"] = "Egyptian Pound",
        ["VND"] = "Vietnamese Dong",
        ["PKR"] = "Pakistani Rupee",
        ["BDT"] = "Bangladeshi Taka",
        ["NGN"] = "Nigerian Naira",
        ["KES"] = "Kenyan Shilling",
    };

    // Calendar → display name
    private static readonly Dictionary<string, string> CalendarNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gregory"] = "Gregorian Calendar",
        ["chinese"] = "Chinese Calendar",
        ["islamic"] = "Islamic Calendar",
        ["islamic-civil"] = "Islamic Calendar (tabular, civil epoch)",
        ["islamic-umalqura"] = "Islamic Calendar (Umm al-Qura)",
        ["hebrew"] = "Hebrew Calendar",
        ["buddhist"] = "Buddhist Calendar",
        ["japanese"] = "Japanese Calendar",
        ["persian"] = "Persian Calendar",
        ["indian"] = "Indian National Calendar",
        ["coptic"] = "Coptic Calendar",
        ["ethiopic"] = "Ethiopic Calendar",
        ["iso8601"] = "ISO-8601 Calendar",
        ["roc"] = "Minguo Calendar",
    };

    // Date/time field → display name
    private static readonly Dictionary<string, string> DateTimeFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["era"] = "era",
        ["year"] = "year",
        ["quarter"] = "quarter",
        ["month"] = "month",
        ["weekOfYear"] = "week",
        ["weekday"] = "weekday",
        ["day"] = "day",
        ["dayPeriod"] = "AM/PM",
        ["hour"] = "hour",
        ["minute"] = "minute",
        ["second"] = "second",
        ["timeZoneName"] = "time zone",
    };

    public SharpTSIntlDisplayNames(object? locale, object? options)
    {
        ResolveLocale(locale);

        // Defaults
        _type = "language";
        _style = "long";
        _fallback = "code";
        _languageDisplay = "dialect";

        var opts = NormalizeOptions(options);
        if (opts != null)
            ParseOptions(opts);
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts)
    {
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("type", out var typeVal) && typeVal is string t)
            _type = t;

        if (dict.TryGetValue("style", out var styleVal) && styleVal is string s)
            _style = s;

        if (dict.TryGetValue("fallback", out var fallbackVal) && fallbackVal is string f)
            _fallback = f;

        if (dict.TryGetValue("languageDisplay", out var langDispVal) && langDispVal is string ld)
            _languageDisplay = ld;
    }

    /// <summary>
    /// Returns the display name for the given code, or null/code depending on fallback setting.
    /// </summary>
    public object? LookupDisplayName(string code)
    {
        string? result = _type switch
        {
            "language" => GetLanguageName(code),
            "region" => GetRegionName(code),
            "script" => GetScriptName(code),
            "currency" => GetCurrencyName(code),
            "calendar" => GetCalendarName(code),
            "dateTimeField" => GetDateTimeFieldName(code),
            _ => null
        };

        if (result != null)
            return result;

        // Fallback behavior — return code string or null (runtime converts to undefined)
        return _fallback == "code" ? code : null;
    }

    private static string? GetLanguageName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code.Replace('_', '-'));
            return culture.EnglishName;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetRegionName(string code)
    {
        try
        {
            var region = new RegionInfo(code);
            return region.DisplayName;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetScriptName(string code)
    {
        return ScriptNames.TryGetValue(code, out var name) ? name : null;
    }

    private static string? GetCurrencyName(string code)
    {
        return CurrencyNames.TryGetValue(code, out var name) ? name : null;
    }

    private static string? GetCalendarName(string code)
    {
        return CalendarNames.TryGetValue(code, out var name) ? name : null;
    }

    private static string? GetDateTimeFieldName(string code)
    {
        return DateTimeFieldNames.TryGetValue(code, out var name) ? name : null;
    }

    public override Dictionary<string, object?> GetResolvedOptions()
    {
        return new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["type"] = _type,
            ["style"] = _style,
            ["fallback"] = _fallback,
            ["languageDisplay"] = _languageDisplay,
        };
    }

    /// <summary>
    /// JS-facing of() method for compiled mode reflection dispatch.
    /// </summary>
    public object? of(object? code)
    {
        return LookupDisplayName(code?.ToString() ?? "");
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "of" => BuiltInMethod.CreateV2("of", 1, (_, _, args) =>
            {
                string code = (args.Length > 0 ? args[0].ToObject() : null)?.ToString() ?? "";
                return RuntimeValue.FromBoxed(LookupDisplayName(code));
            }),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object Intl.DisplayNames]";
}
