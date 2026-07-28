using System.Globalization;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript Date objects.
/// </summary>
/// <remarks>
/// Stores time internally as UTC DateTime, converts to local time for getters/setters.
/// Follows JavaScript Date semantics including 0-indexed months and mutable setters.
/// </remarks>
public class SharpTSDate : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Date;

    private DateTime _utcDateTime;
    private bool _isInvalid;
    private Dictionary<string, object?>? _extras;

    public bool HasExtra(string name) => _extras is not null && _extras.ContainsKey(name);
    public object? TryGetExtra(string name) =>
        _extras is not null && _extras.TryGetValue(name, out var value) ? value : null;
    public void SetExtra(string name, object? value)
    {
        _extras ??= new Dictionary<string, object?>();
        _extras[name] = value;
    }

    /// <summary>Unix epoch (January 1, 1970 00:00:00 UTC)</summary>
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Creates a Date with the current date and time.
    /// </summary>
    public SharpTSDate()
    {
        _utcDateTime = DateTime.UtcNow;
        _isInvalid = false;
    }

    /// <summary>
    /// Creates a Date from milliseconds since Unix epoch.
    /// </summary>
    public SharpTSDate(double milliseconds)
    {
        SetFromEpochMilliseconds(milliseconds);
    }

    /// <summary>
    /// Creates a Date by parsing an ISO 8601 string.
    /// </summary>
    public SharpTSDate(string isoString)
    {
        ParseIsoString(isoString);
    }

    /// <summary>
    /// Creates a Date from component values (year, month, etc.).
    /// </summary>
    /// <remarks>
    /// Month is 0-indexed (0 = January, 11 = December) per JavaScript semantics.
    /// Years 0-99 are mapped to 1900-1999 per JavaScript Date constructor behavior.
    /// </remarks>
    public SharpTSDate(int year, int month, int day = 1, int hours = 0,
                       int minutes = 0, int seconds = 0, int milliseconds = 0)
    {
        SetFromComponents(year, month, day, hours, minutes, seconds, milliseconds);
    }

    private void SetFromEpochMilliseconds(double milliseconds)
    {
        if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
        {
            _isInvalid = true;
            return;
        }

        try
        {
            // JavaScript Date range: approximately -8,640,000,000,000,000 to 8,640,000,000,000,000 ms
            const double MaxMs = 8640000000000000;
            if (milliseconds < -MaxMs || milliseconds > MaxMs)
            {
                _isInvalid = true;
                return;
            }

            _utcDateTime = UnixEpoch.AddMilliseconds(milliseconds);
            _isInvalid = false;
        }
        catch
        {
            _isInvalid = true;
        }
    }

    private void ParseIsoString(string isoString)
    {
        if (string.IsNullOrWhiteSpace(isoString))
        {
            _isInvalid = true;
            return;
        }

        try
        {
            // Try strict ISO 8601 formats first
            string[] isoFormats =
            [
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-dd",
                "yyyy-MM-ddTHH:mm:ss.fffzzz",
                "yyyy-MM-ddTHH:mm:sszzz",
            ];

            if (DateTime.TryParseExact(isoString, isoFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var result))
            {
                // Convert to UTC if not already
                _utcDateTime = result.Kind == DateTimeKind.Utc
                    ? result
                    : result.ToUniversalTime();
                _isInvalid = false;
            }
            else
            {
                _isInvalid = true;
            }
        }
        catch
        {
            _isInvalid = true;
        }
    }

    private void SetFromComponents(int year, int month, int day, int hours,
                                   int minutes, int seconds, int milliseconds)
    {
        try
        {
            // JavaScript quirk: 2-digit years (0-99) map to 1900-1999
            if (year >= 0 && year <= 99)
            {
                year += 1900;
            }

            // JavaScript month is 0-indexed, .NET is 1-indexed
            int netMonth = month + 1;

            // Handle overflow: JavaScript allows month > 11, day > daysInMonth, etc.
            // We build from a base date and add the overflows
            var baseDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Local);
            var localDateTime = baseDate
                .AddMonths(month)  // month is already 0-indexed
                .AddDays(day - 1)  // day is 1-indexed in JavaScript
                .AddHours(hours)
                .AddMinutes(minutes)
                .AddSeconds(seconds)
                .AddMilliseconds(milliseconds);

            _utcDateTime = localDateTime.ToUniversalTime();
            _isInvalid = false;
        }
        catch
        {
            _isInvalid = true;
        }
    }

    // ========== Getter Methods ==========

    /// <summary>
    /// Returns the numeric value of the date as milliseconds since Unix epoch.
    /// Returns NaN for invalid dates.
    /// </summary>
    public double GetTime()
    {
        if (_isInvalid) return double.NaN;
        return (_utcDateTime - UnixEpoch).TotalMilliseconds;
    }

    /// <summary>Returns the 4-digit year in local time.</summary>
    public double GetFullYear()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Year;
    }

    /// <summary>Returns the month (0-11) in local time. 0 = January, 11 = December.</summary>
    public double GetMonth()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Month - 1; // Convert to 0-indexed
    }

    /// <summary>Returns the day of the month (1-31) in local time.</summary>
    public double GetDate()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Day;
    }

    /// <summary>Returns the day of the week (0-6) in local time. 0 = Sunday, 6 = Saturday.</summary>
    public double GetDay()
    {
        if (_isInvalid) return double.NaN;
        return (double)_utcDateTime.ToLocalTime().DayOfWeek;
    }

    /// <summary>Returns the hour (0-23) in local time.</summary>
    public double GetHours()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Hour;
    }

    /// <summary>Returns the minutes (0-59) in local time.</summary>
    public double GetMinutes()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Minute;
    }

    /// <summary>Returns the seconds (0-59) in local time.</summary>
    public double GetSeconds()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Second;
    }

    /// <summary>Returns the milliseconds (0-999) in local time.</summary>
    public double GetMilliseconds()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Millisecond;
    }

    /// <summary>
    /// Returns the timezone offset in minutes between UTC and local time.
    /// Positive values indicate time zones west of UTC.
    /// </summary>
    public double GetTimezoneOffset()
    {
        if (_isInvalid) return double.NaN;
        // JavaScript returns offset in minutes, positive for west of UTC
        return -TimeZoneInfo.Local.GetUtcOffset(_utcDateTime).TotalMinutes;
    }

    // ========== UTC Getter Methods ==========
    // These read the stored UTC instant directly, with no local-time conversion.

    /// <summary>Returns the 4-digit year in UTC.</summary>
    public double GetUTCFullYear()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Year;
    }

    /// <summary>Returns the month (0-11) in UTC. 0 = January, 11 = December.</summary>
    public double GetUTCMonth()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Month - 1; // Convert to 0-indexed
    }

    /// <summary>Returns the day of the month (1-31) in UTC.</summary>
    public double GetUTCDate()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Day;
    }

    /// <summary>Returns the day of the week (0-6) in UTC. 0 = Sunday, 6 = Saturday.</summary>
    public double GetUTCDay()
    {
        if (_isInvalid) return double.NaN;
        return (double)_utcDateTime.DayOfWeek;
    }

    /// <summary>Returns the hour (0-23) in UTC.</summary>
    public double GetUTCHours()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Hour;
    }

    /// <summary>Returns the minutes (0-59) in UTC.</summary>
    public double GetUTCMinutes()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Minute;
    }

    /// <summary>Returns the seconds (0-59) in UTC.</summary>
    public double GetUTCSeconds()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Second;
    }

    /// <summary>Returns the milliseconds (0-999) in UTC.</summary>
    public double GetUTCMilliseconds()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.Millisecond;
    }

    // ========== Setter Methods ==========
    // All setters mutate the date and return the new timestamp

    /// <summary>
    /// Sets the date from milliseconds since epoch.
    /// Returns the new timestamp.
    /// </summary>
    public double SetTime(double time)
    {
        SetFromEpochMilliseconds(time);
        return GetTime();
    }

    /// <summary>
    /// Sets the full year, optionally also month and day.
    /// Returns the new timestamp.
    /// </summary>
    public double SetFullYear(double year, double? month = null, double? date = null)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();
        int newYear = (int)year;
        int newMonth = month.HasValue ? (int)month.Value + 1 : local.Month; // Convert 0-indexed to 1-indexed
        int newDay = date.HasValue ? (int)date.Value : local.Day;

        try
        {
            var newLocal = new DateTime(newYear, 1, 1, local.Hour, local.Minute, local.Second, local.Millisecond, DateTimeKind.Local)
                .AddMonths(newMonth - 1)
                .AddDays(newDay - 1);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the month (0-indexed), optionally also day.
    /// Returns the new timestamp.
    /// </summary>
    public double SetMonth(double month, double? date = null)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();
        int newDay = date.HasValue ? (int)date.Value : local.Day;

        try
        {
            var newLocal = new DateTime(local.Year, 1, 1, local.Hour, local.Minute, local.Second, local.Millisecond, DateTimeKind.Local)
                .AddMonths((int)month)
                .AddDays(newDay - 1);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the day of the month.
    /// Returns the new timestamp.
    /// </summary>
    public double SetDate(double date)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();

        try
        {
            var newLocal = new DateTime(local.Year, local.Month, 1, local.Hour, local.Minute, local.Second, local.Millisecond, DateTimeKind.Local)
                .AddDays((int)date - 1);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the hour, optionally also minutes, seconds, and milliseconds.
    /// Returns the new timestamp.
    /// </summary>
    public double SetHours(double hours, double? min = null, double? sec = null, double? ms = null)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();
        int newHours = (int)hours;
        int newMin = min.HasValue ? (int)min.Value : local.Minute;
        int newSec = sec.HasValue ? (int)sec.Value : local.Second;
        int newMs = ms.HasValue ? (int)ms.Value : local.Millisecond;

        try
        {
            var newLocal = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, 0, DateTimeKind.Local)
                .AddHours(newHours)
                .AddMinutes(newMin)
                .AddSeconds(newSec)
                .AddMilliseconds(newMs);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the minutes, optionally also seconds and milliseconds.
    /// Returns the new timestamp.
    /// </summary>
    public double SetMinutes(double min, double? sec = null, double? ms = null)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();
        int newMin = (int)min;
        int newSec = sec.HasValue ? (int)sec.Value : local.Second;
        int newMs = ms.HasValue ? (int)ms.Value : local.Millisecond;

        try
        {
            var newLocal = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, 0, DateTimeKind.Local)
                .AddMinutes(newMin)
                .AddSeconds(newSec)
                .AddMilliseconds(newMs);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the seconds, optionally also milliseconds.
    /// Returns the new timestamp.
    /// </summary>
    public double SetSeconds(double sec, double? ms = null)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();
        int newSec = (int)sec;
        int newMs = ms.HasValue ? (int)ms.Value : local.Millisecond;

        try
        {
            var newLocal = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, 0, DateTimeKind.Local)
                .AddSeconds(newSec)
                .AddMilliseconds(newMs);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the milliseconds.
    /// Returns the new timestamp.
    /// </summary>
    public double SetMilliseconds(double ms)
    {
        if (_isInvalid) return double.NaN;

        var local = _utcDateTime.ToLocalTime();

        try
        {
            var newLocal = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, 0, DateTimeKind.Local)
                .AddMilliseconds((int)ms);
            _utcDateTime = newLocal.ToUniversalTime();
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    // ========== UTC Setter Methods ==========
    // These build the new instant directly in UTC; no local-time round-trip.
    // Overflowing components roll over (e.g. setUTCMonth(13) advances the year),
    // matching JavaScript semantics via DateTime's Add* methods.

    /// <summary>
    /// Sets the UTC full year, optionally also month and day. Returns the new timestamp.
    /// </summary>
    public double SetUTCFullYear(double year, double? month = null, double? date = null)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;
        int newYear = (int)year;
        int newMonth = month.HasValue ? (int)month.Value + 1 : utc.Month; // Convert 0-indexed to 1-indexed
        int newDay = date.HasValue ? (int)date.Value : utc.Day;

        try
        {
            _utcDateTime = new DateTime(newYear, 1, 1, utc.Hour, utc.Minute, utc.Second, utc.Millisecond, DateTimeKind.Utc)
                .AddMonths(newMonth - 1)
                .AddDays(newDay - 1);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the UTC month (0-indexed), optionally also day. Returns the new timestamp.
    /// </summary>
    public double SetUTCMonth(double month, double? date = null)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;
        int newDay = date.HasValue ? (int)date.Value : utc.Day;

        try
        {
            _utcDateTime = new DateTime(utc.Year, 1, 1, utc.Hour, utc.Minute, utc.Second, utc.Millisecond, DateTimeKind.Utc)
                .AddMonths((int)month)
                .AddDays(newDay - 1);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the UTC day of the month. Returns the new timestamp.
    /// </summary>
    public double SetUTCDate(double date)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;

        try
        {
            _utcDateTime = new DateTime(utc.Year, utc.Month, 1, utc.Hour, utc.Minute, utc.Second, utc.Millisecond, DateTimeKind.Utc)
                .AddDays((int)date - 1);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the UTC hour, optionally also minutes, seconds, and milliseconds. Returns the new timestamp.
    /// </summary>
    public double SetUTCHours(double hours, double? min = null, double? sec = null, double? ms = null)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;
        int newHours = (int)hours;
        int newMin = min.HasValue ? (int)min.Value : utc.Minute;
        int newSec = sec.HasValue ? (int)sec.Value : utc.Second;
        int newMs = ms.HasValue ? (int)ms.Value : utc.Millisecond;

        try
        {
            _utcDateTime = new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, 0, DateTimeKind.Utc)
                .AddHours(newHours)
                .AddMinutes(newMin)
                .AddSeconds(newSec)
                .AddMilliseconds(newMs);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the UTC minutes, optionally also seconds and milliseconds. Returns the new timestamp.
    /// </summary>
    public double SetUTCMinutes(double min, double? sec = null, double? ms = null)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;
        int newMin = (int)min;
        int newSec = sec.HasValue ? (int)sec.Value : utc.Second;
        int newMs = ms.HasValue ? (int)ms.Value : utc.Millisecond;

        try
        {
            _utcDateTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, 0, DateTimeKind.Utc)
                .AddMinutes(newMin)
                .AddSeconds(newSec)
                .AddMilliseconds(newMs);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the UTC seconds, optionally also milliseconds. Returns the new timestamp.
    /// </summary>
    public double SetUTCSeconds(double sec, double? ms = null)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;
        int newSec = (int)sec;
        int newMs = ms.HasValue ? (int)ms.Value : utc.Millisecond;

        try
        {
            _utcDateTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, 0, DateTimeKind.Utc)
                .AddSeconds(newSec)
                .AddMilliseconds(newMs);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    /// <summary>
    /// Sets the UTC milliseconds. Returns the new timestamp.
    /// </summary>
    public double SetUTCMilliseconds(double ms)
    {
        if (_isInvalid) return double.NaN;

        var utc = _utcDateTime;

        try
        {
            _utcDateTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, 0, DateTimeKind.Utc)
                .AddMilliseconds((int)ms);
        }
        catch
        {
            _isInvalid = true;
        }

        return GetTime();
    }

    // ========== Conversion Methods ==========

    /// <summary>
    /// Returns a string representation of the date in local time.
    /// </summary>
    public override string ToString()
    {
        if (_isInvalid) return "Invalid Date";

        var local = _utcDateTime.ToLocalTime();
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();

        // Format: "Thu Jan 01 1970 00:00:00 GMT+0000"
        return local.ToString("ddd MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            + $" GMT{sign}{absOffset.Hours:D2}{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Returns the date in ISO 8601 format (UTC).
    /// </summary>
    public string ToISOString()
    {
        if (_isInvalid)
            throw new Exception("Runtime Error: Invalid Date");

        return _utcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns only the date portion as a string.
    /// </summary>
    public string ToDateString()
    {
        if (_isInvalid) return "Invalid Date";
        return _utcDateTime.ToLocalTime().ToString("ddd MMM dd yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns only the time portion as a string.
    /// </summary>
    public string ToTimeString()
    {
        if (_isInvalid) return "Invalid Date";

        var local = _utcDateTime.ToLocalTime();
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();

        return local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            + $" GMT{sign}{absOffset.Hours:D2}{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Returns the date as a UTC string in RFC 7231 format, e.g. "Thu, 01 Jan 1970 00:00:00 GMT".
    /// </summary>
    public string ToUTCString()
    {
        if (_isInvalid) return "Invalid Date";
        return _utcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);
    }

    // The toLocale* family formats in local time using the host's current culture.
    // Locale/options arguments (lib.es2020.date) are accepted by the type checker but
    // not yet honored at runtime — full Intl-options support would route through
    // SharpTSIntlDateTimeFormat (see #539). Output is implementation-defined per
    // ECMA-262, so callers should not depend on the exact format.

    /// <summary>Returns the date portion as a locale-formatted string in local time.</summary>
    public string ToLocaleDateString()
    {
        if (_isInvalid) return "Invalid Date";
        return _utcDateTime.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>Returns the time portion as a locale-formatted string in local time.</summary>
    public string ToLocaleTimeString()
    {
        if (_isInvalid) return "Invalid Date";
        return _utcDateTime.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
    }

    /// <summary>Returns the full date and time as a locale-formatted string in local time.</summary>
    public string ToLocaleString()
    {
        if (_isInvalid) return "Invalid Date";
        return _utcDateTime.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);
    }

    /// <summary>Default component set for <see cref="FormatToLocale"/> (which toLocale* method called it).</summary>
    public const int LocaleKindDate = 0;
    public const int LocaleKindTime = 1;
    public const int LocaleKindDateTime = 2;

    // Date/time formatting keys (Intl.DateTimeFormat options) whose presence suppresses the
    // ToDateTimeOptions defaults. Calendar/numberingSystem/timeZone/hour12/hourCycle don't count.
    private static readonly HashSet<string> LocaleFormatComponentKeys =
    [
        "dateStyle", "timeStyle", "weekday", "year", "month", "day",
        "hour", "minute", "second", "fractionalSecondDigits", "dayPeriod", "era"
    ];

    /// <summary>
    /// Formats this instant for one of the toLocale* methods (#539), honoring the BCP 47
    /// <paramref name="locale"/> and Intl.DateTimeFormat <paramref name="options"/>. When the caller
    /// supplies explicit date/time components or a dateStyle/timeStyle, formatting routes through
    /// <see cref="SharpTSIntlDateTimeFormat"/>; otherwise the instant is formatted with the requested
    /// locale's culture using this method's default pattern (<paramref name="kind"/>: date / time /
    /// both — a pragmatic form of ECMA-402 ToDateTimeOptions). Static so the compiled standalone path
    /// can reach the same logic by reflection (see RuntimeTypes.FormatDateToLocale).
    /// </summary>
    public static string FormatToLocale(double epochMs, int kind, object? locale, object? options)
    {
        if (double.IsNaN(epochMs)) return "Invalid Date";
        // The formatter applies an explicit timeZone option itself; otherwise format in local time.
        var local = UnixEpoch.AddMilliseconds(epochMs).ToLocalTime();
        try
        {
            if (HasFormatComponents(options))
                return new SharpTSIntlDateTimeFormat(locale, options).FormatDate(local);

            // No explicit components: use the requested locale's culture (SharpTSIntlDateTimeFormat's
            // component formatter does not reorder y/m/d to locale convention, so the culture's own
            // standard pattern gives the better default).
            var pattern = kind switch
            {
                LocaleKindTime => "T",
                LocaleKindDateTime => "G",
                _ => "d",
            };
            return local.ToString(pattern, ResolveCulture(locale));
        }
        catch
        {
            return "Invalid Date";
        }
    }

    /// <summary>True if the options bag requests an explicit date/time component or style.</summary>
    private static bool HasFormatComponents(object? options)
    {
        IEnumerable<KeyValuePair<string, object?>>? entries = options switch
        {
            SharpTSObject obj => obj.Fields,
            IDictionary<string, object?> dict => dict,
            _ => null
        };
        if (entries == null) return false;
        foreach (var kv in entries)
            if (LocaleFormatComponentKeys.Contains(kv.Key)) return true;
        return false;
    }

    /// <summary>Resolves a BCP 47 locale (extensions stripped) to a CultureInfo, falling back gracefully.</summary>
    private static CultureInfo ResolveCulture(object? locale)
    {
        var s = locale?.ToString();
        if (string.IsNullOrWhiteSpace(s)) return CultureInfo.CurrentCulture;
        var baseLocale = Bcp47Extensions.Parse(s.Replace('_', '-')).BaseLocale;
        if (string.IsNullOrEmpty(baseLocale)) return CultureInfo.CurrentCulture;
        try { return CultureInfo.GetCultureInfo(baseLocale); }
        catch { return CultureInfo.InvariantCulture; }
    }

    /// <summary>
    /// Returns the primitive value (timestamp) of the date.
    /// </summary>
    public double ValueOf()
    {
        return GetTime();
    }

    // ========== Legacy Methods (ECMA-262 Annex B) ==========

    /// <summary>
    /// ECMA-262 Annex B B.2.4.1: returns the local-time year minus 1900.
    /// </summary>
    public double GetYear()
    {
        if (_isInvalid) return double.NaN;
        return _utcDateTime.ToLocalTime().Year - 1900;
    }

    /// <summary>
    /// ECMA-262 Annex B B.2.4.2: sets the local-time year, mapping 0-99 to 1900-1999.
    /// Returns the new timestamp.
    /// </summary>
    public double SetYear(double year)
    {
        if (_isInvalid) return double.NaN;

        int y = (int)year;
        if (y >= 0 && y <= 99) y += 1900;
        return SetFullYear(y);
    }

    /// <summary>
    /// Static method: Returns current timestamp in milliseconds since Unix epoch.
    /// </summary>
    public static double Now()
    {
        return (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
    }

    /// <summary>
    /// ECMA-262 §21.4.3.4 (Date.UTC): interprets the components as a UTC date and returns the
    /// corresponding timestamp in milliseconds since the Unix epoch. Month is 0-indexed; years
    /// 0-99 map to 1900-1999. Absent components default to month 0, date 1, and 0 for the time
    /// parts. Returns NaN if any supplied component is non-finite or the date is out of range.
    /// </summary>
    public static double UTC(double year, double? month = null, double? date = null,
                             double? hours = null, double? minutes = null,
                             double? seconds = null, double? milliseconds = null)
    {
        // A non-finite component (NaN/Infinity) yields NaN, matching ECMA-262 MakeDay/MakeTime;
        // note (int)NaN would otherwise collapse to 0, so the check must precede truncation.
        if (!double.IsFinite(year)) return double.NaN;
        if (month is { } moV && !double.IsFinite(moV)) return double.NaN;
        if (date is { } dV && !double.IsFinite(dV)) return double.NaN;
        if (hours is { } hV && !double.IsFinite(hV)) return double.NaN;
        if (minutes is { } miV && !double.IsFinite(miV)) return double.NaN;
        if (seconds is { } sV && !double.IsFinite(sV)) return double.NaN;
        if (milliseconds is { } msV && !double.IsFinite(msV)) return double.NaN;

        int y = (int)year;
        if (y >= 0 && y <= 99) y += 1900;
        int mo = month.HasValue ? (int)month.Value : 0;
        int d = date.HasValue ? (int)date.Value : 1;
        int h = hours.HasValue ? (int)hours.Value : 0;
        int mi = minutes.HasValue ? (int)minutes.Value : 0;
        int s = seconds.HasValue ? (int)seconds.Value : 0;
        int ms = milliseconds.HasValue ? (int)milliseconds.Value : 0;

        try
        {
            // Build directly in UTC, mirroring SetFromComponents' overflow handling.
            var utc = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(mo)
                .AddDays(d - 1)
                .AddHours(h)
                .AddMinutes(mi)
                .AddSeconds(s)
                .AddMilliseconds(ms);
            return (utc - UnixEpoch).TotalMilliseconds;
        }
        catch
        {
            return double.NaN;
        }
    }

    /// <summary>
    /// ECMA-262 §21.4.3.2 (Date.parse): parses a date string and returns the corresponding
    /// timestamp in milliseconds since the Unix epoch, or NaN if it cannot be parsed. Uses the
    /// same parsing as the string constructor (ISO 8601).
    /// </summary>
    public static double Parse(string s) => new SharpTSDate(s).GetTime();
}
