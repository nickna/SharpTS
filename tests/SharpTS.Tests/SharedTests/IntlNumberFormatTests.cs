using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Intl.NumberFormat API.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class IntlNumberFormatTests
{
    [Theory, ModeData]
    public void Intl_GlobalNamespaceHasOneLiveObjectBinding(ExecutionMode mode)
    {
        const string source = """
            const root = globalThis;
            const original = root.Intl;
            console.log(typeof globalThis.Intl, typeof root.Intl, Intl === original);
            const d = Object.getOwnPropertyDescriptor(root, 'Intl');
            console.log(d.value === original, d.writable, d.enumerable, d.configurable);
            console.log(root['Intl'].NumberFormat === Intl.NumberFormat);
            console.log(new root.Intl.NumberFormat('en-US', { useGrouping: false }).format(42));
            root.Intl = { marker: 7 };
            console.log(Intl.marker, globalThis.Intl.marker);
            console.log(delete root.Intl, typeof Intl, Object.hasOwn(root, 'Intl'));
            """;
        Assert.Equal("object object true\ntrue true false true\ntrue\n42\n7 7\ntrue undefined false\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Intl_ConstructionResolvesTheCurrentFactoryBeforeArguments(ExecutionMode mode)
    {
        const string source = """
            const original = Intl;
            console.log(new Intl.NumberFormat('en-US').format(1234));
            const replacement = {
                get NumberFormat() {
                    console.log('get');
                    return function (value) { this.marker = value; };
                }
            };
            function restore() { console.log('arg'); globalThis.Intl = original; return ['value']; }
            globalThis.Intl = replacement;
            console.log(new Intl.NumberFormat(...restore()).marker);
            globalThis.Intl = replacement;
            function* construct() { return new Intl.NumberFormat(yield 'wait'); }
            const call = construct();
            console.log(call.next().value);
            globalThis.Intl = original;
            console.log(call.next('resumed').value.marker);
            console.log(new Intl.NumberFormat('en-US').format(1234));
            """;
        Assert.Equal("1,234\nget\narg\nvalue\nget\nwait\nresumed\n1,234\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Intl_FactoriesAreNonEnumerableOwnProperties(ExecutionMode mode)
    {
        const string source = """
            const intl = globalThis.Intl;
            const names = ['NumberFormat', 'DateTimeFormat', 'Collator', 'PluralRules',
                'RelativeTimeFormat', 'ListFormat', 'Segmenter', 'DisplayNames'];
            const ownNames = Object.getOwnPropertyNames(intl);
            for (const name of names) {
                const descriptor = Object.getOwnPropertyDescriptor(intl, name);
                console.log(ownNames.includes(name), descriptor.value === intl[name],
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
            }
            console.log(Object.keys(intl).length);
            intl.NumberFormat = () => 42;
            console.log(intl.NumberFormat(), Object.keys(intl).includes('NumberFormat'));
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true true true false true\n", 8)) + "0\n42 false\n",
            TestHarness.RunUnchecked(source, mode));
    }

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
