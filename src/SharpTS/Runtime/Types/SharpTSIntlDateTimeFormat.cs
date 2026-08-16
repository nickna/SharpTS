using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Intl.DateTimeFormat.
/// Provides locale-aware date/time formatting using .NET's CultureInfo/DateTimeFormatInfo.
/// Supports BCP 47 Unicode extensions (-u-ca-, -u-nu-, -u-hc-), calendar systems,
/// numbering systems, formatToParts, formatRange, and formatRangeToParts.
/// </summary>
public class SharpTSIntlDateTimeFormat : SharpTSIntlFormatterBase
{
    private string? _dateStyle;
    private string? _timeStyle;
    private string? _year;
    private string? _month;
    private string? _day;
    private string? _weekday;
    private string? _hour;
    private string? _minute;
    private string? _second;
    private bool? _hour12;
    private string? _timeZone;
    private string? _timeZoneName;
    private string? _era;
    private int? _fractionalSecondDigits;
    private string _calendar;
    private string _numberingSystem;
    private string? _hourCycle;
    private readonly CultureInfo _culture;
    private readonly DateTimeFormatInfo _dtfi;
    private readonly string _formatString;
    private TimeZoneInfo? _resolvedTimeZone;

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Sentinel markers for narrow weekday/month that can't be represented as .NET format specifiers.
    // These are wrapped in single quotes so DateTime.ToString treats them as literals.
    // The format string uses: 'NWKD' and 'NMTH' (quoted to prevent interpretation as specifiers).
    // After formatting, we replace them with the actual narrow values.
    private const string NarrowWeekdaySentinel = "'NWKD'";
    private const string NarrowWeekdayOutput = "NWKD";
    private const string NarrowMonthSentinel = "'NMTH'";
    private const string NarrowMonthOutput = "NMTH";

    /// <summary>
    /// Numbering system digit tables: maps system name to Unicode code point of digit '0'.
    /// </summary>
    private static readonly Dictionary<string, int> NumberingSystemBaseDigits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arab"] = 0x0660,     // ٠١٢٣٤٥٦٧٨٩
        ["arabext"] = 0x06F0,  // ۰۱۲۳۴۵۶۷۸۹
        ["thai"] = 0x0E50,     // ๐๑๒๓๔๕๖๗๘๙
        ["deva"] = 0x0966,     // ०१२३४५६७८९
        ["beng"] = 0x09E6,     // ০১২৩৪৫৬৭৮৯
        ["guru"] = 0x0A66,     // ੦੧੨੩੪੫੬੭੮੯
        ["tibt"] = 0x0F20,     // ༠༡༢༣༤༥༦༧༨༩
        ["laoo"] = 0x0ED0,     // ໐໑໒໓໔໕໖໗໘໙
        ["mymr"] = 0x1040,     // ၀၁၂၃၄၅၆၇၈၉
    };

    public SharpTSIntlDateTimeFormat(object? locale, object? options)
    {
        // Parse locale with BCP 47 extension support
        string localeStr = locale?.ToString() ?? "";
        var bcp47 = Bcp47Extensions.Parse(localeStr.Replace('_', '-'));
        _culture = ResolveCulture(bcp47.BaseLocale);

        // Defaults from BCP 47 extensions (options override later)
        _calendar = bcp47.Calendar ?? "gregory";
        _numberingSystem = bcp47.NumberingSystem ?? "latn";
        _hourCycle = bcp47.HourCycle;

        // Clone DateTimeFormatInfo so we can customize calendar
        _dtfi = (DateTimeFormatInfo)_culture.DateTimeFormat.Clone();

        // Parse options (may override BCP 47 extensions)
        var opts = NormalizeOptions(options);
        if (opts != null)
            ParseOptions(opts);

        // Apply calendar
        var cal = MapCalendar(_calendar);
        if (cal != null)
        {
            try { _dtfi.Calendar = cal; } catch { /* unsupported calendar for culture */ }
        }

        // Apply hour cycle to _hour12 if not explicitly set
        if (_hour12 == null && _hourCycle != null)
        {
            _hour12 = _hourCycle is "h11" or "h12";
        }

        // Resolve timezone
        if (_timeZone != null)
        {
            try
            {
                if (_timeZone.Equals("UTC", StringComparison.OrdinalIgnoreCase))
                {
                    _resolvedTimeZone = TimeZoneInfo.Utc;
                }
                else
                {
                    var resolved = IanaTimeZoneAliases.Resolve(_timeZone);
                    _resolvedTimeZone = TimeZoneInfo.FindSystemTimeZoneById(resolved);
                }
            }
            catch
            {
                // Invalid timezone, will use local
            }
        }

        // Build format string from options
        _formatString = BuildFormatString();
    }

    private void ParseOptions(IEnumerable<KeyValuePair<string, object?>> opts)
    {
        var dict = opts is IDictionary<string, object?> d
            ? d
            : opts.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (dict.TryGetValue("dateStyle", out var ds) && ds is string dateStyle)
            _dateStyle = dateStyle;

        if (dict.TryGetValue("timeStyle", out var ts) && ts is string timeStyle)
            _timeStyle = timeStyle;

        if (dict.TryGetValue("year", out var y) && y is string year)
            _year = year;

        if (dict.TryGetValue("month", out var m) && m is string month)
            _month = month;

        if (dict.TryGetValue("day", out var dy) && dy is string day)
            _day = day;

        if (dict.TryGetValue("weekday", out var wd) && wd is string weekday)
            _weekday = weekday;

        if (dict.TryGetValue("hour", out var h) && h is string hour)
            _hour = hour;

        if (dict.TryGetValue("minute", out var mi) && mi is string minute)
            _minute = minute;

        if (dict.TryGetValue("second", out var s) && s is string second)
            _second = second;

        if (dict.TryGetValue("hour12", out var h12))
        {
            if (h12 is bool b)
                _hour12 = b;
            else if (h12 is string h12s)
                _hour12 = h12s != "false";
        }

        if (dict.TryGetValue("timeZone", out var tz) && tz is string timeZone)
            _timeZone = timeZone;

        if (dict.TryGetValue("timeZoneName", out var tzn) && tzn is string timeZoneName)
            _timeZoneName = timeZoneName;

        if (dict.TryGetValue("era", out var e) && e is string era)
            _era = era;

        if (dict.TryGetValue("fractionalSecondDigits", out var fsd))
        {
            _fractionalSecondDigits = fsd switch
            {
                double dv => (int)dv,
                int iv => iv,
                _ => null
            };
        }

        // Options override BCP 47 extensions
        if (dict.TryGetValue("calendar", out var calOpt) && calOpt is string calStr)
            _calendar = calStr;

        if (dict.TryGetValue("numberingSystem", out var nuOpt) && nuOpt is string nuStr)
            _numberingSystem = nuStr;

        if (dict.TryGetValue("hourCycle", out var hcOpt) && hcOpt is string hcStr)
            _hourCycle = hcStr;
    }

    private static Calendar? MapCalendar(string name) => name switch
    {
        "gregory" => new GregorianCalendar(),
        "japanese" => new JapaneseCalendar(),
        "buddhist" => new ThaiBuddhistCalendar(),
        "hebrew" => new HebrewCalendar(),
        "islamic" => new HijriCalendar(),
        "islamic-umalqura" => new UmAlQuraCalendar(),
        "persian" => new PersianCalendar(),
        "roc" => new TaiwanCalendar(),
        _ => null
    };

    private string BuildFormatString()
    {
        // If dateStyle/timeStyle are specified, use style-based format
        if (_dateStyle != null || _timeStyle != null)
        {
            return BuildStyleFormat();
        }

        // If individual components are specified, build a custom format
        if (_year != null || _month != null || _day != null || _weekday != null ||
            _hour != null || _minute != null || _second != null)
        {
            return BuildComponentFormat();
        }

        // Default: date and time in medium style
        return "G";
    }

    private string BuildStyleFormat()
    {
        string? datePattern = null;
        if (_dateStyle != null)
        {
            datePattern = _dateStyle switch
            {
                "full" => BuildFullDatePattern(),
                "long" => BuildLongDatePattern(),
                "medium" => BuildMediumDatePattern(),
                "short" => _dtfi.ShortDatePattern,
                _ => null
            };
        }

        string? timePattern = null;
        if (_timeStyle != null)
        {
            timePattern = _timeStyle switch
            {
                "full" => _dtfi.LongTimePattern,
                "long" => _dtfi.LongTimePattern,
                "medium" => _dtfi.LongTimePattern,
                "short" => _dtfi.ShortTimePattern,
                _ => null
            };
        }

        if (datePattern != null && timePattern != null)
            return datePattern + " " + timePattern;

        if (datePattern != null) return datePattern;
        if (timePattern != null) return timePattern;

        return "G";
    }

    /// <summary>
    /// Full date: includes weekday. E.g. "Monday, January 15, 2024"
    /// </summary>
    private string BuildFullDatePattern()
    {
        var longDate = _dtfi.LongDatePattern;
        // If the long date pattern already contains weekday (dddd), use it
        if (longDate.Contains("dddd"))
            return longDate;
        // Prepend weekday
        return "dddd, " + longDate;
    }

    /// <summary>
    /// Long date: no weekday. E.g. "January 15, 2024"
    /// </summary>
    private string BuildLongDatePattern()
    {
        var longDate = _dtfi.LongDatePattern;
        // Remove weekday parts (dddd followed by optional comma/space)
        longDate = Regex.Replace(longDate, @"dddd,?\s*", "");
        return longDate.Trim();
    }

    /// <summary>
    /// Medium date: abbreviated month. E.g. "Jan 15, 2024"
    /// </summary>
    private string BuildMediumDatePattern()
    {
        // Build from long date but use abbreviated month
        var longDate = _dtfi.LongDatePattern;
        // Remove weekday
        longDate = Regex.Replace(longDate, @"dddd,?\s*", "");
        // Replace full month with abbreviated
        longDate = longDate.Replace("MMMM", "MMM");
        return longDate.Trim();
    }

    private string BuildComponentFormat()
    {
        var parts = new List<string>();

        // Weekday
        if (_weekday != null)
        {
            parts.Add(_weekday switch
            {
                "long" => "dddd",
                "short" => "ddd",
                "narrow" => NarrowWeekdaySentinel,
                _ => "ddd"
            });
        }

        // Era (approximate)
        if (_era != null)
        {
            parts.Add("g");
        }

        // Year
        if (_year != null)
        {
            parts.Add(_year switch
            {
                "2-digit" => "yy",
                _ => "yyyy" // "numeric"
            });
        }

        // Month
        if (_month != null)
        {
            parts.Add(_month switch
            {
                "2-digit" => "MM",
                "long" => "MMMM",
                "short" => "MMM",
                "narrow" => NarrowMonthSentinel,
                _ => "M" // "numeric"
            });
        }

        // Day
        if (_day != null)
        {
            parts.Add(_day switch
            {
                "2-digit" => "dd",
                _ => "d" // "numeric"
            });
        }

        // Build date portion
        string datePortion = "";
        if (parts.Count > 0)
        {
            datePortion = string.Join(" ", parts);
        }

        // Time portion
        var timeParts = new List<string>();

        if (_hour != null)
        {
            bool use12 = _hour12 ?? false;
            timeParts.Add(_hour switch
            {
                "2-digit" when use12 => "hh",
                "2-digit" => "HH",
                _ when use12 => "h",
                _ => "H" // "numeric"
            });
        }

        if (_minute != null)
        {
            timeParts.Add(_minute switch
            {
                "2-digit" => "mm",
                _ => "m" // "numeric"
            });
        }

        if (_second != null)
        {
            string secFmt = _second switch
            {
                "2-digit" => "ss",
                _ => "s" // "numeric"
            };

            if (_fractionalSecondDigits.HasValue && _fractionalSecondDigits.Value > 0)
            {
                secFmt += "." + new string('f', Math.Min(_fractionalSecondDigits.Value, 3));
            }

            timeParts.Add(secFmt);
        }

        string timePortion = "";
        if (timeParts.Count > 0)
        {
            timePortion = string.Join(":", timeParts);
            if (_hour12 == true)
            {
                timePortion += " tt";
            }
        }

        // Combine
        if (!string.IsNullOrEmpty(datePortion) && !string.IsNullOrEmpty(timePortion))
            return datePortion + " " + timePortion;
        if (!string.IsNullOrEmpty(datePortion))
            return datePortion;
        if (!string.IsNullOrEmpty(timePortion))
            return timePortion;

        return "G";
    }

    /// <summary>
    /// Applies timezone conversion to a DateTime.
    /// </summary>
    private DateTime ApplyTimeZone(DateTime dateTime)
    {
        if (_resolvedTimeZone == null) return dateTime;
        try
        {
            return TimeZoneInfo.ConvertTime(dateTime, _resolvedTimeZone);
        }
        catch
        {
            return dateTime;
        }
    }

    /// <summary>
    /// Formats a DateTime according to the locale and options.
    /// </summary>
    public string FormatDate(DateTime dateTime)
    {
        dateTime = ApplyTimeZone(dateTime);

        string result = dateTime.ToString(_formatString, _dtfi);

        // Replace narrow sentinels (output form, without quotes)
        if (result.Contains(NarrowWeekdayOutput))
        {
            var abbrevDay = _dtfi.GetAbbreviatedDayName(dateTime.DayOfWeek);
            result = result.Replace(NarrowWeekdayOutput, abbrevDay.Length > 0 ? abbrevDay[..1] : "");
        }
        if (result.Contains(NarrowMonthOutput))
        {
            var abbrevMonth = _dtfi.GetAbbreviatedMonthName(dateTime.Month);
            result = result.Replace(NarrowMonthOutput, abbrevMonth.Length > 0 ? abbrevMonth[..1] : "");
        }

        // Apply numbering system digit substitution
        if (_numberingSystem != "latn" && NumberingSystemBaseDigits.TryGetValue(_numberingSystem, out var baseDigit))
        {
            result = SubstituteDigits(result, baseDigit);
        }

        // Append timezone name if requested
        if (_timeZoneName != null)
        {
            string tzDisplay = GetTimeZoneDisplay(dateTime);
            result += " " + tzDisplay;
        }

        return result;
    }

    private static string SubstituteDigits(string input, int baseCodePoint)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch >= '0' && ch <= '9')
                sb.Append(char.ConvertFromUtf32(baseCodePoint + (ch - '0')));
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private string GetTimeZoneDisplay(DateTime dateTime)
    {
        var tz = _resolvedTimeZone ?? TimeZoneInfo.Local;

        if (tz == TimeZoneInfo.Utc || _timeZone?.Equals("UTC", StringComparison.OrdinalIgnoreCase) == true)
        {
            return _timeZoneName switch
            {
                "long" => "Coordinated Universal Time",
                "longGeneric" => "Coordinated Universal Time",
                "longOffset" => "GMT+00:00",
                "shortOffset" => "GMT",
                _ => "UTC"
            };
        }

        var offset = tz.GetUtcOffset(dateTime);
        string gmtOffset = $"GMT{(offset >= TimeSpan.Zero ? "+" : "")}{offset.Hours:D2}:{Math.Abs(offset.Minutes):D2}";

        return _timeZoneName switch
        {
            "long" => tz.IsDaylightSavingTime(dateTime) ? tz.DaylightName : tz.StandardName,
            "longGeneric" =>
                // Strip offset prefix from DisplayName: "(UTC-05:00) Eastern Time..." → "Eastern Time..."
                StripOffsetFromDisplayName(tz.DisplayName),
            "longOffset" => gmtOffset,
            "short" => gmtOffset,
            "shortOffset" => gmtOffset,
            "shortGeneric" => gmtOffset,
            _ => gmtOffset
        };
    }

    private static string StripOffsetFromDisplayName(string displayName)
    {
        // DisplayName format: "(UTC-05:00) Eastern Time (US & Canada)"
        var idx = displayName.IndexOf(')');
        if (idx >= 0 && idx + 1 < displayName.Length)
            return displayName[(idx + 1)..].Trim();
        return displayName;
    }

    /// <summary>
    /// Returns an object describing the computed options of the formatter.
    /// </summary>
    public override Dictionary<string, object?> GetResolvedOptions()
    {
        var result = new Dictionary<string, object?>
        {
            ["locale"] = _locale,
            ["numberingSystem"] = _numberingSystem,
            ["calendar"] = _calendar,
        };

        if (_hourCycle != null) result["hourCycle"] = _hourCycle;
        if (_dateStyle != null) result["dateStyle"] = _dateStyle;
        if (_timeStyle != null) result["timeStyle"] = _timeStyle;
        if (_year != null) result["year"] = _year;
        if (_month != null) result["month"] = _month;
        if (_day != null) result["day"] = _day;
        if (_weekday != null) result["weekday"] = _weekday;
        if (_hour != null) result["hour"] = _hour;
        if (_minute != null) result["minute"] = _minute;
        if (_second != null) result["second"] = _second;
        if (_hour12 != null) result["hour12"] = _hour12.Value;
        if (_timeZone != null) result["timeZone"] = _timeZone;
        if (_timeZoneName != null) result["timeZoneName"] = _timeZoneName;
        if (_era != null) result["era"] = _era;
        if (_fractionalSecondDigits != null) result["fractionalSecondDigits"] = (double)_fractionalSecondDigits.Value;

        return result;
    }

    /// <summary>
    /// Converts a JS date value to DateTime.
    /// Handles DateTime, double (epoch ms), string, SharpTSDate, and emitted $TSDate (via reflection).
    /// </summary>
    internal static DateTime ToDateTime(object? value)
    {
        if (value == null) return DateTime.Now;
        if (value is DateTime dt) return dt;
        if (value is SharpTSDate d) return UnixEpoch.AddMilliseconds(d.GetTime()).ToLocalTime();
        if (value is double ms) return UnixEpoch.AddMilliseconds(ms).ToLocalTime();
        if (value is int i) return UnixEpoch.AddMilliseconds(i).ToLocalTime();
        if (value is long l) return UnixEpoch.AddMilliseconds(l).ToLocalTime();
        if (value is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        // Handle the compiler-emitted $TSDate managed shape.
        var type = value.GetType();
        if (ManagedEmittedShapeReflection.IsShape(type, ManagedEmittedShape.Date))
        {
            var getTimeMethod = ManagedEmittedShapeReflection.GetPublicMethod(
                    type, ManagedEmittedShape.Date, "GetTime", Type.EmptyTypes)
                ?? throw new InvalidOperationException(
                    "Compiler-emitted $TSDate has no GetTime method.");
            var result = getTimeMethod.Invoke(value, null);
            if (result is double epochMs)
                return UnixEpoch.AddMilliseconds(epochMs).ToLocalTime();
        }

        return DateTime.Now;
    }

    /// <summary>
    /// JS-facing format method for compiled mode reflection dispatch.
    /// </summary>
    public object? format(object? date)
    {
        DateTime dt = ToDateTime(date);
        return FormatDate(dt);
    }

    #region formatToParts

    /// <summary>
    /// Formats a date and returns an array of parts with type and value.
    /// Internal name differs from JS name to avoid AmbiguousMatchException in GetFieldsProperty.
    /// </summary>
    public List<Dictionary<string, object?>> FormatDateToParts(DateTime dateTime)
    {
        dateTime = ApplyTimeZone(dateTime);
        var parts = new List<Dictionary<string, object?>>();

        // Walk the format string and decompose into typed parts
        var fmt = _formatString;
        int pos = 0;
        while (pos < fmt.Length)
        {
            // Quoted literal (includes sentinel markers like 'NWKD' and 'NMTH')
            if (fmt[pos] == '\'')
            {
                int end = fmt.IndexOf('\'', pos + 1);
                if (end < 0) end = fmt.Length;
                string literal = fmt[(pos + 1)..end];

                // Check for sentinel markers
                if (literal == NarrowWeekdayOutput)
                {
                    var abbrevDay = _dtfi.GetAbbreviatedDayName(dateTime.DayOfWeek);
                    parts.Add(MakePart("weekday", abbrevDay.Length > 0 ? abbrevDay[..1] : ""));
                }
                else if (literal == NarrowMonthOutput)
                {
                    var abbrevMonth = _dtfi.GetAbbreviatedMonthName(dateTime.Month);
                    parts.Add(MakePart("month", abbrevMonth.Length > 0 ? abbrevMonth[..1] : ""));
                }
                else if (literal.Length > 0)
                {
                    parts.Add(MakePart("literal", literal));
                }

                pos = end + 1;
                continue;
            }

            char c = fmt[pos];
            if (IsFormatSpecChar(c))
            {
                // Consume consecutive same chars
                int start = pos;
                while (pos < fmt.Length && fmt[pos] == c) pos++;
                string spec = fmt[start..pos];
                string type = ClassifySpecifier(spec);
                string value = dateTime.ToString(spec, _dtfi);
                parts.Add(MakePart(type, value));
            }
            else
            {
                // Literal character(s)
                int start = pos;
                while (pos < fmt.Length && !IsFormatSpecChar(fmt[pos]) && fmt[pos] != '\'')
                    pos++;
                parts.Add(MakePart("literal", fmt[start..pos]));
            }
        }

        // Apply numbering system digit substitution to part values
        if (_numberingSystem != "latn" && NumberingSystemBaseDigits.TryGetValue(_numberingSystem, out var baseDigit))
        {
            foreach (var part in parts)
            {
                if (part["value"] is string val)
                    part["value"] = SubstituteDigits(val, baseDigit);
            }
        }

        return parts;
    }

    private static bool IsFormatSpecChar(char c) =>
        c is 'y' or 'M' or 'd' or 'H' or 'h' or 'm' or 's' or 'f' or 'F' or 't' or 'g' or 'K' or 'z';

    private static string ClassifySpecifier(string spec) => spec[0] switch
    {
        'y' => "year",
        'M' => "month",
        'd' when spec.Length >= 3 => "weekday",  // ddd or dddd
        'd' => "day",
        'H' or 'h' => "hour",
        'm' => "minute",
        's' => "second",
        'f' or 'F' => "fractionalSecond",
        't' => "dayPeriod",
        'g' => "era",
        'K' or 'z' => "timeZoneName",
        _ => "literal"
    };

    private static Dictionary<string, object?> MakePart(string type, string value) =>
        new() { ["type"] = type, ["value"] = value };

    /// <summary>
    /// JS-facing formatToParts method for compiled mode reflection dispatch.
    /// Returns List&lt;object?&gt; containing Dictionary&lt;string,object?&gt; for compiled mode compatibility.
    /// </summary>
    public object? formatToParts(object? date)
    {
        DateTime dt = ToDateTime(date);
        var parts = FormatDateToParts(dt);
        var items = new List<object?>();
        foreach (var p in parts)
            items.Add((object?)p);
        return items;
    }

    #endregion

    #region formatRange / formatRangeToParts

    /// <summary>
    /// Formats a date range. Internal name differs to avoid AmbiguousMatchException.
    /// </summary>
    public string FormatDateRange(DateTime start, DateTime end)
    {
        start = ApplyTimeZone(start);
        end = ApplyTimeZone(end);

        // If same date/time, return single format
        if (start == end)
            return FormatDate(start);

        string startStr = FormatSingle(start);
        string endStr = FormatSingle(end);

        // Find common prefix/suffix and build range
        return startStr + " \u2013 " + endStr;
    }

    /// <summary>
    /// Formats a date range and returns typed parts with source attribution.
    /// Internal name differs to avoid AmbiguousMatchException.
    /// </summary>
    public List<Dictionary<string, object?>> FormatDateRangeToParts(DateTime start, DateTime end)
    {
        start = ApplyTimeZone(start);
        end = ApplyTimeZone(end);

        var result = new List<Dictionary<string, object?>>();

        if (start == end)
        {
            // Same date — all parts are "shared"
            var parts = FormatDateToParts(start);
            foreach (var p in parts)
            {
                p["source"] = "shared";
                result.Add(p);
            }
            return result;
        }

        // Get parts for both dates
        var startParts = FormatDateToParts(start);
        var endParts = FormatDateToParts(end);

        // Mark start parts
        foreach (var p in startParts)
        {
            p["source"] = "startRange";
            result.Add(p);
        }

        // Add range separator
        result.Add(new Dictionary<string, object?>
        {
            ["type"] = "literal",
            ["value"] = " \u2013 ",
            ["source"] = "shared"
        });

        // Mark end parts
        foreach (var p in endParts)
        {
            p["source"] = "endRange";
            result.Add(p);
        }

        return result;
    }

    /// <summary>
    /// Format a single date without timezone name suffix (for range composition).
    /// </summary>
    private string FormatSingle(DateTime dateTime)
    {
        string result = dateTime.ToString(_formatString, _dtfi);

        if (result.Contains(NarrowWeekdaySentinel))
        {
            var abbrevDay = _dtfi.GetAbbreviatedDayName(dateTime.DayOfWeek);
            result = result.Replace(NarrowWeekdaySentinel, abbrevDay.Length > 0 ? abbrevDay[..1] : "");
        }
        if (result.Contains(NarrowMonthSentinel))
        {
            var abbrevMonth = _dtfi.GetAbbreviatedMonthName(dateTime.Month);
            result = result.Replace(NarrowMonthSentinel, abbrevMonth.Length > 0 ? abbrevMonth[..1] : "");
        }

        if (_numberingSystem != "latn" && NumberingSystemBaseDigits.TryGetValue(_numberingSystem, out var baseDigit))
        {
            result = SubstituteDigits(result, baseDigit);
        }

        return result;
    }

    /// <summary>
    /// JS-facing formatRange method for compiled mode reflection dispatch.
    /// </summary>
    public object? formatRange(object? start, object? end)
    {
        DateTime startDt = ToDateTime(start);
        DateTime endDt = ToDateTime(end);
        return FormatDateRange(startDt, endDt);
    }

    /// <summary>
    /// JS-facing formatRangeToParts method for compiled mode reflection dispatch.
    /// Returns List&lt;object?&gt; containing Dictionary&lt;string,object?&gt; for compiled mode compatibility.
    /// </summary>
    public object? formatRangeToParts(object? start, object? end)
    {
        DateTime startDt = ToDateTime(start);
        DateTime endDt = ToDateTime(end);
        var parts = FormatDateRangeToParts(startDt, endDt);
        var items = new List<object?>();
        foreach (var p in parts)
            items.Add((object?)p);
        return items;
    }

    #endregion

    /// <summary>
    /// Gets a member (method) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "format" => BuiltInMethod.CreateV2("format", 1, (_, _, args) =>
            {
                DateTime dt = ToDateTime(args.Length > 0 ? args[0].ToObject() : null);
                return RuntimeValue.FromBoxed(FormatDate(dt));
            }),
            "formatToParts" => BuiltInMethod.CreateV2("formatToParts", 1, (_, _, args) =>
            {
                DateTime dt = ToDateTime(args.Length > 0 ? args[0].ToObject() : null);
                var parts = FormatDateToParts(dt);
                var items = new List<object?>();
                foreach (var p in parts)
                    items.Add(new SharpTSObject(p));
                return RuntimeValue.FromObject(new SharpTSArray(items));
            }),
            "formatRange" => BuiltInMethod.CreateV2("formatRange", 2, (_, _, args) =>
            {
                DateTime startDt = ToDateTime(args.Length > 0 ? args[0].ToObject() : null);
                DateTime endDt = ToDateTime(args.Length > 1 ? args[1].ToObject() : null);
                return RuntimeValue.FromBoxed(FormatDateRange(startDt, endDt));
            }),
            "formatRangeToParts" => BuiltInMethod.CreateV2("formatRangeToParts", 2, (_, _, args) =>
            {
                DateTime startDt = ToDateTime(args.Length > 0 ? args[0].ToObject() : null);
                DateTime endDt = ToDateTime(args.Length > 1 ? args[1].ToObject() : null);
                var parts = FormatDateRangeToParts(startDt, endDt);
                var items = new List<object?>();
                foreach (var p in parts)
                    items.Add(new SharpTSObject(p));
                return RuntimeValue.FromObject(new SharpTSArray(items));
            }),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object Intl.DateTimeFormat]";
}
