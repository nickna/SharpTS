using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayIndexedAccessorCompoundAssignmentTests
{
    public const string IssueSource = """
        const values: any[] = [1], token = {};
        let rights = 0;
        Object.defineProperty(values, "0", { get() { throw token; }, configurable: true });
        try { values[0] += (() => { rights++; return 2; })(); console.log("completed", rights); }
        catch (error) { console.log(error === token, rights); }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["throwing-own-indexed-getter-prevents-rhs-issue", IssueSource, "true 0\n"];
        yield return ["own-numeric-pair-retains-old-value-original-receiver-and-once-order", """
            const values: any[] = [1], other: any[] = [20];
            const state = { value: 4, order: '', picks: 0, indexes: 0, gets: 0, rights: 0, sets: 0, same: true };
            Object.defineProperty(values, '0', {
                get() { state.order += 'get>'; state.gets++; state.same = state.same && this === values; return state.value; },
                set(next: number) { state.order += 'set>'; state.sets++; state.same = state.same && this === values; state.value = next; },
                configurable: true
            });
            let selected: any[] = values;
            function receiver(): any[] { state.order += 'receiver>'; state.picks++; return selected; }
            function index(): number { state.order += 'index>'; state.indexes++; return 0; }
            function rhs(): number { state.order += 'rhs>'; state.rights++; state.value = 100; selected = other; return 2; }
            const result = receiver()[index()] += rhs();
            console.log(result, state.value, other[0], state.picks, state.indexes, state.gets, state.rights,
                state.sets, state.same, state.order, Object.getOwnPropertyDescriptor(values, '0')!.get !== undefined);
            """, "6 6 20 1 1 1 1 1 true receiver>index>get>rhs>set> true\n"];
        yield return ["own-numeric-index-string-pair-concatenates-primitives", """
            const values: any[] = ['seed'];
            const state = { value: 'left', order: '', gets: 0, rights: 0, sets: 0, same: true };
            Object.defineProperty(values, '0', {
                get() { state.order += 'get>'; state.gets++; state.same = state.same && this === values; return state.value; },
                set(next: string) { state.order += 'set>'; state.sets++; state.same = state.same && this === values; state.value = next; },
                configurable: true
            });
            function rhs(): string { state.order += 'rhs>'; state.rights++; state.value = 'changed'; return 'right'; }
            const result = values[0] += rhs();
            console.log(result, state.value, state.gets, state.rights, state.sets, state.same, state.order);
            """, "leftright leftright 1 1 1 true get>rhs>set>\n"];
        yield return ["own-getter-only-and-setter-only-permissions", """
            const getterOnly: any[] = [0];
            let reads = 0, rights = 0;
            Object.defineProperty(getterOnly, '0', { get() { reads++; return 4; }, configurable: true });
            function rhs(): number { rights++; return 2; }
            console.log(getterOnly[0] += rhs(), reads, rights);
            function strictWrite() { 'use strict'; return getterOnly[0] += rhs(); }
            try { strictWrite(); console.log('unexpected'); }
            catch (error) { console.log(error instanceof TypeError, reads, rights,
                Object.getOwnPropertyDescriptor(getterOnly, '0')!.set === undefined); }
            const setterOnly: any[] = [0];
            let sets = 0, last = 0;
            Object.defineProperty(setterOnly, '0', { set(next: number) { sets++; last = next; }, configurable: true });
            const result = setterOnly[0] += 1;
            console.log(Number.isNaN(result), Number.isNaN(last), sets,
                Object.getOwnPropertyDescriptor(setterOnly, '0')!.get === undefined);
            """, "6 1 1\ntrue 2 2 true\ntrue true 1 true\n"];
        yield return ["pending-own-pair-keeps-reference-and-defers-setter", """
            const values: any[] = [1], other: any[] = [20];
            const state = { value: 4, order: '', picks: 0, indexes: 0, gets: 0, rights: 0, sets: 0, same: true };
            Object.defineProperty(values, '0', {
                get() { state.order += 'get>'; state.gets++; state.same = state.same && this === values; return state.value; },
                set(next: number) { state.order += 'set>'; state.sets++; state.same = state.same && this === values; state.value = next; },
                configurable: true
            });
            let selected: any[] = values;
            function receiver(): any[] { state.order += 'receiver>'; state.picks++; return selected; }
            function index(): number { state.order += 'index>'; state.indexes++; return 0; }
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function rhs(): Promise<number> { state.order += 'rhs>'; state.rights++; return pending; }
                async function change() { return receiver()[index()] += await rhs(); }
                const task = change();
                console.log('pending', state.order, state.picks, state.indexes, state.gets, state.rights, state.sets);
                state.value = 100; selected = other;
                state.order += 'resolve>'; release(2);
                console.log('settled', await task, state.value, other[0], state.picks, state.indexes, state.gets,
                    state.rights, state.sets, state.same, state.order);
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "pending receiver>index>get>rhs> 1 1 1 1 0\nsettled 6 6 20 1 1 1 1 1 true receiver>index>get>rhs>resolve>set>\n"];
        yield return ["inherited-numeric-pair-keeps-receiver-and-no-own-shadow", """
            const values: any[] = new Array(1), state = { value: 4, gets: 0, sets: 0, same: true }, events: string[] = [];
            const prototype = Object.create(Array.prototype);
            Object.defineProperty(prototype, "0", {
                get() { state.gets++; state.same = state.same && this === values; events.push("get"); return state.value; },
                set(value: number) { state.sets++; state.same = state.same && this === values; events.push("set"); state.value = value; },
                configurable: true
            });
            Object.setPrototypeOf(values, prototype);
            const result = values[0] += (() => { events.push("rhs"); state.value = 100; return 2; })();
            console.log(result, state.value, state.gets, state.sets, state.same, Object.hasOwn(values, "0"), events.join(","));
            """, "6 6 1 1 true false get,rhs,set\n"];
        yield return ["inherited-setter-throw-preserves-marker-and-never-creates-own-element", """
            const values: any[] = new Array(1), marker = {};
            const state = { value: 4, last: 0, gets: 0, rights: 0, sets: 0, same: true, order: '' };
            const prototype = Object.create(Array.prototype);
            Object.defineProperty(prototype, '0', {
                get() { state.gets++; state.order += 'get>'; return state.value; },
                set(next: number) { state.sets++; state.last = next; state.same = state.same && this === values; state.order += 'set>'; throw marker; },
                configurable: true
            });
            Object.setPrototypeOf(values, prototype);
            function rhs(): number { state.rights++; state.order += 'rhs>'; state.value = 100; return 2; }
            try { values[0] += rhs(); console.log('unexpected'); }
            catch (error) {
                console.log(error === marker, state.last, state.value, state.gets, state.rights, state.sets,
                    state.same, Object.hasOwn(values, '0'), state.order);
            }
            """, "true 6 100 1 1 1 true false get>rhs>set>\n"];
        yield return ["rhs-replaced-inherited-descriptor-is-used-for-the-write", """
            const values: any[] = new Array(1);
            const state = { value: 4, last: 0, gets: 0, rights: 0, oldSets: 0, newSets: 0, same: true, order: '' };
            const prototype = Object.create(Array.prototype);
            Object.defineProperty(prototype, '0', {
                get() { state.gets++; state.order += 'get>'; return state.value; },
                set(next: number) { state.oldSets++; state.order += 'old>'; }, configurable: true
            });
            Object.setPrototypeOf(values, prototype);
            function rhs(): number {
                state.rights++; state.order += 'rhs>'; state.value = 100;
                // #2303: remove the old getter before defineProperty so replacement does not read it.
                delete prototype['0'];
                Object.defineProperty(prototype, '0', {
                    set(next: number) { state.newSets++; state.last = next; state.same = state.same && this === values; state.order += 'new>'; },
                    configurable: true
                });
                return 2;
            }
            const result = values[0] += rhs();
            console.log(result, state.last, state.gets, state.rights, state.oldSets, state.newSets,
                state.same, Object.hasOwn(values, '0'), state.order);
            """, "6 6 1 1 0 1 true false get>rhs>new>\n"];
        yield return ["nearest-getter-only-barrier-and-own-data-shadow-preserve-precedence", """
            const state = { nearGets: 0, farGets: 0, farSets: 0, rights: 0 };
            const farther = Object.create(Array.prototype);
            Object.defineProperty(farther, '0', {
                get() { state.farGets++; return 99; },
                set(next: number) { state.farSets++; }, configurable: true
            });
            const nearest = Object.create(farther);
            Object.defineProperty(nearest, '0', { get() { state.nearGets++; return 4; }, configurable: true });
            const blocked: any[] = new Array(1);
            Object.setPrototypeOf(blocked, nearest);
            function rhs(): number { state.rights++; return 2; }
            console.log(blocked[0] += rhs(), state.nearGets, state.farGets, state.farSets, state.rights,
                Object.hasOwn(blocked, '0'));
            function strictWrite() { 'use strict'; return blocked[0] += rhs(); }
            try { strictWrite(); console.log('unexpected'); }
            catch (error) {
                console.log(error instanceof TypeError, state.nearGets, state.farGets, state.farSets, state.rights,
                    Object.hasOwn(blocked, '0'));
            }
            const own: any[] = [4];
            Object.setPrototypeOf(own, farther);
            console.log(own[0] += 2, own[0], Object.hasOwn(own, '0'), state.farGets, state.farSets);
            """, "6 1 0 0 1 false\ntrue 2 0 0 2 false\n6 6 true 0 0\n"];
        yield return ["pending-inherited-pair-retains-reference-old-value-and-setter-receiver", """
            const values: any[] = new Array(1), other: any[] = [20];
            const state = { value: 4, order: '', picks: 0, indexes: 0, gets: 0, rights: 0, sets: 0, same: true };
            const prototype = Object.create(Array.prototype);
            Object.defineProperty(prototype, '0', {
                get() { state.order += 'get>'; state.gets++; state.same = state.same && this === values; return state.value; },
                set(next: number) { state.order += 'set>'; state.sets++; state.same = state.same && this === values; state.value = next; },
                configurable: true
            });
            Object.setPrototypeOf(values, prototype);
            let selected: any[] = values;
            function receiver(): any[] { state.order += 'receiver>'; state.picks++; return selected; }
            function index(): number { state.order += 'index>'; state.indexes++; return 0; }
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function rhs(): Promise<number> { state.order += 'rhs>'; state.rights++; return pending; }
                async function change() { return receiver()[index()] += await rhs(); }
                const task = change();
                console.log('pending', state.order, state.picks, state.indexes, state.gets, state.rights, state.sets);
                state.value = 100; selected = other;
                state.order += 'resolve>'; release(2);
                console.log('settled', await task, state.value, other[0], state.picks, state.indexes, state.gets,
                    state.rights, state.sets, state.same, Object.hasOwn(values, '0'), state.order);
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "pending receiver>index>get>rhs> 1 1 1 1 0\nsettled 6 6 20 1 1 1 1 1 true false receiver>index>get>rhs>resolve>set>\n"];
        yield return ["nonextensible-and-frozen-receivers-still-use-inherited-setters", """
            function strictWrite(values: any[]) { 'use strict'; return values[0] += 2; }
            function check(frozen: boolean) {
                const values: any[] = new Array(1), state = { value: 4, gets: 0, sets: 0, same: true };
                const prototype = Object.create(Array.prototype);
                Object.defineProperty(prototype, '0', {
                    get() { state.gets++; state.same = state.same && this === values; return state.value; },
                    set(next: number) { state.sets++; state.same = state.same && this === values; state.value = next; },
                    configurable: true
                });
                Object.setPrototypeOf(values, prototype);
                if (frozen) Object.freeze(values); else Object.preventExtensions(values);
                console.log(frozen, values[0] += 2, state.value, state.gets, state.sets, state.same,
                    Object.hasOwn(values, '0'), Object.isExtensible(values));
                console.log('strict', strictWrite(values), state.value, state.gets, state.sets, state.same,
                    Object.hasOwn(values, '0'));
            }
            check(false);
            check(true);
            """, "false 6 6 1 1 true false false\nstrict 8 8 2 2 true false\ntrue 6 6 1 1 true false false\nstrict 8 8 2 2 true false\n"];
        yield return ["nearest-native-data-properties-stop-farther-accessor-walk", """
            const state = { gets: 0, sets: 0 };
            const farther = Object.create(Array.prototype);
            Object.defineProperty(farther, "0", {
                get() { state.gets++; return 99; },
                set(next: number) { state.sets++; },
                configurable: true
            });
            const own: any[] = [4];
            Object.setPrototypeOf(own, farther);
            const ownResult = own[0] += 2;
            console.log("own", ownResult, own[0], state.gets, state.sets, Object.hasOwn(own, "0"));
            const plain: any = { "0": 4 };
            Object.setPrototypeOf(plain, farther);
            const plainChild: any[] = new Array(1);
            Object.setPrototypeOf(plainChild, plain);
            const plainResult = plainChild[0] += 2;
            console.log("plain", plainResult, plainChild[0], plain[0], state.gets, state.sets, Object.hasOwn(plainChild, "0"));
            const dense: any[] = [4];
            Object.setPrototypeOf(dense, farther);
            const denseChild: any[] = new Array(1);
            Object.setPrototypeOf(denseChild, dense);
            const denseResult = denseChild[0] += 2;
            console.log("dense", denseResult, denseChild[0], dense[0], state.gets, state.sets, Object.hasOwn(denseChild, "0"));
            """, "own 6 6 0 0 true\nplain 6 6 4 0 0 true\ndense 6 6 4 0 0 true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases();

    [Theory, MemberData(nameof(ModeCases))]
    public void NumericArrayCompoundsInvokeIndexedAccessorsInOrder(
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
