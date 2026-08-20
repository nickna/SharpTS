using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression coverage for RegExpBuiltinExec argument coercion, capture
/// materialization, and observable lastIndex operations.
/// </summary>
public class RegExpExecSemanticsTests
{
    [Fact]
    public void IntrinsicGlobalMatchBuffer_BecomesArrayBackingWithoutCopy()
    {
        var regexp = new SharpTSRegExp("[a-z]+", "g");
        var matches = regexp.MatchAll("alpha beta gamma");
        var result = new SharpTSArray(matches);

        Assert.Same(matches, result.Elements);
        Assert.Equal(new object?[] { "alpha", "beta", "gamma" }, result);
    }

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

    [Theory, ModeData]
    public void StringMatchAndReplace_IntrinsicGlobalRegExpResetLastIndex(
        ExecutionMode mode)
    {
        var source = """
            const matcher: any = /a/g;
            matcher.lastIndex = 2;
            const matches: any = "aba".match(matcher);
            console.log(matches.join(",") + ":" + matcher.lastIndex);

            const replacer: any = /a/g;
            replacer.lastIndex = 2;
            console.log("aba".replace(replacer, "x") + ":" + replacer.lastIndex);
            """;

        Assert.Equal("a,a:0\nxbx:0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StringMatch_ObservesSymbolAccessorBeforeCustomExec(
        ExecutionMode mode)
    {
        var source = """
            const intrinsicMatch: any = RegExp.prototype[Symbol.match];
            const intrinsicExec: any = RegExp.prototype.exec;
            const regexp: any = /a/g;
            let order: string = "";

            Object.defineProperty(regexp, Symbol.match, {
                configurable: true,
                get: function (): any {
                    order = order + "symbol>";
                    return intrinsicMatch;
                }
            });
            regexp.exec = function (input: string): any {
                order = order + "exec>";
                return intrinsicExec.call(this, input);
            };

            console.log("aba".match(regexp).join(","));
            console.log(order);
            """;

        Assert.Equal(
            "a,a\nsymbol>exec>exec>exec>\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StringReplace_InvokesPrototypeExecAccessorForEveryAttempt(
        ExecutionMode mode)
    {
        var source = """
            const intrinsicExec: any = RegExp.prototype.exec;
            let gets: number = 0;
            let calls: number = 0;
            Object.defineProperty(RegExp.prototype, "exec", {
                configurable: true,
                get: function (): any {
                    gets = gets + 1;
                    return function (input: string): any {
                        calls = calls + 1;
                        return intrinsicExec.call(this, input);
                    };
                }
            });

            console.log("aba".replace(/a/g, "x") + ":" + gets + ":" + calls);
            """;

        Assert.Equal("xbx:3:3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StringMatchAndReplace_InvokePrototypeSymbolOverrides(
        ExecutionMode mode)
    {
        var source = """
            RegExp.prototype[Symbol.match] = function (input: string): any {
                console.log("match:" + input + ":" + (this instanceof RegExp));
                return "custom-match";
            };
            RegExp.prototype[Symbol.replace] = function (
                input: string, replacement: any): any {
                console.log("replace:" + input + ":" + replacement + ":" +
                    (this instanceof RegExp));
                return "custom-replace";
            };

            console.log("aba".match(/a/g));
            console.log("aba".replace(/a/g, "x"));
            """;

        Assert.Equal(
            "match:aba:true\ncustom-match\n" +
            "replace:aba:x:true\ncustom-replace\n",
            TestHarness.Run(source, mode));
    }
}
