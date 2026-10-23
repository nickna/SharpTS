using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AwaitedAccessorCompoundAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return AccessorCompoundAssignmentTests.Cases().Single(row => (string)row[0] == "await-retains-the-receiver-and-read");
        yield return ["pending-numeric-declaration-writes-original-receiver-only-after-resolution", """
            const state = { value: 4, order: '', picks: 0, gets: 0, rights: 0, sets: 0, same: true };
            const holder: any = {
                get value() { state.order += 'get>'; state.gets++; state.same = state.same && this === holder; return state.value; },
                set value(next: number) { state.order += 'set>'; state.sets++; state.same = state.same && this === holder; state.value = next; }
            };
            const other: any = { value: 20 };
            let selected: any = holder;
            function receiver(): any { state.order += 'receiver>'; state.picks++; return selected; }
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function rhs(): Promise<number> { state.order += 'rhs>'; state.rights++; return pending; }
                async function change() { return receiver().value += await rhs(); }
                const task = change();
                console.log('pending', state.order, state.picks, state.gets, state.rights, state.sets, state.value);
                state.value = 100; selected = other;
                console.log('mutated', state.value, other.value, state.sets);
                state.order += 'resolve>'; release(2);
                const result = await task;
                console.log('settled', result, state.value, other.value, state.picks, state.gets, state.rights,
                    state.sets, state.same, state.order);
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "pending receiver>get>rhs> 1 1 1 0 4\nmutated 100 20 0\nsettled 6 6 20 1 1 1 1 true receiver>get>rhs>resolve>set>\n"];
        yield return ["pending-string-arrow-retains-old-primitive-and-single-reference", """
            const state = { value: 'left', order: '', picks: 0, gets: 0, rights: 0, sets: 0, same: true };
            const holder: any = {
                get value() { state.order += 'get>'; state.gets++; state.same = state.same && this === holder; return state.value; },
                set value(next: string) { state.order += 'set>'; state.sets++; state.same = state.same && this === holder; state.value = next; }
            };
            const other: any = { value: 'other' };
            let selected: any = holder;
            function receiver(): any { state.order += 'receiver>'; state.picks++; return selected; }
            async function run() {
                let release: (value: string) => void = () => {};
                const pending = new Promise<string>(resolve => { release = resolve; });
                function rhs(): Promise<string> { state.order += 'rhs>'; state.rights++; return pending; }
                const change = async () => receiver().value += await rhs();
                const task = change();
                console.log('pending', state.order, state.picks, state.gets, state.rights, state.sets, state.value);
                state.value = 'changed'; selected = other;
                console.log('mutated', state.value, other.value, state.sets);
                state.order += 'resolve>'; release('right');
                const result = await task;
                console.log('settled', result, state.value, other.value, state.picks, state.gets, state.rights,
                    state.sets, state.same, state.order);
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "pending receiver>get>rhs> 1 1 1 0 left\nmutated changed other 0\nsettled leftright leftright other 1 1 1 1 true receiver>get>rhs>resolve>set>\n"];
        yield return ["pending-rejection-preserves-identity-and-never-writes", """
            const state = { value: 4, order: '', picks: 0, gets: 0, rights: 0, sets: 0 };
            const holder: any = {
                get value() { state.order += 'get>'; state.gets++; return state.value; },
                set value(next: number) { state.order += 'set>'; state.sets++; state.value = next; }
            };
            function receiver(): any { state.order += 'receiver>'; state.picks++; return holder; }
            async function run() {
                let reject: (reason: any) => void = () => {};
                const marker = {};
                const pending = new Promise<number>((resolve, fail) => { reject = fail; });
                function rhs(): Promise<number> { state.order += 'rhs>'; state.rights++; return pending; }
                async function change() { return receiver().value += await rhs(); }
                const task = change();
                console.log('pending', state.order, state.picks, state.gets, state.rights, state.sets, state.value);
                state.order += 'reject>'; reject(marker);
                try { await task; console.log('unexpected fulfillment'); }
                catch (error) {
                    console.log('rejected', error === marker, state.value, state.picks, state.gets, state.rights,
                        state.sets, state.order);
                }
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "pending receiver>get>rhs> 1 1 1 0 4\nrejected true 4 1 1 1 0 receiver>get>rhs>reject>\n"];
        yield return ["hoisted-async-declarations-read-initialized-scalar-and-promise-cells", """
            async function outer() {
                const value = 7;
                async function read() { return value; }
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                async function nested() { return pending; }
                const task = nested();
                release(7);
                console.log(await read(), await task);
            }
            outer();
            """, "7 7\n"];
        yield return ["ordinary-and-arrow-async-outers-relay-live-scalar-and-promise-captures", """
            async function declarationOuter() {
                let declarationValue = 7;
                async function readDeclarationValue() { return declarationValue; }
                let releaseDeclaration: (value: number) => void = () => {};
                const declarationPending = new Promise<number>(resolve => { releaseDeclaration = resolve; });
                async function passDeclarationPromise() { return declarationPending; }
                const initialDeclaration = await readDeclarationValue();
                declarationValue = 9;
                const declarationTask = passDeclarationPromise();
                releaseDeclaration(7);
                console.log('declaration', initialDeclaration, await readDeclarationValue(), await declarationTask);
            }
            const arrowOuter = async () => {
                let arrowValue = 7;
                async function readArrowValue() { return arrowValue; }
                let releaseArrow: (value: number) => void = () => {};
                const arrowPending = new Promise<number>(resolve => { releaseArrow = resolve; });
                async function passArrowPromise() { return arrowPending; }
                const initialArrow = await readArrowValue();
                arrowValue = 9;
                const arrowTask = passArrowPromise();
                releaseArrow(7);
                console.log('arrow', initialArrow, await readArrowValue(), await arrowTask);
            };
            declarationOuter().then(() => arrowOuter())
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "declaration 7 9 7\narrow 7 9 7\n"];
        yield return ["async-arrow-promoted-capture-remains-visible-to-async-arrow-sibling", """
            const outer = async () => {
                const promotedValue = 7;
                async function readDeclaration() { return promotedValue; }
                const sibling = async () => promotedValue;
                console.log(await readDeclaration(), await sibling());
            };
            outer();
            """, "7 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AwaitedPropertyCompoundsRetainReferencesUntilSettlement(
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
