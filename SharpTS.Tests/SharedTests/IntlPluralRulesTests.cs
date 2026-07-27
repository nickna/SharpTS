using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.PluralRules API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlPluralRulesTests
{
    // ========== English Cardinal ==========

    [Theory, ModeData]
    public void IntlPluralRules_EnglishCardinal_One(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""en-US"");
            console.log(pr.select(1));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("one\n", output);
    }

    [Theory, ModeData]
    public void IntlPluralRules_EnglishCardinal_Other(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""en-US"");
            console.log(pr.select(0));
            console.log(pr.select(2));
            console.log(pr.select(5));
            console.log(pr.select(100));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("other\nother\nother\nother\n", output);
    }

    // ========== English Ordinal ==========

    [Theory, ModeData]
    public void IntlPluralRules_EnglishOrdinal(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""en-US"", {type: ""ordinal""});
            console.log(pr.select(1));
            console.log(pr.select(2));
            console.log(pr.select(3));
            console.log(pr.select(4));
            console.log(pr.select(11));
            console.log(pr.select(21));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("one\ntwo\nfew\nother\nother\none\n", output);
    }

    // ========== French Cardinal ==========

    [Theory, ModeData]
    public void IntlPluralRules_FrenchCardinal(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""fr"");
            console.log(pr.select(0));
            console.log(pr.select(1));
            console.log(pr.select(2));
        ";
        var output = TestHarness.Run(source, mode);
        // French: 0 and 1 are "one", 2+ is "other"
        Assert.Equal("one\none\nother\n", output);
    }

    // ========== Arabic Cardinal ==========

    [Theory, ModeData]
    public void IntlPluralRules_ArabicCardinal(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""ar"");
            console.log(pr.select(0));
            console.log(pr.select(1));
            console.log(pr.select(2));
            console.log(pr.select(5));
            console.log(pr.select(11));
            console.log(pr.select(100));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("zero\none\ntwo\nfew\nmany\nother\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void IntlPluralRules_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""en-US"", {type: ""ordinal""});
            const opts = pr.resolvedOptions();
            console.log(opts.type);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ordinal\n", output);
    }

    // ========== Default (No Arguments) ==========

    [Theory, ModeData]
    public void IntlPluralRules_DefaultNoArgs(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules();
            const result = pr.select(1);
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    // ========== Decimal Numbers ==========

    [Theory, ModeData]
    public void IntlPluralRules_DecimalNumber(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""en-US"");
            console.log(pr.select(1.5));
        ";
        var output = TestHarness.Run(source, mode);
        // 1.5 has visible fraction digits, so it's "other" in English (i != 1 or v != 0)
        Assert.Equal("other\n", output);
    }

    // ========== Suffix Helper ==========

    [Theory, ModeData]
    public void IntlPluralRules_OrdinalSuffixHelper(ExecutionMode mode)
    {
        var source = @"
            const pr = new Intl.PluralRules(""en-US"", {type: ""ordinal""});
            const suffixes: Record<string, string> = {
                one: ""st"",
                two: ""nd"",
                few: ""rd"",
                other: ""th""
            };
            function ordinal(n: number): string {
                const category = pr.select(n);
                return n + suffixes[category];
            }
            console.log(ordinal(1));
            console.log(ordinal(2));
            console.log(ordinal(3));
            console.log(ordinal(4));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1st\n2nd\n3rd\n4th\n", output);
    }
}
