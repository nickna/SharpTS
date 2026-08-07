using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for String.prototype.matchAll().
/// </summary>
public class StringMatchAllTests
{
    [Theory, ModeData]
    public void MatchAll_BasicGlobalRegex_ReturnsAllMatches(ExecutionMode mode)
    {
        var source = """
            const str = "test1 test2 test3";
            const matches = Array.from(str.matchAll(/test\d/g));
            console.log(matches.length);
            const m0 = matches[0];
            const m1 = matches[1];
            const m2 = matches[2];
            console.log(m0["0"]);
            console.log(m1["0"]);
            console.log(m2["0"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\ntest1\ntest2\ntest3\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_AccessIndexProperty(ExecutionMode mode)
    {
        var source = """
            const str = "hello world";
            const matches = Array.from(str.matchAll(/\w+/g));
            console.log(matches[0].index);
            console.log(matches[1].index);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n6\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_AccessInputProperty(ExecutionMode mode)
    {
        var source = """
            const str = "abc 123";
            const matches = Array.from(str.matchAll(/\d+/g));
            console.log(matches[0].input);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("abc 123\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_CaptureGroups(ExecutionMode mode)
    {
        var source = """
            const str = "12-ab 34-cd";
            const matches = Array.from(str.matchAll(/(\d+)-(\w+)/g));
            console.log(matches.length);
            console.log(matches[0]["0"]);
            console.log(matches[0]["1"]);
            console.log(matches[0]["2"]);
            console.log(matches[1]["0"]);
            console.log(matches[1]["1"]);
            console.log(matches[1]["2"]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n12-ab\n12\nab\n34-cd\n34\ncd\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_NoMatches_ReturnsEmptyArray(ExecutionMode mode)
    {
        var source = """
            const str = "hello world";
            const matches = Array.from(str.matchAll(/\d+/g));
            console.log(matches.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_NonGlobalRegex_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            try {
                const matches = "hello".matchAll(/hello/);
                console.log("no error");
            } catch (e) {
                console.log("non-global");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("non-global", output);
    }

    [Theory, ModeData]
    public void MatchAll_ForOfIteration(ExecutionMode mode)
    {
        var source = """
            const str = "a1 b2 c3";
            const results: string[] = [];
            for (const match of str.matchAll(/[a-z]\d/g)) {
                results.push(match["0"]);
            }
            console.log(results.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a1,b2,c3\n", output);
    }

    [Theory, ModeData]
    public void MatchAll_StringPattern_MatchesLiterally(ExecutionMode mode)
    {
        var source = """
            const str = "foo bar foo baz foo";
            const matches = Array.from(str.matchAll("foo"));
            console.log(matches.length);
            console.log(matches[0]["0"]);
            console.log(matches[0].index);
            console.log(matches[2].index);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\nfoo\n0\n16\n", output);
    }
}
