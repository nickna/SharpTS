using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncIteratorMethodRejectionTimingTests
{
    [Theory, ModeData]
    public void InvalidReceiversReturnRejectedPromisesAcrossLoopIterations(ExecutionMode mode)
    {
        const string source = """
            async function* values() {}
            async function run() {
                const fn: any = values;
                const intrinsic: any = Object.getPrototypeOf(fn.prototype);
                for (const name of ['next', 'return', 'throw']) {
                    let returned = false;
                    try {
                        const result = intrinsic[name].call({});
                        returned = true;
                        await result;
                    } catch (error: any) { console.log(name, error.name, returned); }
                }
            }
            run();
            """;
        Assert.Equal("next TypeError true\nreturn TypeError true\nthrow TypeError true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForInBackedgesRetainLocalsReadBeforeAwait(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                const state = { value: 7 };
                for (const key in { a: 1, b: 2 }) {
                    console.log(key, state.value);
                    await Promise.resolve();
                }
            }
            run();
            """;
        Assert.Equal("a 7\nb 7\n", TestHarness.Run(source, mode));
    }
}
