using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.NumberFormat API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlNumberFormatTests
{
    // ========== Basic Decimal Formatting ==========

    [Theory, ModeData]
    public void IntlNumberFormat_BasicDecimal(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"");
            console.log(nf.format(1234.567));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,234.567\n", output);
    }

    // ========== Currency Formatting ==========

    [Theory, ModeData]
    public void IntlNumberFormat_CurrencyUSD(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {style: ""currency"", currency: ""USD""});
            console.log(nf.format(1234.5));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("$1,234.50\n", output);
    }

    [Theory, ModeData]
    public void IntlNumberFormat_CurrencyEUR(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {style: ""currency"", currency: ""EUR""});
            console.log(nf.format(1234.5));
        ";
        var output = TestHarness.Run(source, mode);
        // Check the numeric portion; currency symbol may vary with encoding
        Assert.Contains("1,234.50", output);
    }

    // ========== Percent Formatting ==========

    [Theory, ModeData]
    public void IntlNumberFormat_Percent(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {style: ""percent""});
            console.log(nf.format(0.75));
        ";
        var output = TestHarness.Run(source, mode);
        // .NET "P0" format: 75%  (with space before % on some cultures, but en-US uses no space)
        Assert.Contains("75", output);
        Assert.Contains("%", output);
    }

    // ========== Fraction Digits ==========

    [Theory, ModeData]
    public void IntlNumberFormat_FractionDigits(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {minimumFractionDigits: 2, maximumFractionDigits: 2});
            console.log(nf.format(1234));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,234.00\n", output);
    }

    // ========== No Grouping ==========

    [Theory, ModeData]
    public void IntlNumberFormat_NoGrouping(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {useGrouping: false});
            console.log(nf.format(1234567));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1234567\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void IntlNumberFormat_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {style: ""currency"", currency: ""USD""});
            const opts = nf.resolvedOptions();
            console.log(opts.style);
            console.log(opts.currency);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("currency\nUSD\n", output);
    }

    // ========== Minimum Integer Digits ==========

    [Theory, ModeData]
    public void IntlNumberFormat_MinimumIntegerDigits(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"", {minimumIntegerDigits: 5});
            console.log(nf.format(42));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("00,042\n", output);
    }

    // ========== Default (No Arguments) ==========

    [Theory, ModeData]
    public void IntlNumberFormat_DefaultNoArgs(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat();
            const result = nf.format(1234.5);
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        // Should return a string regardless of locale
        Assert.Equal("string\n", output);
    }

    // ========== Integer Formatting ==========

    [Theory, ModeData]
    public void IntlNumberFormat_Integer(ExecutionMode mode)
    {
        var source = @"
            const nf = new Intl.NumberFormat(""en-US"");
            console.log(nf.format(1000000));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,000,000\n", output);
    }
}
