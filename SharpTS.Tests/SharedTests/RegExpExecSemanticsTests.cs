using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression coverage for RegExpBuiltinExec argument coercion, capture
/// materialization, and observable lastIndex operations.
/// </summary>
public class RegExpExecSemanticsTests
{
    [Theory, ModeData]
    public void Exec_ReturnsUndefinedForUnmatchedCaptures(
        ExecutionMode mode)
    {
        var source = """
            let match: any = /((1)|(12))((3)|(23))/.exec("123");
            console.log(match[0] + ":" + match.index + ":" + match.input);
            console.log(match[3] === undefined);
            """;
        Assert.Equal("123:0:123\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Exec_ReadsRawLastIndexOnceAndPreservesItForNonGlobalRegExp(
        ExecutionMode mode)
    {
        var source = """
            let reads = 0;
            let counter: any = { valueOf: function (): any { reads++; return 0; } };
            let re: any = /./;
            re.lastIndex = counter;
            let match = re.exec("abc");
            console.log(match[0] + ":" + reads + ":" + (re.lastIndex === counter));
            """;
        Assert.Equal("a:1:true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Exec_GlobalRegExpWritesNumericLastIndexAfterCoercion(ExecutionMode mode)
    {
        var source = """
            let reads = 0;
            let re: any = /./g;
            re.lastIndex = { valueOf: function (): any { reads++; return 1; } };
            let match = re.exec("abc");
            console.log(match[0] + ":" + reads + ":" + re.lastIndex);
            """;
        Assert.Equal("b:1:2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Exec_MissingArgumentUsesUndefinedAndMethodIsNotConstructable(
        ExecutionMode mode)
    {
        var source = """
            console.log((/undefined/).exec()[0]);
            let exec: any = RegExp.prototype.exec;
            try {
                new exec();
                console.log("constructed");
            } catch (e) {
                console.log(e instanceof TypeError);
            }
            """;
        Assert.Equal("undefined\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Exec_CoercesExplicitNullAndReturnsAnArray(ExecutionMode mode)
    {
        var source = """
            let regexp: any = /ll|l/;
            let match: any = regexp.exec(null);
            console.log(match instanceof Array);
            console.log(match[0] + ":" + match.index + ":" + match.input);
            """;
        Assert.Equal("true\nll:2:null\n", TestHarness.Run(source, mode));
    }
}
