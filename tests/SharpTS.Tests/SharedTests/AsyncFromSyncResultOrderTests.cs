using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncFromSyncResultOrderTests
{
    public const string Source = """
        function step(tag, done, value) {
            return {
                get done() { console.log(tag, "done", done); return done; },
                get value() {
                    console.log(tag, "value");
                    return { then(resolve) {
                        console.log(tag, "then");
                        done = !done;
                        resolve(value);
                    } };
                }
            };
        }
        function source() {
            return {
                [Symbol.iterator]() {
                    let count = 0;
                    return {
                        next(sent) { return step("next", ++count > 1, count === 1 ? 7 : sent); },
                        return(sent) { return step("return", true, sent); },
                        throw(sent) { return step("throw", false, sent); }
                    };
                }
            };
        }
        async function* run() { return yield* source(); }
        async function show(result) { console.log(result.value, result.done); }
        const marker = {};
        const bad = {
            [Symbol.iterator]() {
                return { next() { return {
                    get done() { console.log("poison done"); throw marker; },
                    get value() { console.log("unexpected value"); return 0; }
                }; } };
            }
        };
        async function* fail() { try { yield* bad; } catch (error) { yield error === marker; } }
        async function main() {
            let generator = run();
            await show(await generator.next());
            await show(await generator.return(9));
            generator = run();
            await show(await generator.next());
            await show(await generator.throw(8));
            await show(await generator.next(11));
            for await (const value of source()) console.log("loop", value);
            await show(await fail().next());
            try { for await (const value of bad) console.log("unexpected loop"); }
            catch (error) { console.log(error === marker); }
        }
        main();
        """;

    public const string Expected =
        "next done false\nnext value\nnext then\n7 false\n" +
        "return done true\nreturn value\nreturn then\n9 true\n" +
        "next done false\nnext value\nnext then\n7 false\n" +
        "throw done false\nthrow value\nthrow then\n8 false\n" +
        "next done true\nnext value\nnext then\n11 true\n" +
        "next done false\nnext value\nnext then\nloop 7\n" +
        "next done true\nnext value\nnext then\n" +
        "poison done\ntrue false\npoison done\ntrue\n";

    [Theory, ModeData]
    public void SyncResults_ReadDoneBeforeValueAndPreserveSnapshotAcrossAdoption(ExecutionMode mode)
        => Assert.Equal(Expected, TestHarness.RunUnchecked(Source, mode));
}
