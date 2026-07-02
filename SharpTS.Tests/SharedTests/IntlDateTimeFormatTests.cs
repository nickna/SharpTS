using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.DateTimeFormat API.
/// Tests run against both interpreter and compiler modes.
/// Uses en-US locale and a fixed date (Jan 15, 2024, 2:30:45 PM) for deterministic output.
/// </summary>
public class IntlDateTimeFormatTests
{
    // ========== Basic Date Formatting ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DateStyleShort(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""short""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("1/15/2024", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DateStyleLong(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""long""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("January", output);
        Assert.Contains("15", output);
        Assert.Contains("2024", output);
    }

    // ========== Time Formatting ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeStyleShort(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeStyle: ""short""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // Should contain time portion (2:30 PM or 14:30)
        Assert.Contains("30", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeStyleLong(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeStyle: ""long""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // Should contain seconds
        Assert.Contains("45", output);
    }

    // ========== Combined Date + Time ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DateAndTimeStyle(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""short"", timeStyle: ""short""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // Should contain both date and time
        Assert.Contains("1/15/2024", output);
        Assert.Contains("30", output);
    }

    // ========== Individual Component Options ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_YearMonthDay(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {year: ""numeric"", month: ""long"", day: ""numeric""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("2024", output);
        Assert.Contains("January", output);
        Assert.Contains("15", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TwoDigitYear(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {year: ""2-digit"", month: ""2-digit"", day: ""2-digit""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("24", output);
        Assert.Contains("01", output);
        Assert.Contains("15", output);
    }

    // ========== Weekday ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_WeekdayLong(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {weekday: ""long""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // Jan 15, 2024 is a Monday
        Assert.Contains("Monday", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_WeekdayShort(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {weekday: ""short""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("Mon", output);
    }

    // ========== Weekday Narrow (Bug Fix) ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_WeekdayNarrow(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {weekday: ""narrow""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // Monday's first letter
        Assert.Contains("M", output);
    }

    // ========== Month Narrow (Bug Fix) ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_MonthNarrow(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {month: ""narrow"", day: ""numeric"", year: ""numeric""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // January's first letter
        Assert.Contains("J", output);
    }

    // ========== dateStyle full/long differentiation ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DateStyleFull_IncludesWeekday(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""full""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("Monday", output);
        Assert.Contains("January", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DateStyleLong_NoWeekday(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""long""});
            const result = dtf.format(d);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.DoesNotContain("Monday", output);
        Assert.Contains("January", output);
    }

    // ========== timeStyle medium/short differentiation ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeStyleMedium_HasSeconds(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeStyle: ""medium""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains(":45", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeStyleShort_NoSeconds(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeStyle: ""short""});
            const result = dtf.format(d);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.DoesNotContain(":45", output);
    }

    // ========== dateStyle medium ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DateStyleMedium(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""medium""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        // Medium date should use abbreviated month
        Assert.Contains("Jan", output);
        Assert.Contains("15", output);
        Assert.Contains("2024", output);
    }

    // ========== Hour12 ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_Hour12(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {hour: ""numeric"", minute: ""2-digit"", hour12: true});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("2", output);
        Assert.Contains("30", output);
        Assert.Contains("PM", output);
    }

    // ========== Resolved Options ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""short""});
            const opts = dtf.resolvedOptions();
            console.log(opts.dateStyle);
            console.log(opts.calendar);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("short\ngregory\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_ResolvedOptions_ReflectsCalendarAndNumbering(ExecutionMode mode)
    {
        var source = @"
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""short"", calendar: ""buddhist""});
            const opts = dtf.resolvedOptions();
            console.log(opts.calendar);
            console.log(opts.numberingSystem);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("buddhist", output);
        Assert.Contains("latn", output);
    }

    // ========== Default (No Arguments) ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_DefaultNoArgs(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat();
            const result = dtf.format(d);
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    // ========== No-argument constructor ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_LocaleOnly(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"");
            const result = dtf.format(d);
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    // ========== Month Short ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_MonthShort(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {month: ""short"", day: ""numeric"", year: ""numeric""});
            console.log(dtf.format(d));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("Jan", output);
        Assert.Contains("15", output);
        Assert.Contains("2024", output);
    }

    // ========== formatToParts ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatToParts_ReturnsArray(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {year: ""numeric"", month: ""long"", day: ""numeric""});
            const parts = dtf.formatToParts(d);
            console.log(Array.isArray(parts));
            console.log(parts.length > 0);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    // ========================================================================
    // HISTORICAL NOTE — .NET 10 tier-0 JIT miscompilation (issue #39, RESOLVED)
    // ========================================================================
    // From 2026-04-20 to 2026-07-01 the two formatToParts tests below were
    // skipped in compiled mode on Linux/macOS: a .NET 10 runtime JIT bug made
    // `parts.map(p => p.type).includes(...)` return a wrong boolean under
    // DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 (~90% of runs, first calls only,
    // interpreted mode unaffected). Root-caused 2026-07-01 with a local repro:
    // the trigger was the April-era ArrayMap/ArrayIncludes emission shape
    // (rewritten in ca9a6641) on early .NET 10 servicing — identical artifacts
    // fail 9/10 on runtime 10.0.0 and pass 10/10 on 10.0.6/7/8/9, so the
    // runtime fixed it, not us. Full repro matrix and history in issue #39;
    // commits 696bdbc and cfc667b document the original workarounds.
    // ========================================================================

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatToParts_HasTypeAndValue(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {year: ""numeric"", month: ""long"", day: ""numeric""});
            const parts = dtf.formatToParts(d);
            const types = parts.map(p => p.type);
            console.log(types.includes(""year""));
            console.log(types.includes(""month""));
            console.log(types.includes(""day""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatToParts_ContainsLiterals(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {year: ""numeric"", month: ""long"", day: ""numeric""});
            const parts = dtf.formatToParts(d);
            const types = parts.map(p => p.type);
            console.log(types.includes(""literal""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // ========== formatRange ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatRange_SameDate(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""short""});
            const result = dtf.formatRange(d, d);
            console.log(typeof result);
            console.log(result.includes(""1/15/2024""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatRange_DifferentDates(ExecutionMode mode)
    {
        var source = @"
            const d1 = new Date(2024, 0, 15, 14, 30, 45);
            const d2 = new Date(2024, 2, 20, 10, 0, 0);
            const dtf = new Intl.DateTimeFormat(""en-US"", {dateStyle: ""short""});
            const result = dtf.formatRange(d1, d2);
            console.log(result.includes(""\u2013""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatRange_DifferentMonths(ExecutionMode mode)
    {
        var source = @"
            const d1 = new Date(2024, 0, 15);
            const d2 = new Date(2024, 5, 20);
            const dtf = new Intl.DateTimeFormat(""en-US"", {month: ""long"", day: ""numeric"", year: ""numeric""});
            const result = dtf.formatRange(d1, d2);
            console.log(result.includes(""January""));
            console.log(result.includes(""June""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    // ========== formatRangeToParts ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatRangeToParts_HasSourceField(ExecutionMode mode)
    {
        var source = @"
            const d1 = new Date(2024, 0, 15);
            const d2 = new Date(2024, 5, 20);
            const dtf = new Intl.DateTimeFormat(""en-US"", {month: ""long"", day: ""numeric""});
            const parts = dtf.formatRangeToParts(d1, d2);
            const sources = parts.map(p => p.source);
            console.log(sources.includes(""startRange""));
            console.log(sources.includes(""endRange""));
            console.log(sources.includes(""shared""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_FormatRangeToParts_SameDateIsShared(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15);
            const dtf = new Intl.DateTimeFormat(""en-US"", {month: ""long"", day: ""numeric""});
            const parts = dtf.formatRangeToParts(d, d);
            const allShared = parts.every(p => p.source === ""shared"");
            console.log(allShared);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // ========== BCP 47 Extensions ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_HourCycleExtension_H23(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US-u-hc-h23"", {hour: ""numeric"", minute: ""2-digit""});
            const opts = dtf.resolvedOptions();
            console.log(opts.hourCycle);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("h23\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_CalendarOption(ExecutionMode mode)
    {
        var source = @"
            const dtf = new Intl.DateTimeFormat(""en-US"", {calendar: ""buddhist"", dateStyle: ""short""});
            const opts = dtf.resolvedOptions();
            console.log(opts.calendar);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("buddhist\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_NumberingSystemOption(ExecutionMode mode)
    {
        var source = @"
            const dtf = new Intl.DateTimeFormat(""en-US"", {numberingSystem: ""arab"", dateStyle: ""short""});
            const opts = dtf.resolvedOptions();
            console.log(opts.numberingSystem);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("arab\n", output);
    }

    // ========== Timezone ==========

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeZoneUTC(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeZone: ""UTC"", timeStyle: ""short""});
            const result = dtf.format(d);
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeZoneNameLong_UTC(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeZone: ""UTC"", hour: ""numeric"", minute: ""2-digit"", timeZoneName: ""long""});
            const result = dtf.format(d);
            console.log(result.includes(""Coordinated Universal Time""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IntlDateTimeFormat_TimeZoneNameShort_UTC(ExecutionMode mode)
    {
        var source = @"
            const d = new Date(2024, 0, 15, 14, 30, 45);
            const dtf = new Intl.DateTimeFormat(""en-US"", {timeZone: ""UTC"", hour: ""numeric"", minute: ""2-digit"", timeZoneName: ""short""});
            const result = dtf.format(d);
            console.log(result.includes(""UTC""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }
}
