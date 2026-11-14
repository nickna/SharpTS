using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ForAwaitFinalSyncValueTests
{
    [Theory, ModeData]
    public void FinalSyncValuesAreReadAndAdoptedBeforeCompletion(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            function source(kind) {
                return { [Symbol.iterator]() { return {
                    next() { return {
                        get done() { console.log('done'); return true; },
                        get value() {
                            console.log('value');
                            if (kind === 'getter') throw marker;
                            return { then(resolve, reject) {
                                console.log('then');
                                if (kind === 'reject') reject(marker); else resolve(7);
                            } };
                        }
                    }; },
                    return() { console.log('unexpected close'); return { done: true }; }
                }; } };
            }
            async function main() {
                for (const kind of ['resolve', 'getter', 'reject']) {
                    try { for await (const value of source(kind)) console.log('unexpected body'); }
                    catch (error) { console.log(error === marker); }
                }
                function* values() { return Promise.reject(marker); }
                try { for await (const value of values()) console.log('unexpected native body'); }
                catch (error) { console.log(error === marker); }
            }
            main();
            """;
        Assert.Equal("done\nvalue\nthen\ndone\nvalue\ntrue\ndone\nvalue\nthen\ntrue\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
