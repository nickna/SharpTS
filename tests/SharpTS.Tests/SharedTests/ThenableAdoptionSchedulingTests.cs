using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ThenableAdoptionSchedulingTests
{
    public const string IssueSource = """
        const value: any = { then(resolve: any) {
            resolve({ then(again: any) { again(47); } });
        } };
        Promise.resolve(value).then(result => console.log("resolved", result));
        setTimeout(() => console.log("timer"), 0);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-nested-adoption", IssueSource, "resolved 47\ntimer\n"];
        yield return ["timer-before-nested-resolution-and-reactions", """
            setTimeout(() => console.log("timer"), 0);
            const value: any = { then(resolve: any) {
                resolve({ then(again: any) {
                    again({ then(last: any) { last(47); } });
                } });
            } };
            const observed = Promise.resolve(value)
                .then(result => { console.log("resolved", result); return result + 1; })
                .then(result => console.log("chained", result));
            """, "resolved 47\nchained 48\ntimer\n"];
        yield return ["captured-then-and-microtask-fifo", """
            const order: string[] = [];
            const state = { gets: 0, calls: 0 };
            const value: any = {};
            Object.defineProperty(value, "then", {
                configurable: true,
                get() {
                    state.gets++;
                    order.push("get");
                    return function(this: any, resolve: any) {
                        state.calls++;
                        order.push(this === value ? "outer" : "wrong receiver");
                        resolve({ then(again: any) { order.push("inner"); again(47); } });
                        order.push("outer-return");
                    };
                }
            });
            const observed = Promise.resolve(value).then(result => {
                order.push("reaction:" + result);
                console.log(order.join(","), state.gets, state.calls);
            });
            Object.defineProperty(value, "then", { value() { order.push("replacement"); } });
            queueMicrotask(() => order.push("queued"));
            order.push("sync");
            setTimeout(() => console.log("timer"), 0);
            """, "get,sync,outer,outer-return,queued,inner,reaction:47 1 1\ntimer\n"];
        yield return ["first-settlement-and-nested-rejection", """
            const marker = {}, other = {};
            const fulfilled: any = { then(resolve: any, reject: any) {
                resolve({ then(again: any, fail: any) { again(47); fail(marker); throw other; } });
                reject(marker);
                throw other;
            } };
            const rejected: any = { then(resolve: any, reject: any) {
                reject(marker);
                resolve(99);
                throw other;
            } };
            const first = Promise.resolve(fulfilled).then(result => console.log("resolved", result));
            const second = Promise.resolve(rejected).then(
                () => console.log("wrong fulfillment"),
                error => console.log("rejected", error === marker));
            setTimeout(() => console.log("timer"), 0);
            """, "rejected true\nresolved 47\ntimer\n"];
        yield return ["nested-getter-rejection", """
            const marker = {}, state = { gets: 0 }, inner: any = {};
            Object.defineProperty(inner, "then", { get() { state.gets++; throw marker; } });
            const value: any = { then(resolve: any) { resolve(inner); } };
            const observed = Promise.resolve(value).then(
                () => console.log("wrong fulfillment"),
                error => console.log(error === marker, state.gets));
            setTimeout(() => console.log("timer"), 0);
            """, "true 1\ntimer\n"];
        yield return ["loop-inside-then-keeps-job-deferred", """
            const order: string[] = [];
            setTimeout(() => console.log("timer"), 0);
            const value: any = { then(resolve: any) {
                order.push("outer");
                resolve({ then(again: any) { order.push("inner"); again(47); } });
                for (let index = 0; index < 1; index++) order.push("loop");
                order.push("outer-return");
            } };
            const observed = Promise.resolve(value).then(result => console.log(order.join(","), result));
            """, "outer,loop,outer-return,inner 47\ntimer\n"];
        yield return ["nested-self-resolution", """
            const value: any = { then(resolve: any) {
                resolve({ then(again: any) { again(pending); } });
            } };
            const pending: any = Promise.resolve(value);
            const observed = pending.catch((error: any) => console.log(error instanceof TypeError));
            setTimeout(() => console.log("timer"), 0);
            """, "true\ntimer\n"];
        yield return ["deeper-cycle-with-stored-definition-result", """
            const state = { reads: 0 };
            const marker = {};
            const value: any = { then(resolve: any) {
                resolve({ then(again: any) {
                    again({ then(last: any) { last(pending); } });
                } });
            } };
            const pending: any = Promise.resolve(value);
            const observed = pending.catch((error: any) => console.log(error instanceof TypeError, state.reads));
            const configured = Object.defineProperty(pending, 'then', {
                configurable: true,
                get() { state.reads++; throw marker; }
            });
            setTimeout(() => console.log('timer', state.reads), 0);
            """, "true 0\ntimer 0\n"];
        yield return ["timer-released-nested-self-resolution", """
            const state = { release: null as any };
            const value: any = { then(resolve: any) {
                resolve({ then(again: any) { state.release = again; } });
            } };
            const pending: any = Promise.resolve(value);
            const observed = pending.catch((error: any) => console.log(error instanceof TypeError));
            setTimeout(() => {
                console.log('release');
                state.release(pending);
                setTimeout(() => console.log('done'), 0);
            }, 0);
            """, "release\ntrue\ndone\n"];
        yield return ["first-settlement-and-independent-foreign-promise-adoption", """
            const marker = {};
            let first: any;
            const stable: any = { then(resolve: any, reject: any) {
                resolve(17);
                resolve(first);
                reject(marker);
                throw marker;
            } };
            first = Promise.resolve(stable);
            const firstObserved = first.then((value: any) => console.log('first', value));
            function make(cycle: boolean, foreign: any) {
                let pending: any;
                const value: any = { then(resolve: any) {
                    resolve({ then(again: any) { again(cycle ? pending : foreign); } });
                } };
                pending = Promise.resolve(value);
                return pending;
            }
            const cycle = make(true, 0);
            const foreign = Promise.resolve(23);
            const adopted = make(false, foreign);
            const cycleObserved = cycle.catch((error: any) => console.log('cycle', error instanceof TypeError));
            const foreignObserved = adopted.then((value: any) => console.log('foreign', value, adopted !== foreign));
            setTimeout(() => console.log('timer'), 0);
            """, "first 17\ncycle true\nforeign 23 true\ntimer\n"];
    }

    public static IEnumerable<object[]> CompiledCases() => Cases();

    public static IEnumerable<object[]> ModeCases() => Cases()
        .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted })
        .Concat(CompiledCases()
            .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Compiled }));

    public static IEnumerable<object[]> SavedCases() => Cases()
        .Where(row => (string)row[0] is "original-nested-adoption" or
            "timer-before-nested-resolution-and-reactions" or
            "captured-then-and-microtask-fifo" or "loop-inside-then-keeps-job-deferred" or
            "nested-self-resolution" or
            "deeper-cycle-with-stored-definition-result" or
            "timer-released-nested-self-resolution" or
            "first-settlement-and-independent-foreign-promise-adoption")
        .Concat(SynchronousLoopMicrotaskTests.Cases()
            .Where(row => (string)row[0] == "thenable-and-due-timer"));

    [Theory, MemberData(nameof(ModeCases))]
    public void ThenableJobsDrainBeforeDueTimers(
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
