using System.Globalization;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.NumberFormat.
/// Provides locale-aware number formatting using .NET's CultureInfo/NumberFormatInfo.
/// </summary>
public class SharpTSIntlNumberFormat : SharpTSIntlFormatterBase
{
    private string _style;
    private string? _currency;
    private int _minimumFractionDigits;
    private int _maximumFractionDigits;
    private int _minimumIntegerDigits;
    private bool _useGrouping;
    private readonly NumberFormatInfo _numberFormat;

    /// <summary>
    /// ISO 4217 currency code to symbol mapping for common currencies.
    /// </summary>
    private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = "$",
        ["EUR"] = "€",
        ["GBP"] = "£",
        ["JPY"] = "¥",
        ["CNY"] = "¥",
        ["KRW"] = "₩",
        ["INR"] = "₹",
        ["RUB"] = "₽",
        ["BRL"] = "R$",
        ["CAD"] = "CA$",
        ["AUD"] = "A$",
        ["CHF"] = "CHF",
        ["SEK"] = "kr",
        ["NOK"] = "kr",
        ["DKK"] = "kr",
        ["PLN"] = "zł",
        ["THB"] = "฿",
        ["MXN"] = "MX$",
        ["ZAR"] = "R",
        ["TRY"] = "₺",
    };

    public SharpTSIntlNumberFormat(object? locale, object? options)
    {
        // Parse locale
        var culture = ResolveLocale(locale);

        // Clone number format so we can modify it
        _numberFormat = (NumberFormatInfo)culture.NumberFormat.Clone();

        // Parse options
        _style = "decimal";
        _currency = null;
        _minimumIntegerDigits = 1;
        _useGrouping = true;

        // Default fraction digits depend on style
        int defaultMinFrac = 0;
        int defaultMaxFrac = 3;

        var opts = NormalizeOptions(options);
        if (opts != null)
            ParseOptions(opts, ref defaultMinFrac, ref defaultMaxFrac);

        _minimumFractionDigits = defaultMinFrac;
        _maximumFractionDigits = defaultMaxFrac;
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts, ref int defaultMinFrac, ref int defaultMaxFrac)
    {
        // Build a local dictionary for easy lookup from any dict-like source
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("style", out var styleVal) && styleVal is string s)
        {
            _style = s;
        }

        if (dict.TryGetValue("currency", out var currVal) && currVal is string curr)
        {
            _currency = curr.ToUpperInvariant();
            // Set the currency symbol
            if (CurrencySymbols.TryGetValue(_currency, out var symbol))
            {
                _numberFormat.CurrencySymbol = symbol;
            }
        }

        // Currency style defaults: 2 fraction digits
        if (_style == "currency")
        {
            defaultMinFrac = 2;
            defaultMaxFrac = 2;
        }
        else if (_style == "percent")
        {
            defaultMinFrac = 0;
            defaultMaxFrac = 0;
        }

        if (dict.TryGetValue("minimumFractionDigits", out var minFracVal))
        {
            defaultMinFrac = (int)Interp.ToNumber(minFracVal);
        }

        if (dict.TryGetValue("maximumFractionDigits", out var maxFracVal))
        {
            defaultMaxFrac = (int)Interp.ToNumber(maxFracVal);
        }

        if (dict.TryGetValue("minimumIntegerDigits", out var minIntVal))
        {
            _minimumIntegerDigits = (int)Interp.ToNumber(minIntVal);
        }

        if (dict.TryGetValue("useGrouping", out var groupVal))
        {
            if (groupVal is bool b)
                _useGrouping = b;
            else if (groupVal is string gs)
                _useGrouping = gs != "false";
        }
    }

    /// <summary>
    /// Formats a number according to the locale and options.
    /// </summary>
    public string FormatNumber(double number)
    {
        // If grouping is disabled, clone and override
        var nfi = _numberFormat;
        if (!_useGrouping)
        {
            nfi = (NumberFormatInfo)_numberFormat.Clone();
            nfi.NumberGroupSeparator = "";
            nfi.CurrencyGroupSeparator = "";
            nfi.PercentGroupSeparator = "";
        }

        string result;
        switch (_style)
        {
            case "currency":
                result = number.ToString("C" + _maximumFractionDigits, nfi);
                break;
            case "percent":
                result = number.ToString("P" + _maximumFractionDigits, nfi);
                break;
            default: // "decimal"
                result = number.ToString("N" + _maximumFractionDigits, nfi);
                break;
        }

        // Strip trailing zeros between minimumFractionDigits and maximumFractionDigits
        // JS Intl.NumberFormat uses "up to N" fraction digits, while .NET "N3" always shows 3
        if (_maximumFractionDigits > _minimumFractionDigits)
        {
            result = TrimTrailingFractionZeros(result, nfi);
        }

        // Handle minimumIntegerDigits (pad with zeros before the decimal/group separators)
        if (_minimumIntegerDigits > 1)
        {
            result = PadIntegerPart(result, nfi);
        }

        return result;
    }

    /// <summary>
    /// Strips trailing zeros from the fractional part down to minimumFractionDigits.
    /// If all fraction digits are stripped, removes the decimal separator too.
    /// Handles percent sign and currency suffixes by working only on the numeric portion.
    /// </summary>
    private string TrimTrailingFractionZeros(string formatted, NumberFormatInfo nfi)
    {
        string decimalSep = _style switch
        {
            "currency" => nfi.CurrencyDecimalSeparator,
            "percent" => nfi.PercentDecimalSeparator,
            _ => nfi.NumberDecimalSeparator
        };

        int decPos = formatted.IndexOf(decimalSep, StringComparison.Ordinal);
        if (decPos < 0) return formatted; // No decimal point

        // Find the end of numeric digits (may have suffix like % or trailing whitespace)
        int endOfDigits = decPos + decimalSep.Length;
        while (endOfDigits < formatted.Length && char.IsDigit(formatted[endOfDigits]))
            endOfDigits++;

        string fracPart = formatted[(decPos + decimalSep.Length)..endOfDigits];
        string suffix = formatted[endOfDigits..];

        // Trim trailing zeros, but keep at least _minimumFractionDigits
        int trimTo = fracPart.Length;
        while (trimTo > _minimumFractionDigits && fracPart[trimTo - 1] == '0')
            trimTo--;

        if (trimTo == 0)
        {
            // Remove decimal separator entirely
            return formatted[..decPos] + suffix;
        }

        return formatted[..decPos] + decimalSep + fracPart[..trimTo] + suffix;
    }

    /// <summary>
    /// Pads the integer part of the formatted number to meet minimumIntegerDigits.
    /// </summary>
    private string PadIntegerPart(string formatted, NumberFormatInfo nfi)
    {
        // Find where the decimal point is (or end of string for integers)
        string decimalSep = _style switch
        {
            "currency" => nfi.CurrencyDecimalSeparator,
            "percent" => nfi.PercentDecimalSeparator,
            _ => nfi.NumberDecimalSeparator
        };

        string groupSep = _style switch
        {
            "currency" => nfi.CurrencyGroupSeparator,
            "percent" => nfi.PercentGroupSeparator,
            _ => nfi.NumberGroupSeparator
        };

        // Strip currency symbol and percent sign to find integer digits
        // Then re-apply. This is complex, so use a simpler approach:
        // Find the first digit and the decimal point, count integer digits.
        int firstDigit = -1;
        int decimalPos = formatted.IndexOf(decimalSep, StringComparison.Ordinal);
        if (decimalPos < 0) decimalPos = formatted.Length;

        // Count actual integer digits (excluding group separators)
        int integerDigitCount = 0;
        for (int i = 0; i < decimalPos; i++)
        {
            if (char.IsDigit(formatted[i]))
            {
                if (firstDigit < 0) firstDigit = i;
                integerDigitCount++;
            }
        }

        if (integerDigitCount >= _minimumIntegerDigits || firstDigit < 0)
            return formatted;

        // Need to pad with zeros
        int zerosNeeded = _minimumIntegerDigits - integerDigitCount;
        string zeros = new string('0', zerosNeeded);

        // Insert zeros right before the first digit
        string padded = formatted.Insert(firstDigit, zeros);

        // Re-apply grouping if enabled
        if (_useGrouping && !string.IsNullOrEmpty(groupSep))
        {
            // Strip existing group separators from the integer part, then re-group
            string prefix = padded[..firstDigit];
            int newDecimalPos = padded.IndexOf(decimalSep, StringComparison.Ordinal);
            if (newDecimalPos < 0) newDecimalPos = padded.Length;
            string intPart = padded[firstDigit..newDecimalPos];
            string suffix = padded[newDecimalPos..];

            // Remove old group separators
            intPart = intPart.Replace(groupSep, "");

            // Re-group from right
            if (intPart.Length > 3)
            {
                var grouped = new System.Text.StringBuilder();
                for (int i = intPart.Length - 1, count = 0; i >= 0; i--, count++)
                {
                    if (count > 0 && count % 3 == 0)
                        grouped.Insert(0, groupSep);
                    grouped.Insert(0, intPart[i]);
                }
                intPart = grouped.ToString();
            }

            padded = prefix + intPart + suffix;
        }

        return padded;
    }

    /// <summary>
    /// Returns an object describing the computed options of the formatter.
    /// </summary>
    public override Dictionary<string, object?> GetResolvedOptions()
    {
        var result = new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["numberingSystem"] = "latn",
            ["style"] = _style,
            ["minimumIntegerDigits"] = (double)_minimumIntegerDigits,
            ["minimumFractionDigits"] = (double)_minimumFractionDigits,
            ["maximumFractionDigits"] = (double)_maximumFractionDigits,
            ["useGrouping"] = _useGrouping,
        };

        if (_style == "currency" && _currency != null)
        {
            result["currency"] = _currency;
            result["currencyDisplay"] = "symbol";
        }

        return result;
    }

    /// <summary>
    /// JS-facing format method for compiled mode reflection dispatch.
    /// Named lowercase to match JS convention; FormatNumber is the PascalCase internal version.
    /// </summary>
    public object? format(object? number)
    {
        double num = Interp.ToNumber(number);
        return FormatNumber(num);
    }

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "format" => BuiltInMethod.CreateV2("format", 1, (_, _, args) =>
            {
                double num = Interp.ToNumber(args.Length > 0 ? args[0].ToObject() : null);
                return RuntimeValue.FromBoxed(FormatNumber(num));
            }),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object Intl.NumberFormat]";
}
