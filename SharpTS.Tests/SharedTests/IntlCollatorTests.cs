using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.Collator API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlCollatorTests
{
    // ========== Basic Comparison ==========

    [Theory, ModeData]
    public void IntlCollator_BasicCompare(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"");
            console.log(collator.compare(""a"", ""b""));
            console.log(collator.compare(""b"", ""a""));
            console.log(collator.compare(""a"", ""a""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n1\n0\n", output);
    }

    // ========== Case Sensitivity ==========

    [Theory, ModeData]
    public void IntlCollator_CaseInsensitive(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"", {sensitivity: ""base""});
            console.log(collator.compare(""a"", ""A""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void IntlCollator_CaseSensitive(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"", {sensitivity: ""variant""});
            const result = collator.compare(""a"", ""A"");
            console.log(result !== 0);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // ========== Accent Sensitivity ==========

    [Theory, ModeData]
    public void IntlCollator_AccentInsensitive(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"", {sensitivity: ""base""});
            console.log(collator.compare(""e"", ""é""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void IntlCollator_AccentSensitive(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"", {sensitivity: ""accent""});
            const result = collator.compare(""e"", ""é"");
            console.log(result !== 0);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void IntlCollator_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"", {sensitivity: ""base"", numeric: true});
            const opts = collator.resolvedOptions();
            console.log(opts.sensitivity);
            console.log(opts.numeric);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("base\ntrue\n", output);
    }

    // ========== Sorting ==========

    [Theory, ModeData]
    public void IntlCollator_SortArray(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"");
            const items = [""banana"", ""apple"", ""cherry""];
            items.sort((a: any, b: any) => collator.compare(a, b) as number);
            console.log(items.join("", ""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("apple, banana, cherry\n", output);
    }

    // ========== Default (No Arguments) ==========

    [Theory, ModeData]
    public void IntlCollator_DefaultNoArgs(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator();
            const result = collator.compare(""a"", ""b"");
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n", output);
    }

    // ========== Ignore Punctuation ==========

    [Theory, ModeData]
    public void IntlCollator_IgnorePunctuation(ExecutionMode mode)
    {
        var source = @"
            const collator = new Intl.Collator(""en-US"", {ignorePunctuation: true});
            const result = collator.compare(""hello"", ""hello!"");
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }
}
