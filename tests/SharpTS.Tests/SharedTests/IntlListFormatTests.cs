using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.ListFormat API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlListFormatTests
{
    // ========== Conjunction (default) ==========

    [Theory, ModeData]
    public void IntlListFormat_ConjunctionThreeItems(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            console.log(lf.format([""Apple"", ""Banana"", ""Cherry""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Apple, Banana, and Cherry\n", output);
    }

    [Theory, ModeData]
    public void IntlListFormat_ConjunctionTwoItems(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            console.log(lf.format([""Alice"", ""Bob""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice and Bob\n", output);
    }

    [Theory, ModeData]
    public void IntlListFormat_ConjunctionOneItem(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            console.log(lf.format([""Solo""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Solo\n", output);
    }

    [Theory, ModeData]
    public void IntlListFormat_ConjunctionEmpty(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            console.log(lf.format([]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("\n", output);
    }

    // ========== Disjunction ==========

    [Theory, ModeData]
    public void IntlListFormat_DisjunctionThreeItems(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"", {type: ""disjunction""});
            console.log(lf.format([""Apple"", ""Banana"", ""Cherry""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Apple, Banana, or Cherry\n", output);
    }

    [Theory, ModeData]
    public void IntlListFormat_DisjunctionTwoItems(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"", {type: ""disjunction""});
            console.log(lf.format([""Yes"", ""No""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Yes or No\n", output);
    }

    // ========== Unit ==========

    [Theory, ModeData]
    public void IntlListFormat_UnitShort(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"", {type: ""unit"", style: ""short""});
            console.log(lf.format([""6 hours"", ""30 minutes""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6 hours, 30 minutes\n", output);
    }

    [Theory, ModeData]
    public void IntlListFormat_UnitNarrow(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"", {type: ""unit"", style: ""narrow""});
            console.log(lf.format([""5 lb"", ""8 oz""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5 lb 8 oz\n", output);
    }

    // ========== Resolved Options ==========

    [Theory, ModeData]
    public void IntlListFormat_ResolvedOptions(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"", {type: ""disjunction"", style: ""short""});
            const opts = lf.resolvedOptions();
            console.log(opts.type);
            console.log(opts.style);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("disjunction\nshort\n", output);
    }

    // ========== Format To Parts ==========

    [Theory, ModeData]
    public void IntlListFormat_FormatToParts(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            const parts = lf.formatToParts([""A"", ""B""]);
            for (const part of parts) {
                console.log(part.type + "":"" + part.value);
            }
        ";
        var output = TestHarness.Run(source, mode);
        // Should have element and literal parts
        Assert.Contains("element:A", output);
        Assert.Contains("element:B", output);
    }

    [Theory, ModeData]
    public void IntlListFormat_FormatToPartsReturnsArray(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            const parts: any = lf.formatToParts([""A"", ""B""]);
            console.log(typeof parts);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    // ========== Default (No Arguments) ==========

    [Theory, ModeData]
    public void IntlListFormat_DefaultNoArgs(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat();
            const result = lf.format([""A"", ""B"", ""C""]);
            console.log(typeof result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    // ========== Four+ Items ==========

    [Theory, ModeData]
    public void IntlListFormat_FourItems(ExecutionMode mode)
    {
        var source = @"
            const lf = new Intl.ListFormat(""en-US"");
            console.log(lf.format([""A"", ""B"", ""C"", ""D""]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("A, B, C, and D\n", output);
    }
}
