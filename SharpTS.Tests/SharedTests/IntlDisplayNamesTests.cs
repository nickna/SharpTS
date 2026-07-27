using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.DisplayNames API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlDisplayNamesTests
{
    // ========== Language Display Names ==========

    [Theory, ModeData]
    public void DisplayNames_LanguageFrench(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""language""});
            console.log(dn.of(""fr""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("French", output);
    }

    [Theory, ModeData]
    public void DisplayNames_LanguageEnglish(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""language""});
            console.log(dn.of(""en""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("English", output);
    }

    // ========== Region Display Names ==========

    [Theory, ModeData]
    public void DisplayNames_RegionUS(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""region""});
            console.log(dn.of(""US""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("United States", output);
    }

    [Theory, ModeData]
    public void DisplayNames_RegionGB(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""region""});
            console.log(dn.of(""GB""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("United Kingdom", output);
    }

    // ========== Script Display Names ==========

    [Theory, ModeData]
    public void DisplayNames_ScriptLatin(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""script""});
            console.log(dn.of(""Latn""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Latin\n", output);
    }

    // ========== Currency Display Names ==========

    [Theory, ModeData]
    public void DisplayNames_CurrencyUSD(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""currency""});
            console.log(dn.of(""USD""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("Dollar", output);
    }

    [Theory, ModeData]
    public void DisplayNames_CurrencyEUR(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""currency""});
            console.log(dn.of(""EUR""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Euro\n", output);
    }

    // ========== Calendar Display Names ==========

    [Theory, ModeData]
    public void DisplayNames_CalendarGregory(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""calendar""});
            console.log(dn.of(""gregory""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("Gregorian", output);
    }

    // ========== DateTimeField Display Names ==========

    [Theory, ModeData]
    public void DisplayNames_DateTimeFieldYear(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""dateTimeField""});
            console.log(dn.of(""year""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("year\n", output);
    }

    // ========== Fallback Behavior ==========

    [Theory, ModeData]
    public void DisplayNames_FallbackCode(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""region"", fallback: ""code""});
            console.log(dn.of(""ZZZ""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ZZZ\n", output);
    }

    [Theory, ModeData]
    public void DisplayNames_FallbackNone(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""region"", fallback: ""none""});
            const result = dn.of(""ZZZ"");
            console.log(result == null);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void DisplayNames_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const dn = new Intl.DisplayNames(""en-US"", {type: ""language"", style: ""short"", fallback: ""none""});
            const opts = dn.resolvedOptions();
            console.log(opts.type);
            console.log(opts.style);
            console.log(opts.fallback);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("language\nshort\nnone\n", output);
    }
}
