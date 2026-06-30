using System.Globalization;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.RelativeTimeFormat.
/// Provides locale-aware relative time formatting ("3 days ago", "in 2 hours").
/// </summary>
public class SharpTSIntlRelativeTimeFormat
{
    private readonly string _locale;
    private string _style; // "long", "short", "narrow"
    private string _numeric; // "always", "auto"
    private readonly string _lang;

    // Unit names for different styles and languages
    private static readonly Dictionary<string, Dictionary<string, (string singular, string plural, string past, string future)>> UnitNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new()
            {
                ["year"] = ("year", "years", "ago", "in"),
                ["quarter"] = ("quarter", "quarters", "ago", "in"),
                ["month"] = ("month", "months", "ago", "in"),
                ["week"] = ("week", "weeks", "ago", "in"),
                ["day"] = ("day", "days", "ago", "in"),
                ["hour"] = ("hour", "hours", "ago", "in"),
                ["minute"] = ("minute", "minutes", "ago", "in"),
                ["second"] = ("second", "seconds", "ago", "in"),
            }
        };

    // Auto-format for 0/-1/1 values (when numeric: "auto")
    private static readonly Dictionary<string, Dictionary<string, (string past, string future)>> AutoFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new()
            {
                ["year"] = ("last year", "next year"),
                ["quarter"] = ("last quarter", "next quarter"),
                ["month"] = ("last month", "next month"),
                ["week"] = ("last week", "next week"),
                ["day"] = ("yesterday", "tomorrow"),
                ["hour"] = ("1 hour ago", "in 1 hour"),
                ["minute"] = ("1 minute ago", "in 1 minute"),
                ["second"] = ("1 second ago", "in 1 second"),
            }
        };

    // Short unit names
    private static readonly Dictionary<string, string> ShortUnits = new()
    {
        ["year"] = "yr.",
        ["quarter"] = "qtr.",
        ["month"] = "mo.",
        ["week"] = "wk.",
        ["day"] = "day",
        ["hour"] = "hr.",
        ["minute"] = "min.",
        ["second"] = "sec.",
    };

    // Narrow unit names
    private static readonly Dictionary<string, string> NarrowUnits = new()
    {
        ["year"] = "y",
        ["quarter"] = "q",
        ["month"] = "m",
        ["week"] = "w",
        ["day"] = "d",
        ["hour"] = "h",
        ["minute"] = "m",
        ["second"] = "s",
    };

    public SharpTSIntlRelativeTimeFormat(object? locale, object? options)
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

        int dashIndex = _locale.IndexOf('-');
        _lang = dashIndex >= 0 ? _locale[..dashIndex].ToLowerInvariant() : _locale.ToLowerInvariant();

        // Defaults
        _style = "long";
        _numeric = "always";

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

        if (dict.TryGetValue("style", out var styleVal) && styleVal is string s)
            _style = s;

        if (dict.TryGetValue("numeric", out var numericVal) && numericVal is string n)
            _numeric = n;
    }

    /// <summary>
    /// Formats a value and unit as a relative time string.
    /// Positive values = future ("in X days"), negative values = past ("X days ago").
    /// </summary>
    public string FormatRelativeTime(double value, string unit)
    {
        // Normalize unit (remove trailing 's' if present for lookup)
        unit = NormalizeUnit(unit);

        double absValue = Math.Abs(value);
        long intValue = (long)absValue;

        // Handle "auto" numeric for -1, 0, 1
        if (_numeric == "auto" && _style == "long" && (intValue == 0 || intValue == 1) && absValue == intValue)
        {
            if (intValue == 0 && value == 0)
            {
                // "now" for seconds, "this X" for others
                if (unit == "second") return "now";
                return GetAutoCurrentForm(unit);
            }

            if (intValue == 1)
            {
                if (AutoFormats.TryGetValue(_lang, out var autoForms) ||
                    AutoFormats.TryGetValue("en", out autoForms))
                {
                    if (autoForms.TryGetValue(unit, out var forms))
                    {
                        return value < 0 ? forms.past : forms.future;
                    }
                }
            }
        }

        // Regular numeric formatting
        string unitName = GetUnitName(unit, absValue);
        string absValueStr = FormatNumber(absValue);

        if (value < 0)
        {
            // Past: "X units ago"
            return _style switch
            {
                "narrow" => $"-{absValueStr}{unitName}",
                _ => $"{absValueStr} {unitName} ago"
            };
        }
        else
        {
            // Future: "in X units"
            return _style switch
            {
                "narrow" => $"+{absValueStr}{unitName}",
                _ => $"in {absValueStr} {unitName}"
            };
        }
    }

    private string GetAutoCurrentForm(string unit)
    {
        return unit switch
        {
            "year" => "this year",
            "quarter" => "this quarter",
            "month" => "this month",
            "week" => "this week",
            "day" => "today",
            _ => $"in 0 {GetUnitName(unit, 0)}"
        };
    }

    private string GetUnitName(string unit, double absValue)
    {
        if (_style == "narrow")
        {
            return NarrowUnits.TryGetValue(unit, out var narrow) ? narrow : unit;
        }

        if (_style == "short")
        {
            return ShortUnits.TryGetValue(unit, out var shortName) ? shortName : unit;
        }

        // Long style
        bool isPlural = absValue != 1;
        if (UnitNames.TryGetValue(_lang, out var units) || UnitNames.TryGetValue("en", out units))
        {
            if (units.TryGetValue(unit, out var names))
            {
                return isPlural ? names.plural : names.singular;
            }
        }

        return unit;
    }

    private static string FormatNumber(double value)
    {
        if (value == Math.Floor(value))
            return ((long)value).ToString();
        return value.ToString("G", CultureInfo.InvariantCulture);
    }

    private static string NormalizeUnit(string unit)
    {
        // Remove trailing 's' if present (e.g., "days" → "day")
        if (unit.EndsWith('s') && unit != "hours" && unit.Length > 1)
        {
            string singular = unit[..^1];
            if (singular is "year" or "quarter" or "month" or "week" or "day" or "hour" or "minute" or "second")
                return singular;
        }
        // Also handle "hours" → "hour"
        if (unit == "hours") return "hour";
        return unit;
    }

    /// <summary>
    /// Formats the value and unit into parts.
    /// </summary>
    public SharpTSArray GetFormattedParts(double value, string unit)
    {
        unit = NormalizeUnit(unit);
        string formatted = FormatRelativeTime(value, unit);

        // Simple parts: for now, return a single "literal" part
        var parts = new List<object?>
        {
            new SharpTSObject(new Dictionary<string, object?>
            {
                ["type"] = "literal",
                ["value"] = formatted,
                ["unit"] = unit,
            })
        };

        return new SharpTSArray(parts);
    }

    public Dictionary<string, object?> GetResolvedOptions()
    {
        return new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["style"] = _style,
            ["numeric"] = _numeric,
            ["numberingSystem"] = "latn",
        };
    }

    /// <summary>
    /// JS-facing format method for compiled mode reflection dispatch.
    /// </summary>
    public object? format(object? value, object? unit)
    {
        double num = Interp.ToNumber(value);
        string unitStr = unit?.ToString() ?? "second";
        return FormatRelativeTime(num, unitStr);
    }

    /// <summary>
    /// JS-facing formatToParts method for compiled mode reflection dispatch.
    /// </summary>
    public object? formatToParts(object? value, object? unit)
    {
        double num = Interp.ToNumber(value);
        string unitStr = unit?.ToString() ?? "second";
        return GetFormattedParts(num, unitStr);
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
            "format" => BuiltInMethod.CreateV2("format", 2, (_, _, args) =>
            {
                double num = Interp.ToNumber(args.Length > 0 ? args[0].ToObject() : null);
                string unit = (args.Length > 1 ? args[1].ToObject() : null)?.ToString() ?? "second";
                return RuntimeValue.FromBoxed(FormatRelativeTime(num, unit));
            }),
            "formatToParts" => BuiltInMethod.CreateV2("formatToParts", 2, (_, _, args) =>
            {
                double num = Interp.ToNumber(args.Length > 0 ? args[0].ToObject() : null);
                string unit = (args.Length > 1 ? args[1].ToObject() : null)?.ToString() ?? "second";
                return RuntimeValue.FromBoxed(GetFormattedParts(num, unit));
            }),
            "resolvedOptions" => BuiltInMethod.CreateV2("resolvedOptions", 0, (_, _, _) =>
            {
                return RuntimeValue.FromObject(new SharpTSObject(GetResolvedOptions()));
            }),
            _ => null
        };
    }

    public override string ToString() => "[object Intl.RelativeTimeFormat]";
}
