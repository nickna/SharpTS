using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorSyncFactoryAccessorTests
{
    public const string FactoryAccessorSource = """
        const iterable = {
            get [Symbol.iterator]() {
                console.log("lookup iterator", this === iterable);
                return function () {
                    console.log("acquire", this === iterable);
                    let done = false;
                    const iterator = {
                        next(value) {
                            console.log("next", arguments.length, value, this === iterator);
                            if (done) return { done };
                            done = true;
                            return { done: false, value: 7 };
                        }
                    };
                    return iterator;
                };
            }
        };
        async function* run() { yield* iterable; }
        async function main() {
            const generator = run();
            console.log((await generator.next()).value);
            console.log((await generator.next(9)).done);
        }
        main();
        """;

    [Theory, ModeData]
    public void YieldStar_CapturesSyncIteratorFactoryAccessorAndForwardsSentValues(ExecutionMode mode)
    {
        AssertFactoryAccessor(FactoryAccessorSource, mode);
    }

    // The compiled async-from-sync acquisition path ignores inherited symbol
    // accessors; its separately reported defect is tracked in #3658.
    [Theory, InterpretedOnlyData]
    public void YieldStar_CapturesInheritedSyncIteratorFactoryAccessorWithOriginalReceiver(ExecutionMode mode)
    {
        var source = FactoryAccessorSource.Replace("const iterable = {", "const prototype = {")
            .Replace("async function* run()", "const iterable = Object.create(prototype); async function* run()");
        AssertFactoryAccessor(source, mode);
    }

    [Theory, ModeData]
    public void YieldStar_SyncIteratorFactoryGetterFailureReachesOuterCatch(ExecutionMode mode)
    {
        const string source = """
            const token = {};
            const iterable = { get [Symbol.iterator]() { throw token; } };
            async function* run() {
                try { yield* iterable; }
                catch (error) { yield error === token; }
            }
            run().next().then(result => console.log(result.value, result.done));
            """;
        Assert.Equal("true false\n", TestHarness.RunUnchecked(source, mode));
    }

    private static void AssertFactoryAccessor(string source, ExecutionMode mode)
    {
        Assert.Equal("lookup iterator true\nacquire true\nnext 1 undefined true\n7\nnext 1 9 true\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
