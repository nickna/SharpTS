using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncLocalFactoryTests
{
    [Theory, ModeData]
    public void LocalFactoryRetainsItsBindingAndLiveCapturesAcrossAwait(ExecutionMode mode)
    {
        const string source = """
            async function main() {
                let log = [];
                function make() {
                    return {
                        [Symbol.iterator]() { return this; },
                        return() { log.push(1); return {}; }
                    };
                }
                const factory = { create() { return make(); } };
                console.log(typeof make, typeof make());
                const first = await factory.create();
                console.log(typeof first, first[Symbol.iterator]() === first);
                first.return();
                console.log(log.join(','));
                log = [];
                const second = await factory.create();
                first.return(); second.return();
                console.log(log.join(','));
            }
            main();
            """;
        Assert.Equal("function object\nobject true\n1\n1,1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void IndependentAsyncInvocationsKeepTheirOwnFactoryEnvironment(ExecutionMode mode)
    {
        const string source = """
            async function main(value) {
                const log = [];
                function make() {
                    return {
                        [Symbol.iterator]() { return this; },
                        return() { log.push(value); return {}; }
                    };
                }
                const factory = { create() { return make(); } };
                const result = await factory.create();
                result.return();
                console.log(log.join(','));
            }
            main(3); main(7);
            """;
        Assert.Equal("3\n7\n", TestHarness.RunUnchecked(source, mode));
    }
}
