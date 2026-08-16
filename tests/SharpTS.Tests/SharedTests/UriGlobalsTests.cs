using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the ECMAScript URI encoding globals: encodeURIComponent, decodeURIComponent.
/// Runs against both interpreter and compiler.
/// </summary>
public class UriGlobalsTests
{
    [Theory, ModeData]
    public void EncodeURIComponent_EncodesSpaces(ExecutionMode mode)
    {
        var source = "console.log(encodeURIComponent('hello world'));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello%20world\n", output);
    }

    [Theory, ModeData]
    public void EncodeURIComponent_EncodesReservedChars(ExecutionMode mode)
    {
        // & / ? = # are all reserved and must be encoded
        var source = "console.log(encodeURIComponent('a=b&c/d?e#f'));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("a%3Db%26c%2Fd%3Fe%23f\n", output);
    }

    [Theory, ModeData]
    public void EncodeURIComponent_PassesThroughUnreserved(ExecutionMode mode)
    {
        // A-Z a-z 0-9 - _ . ~ pass through per RFC 3986
        var source = "console.log(encodeURIComponent('abcABC123-_.~'));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("abcABC123-_.~\n", output);
    }

    [Theory, ModeData]
    public void DecodeURIComponent_DecodesPercentEncoded(ExecutionMode mode)
    {
        var source = "console.log(decodeURIComponent('hello%20world'));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void DecodeURIComponent_RoundTripPreservesString(ExecutionMode mode)
    {
        var source = """
            const original = 'a=b&c d/e?f#g';
            const encoded = encodeURIComponent(original);
            const decoded = decodeURIComponent(encoded);
            console.log(decoded === original);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void EncodeURIComponent_CoercesNonString(ExecutionMode mode)
    {
        var source = "console.log(encodeURIComponent(42));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }
}
