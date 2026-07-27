using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the Date object. Runs against both interpreter and compiler.
/// </summary>
public class DateTests
{
    #region Constructor Tests

    [Theory, ModeData]
    public void Date_NoArgs_CreatesCurrentDate(ExecutionMode mode)
    {
        // Date with no args should create a date near current time
        var source = @"
            let d = new Date();
            let now = Date.now();
            let diff = now - d.getTime();
            console.log(diff >= 0 && diff < 1000);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Date_Milliseconds_CreatesFromEpoch(ExecutionMode mode)
    {
        // Create date from milliseconds since epoch
        var source = @"
            let d = new Date(0);
            console.log(d.getTime());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Date_Components_CreatesCorrectDate(ExecutionMode mode)
    {
        // Create date from year, month, day
        // Note: month is 0-indexed (0 = January)
        var source = @"
            let d = new Date(2024, 0, 15);
            console.log(d.getFullYear());
            console.log(d.getMonth());
            console.log(d.getDate());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2024\n0\n15\n", output);
    }

    [Theory, ModeData]
    public void Date_Components_WithTime(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(2024, 5, 20, 14, 30, 45, 123);
            console.log(d.getFullYear());
            console.log(d.getMonth());
            console.log(d.getDate());
            console.log(d.getHours());
            console.log(d.getMinutes());
            console.log(d.getSeconds());
            console.log(d.getMilliseconds());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2024\n5\n20\n14\n30\n45\n123\n", output);
    }

    [Theory, ModeData]
    public void Date_ISOString_ParsesCorrectly(ExecutionMode mode)
    {
        var source = @"
            let d = new Date('2024-01-15T10:30:00Z');
            console.log(d.toISOString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2024-01-15T10:30:00.000Z\n", output);
    }

    #endregion

    #region Static Method Tests

    [Theory, ModeData]
    public void Date_Now_ReturnsNumber(ExecutionMode mode)
    {
        var source = @"
            let timestamp = Date.now();
            console.log(typeof timestamp);
            console.log(timestamp > 0);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Date_FunctionCall_ReturnsString(ExecutionMode mode)
    {
        // Date() called without 'new' returns a string
        var source = @"
            let s = Date();
            console.log(typeof s);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    #endregion

    #region Getter Tests

    [Theory, ModeData]
    public void Date_GetMonth_Returns0Indexed(ExecutionMode mode)
    {
        // January is 0, December is 11
        var source = @"
            let jan = new Date(2024, 0, 1);
            let dec = new Date(2024, 11, 1);
            console.log(jan.getMonth());
            console.log(dec.getMonth());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n11\n", output);
    }

    [Theory, ModeData]
    public void Date_GetDay_ReturnsCorrectDayOfWeek(ExecutionMode mode)
    {
        // 2024-01-01 is Monday (day 1), Sunday is 0
        var source = @"
            let d = new Date(2024, 0, 7);
            console.log(d.getDay());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output); // January 7, 2024 is Sunday
    }

    [Theory, ModeData]
    public void Date_GetTimezoneOffset_ReturnsNumber(ExecutionMode mode)
    {
        var source = @"
            let d = new Date();
            let offset = d.getTimezoneOffset();
            console.log(typeof offset);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n", output);
    }

    #endregion

    #region Setter Tests

    [Theory, ModeData]
    public void Date_SetFullYear_MutatesAndReturnsTimestamp(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            let result = d.setFullYear(2025);
            console.log(d.getFullYear());
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2025\nnumber\n", output);
    }

    [Theory, ModeData]
    public void Date_SetMonth_MutatesDate(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            d.setMonth(6);
            console.log(d.getMonth());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void Date_SetDate_MutatesDay(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            d.setDate(20);
            console.log(d.getDate());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void Date_SetHours_MutatesTime(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(2024, 0, 15, 10, 30);
            d.setHours(15);
            console.log(d.getHours());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void Date_SetTime_SetsFromEpoch(ExecutionMode mode)
    {
        var source = @"
            let d = new Date();
            d.setTime(0);
            console.log(d.getTime());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Conversion Tests

    [Theory, ModeData]
    public void Date_ToISOString_ReturnsUTCFormat(ExecutionMode mode)
    {
        var source = @"
            let d = new Date('2024-06-15T12:00:00Z');
            let iso = d.toISOString();
            console.log(iso.includes('2024-06-15'));
            console.log(iso.includes('Z'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Date_ToJSON_ReturnsISOString(ExecutionMode mode)
    {
        // toJSON returns the same ISO string as toISOString for a valid date (#491).
        var source = @"
            let d = new Date('2024-06-15T12:00:00Z');
            console.log(d.toJSON() === d.toISOString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Date_ToJSON_InvalidDate_ReturnsNull(ExecutionMode mode)
    {
        // ECMA-262 §21.4.4.37: toJSON returns null for a non-finite (Invalid) date,
        // rather than throwing the way toISOString does.
        var source = @"
            let d = new Date(NaN);
            console.log(d.toJSON());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory, ModeData]
    public void Date_ValueOf_ReturnsTimestamp(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(0);
            console.log(d.valueOf());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Date_ToString_ReturnsString(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(2024, 0, 15);
            let s = d.toString();
            console.log(typeof s);
            console.log(s.includes('2024'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\ntrue\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void Date_InvalidString_CreatesInvalidDate(ExecutionMode mode)
    {
        var source = @"
            let d = new Date('not a date');
            let time = d.getTime();
            console.log(Number.isNaN(time));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Date_MonthOverflow_RollsOver(ExecutionMode mode)
    {
        // Month 12 should roll over to next year
        var source = @"
            let d = new Date(2024, 12, 1);
            console.log(d.getFullYear());
            console.log(d.getMonth());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2025\n0\n", output);
    }

    [Theory, ModeData]
    public void Date_TwoDigitYear_MapsTo1900s(ExecutionMode mode)
    {
        // Years 0-99 in constructor should map to 1900-1999
        var source = @"
            let d = new Date(99, 0, 1);
            console.log(d.getFullYear());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1999\n", output);
    }

    #endregion

    #region UTC, Locale, and Legacy Methods (issue #516)

    [Theory, ModeData]
    public void Date_UTCGetters_ReadEpochInUTC(ExecutionMode mode)
    {
        // UTC getters read the stored instant directly, so the result is timezone-independent.
        var source = @"
            let d = new Date(0);
            console.log(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(), d.getUTCDay());
            console.log(d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1970 0 1 4\n0 0 0 0\n", output);
    }

    [Theory, ModeData]
    public void Date_UTCSetters_MutateInUTC(ExecutionMode mode)
    {
        // Single-argument setters behave identically in both modes (no optional args dropped).
        var source = @"
            let d = new Date(0);
            d.setUTCFullYear(2020);
            d.setUTCMonth(5);
            d.setUTCDate(15);
            d.setUTCHours(13);
            d.setUTCMinutes(30);
            d.setUTCSeconds(45);
            d.setUTCMilliseconds(500);
            console.log(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
            console.log(d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2020 5 15\n13 30 45 500\n", output);
    }

    [Theory, ModeData]
    public void Date_SetUTCMonth_RollsOver(ExecutionMode mode)
    {
        // setUTCMonth(13) advances into the next year (month 1 = February).
        var source = @"
            let d = new Date(0);
            d.setUTCFullYear(2024);
            d.setUTCMonth(13);
            console.log(d.getUTCFullYear(), d.getUTCMonth());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2025 1\n", output);
    }

    [Theory, ModeData]
    public void Date_ToUTCString_FormatsInRFC7231(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(0);
            console.log(d.toUTCString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Thu, 01 Jan 1970 00:00:00 GMT\n", output);
    }

    [Theory, ModeData]
    public void Date_ToLocaleMethods_ReturnStrings(ExecutionMode mode)
    {
        // Output is locale/host-defined, so assert only that they return non-empty strings.
        var source = @"
            let d = new Date(0);
            console.log(typeof d.toLocaleDateString(), d.toLocaleDateString().length > 0);
            console.log(typeof d.toLocaleTimeString(), d.toLocaleTimeString().length > 0);
            console.log(typeof d.toLocaleString(), d.toLocaleString().length > 0);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string true\nstring true\nstring true\n", output);
    }

    [Theory, ModeData]
    public void Date_GetYearAndSetYear_AnnexB(ExecutionMode mode)
    {
        // getYear/setYear operate in local time; constructing and reading locally is TZ-independent.
        // setYear maps 0-99 to 1900-1999 but leaves four-digit years unchanged.
        var source = @"
            let d = new Date(2024, 5, 15);
            console.log(d.getYear());
            d.setYear(99);
            console.log(d.getFullYear());
            d.setYear(2005);
            console.log(d.getFullYear());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("124\n1999\n2005\n", output);
    }

    [Theory, ModeData]
    public void Date_InvalidDate_NewMembersDegradeGracefully(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(NaN);
            console.log(d.toUTCString());
            console.log(d.toLocaleDateString());
            console.log(Number.isNaN(d.getUTCFullYear()));
            console.log(Number.isNaN(d.setUTCSeconds(30)));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Invalid Date\nInvalid Date\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Date_SetMinutesAndSeconds_MutateInBothModes(ExecutionMode mode)
    {
        // Regression guard: compiled setMinutes/setSeconds/setMilliseconds were previously
        // no-op stubs (issue #516 fix). Local get/set round-trips are timezone-independent.
        var source = @"
            let d = new Date(2024, 0, 1, 10, 20, 30, 40);
            d.setMinutes(45);
            d.setSeconds(50);
            d.setMilliseconds(123);
            console.log(d.getMinutes(), d.getSeconds(), d.getMilliseconds());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("45 50 123\n", output);
    }

    [Theory, ModeData]
    public void Date_MultiArgSetters_HonorOptionalArgs(ExecutionMode mode)
    {
        // #536: both modes honor the optional trailing arguments of the multi-argument setters
        // (compiled previously applied only the primary argument). UTC get/set is timezone-
        // independent; local setHours followed by local getters round-trips regardless of zone.
        var source = @"
            let d = new Date(0);
            d.setUTCFullYear(2020, 5, 15);
            d.setUTCHours(13, 30, 45, 500);
            console.log(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
            console.log(d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(), d.getUTCMilliseconds());
            let m = new Date(2024, 0, 1, 10, 20, 30, 40);
            m.setHours(8, 15, 5);
            console.log(m.getHours(), m.getMinutes(), m.getSeconds(), m.getMilliseconds());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2020 5 15\n13 30 45 500\n8 15 5 40\n", output);
    }

    [Theory, ModeData]
    public void Date_MultiArgSetters_OverflowRollsOverAllAtOnce(ExecutionMode mode)
    {
        // setUTCFullYear(2020, 1, 31) = Feb 31 2020 -> Mar 2 (leap year), computed all-at-once,
        // identically in both modes.
        var source = @"
            let d = new Date(0);
            d.setUTCFullYear(2020, 1, 31);
            console.log(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2020 2 2\n", output);
    }

    [Theory, ModeData]
    public void Date_UTC_ReturnsUTCTimestamp(ExecutionMode mode)
    {
        // #538: Date.UTC builds a UTC timestamp; month defaults 0, date 1; 2-digit years map to
        // 1900s; overflow rolls over; a non-finite component yields NaN.
        var source = @"
            console.log(Date.UTC(2024, 0, 1));
            console.log(Date.UTC(2024, 5, 15, 13, 30, 45, 500));
            console.log(Date.UTC(2024));
            console.log(Date.UTC(70, 0, 1));
            console.log(Number.isNaN(Date.UTC(2024, NaN)));
            console.log(new Date(Date.UTC(2000, 0, 1)).toISOString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1704067200000\n1718458245500\n1704067200000\n0\ntrue\n2000-01-01T00:00:00.000Z\n", output);
    }

    [Theory, ModeData]
    public void Date_Parse_ReturnsTimestampOrNaN(ExecutionMode mode)
    {
        // #538: Date.parse returns the timestamp for a parseable string (NaN otherwise), and is
        // consistent with the string constructor.
        var source = @"
            console.log(Date.parse('2024-01-15T10:30:00Z'));
            console.log(Number.isNaN(Date.parse('not a date')));
            console.log(Date.parse('2024-01-15T10:30:00Z') === new Date('2024-01-15T10:30:00Z').getTime());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1705314600000\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Date_UTCAndParse_ValueForm(ExecutionMode mode)
    {
        // #538: the statics are also usable in value form (e.g. const f = Date.UTC).
        var source = @"
            const u = Date.UTC;
            const p = Date.parse;
            console.log(u(2024, 0, 1));
            console.log(p('2024-01-15T10:30:00Z'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1704067200000\n1705314600000\n", output);
    }

    [Theory, ModeData]
    public void Date_ToLocale_HonorsLocaleAndOptions(ExecutionMode mode)
    {
        // #539: locale and options are honored. Exact locale-formatted output is host-dependent
        // (ICU vs Windows NLS differ, e.g. a narrow no-break space before AM/PM), so — like
        // IntlDateTimeFormatTests — assert on stable localized names and structural effects rather
        // than exact strings. timeZone:'UTC' keeps it independent of the host time zone.
        var source = @"
            let d = new Date(Date.UTC(2024, 0, 15, 12, 0, 0));
            let enFull = d.toLocaleDateString('en-US', { dateStyle: 'full', timeZone: 'UTC' });
            let deFull = d.toLocaleDateString('de-DE', { dateStyle: 'full', timeZone: 'UTC' });
            let enShort = d.toLocaleDateString('en-US', { dateStyle: 'short', timeZone: 'UTC' });
            let enTime = d.toLocaleTimeString('en-US', { timeStyle: 'medium', timeZone: 'UTC' });
            console.log(enFull.includes('Monday') && enFull.includes('January') && enFull.includes('2024'));
            console.log(deFull.includes('Montag') && deFull.includes('Januar'));
            console.log(deFull !== enFull);
            console.log(enShort !== enFull && !enShort.includes('Monday'));
            console.log(enTime.includes('12:00:00'));
        ";
        var output = TestHarness.Run(source, mode);
        // en-US full has English weekday/month names; de-DE full uses German names; locale changes
        // the output; dateStyle:'short' drops the weekday; timeStyle:'medium' shows H:M:S.
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Date_ToLocale_NoArgs_RunsStandalone()
    {
        // #539: argument-less toLocale* must stay fully standalone (no SharpTS.dll dependency) —
        // only calls that pass locale/options opt into the soft runtime dependency.
        var source = @"
            let d = new Date(0);
            console.log(typeof d.toLocaleDateString(), d.toLocaleDateString().length > 0);
            console.log(typeof d.toLocaleTimeString(), d.toLocaleTimeString().length > 0);
            console.log(typeof d.toLocaleString(), d.toLocaleString().length > 0);
        ";
        var output = TestHarness.RunCompiledStandalone(source);
        Assert.Equal("string true\nstring true\nstring true\n", output);
    }

    #endregion
}
