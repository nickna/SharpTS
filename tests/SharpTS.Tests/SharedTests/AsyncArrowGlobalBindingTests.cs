using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncArrowGlobalBindingTests
{
    [Theory, ModeData]
    public void ImplicitGlobalReadsRemainLiveAcrossAwait(ExecutionMode mode)
    {
        const string source = """
            const run = async () => {
                reviewGlobal = 7;
                console.log(reviewGlobal, globalThis.reviewGlobal);
                await Promise.resolve();
                globalThis.reviewGlobal = 9;
                console.log(reviewGlobal);
                try { console.log(reviewAbsent); }
                catch (error) { console.log(error instanceof ReferenceError); }
            };
            run();
            """;
        Assert.Equal("7 7\n9\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void StrictAssignmentAcceptsExistingGlobalBindingsAndChecksDescriptors(ExecutionMode mode)
    {
        const string source = """
            globalThis.reviewExisting = 1;
            Object.defineProperty(globalThis, 'reviewReadonly', { value: 3, configurable: true });
            let setterCalls = 0;
            Object.defineProperty(globalThis, 'reviewSetter', {
                get() { throw new Error('assignment must not read'); },
                set(value) { setterCalls++; console.log(this === globalThis, value); },
                configurable: true
            });
            const run = async () => {
                'use strict';
                reviewExisting = await Promise.resolve(7);
                console.log(reviewExisting, globalThis.reviewExisting);
                reviewSetter = 8;
                console.log(setterCalls);
                try { reviewReadonly = 9; } catch (error) { console.log(error instanceof TypeError); }
                try { reviewMissing = 10; } catch (error) { console.log(error instanceof ReferenceError); }
                console.log(globalThis.reviewReadonly, Object.hasOwn(globalThis, 'reviewMissing'));
            };
            run();
            """;
        Assert.Equal("7 7\ntrue 8\n1\ntrue\ntrue\n3 false\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, "async function run() { 'use strict'; reviewExisting = await Promise.resolve(7); console.log(reviewExisting); } run();", "7\n")]
    [InlineData(ExecutionMode.Compiled, "async function run() { 'use strict'; reviewExisting = await Promise.resolve(7); console.log(reviewExisting); } run();", "7\n")]
    [InlineData(ExecutionMode.Interpreted, "function* run() { 'use strict'; reviewExisting = yield 2; console.log(reviewExisting); } const iterator = run(); console.log(iterator.next().value); iterator.next(7);", "2\n7\n")]
    [InlineData(ExecutionMode.Compiled, "function* run() { 'use strict'; reviewExisting = yield 2; console.log(reviewExisting); } const iterator = run(); console.log(iterator.next().value); iterator.next(7);", "2\n7\n")]
    [InlineData(ExecutionMode.Interpreted, "async function* run() { 'use strict'; reviewExisting = yield 2; console.log(reviewExisting); } async function main() { const iterator = run(); console.log((await iterator.next()).value); await iterator.next(7); } main();", "2\n7\n")]
    [InlineData(ExecutionMode.Compiled, "async function* run() { 'use strict'; reviewExisting = yield 2; console.log(reviewExisting); } async function main() { const iterator = run(); console.log((await iterator.next()).value); await iterator.next(7); } main();", "2\n7\n")]
    public void StrictGlobalAssignmentSurvivesStateMachineSuspension(ExecutionMode mode, string body, string expected)
        => Assert.Equal(expected, TestHarness.RunUnchecked("globalThis.reviewExisting = 1;" + body, mode));

    [Theory]
    [InlineData(ExecutionMode.Interpreted, "const run = async () =>", "await Promise.resolve(7)", "run();", "7 0\n")]
    [InlineData(ExecutionMode.Compiled, "const run = async () =>", "await Promise.resolve(7)", "run();", "7 0\n")]
    [InlineData(ExecutionMode.Interpreted, "async function run()", "await Promise.resolve(7)", "run();", "7 0\n")]
    [InlineData(ExecutionMode.Compiled, "async function run()", "await Promise.resolve(7)", "run();", "7 0\n")]
    [InlineData(ExecutionMode.Interpreted, "function* run()", "yield 2", "const iterator = run(); console.log(iterator.next().value); iterator.next(7);", "2\n7 0\n")]
    [InlineData(ExecutionMode.Compiled, "function* run()", "yield 2", "const iterator = run(); console.log(iterator.next().value); iterator.next(7);", "2\n7 0\n")]
    [InlineData(ExecutionMode.Interpreted, "async function* run()", "yield 2", "async function main() { const iterator = run(); console.log((await iterator.next()).value); await iterator.next(7); } main();", "2\n7 0\n")]
    [InlineData(ExecutionMode.Compiled, "async function* run()", "yield 2", "async function main() { const iterator = run(); console.log((await iterator.next()).value); await iterator.next(7); } main();", "2\n7 0\n")]
    public void StrictGlobalReferenceSurvivesDestructuringDefaultSuspension(
        ExecutionMode mode, string declaration, string initializer, string invocation, string expected)
    {
        var source = $$"""
            globalThis.reviewTarget = 0;
            {{declaration}} {
                'use strict';
                let item;
                reviewTarget = ([item = {{initializer}}] = []);
                console.log(item, reviewTarget.length);
            };
            {{invocation}}
            """;
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }
}
