using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncFromSyncMissingThrowTests
{
    public const string Source = """
        function source(kind, marker) {
            const iterator: any = {
                next() { return { value: 1, done: false }; },
                return() {
                    console.log("close", arguments.length, this === iterator);
                    if (kind === "bad-result") return 1;
                    if (kind === "poison-call") throw marker;
                    return {
                        get done() { console.log("unexpected done"); return true; },
                        get value() { console.log("unexpected value"); return 9; }
                    };
                }
            };
            if (kind === "null-throw") iterator.throw = null;
            if (kind === "no-return") iterator.return = undefined;
            if (kind === "null-return") iterator.return = null;
            if (kind === "bad-return") iterator.return = 1;
            if (kind === "poison-get") Object.defineProperty(iterator, "return", {
                get() { console.log("lookup"); throw marker; }
            });
            return { [Symbol.iterator]() { return iterator; } };
        }
        async function* run(iterable, marker) {
            try { yield* iterable; }
            catch (error) {
                console.log(error === marker ? "marker" : error.name);
                yield "caught";
            }
            yield "after";
        }
        async function main() {
            const marker = { tag: 7 };
            for (const kind of ["missing", "null-throw", "no-return", "null-return",
                                "bad-result", "poison-get", "poison-call", "bad-return"]) {
                console.log(kind);
                const generator = run(source(kind, marker), marker);
                console.log((await generator.next()).value);
                console.log((await generator.throw("boom")).value);
                console.log((await generator.next()).value);
                console.log((await generator.next()).done);
            }
        }
        main();
        """;

    public const string Expected =
        "missing\n1\nclose 0 true\nTypeError\ncaught\nafter\ntrue\n" +
        "null-throw\n1\nclose 0 true\nTypeError\ncaught\nafter\ntrue\n" +
        "no-return\n1\nTypeError\ncaught\nafter\ntrue\n" +
        "null-return\n1\nTypeError\ncaught\nafter\ntrue\n" +
        "bad-result\n1\nclose 0 true\nTypeError\ncaught\nafter\ntrue\n" +
        "poison-get\n1\nlookup\nmarker\ncaught\nafter\ntrue\n" +
        "poison-call\n1\nclose 0 true\nmarker\ncaught\nafter\ntrue\n" +
        "bad-return\n1\nTypeError\ncaught\nafter\ntrue\n";

    [Theory, ModeData]
    public void MissingSyncThrow_ClosesAndRejectsWithTypeErrorOrCloseFailure(ExecutionMode mode)
        => Assert.Equal(Expected, TestHarness.RunUnchecked(Source, mode));
}
