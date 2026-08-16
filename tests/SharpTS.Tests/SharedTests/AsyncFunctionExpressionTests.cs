using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for async function EXPRESSIONS (`async function () {}` / `async function name() {}`),
/// as opposed to async arrow functions. Issue #635: the parser previously only attempted an async
/// arrow after `async`, so an async function expression failed to parse.
/// </summary>
public class AsyncFunctionExpressionTests
{
    [Theory, ModeData]
    public void AsyncFunctionExpression_Anonymous_ResolvesValue(ExecutionMode mode)
    {
        // The exact repro from issue #635.
        var source = """
            const af = async function () { return 9; };
            af().then((v: number) => console.log(v));
            """;

        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_Named_ResolvesValue(ExecutionMode mode)
    {
        var source = """
            const compute = async function doubler(x: number) { return x * 2; };
            compute(21).then((v: number) => console.log(v));
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_Awaited(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const af = async function () { return 7; };
                const v = await af();
                console.log(v + 1);
            }
            main();
            """;

        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_ImmediatelyInvoked(ExecutionMode mode)
    {
        var source = """
            (async function () { console.log("ran"); return 1; })();
            """;

        Assert.Equal("ran\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_AwaitInsideBody(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const af = async function (x: number) {
                    const y = await Promise.resolve(x + 1);
                    return y * 10;
                };
                console.log(await af(2));
            }
            main();
            """;

        Assert.Equal("30\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_AsCallbackArgument(ExecutionMode mode)
    {
        // Async function expression passed directly as an argument (a common position that the
        // async-arrow-only parser path could not reach).
        var source = """
            function run(fn: () => Promise<number>): Promise<number> { return fn(); }
            run(async function () { return 5; }).then((v: number) => console.log(v));
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorFunctionExpression_ForAwaitOf(ExecutionMode mode)
    {
        // `async function*` expression — the `function` keyword path also accepts the `*`, giving an
        // async generator expression. (Compiled `for await` over async generators landed in #430/#645.)
        var source = """
            const ag = async function* () { yield 1; yield 2; };
            async function main(): Promise<void> {
                for await (const v of ag()) console.log(v);
            }
            main();
            """;

        Assert.Equal("1\n2\n", TestHarness.Run(source, mode));
    }
}
