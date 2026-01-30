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
    /// Returns the primitive value (timestamp) of the date.
    /// </summary>
    public double ValueOf()
    {
        return GetTime();
    }

    /// <summary>
    /// Static method: Returns current timestamp in milliseconds since Unix epoch.
    /// </summary>
    public static double Now()
    {
        return (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
    }
}
