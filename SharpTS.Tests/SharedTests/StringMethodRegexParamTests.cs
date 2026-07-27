using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a type-check gap surfaced by the Phase 3i util migration:
/// <c>String.prototype.replace / replaceAll / split</c> were declared as
/// <c>(string, string) =&gt; string</c>, which rejected the RegExp overload
/// (<c>s.replace(/pat/, 'x')</c>) that JavaScript has always supported.
/// </summary>
public class StringMethodRegexParamTests
{
    [Theory, ModeData]
    public void Replace_WithRegex(ExecutionMode mode)
    {
        var source = @"
            const s = 'hello world';
            console.log(s.replace(/o/g, 'X'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hellX wXrld\n", output);
    }

    [Theory, ModeData]
    public void ReplaceAll_WithRegex(ExecutionMode mode)
    {
        var source = @"
            const s = 'a,b,c';
            console.log(s.replaceAll(/,/g, '-'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("a-b-c\n", output);
    }

    [Theory, ModeData]
    public void Split_WithRegex(ExecutionMode mode)
    {
        var source = @"
            const s = 'one-two--three';
            const parts = s.split(/-+/);
            console.log(parts.length);
            console.log(parts[0]);
            console.log(parts[1]);
            console.log(parts[2]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\none\ntwo\nthree\n", output);
    }

    [Theory, ModeData]
    public void Replace_StringSecondArg_StillWorks(ExecutionMode mode)
    {
        // Make sure the widened signature didn't break the plain string form.
        var source = @"
            console.log('abc'.replace('b', 'B'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("aBc\n", output);
    }
}
