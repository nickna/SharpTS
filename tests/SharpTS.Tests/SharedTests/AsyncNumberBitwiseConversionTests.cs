using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncNumberBitwiseConversionTests
{
    public const string IssueSource = """
        const large = 4294967297, zero = 0, infinite = Infinity, negative = -4294967297.75;
        console.log("sync", large | zero, infinite | zero, negative | zero);
        async function main() {
            await Promise.resolve(0);
            console.log("async", large | zero, infinite | zero, negative | zero);
        }
        main();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["sync-and-async-number-bitwise-wrapping-issue", IssueSource, "sync 1 0 -1\nasync 1 0 -1\n"];
        yield return ["all-bitwise-and-shift-families-wrap-both-number-operands", """
            const left = 4294967297.75, right = 4294967298.75;
            async function main() {
                await Promise.resolve(0);
                console.log(left & right, left | right, left ^ right, left << right, left >> right, left >>> right);
            }
            main();
            """, "0 3 3 4 0 0\n"];
        yield return ["fractional-nonfinite-and-unsigned-number-results", """
            const positive = 4294967297.75, negative = -4294967297.75;
            const fraction = 3.75, negativeFraction = -3.75, negativeZero = -0;
            const notANumber = NaN, infinite = Infinity, negativeInfinite = -Infinity, zero = 0;
            async function main() {
                await Promise.resolve(0);
                console.log('truncation', positive | zero, negative | zero, fraction | zero,
                    negativeFraction | zero, negativeZero | zero);
                console.log('special', notANumber | zero, infinite | zero, negativeInfinite | zero,
                    7 & notANumber, 7 ^ infinite);
                console.log('unsigned', negative >>> zero, 2147483648 >>> zero, 4294967295 >>> zero,
                    positive >>> zero, infinite >>> zero);
            }
            main();
            """, "truncation 1 -1 3 -3 0\nspecial 0 0 0 0 7\nunsigned 4294967295 2147483648 4294967295 1 0\n"];
        yield return ["wide-fractional-negative-and-nonfinite-right-counts-mask-to-five-bits", """
            const left = -2147483647, wide = 4294967329.75;
            const positive = 3, negative = -4294967297.75;
            const allBits = -1, cycle = 4294967296, infinite = Infinity, notANumber = NaN;
            const main = async () => {
                await Promise.resolve(0);
                console.log('wide', left << wide, left >> wide, left >>> wide);
                console.log('negative', positive << negative, positive >> negative, positive >>> negative);
                console.log('zero', allBits >>> cycle, allBits >>> infinite, allBits >>> notANumber);
            };
            main();
            """, "wide 2 -1073741824 1073741824\nnegative -2147483648 0 0\nzero 4294967295 4294967295 4294967295\n"];
        yield return ["pending-rhs-retains-primitive-left-and-awaits-each-operand-once", """
            const events: string[] = [];
            let selected = 4294967297.75;
            function left(): number { events.push('left'); return selected; }
            async function main() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function right(): Promise<number> { events.push('right'); return pending; }
                const task = (async () => left() << await right())();
                console.log('pending', events.join(','));
                selected = 99;
                release(4294967329.75);
                console.log('settled', await task, selected, events.join(','));
                const result = (await Promise.resolve(4294967297.75)) ^ 4294967298.75;
                console.log('left-await', result, events.join(','));
            }
            main().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "pending left,right\nsettled 2 99 left,right\nleft-await 3 left,right\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AsyncNumberBitwiseOperatorsApplyJavaScriptIntegerConversions(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
