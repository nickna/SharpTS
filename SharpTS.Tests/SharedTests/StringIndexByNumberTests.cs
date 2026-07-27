using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for a type-check gap surfaced while unblocking the Phase 3h
/// URL migration: <c>str[i]</c> where <c>str: string</c> and <c>i: number</c>
/// threw "Index type 'number' is not valid for indexing 'string'", even
/// though JavaScript spec defines this as valid character indexing.
/// </summary>
public class StringIndexByNumberTests
{
    [Theory, ModeData]
    public void IndexString_ByNumber_ReturnsCharacter(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const s: string = 'hello';
                console.log(s[0] + ' ' + s[4] + ' ' + typeof s[1]);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("h o string\n", output);
    }
}
