using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncGeneratorReturnAdoptionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-native-return", """
            async function* empty(): AsyncGenerator<any, any, any> {}

            async function main() {
                const fulfilled = await empty().return(Promise.resolve(42));
                console.log("promise", fulfilled.done, fulfilled.value === 42, typeof fulfilled.value);

                let thenCalls = 0;
                const thenable = {
                    then(resolve: (value: number) => void) {
                        thenCalls++;
                        resolve(43);
                    }
                };
                const adopted = await empty().return(thenable);
                console.log("thenable", adopted.done, adopted.value === 43, thenCalls);

                let rejected = false;
                try {
                    await empty().return(Promise.reject("return rejection"));
                } catch (error) {
                    rejected = error === "return rejection";
                }
                console.log("rejection", rejected);
            }

            main();
            """ + "\n", "promise true true number\nthenable true true 1\nrejection true\n"];
        yield return ["native-return-state-and-queue-control-supported", """
            async function* empty(): AsyncGenerator<any, any, any> {}

            async function* suspended(events: string[]): AsyncGenerator<any, any, any> {
                try {
                    yield 1;
                } finally {
                    events.push("cleanup");
                }
            }

            async function main() {
                const completed = empty();
                await completed.next();
                let getterCalls = 0;
                let release: any;
                let nested: any;
                const completedState = { returned: false, next: false };
                const delayed: any = {
                    get then() {
                        getterCalls++;
                        nested = completed.next();
                        nested.then(() => { completedState.next = true; });
                        return function (resolve: any) { release = resolve; };
                    }
                };
                const completedReturn = completed.return(delayed);
                completedReturn.then(() => { completedState.returned = true; });
                await Promise.resolve();
                console.log("completed-wait", getterCalls, completedState.returned, completedState.next);
                if (typeof release === "function") release(21);
                const completedResult = await completedReturn;
                const completedNext = nested ? await nested : undefined;
                console.log("completed-done", completedResult.value === 21, completedResult.done,
                    completedNext !== undefined && completedNext.value === undefined,
                    completedNext !== undefined && completedNext.done);

                const events: string[] = [];
                const iterator = suspended(events);
                await iterator.next();
                let resolvePending: any;
                const pending: Promise<number> = new Promise<number>(resolve => { resolvePending = resolve; });
                const returned = iterator.return(pending);
                const next = iterator.next();
                const state = { returned: false, next: false };
                returned.then(() => { state.returned = true; });
                next.then(() => { state.next = true; });
                const callerLocal = "caller";
                await Promise.resolve();
                console.log("suspended-wait", state.returned, state.next, events.join(","));
                resolvePending(31);
                const result = await returned;
                const following = await next;
                console.log("suspended-done", result.value === 31, result.done,
                    following.value === undefined, following.done, events.join(","), callerLocal);

                const marker = {};
                async function* recovering() {
                    try {
                        yield 1;
                    } catch (error) {
                        yield error === marker ? 2 : -1;
                    }
                    yield 3;
                    return 4;
                }
                const recovered = recovering();
                await recovered.next();
                const rejected: Promise<any> = Promise.reject(marker);
                const observed = rejected.catch(() => undefined);
                const caught = await recovered.return(rejected);
                const continued = await recovered.next();
                const finished = await recovered.next();
                console.log("rejected-return", caught.value, caught.done,
                    continued.value, continued.done, finished.value, finished.done);

                const poisoned: any = { get then() { throw marker; } };
                let poisonRequest: any;
                let synchronousThrow = false;
                let poisonRejected = false;
                try { poisonRequest = empty().return(poisoned); }
                catch (error) { synchronousThrow = error === marker; }
                try { await poisonRequest; }
                catch (error) { poisonRejected = error === marker; }
                console.log("poisoned", synchronousThrow, poisonRequest && typeof poisonRequest.then, poisonRejected);

                const rawPromise: Promise<number> = Promise.resolve(51);
                let rawIdentity = false;
                try { await empty().throw(rawPromise); }
                catch (error) { rawIdentity = error === rawPromise; }
                console.log("throw-identity", rawIdentity);

                const primitiveEvents: string[] = [];
                const primitive = suspended(primitiveEvents);
                await primitive.next();
                const primitiveReturn = primitive.return(7);
                console.log("primitive-before", primitiveEvents.join(","));
                const primitiveResult = await primitiveReturn;
                console.log("primitive-after", primitiveResult.value, primitiveResult.done, primitiveEvents.join(","));

                const settledEvents: string[] = [];
                const settled = suspended(settledEvents);
                await settled.next();
                const settledReturn = settled.return(Promise.resolve(8));
                console.log("settled-before", settledEvents.join(","));
                const settledResult = await settledReturn;
                console.log("settled-after", settledResult.value, settledResult.done, settledEvents.join(","));

                let caller: any;
                let releaseOther: any;
                let otherReturn: any;
                const otherPending: Promise<number> = new Promise<number>(resolve => { releaseOther = resolve; });
                async function* runningCaller(): AsyncGenerator<any, any, any> {
                    otherReturn = empty().return(otherPending);
                    yield 1;
                    yield 2;
                }
                caller = runningCaller();
                const callerFirst = await caller.next();
                console.log("caller-first", callerFirst.value, callerFirst.done);
                releaseOther(61);
                const otherResult = await otherReturn;
                const callerSecond = await caller.next();
                console.log("caller-next", otherResult.value === 61, otherResult.done, callerSecond.value, callerSecond.done);
                const callerClosed = await caller.next();
                console.log("caller-closed", callerClosed.value === undefined, callerClosed.done);
            }

            const task = main().catch((error: any) => { console.log("unexpected", error.name); });
            """ + "\n", "completed-wait 1 false false\ncompleted-done true true true true\nsuspended-wait false false \nsuspended-done true true true true cleanup caller\nrejected-return 2 false 3 false 4 true\npoisoned false function true\nthrow-identity true\nprimitive-before \nprimitive-after 7 true cleanup\nsettled-before \nsettled-after 8 true cleanup\ncaller-first 1 false\ncaller-next true true 2 false\ncaller-closed true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NativeReturnArgumentsAreAdoptedBeforeResumingOrCompletingTheRequest(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
