using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ForAwaitBodyThrowCleanupTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool asynchronous in new[] { false, true })
        foreach (bool arrow in new[] { false, true })
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [asynchronous, arrow, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public void BodyFailuresCloseBeforePropagatingAndRetainTheOriginalError(
        bool asynchronous, bool arrow, ExecutionMode mode)
    {
        var source = """
            const marker = {};
            function source(tag, closeFailure) {
                const iterator = {
                    [Symbol.ITERATOR]() { return this; },
                    next() { return STEP({ value: 1, done: false }); },
                    return() {
                        console.log('close', tag, arguments.length, this === iterator);
                        if (closeFailure) return Promise.reject('close failure');
                        return new Promise(resolve => setTimeout(() => {
                            console.log('closed', tag);
                            resolve({ done: true });
                        }, 0));
                    }
                };
                return iterator;
            }
            function fail() { throw marker; }
            async function runCase(tag, kind) {
                try {
                    for await (const value of source(tag, kind === 3)) {
                        if (kind === 0) throw marker;
                        if (kind === 1) await Promise.reject(marker);
                        await Promise.resolve();
                        fail();
                    }
                } catch (error) { console.log('caught', tag, error === marker); }
            }
            UNHANDLED {
                for await (const value of source('unhandled', false)) {
                    await Promise.resolve();
                    throw marker;
                }
            }END
            async function main() {
                await runCase('throw', 0);
                await runCase('reject', 1);
                await runCase('call', 2);
                await runCase('close-failure', 3);
                await unhandled().catch(error => console.log('rejected', error === marker));
            }
            main();
            """;
        source = source.Replace("Symbol.ITERATOR", asynchronous ? "Symbol.asyncIterator" : "Symbol.iterator")
            .Replace("STEP({ value: 1, done: false })", asynchronous
                ? "Promise.resolve({ value: 1, done: false })" : "{ value: 1, done: false }")
            .Replace("async function runCase(tag, kind)", arrow ? "const runCase = async (tag, kind) =>" : "async function runCase(tag, kind)")
            .Replace("UNHANDLED", arrow ? "const unhandled = async () =>" : "async function unhandled()")
            .Replace("}END", arrow ? "};" : "}");
        // A sync iterator's return result is adopted through Async-from-Sync: its value
        // promise is awaited, while a genuinely async iterator awaits return itself.
        if (!asynchronous)
            source = source.Replace("return Promise.reject('close failure');", "throw 'close failure';")
                .Replace("return new Promise(resolve => setTimeout(() => {", "return { value: new Promise(resolve => setTimeout(() => {")
                .Replace("resolve({ done: true });", "resolve(undefined);")
                .Replace("}, 0));", "}, 0)), done: true };");

        var expected = """
            close throw 0 true
            closed throw
            caught throw true
            close reject 0 true
            closed reject
            caught reject true
            close call 0 true
            closed call
            caught call true
            close close-failure 0 true
            caught close-failure true
            close unhandled 0 true
            closed unhandled
            rejected true
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }
}
