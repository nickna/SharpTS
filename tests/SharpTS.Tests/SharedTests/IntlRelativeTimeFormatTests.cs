using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.RelativeTimeFormat API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlRelativeTimeFormatTests
{
    // ========== Past Formatting ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_PastDays(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"");
            console.log(rtf.format(-3, ""day""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3 days ago\n", output);
    }

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_PastSingular(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"");
            console.log(rtf.format(-1, ""hour""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 hour ago\n", output);
    }

    // ========== Future Formatting ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_FutureHours(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"");
            console.log(rtf.format(2, ""hour""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("in 2 hours\n", output);
    }

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_FutureMonths(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"");
            console.log(rtf.format(5, ""month""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("in 5 months\n", output);
    }

    // ========== Auto Numeric ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_AutoYesterday(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"", {numeric: ""auto""});
            console.log(rtf.format(-1, ""day""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("yesterday\n", output);
    }

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_AutoTomorrow(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"", {numeric: ""auto""});
            console.log(rtf.format(1, ""day""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("tomorrow\n", output);
    }

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_AutoLastYear(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"", {numeric: ""auto""});
            console.log(rtf.format(-1, ""year""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("last year\n", output);
    }

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_AutoNextMonth(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"", {numeric: ""auto""});
            console.log(rtf.format(1, ""month""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("next month\n", output);
    }

    // ========== Different Units ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_VariousUnits(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"");
            console.log(rtf.format(-2, ""year""));
            console.log(rtf.format(3, ""week""));
            console.log(rtf.format(-10, ""second""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2 years ago\nin 3 weeks\n10 seconds ago\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"", {style: ""short"", numeric: ""auto""});
            const opts = rtf.resolvedOptions();
            console.log(opts.style);
            console.log(opts.numeric);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("short\nauto\n", output);
    }

    // ========== Default (No Arguments) ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_DefaultNoArgs(ExecutionMode mode)
    {
        var source = @"
            const rtf = new Intl.RelativeTimeFormat();
            const result = rtf.format(-1, ""day"");
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    // ========== Plural Unit Names ==========

    [Theory, ModeData]
    public void IntlRelativeTimeFormat_PluralUnitNames(ExecutionMode mode)
    {
        // Should accept both singular and plural unit names
        var source = @"
            const rtf = new Intl.RelativeTimeFormat(""en-US"");
            console.log(rtf.format(-3, ""days""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3 days ago\n", output);
    }
}
