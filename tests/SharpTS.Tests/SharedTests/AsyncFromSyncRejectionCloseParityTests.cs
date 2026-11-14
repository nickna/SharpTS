using System.Text;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncFromSyncRejectionCloseParityTests
{
    [Theory, ModeData]
    public void RejectedValuesCloseTheSourceAndPreserveTheReason(ExecutionMode mode)
    {
        const string source = """
            const marker = {}, closeMarker = {};
            let request = '', failure = '', closing = '', first = true;
            function step() {
                let value;
                if (failure === 'constructor') {
                    value = Promise.resolve(1);
                    Object.defineProperty(value, 'constructor', { get() { console.log('constructor'); throw marker; } });
                } else if (failure === 'later') {
                    value = new Promise((resolve, reject) => setTimeout(() => reject(marker), 0));
                } else value = Promise.reject(marker);
                return { value, done: false };
            }
            const iterator = {
                next() {
                    if (request !== 'next' && first) { first = false; return { value: 1, done: false }; }
                    return step();
                },
                throw() { return step(); },
                get return() {
                    console.log('lookup');
                    if (closing === 'get') throw closeMarker;
                    if (closing === 'noncallable') return 1;
                    if (closing === 'missing') return undefined;
                    return function () {
                        console.log('close', arguments.length, this === iterator);
                        if (closing === 'call') throw closeMarker;
                        if (closing === 'primitive') return 1;
                        return { get done() { throw closeMarker; }, get value() { throw closeMarker; } };
                    };
                }
            };
            async function* run() { yield* { [Symbol.iterator]() { return iterator; } }; }
            async function check(operation, adoption, cleanup) {
                request = operation; failure = adoption; closing = cleanup; first = true;
                console.log(request, failure, closing);
                const generator = run();
                if (request === 'throw') await generator.next();
                try {
                    if (request === 'throw') await generator.throw('injected');
                    else await generator.next();
                } catch (error) { console.log('original', error === marker); }
                console.log((await generator.next()).done);
            }
            async function main() {
                for (const operation of ['next', 'throw'])
                for (const adoption of ['now', 'later', 'constructor'])
                for (const cleanup of ['normal', 'get', 'call', 'primitive', 'noncallable', 'missing'])
                    await check(operation, adoption, cleanup);
            }
            main();
            """;
        var expected = new StringBuilder();
        foreach (var operation in new[] { "next", "throw" })
        foreach (var adoption in new[] { "now", "later", "constructor" })
        foreach (var cleanup in new[] { "normal", "get", "call", "primitive", "noncallable", "missing" })
        {
            expected.AppendLine($"{operation} {adoption} {cleanup}");
            if (adoption == "constructor") expected.AppendLine("constructor");
            expected.AppendLine("lookup");
            if (cleanup is "normal" or "call" or "primitive") expected.AppendLine("close 0 true");
            expected.AppendLine("original true");
            expected.AppendLine("true");
        }
        Assert.Equal(expected.ToString().Replace("\r\n", "\n"), TestHarness.RunUnchecked(source, mode));
    }
}
