using System.Globalization;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.PluralRules.
/// Provides plural category selection (zero, one, two, few, many, other) based on CLDR rules.
/// </summary>
public class SharpTSIntlPluralRules
{
    private readonly string _locale;
    private string _type; // "cardinal" or "ordinal"
    private int _minimumIntegerDigits;
    private int _minimumFractionDigits;
    private int _maximumFractionDigits;

    // CLDR plural rules for common locales
    // See: https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html

    public SharpTSIntlPluralRules(object? locale, object? options)
    {
        string localeStr = locale?.ToString() ?? "";

        CultureInfo culture;
        try
        {
            culture = string.IsNullOrEmpty(localeStr)
                ? CultureInfo.CurrentCulture
                : CultureInfo.GetCultureInfo(localeStr.Replace('_', '-'));
        }
        catch
        {
            culture = CultureInfo.InvariantCulture;
        }

        _locale = culture.Name;
        if (string.IsNullOrEmpty(_locale))
            _locale = "en-US";

        // Defaults
        _type = "cardinal";
        _minimumIntegerDigits = 1;
        _minimumFractionDigits = 0;
        _maximumFractionDigits = 3;

        if (options is SharpTSObject obj)
        {
            ParseOptions(obj.Fields);
        }
        else if (options is IDictionary<string, object?> dict)
        {
            ParseOptions(dict);
        }
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts)
    {
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("type", out var typeVal) && typeVal is string t)
            _type = t;

        if (dict.TryGetValue("minimumIntegerDigits", out var minIntVal))
            _minimumIntegerDigits = (int)Interp.ToNumber(minIntVal);

        if (dict.TryGetValue("minimumFractionDigits", out var minFracVal))
            _minimumFractionDigits = (int)Interp.ToNumber(minFracVal);

        if (dict.TryGetValue("maximumFractionDigits", out var maxFracVal))
            _maximumFractionDigits = (int)Interp.ToNumber(maxFracVal);
    }

    /// <summary>
    /// Selects a plural category for the given number.
    /// Returns: "zero", "one", "two", "few", "many", or "other".
    /// </summary>
    public string SelectCategory(double number)
    {
        string lang = GetLanguageCode();

        if (_type == "ordinal")
            return SelectOrdinal(number, lang);

        return SelectCardinal(number, lang);
    }

    private string GetLanguageCode()
    {
        // Extract primary language from locale (e.g., "en-US" → "en")
        int dashIndex = _locale.IndexOf('-');
        return dashIndex >= 0 ? _locale[..dashIndex].ToLowerInvariant() : _locale.ToLowerInvariant();
    }

    /// <summary>
    /// CLDR cardinal plural rules for common languages.
    /// </summary>
    private static string SelectCardinal(double n, string lang)
    {
        // Handle special values
        if (double.IsNaN(n) || double.IsInfinity(n))
            return "other";

        double absN = Math.Abs(n);
        long i = (long)absN; // integer part
        int v = GetVisibleFractionDigitCount(n); // number of visible fraction digits
        long f = GetVisibleFractionDigits(n, v); // visible fraction digits as integer

        switch (lang)
        {
            // English, German, Dutch, Swedish, Norwegian, Danish, etc.
            // one: i = 1 and v = 0
            case "en": case "de": case "nl": case "sv": case "nb": case "nn":
            case "no": case "da": case "et": case "fi": case "hu": case "it":
            case "pt": case "es": case "bg": case "el": case "he": case "hi":
            case "id": case "ms": case "th": case "tr": case "ur":
                if (i == 1 && v == 0) return "one";
                return "other";

            // French, Portuguese-Brazil: one for 0 and 1
            case "fr":
                if (i == 0 || i == 1) return "one";
                return "other";

            // Arabic
            case "ar":
                if (n == 0) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                long mod100 = i % 100;
                if (mod100 >= 3 && mod100 <= 10) return "few";
                if (mod100 >= 11 && mod100 <= 99) return "many";
                return "other";

            // Polish
            case "pl":
                if (i == 1 && v == 0) return "one";
                if (v == 0 && i % 10 >= 2 && i % 10 <= 4 && (i % 100 < 12 || i % 100 > 14)) return "few";
                if (v == 0 && (i != 1 && i % 10 >= 0 && i % 10 <= 1 || i % 10 >= 5 && i % 10 <= 9 || i % 100 >= 12 && i % 100 <= 14)) return "many";
                return "other";

            // Russian, Ukrainian
            case "ru": case "uk":
                if (v == 0 && i % 10 == 1 && i % 100 != 11) return "one";
                if (v == 0 && i % 10 >= 2 && i % 10 <= 4 && (i % 100 < 12 || i % 100 > 14)) return "few";
                if (v == 0 && (i % 10 == 0 || i % 10 >= 5 && i % 10 <= 9 || i % 100 >= 11 && i % 100 <= 14)) return "many";
                return "other";

            // Czech, Slovak
            case "cs": case "sk":
                if (i == 1 && v == 0) return "one";
                if (i >= 2 && i <= 4 && v == 0) return "few";
                if (v != 0) return "many";
                return "other";

            // Japanese, Chinese, Korean, Vietnamese, Thai - no plural distinctions
            case "ja": case "zh": case "ko": case "vi":
                return "other";

            // Welsh
            case "cy":
                if (n == 0) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                if (n == 3) return "few";
                if (n == 6) return "many";
                return "other";

            // Default (most languages): i=1 and v=0 → one, else other
            default:
                if (i == 1 && v == 0) return "one";
                return "other";
        }
    }

    /// <summary>
    /// CLDR ordinal plural rules for common languages.
    /// </summary>
    private static string SelectOrdinal(double n, string lang)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
            return "other";

        double absN = Math.Abs(n);
        long i = (long)absN;
        long mod10 = i % 10;
        long mod100 = i % 100;

        switch (lang)
        {
            // English: 1st, 2nd, 3rd, 4th...
            case "en":
                if (mod10 == 1 && mod100 != 11) return "one";
                if (mod10 == 2 && mod100 != 12) return "two";
                if (mod10 == 3 && mod100 != 13) return "few";
                return "other";

            // Welsh
            case "cy":
                if (n == 0 || n == 7 || n == 8 || n == 9) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                if (n == 3 || n == 4) return "few";
                if (n == 5 || n == 6) return "many";
                return "other";

            // French, Italian, etc.: one for 1
            case "fr": case "it":
                if (n == 1) return "one";
                return "other";

            // Most languages don't distinguish ordinals
            default:
                return "other";
        }
    }

    /// <summary>
    /// Gets the number of visible fraction digits (v in CLDR).
    /// </summary>
    private static int GetVisibleFractionDigitCount(double n)
    {
        string s = n.ToString("R", CultureInfo.InvariantCulture);
        int dotIndex = s.IndexOf('.');
        if (dotIndex < 0) return 0;
        return s.Length - dotIndex - 1;
    }

    /// <summary>
    /// Gets the visible fraction digits as an integer (f in CLDR).
    /// </summary>
    private static long GetVisibleFractionDigits(double n, int v)
    {
        if (v == 0) return 0;
        string s = n.ToString("R", CultureInfo.InvariantCulture);
        int dotIndex = s.IndexOf('.');
        if (dotIndex < 0) return 0;
        string frac = s[(dotIndex + 1)..];
        return long.TryParse(frac, out var f) ? f : 0;
    }

    public Dictionary<string, object?> GetResolvedOptions()
    {
        return new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["type"] = _type,
            ["minimumIntegerDigits"] = (double)_minimumIntegerDigits,
            ["minimumFractionDigits"] = (double)_minimumFractionDigits,
            ["maximumFractionDigits"] = (double)_maximumFractionDigits,
            ["pluralCategories"] = GetPluralCategories(),
        };
    }

    private SharpTSArray GetPluralCategories()
    {
        string lang = GetLanguageCode();
        List<object?> categories;

        if (_type == "ordinal")
        {
            categories = lang switch
            {
                "en" => ["one", "two", "few", "other"],
                "cy" => ["zero", "one", "two", "few", "many", "other"],
                "fr" or "it" => ["one", "other"],
                _ => new List<object?> { "other" }
            };
        }
        else
        {
            categories = lang switch
            {
                "ar" => ["zero", "one", "two", "few", "many", "other"],
                "cy" => ["zero", "one", "two", "few", "many", "other"],
                "pl" or "ru" or "uk" => ["one", "few", "many", "other"],
                "cs" or "sk" => ["one", "few", "many", "other"],
                "fr" => ["one", "other"],
                "ja" or "zh" or "ko" or "vi" => new List<object?> { "other" },
                _ => ["one", "other"]
            };
        }

        return new SharpTSArray(categories);
    }

    /// <summary>
    /// JS-facing select method for compiled mode reflection dispatch.
    /// </summary>
    public object? select(object? number)
    {
        double num = Interp.ToNumber(number);
        return SelectCategory(num);
    }

    /// <summary>
    /// JS-facing resolvedOptions method for compiled mode reflection dispatch.
    /// </summary>
    public object? resolvedOptions()
    {
        return GetResolvedOptions();
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "select" => BuiltInMethod.CreateV2("select", 1, (_, _, args) =>
            {
                double num = Interp.ToNumber(args.Length > 0 ? args[0].ToObject() : null);
                return RuntimeValue.FromBoxed(SelectCategory(num));
            }),
            "resolvedOptions" => BuiltInMethod.CreateV2("resolvedOptions", 0, (_, _, _) =>
            {
                return RuntimeValue.FromObject(new SharpTSObject(GetResolvedOptions()));
            }),
            _ => null
        };
    }

    public override string ToString() => "[object Intl.PluralRules]";
}
