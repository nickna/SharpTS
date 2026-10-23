using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectFunctionDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-forms-and-configurability", """
            function declaration() { return 7; }
            function lockedDeclaration() { return 7; }
            const pairs: any[] = [[declaration, lockedDeclaration],
                [function() { return 7; }, function() { return 7; }], [() => 7, () => 7]];
            for (const pair of pairs) {
                for (const index of [0, 1]) {
                    const fn: any = pair[index], configurable = index === 0;
                    Object.defineProperty(fn, 'slot', { value: 1, writable: true, enumerable: true, configurable });
                    const result = Reflect.deleteProperty(fn, 'slot');
                    console.log(configurable, result, Object.hasOwn(fn, 'slot'),
                        Object.getOwnPropertyDescriptor(fn, 'slot') === undefined, fn());
                }
            }
            """, string.Concat(Enumerable.Repeat("true true false true 7\nfalse false true false 7\n", 3))];
        yield return ["recreation-after-reflect-deletion", """
            function original() {}
            const target: any = original;
            Object.defineProperty(target, 'slot', { value: 1, writable: true, enumerable: true, configurable: true });
            console.log(Reflect.deleteProperty(target, 'slot'), Object.hasOwn(target, 'slot'),
                Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            Object.defineProperty(target, 'slot', { value: 2, writable: true, enumerable: true, configurable: true });
            console.log(Object.hasOwn(target, 'slot'), Object.prototype.hasOwnProperty.call(target, 'slot'),
                target.slot, Object.getOwnPropertyDescriptor(target, 'slot')!.value, 'slot' in target,
                Object.prototype.propertyIsEnumerable.call(target, 'slot'));
            """, "true false true\ntrue true 2 2 true true\n"];
        yield return ["accessor-deletion-does-not-invoke-accessors", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}], marker = {};
            const state = { gets: 0, sets: 0 };
            const getter = function() { state.gets++; throw marker; };
            for (const fn of functions) {
                Object.defineProperty(fn, 'slot', { get: getter, configurable: true });
                console.log(Reflect.deleteProperty(fn, 'slot'), !Object.hasOwn(fn, 'slot'), state.gets, state.sets);
                Object.defineProperty(fn, 'slot', { set(value: any) { state.sets++; }, configurable: true });
                console.log(Reflect.deleteProperty(fn, 'slot'), !Object.hasOwn(fn, 'slot'), state.gets, state.sets);
                Object.defineProperty(fn, 'locked', { get: getter, configurable: false });
                console.log(Reflect.deleteProperty(fn, 'locked'), Object.hasOwn(fn, 'locked'),
                    Object.getOwnPropertyDescriptor(fn, 'locked')!.get === getter, state.gets, state.sets);
            }
            """, string.Concat(Enumerable.Repeat("true true 0 0\ntrue true 0 0\nfalse true true 0 0\n", 3))];
        yield return ["absent-properties-and-integrity", """
            function create(kind: number): any {
                if (kind === 0) { function declaration() { return 7; } return declaration; }
                if (kind === 1) return function() { return 7; };
                return () => 7;
            }
            for (const kind of [0, 1, 2]) {
                for (const integrity of ['preventExtensions', 'seal', 'freeze']) {
                    const fn: any = create(kind);
                    Object.defineProperty(fn, 'slot', { value: 1, configurable: true });
                    if (integrity === 'preventExtensions') Object.preventExtensions(fn);
                    else if (integrity === 'seal') Object.seal(fn); else Object.freeze(fn);
                    console.log(integrity, Reflect.deleteProperty(fn, 'missing'), Reflect.deleteProperty(fn, 'slot'),
                        Object.hasOwn(fn, 'slot'), Reflect.deleteProperty(fn, 'slot'), fn());
                }
            }
            """, string.Concat(Enumerable.Repeat("preventExtensions true true false true 7\nseal true false true false 7\nfreeze true false true false 7\n", 3))];
        yield return ["symbol-isolation-and-object-key-conversion", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}], symbol = Symbol('slot'), marker = {};
            for (const fn of functions) {
                fn.slot = 1; fn[symbol] = 2; fn[String(symbol)] = 3;
                console.log(Reflect.deleteProperty(fn, 'slot'), !Object.hasOwn(fn, 'slot'), fn[symbol], fn[String(symbol)]);
                console.log(Reflect.deleteProperty(fn, symbol), fn[symbol] === undefined, fn[String(symbol)]);
                const state = { calls: 0, hint: '', targets: 0, keys: 0 };
                const key: any = { [Symbol.toPrimitive](hint: string) { state.calls++; state.hint = hint; return 'slot'; } };
                function target(): any { state.targets++; return fn; }
                function property(): any { state.keys++; return key; }
                Object.defineProperty(fn, 'slot', { value: 4, configurable: true });
                console.log(Reflect.deleteProperty(target(), property()), !Object.hasOwn(fn, 'slot'), state.calls,
                    state.hint, state.targets, state.keys);
                Object.defineProperty(fn, 'slot', { value: 5, configurable: true });
                const bad: any = { [Symbol.toPrimitive]() { throw marker; } };
                try { Reflect.deleteProperty(fn, bad); } catch (error) { console.log(error === marker, fn.slot); }
            }
            """, string.Concat(Enumerable.Repeat("true true 2 3\ntrue true 3\ntrue true 1 string 1 1\ntrue 5\n", 3))];
        yield return ["awaited-target-and-key", """
            const fn: any = () => 7, state = { targets: 0, keys: 0, conversions: 0 };
            Object.defineProperty(fn, 'slot', { value: 1, configurable: true });
            const property: any = { [Symbol.toPrimitive]() { state.conversions++; return 'slot'; } };
            async function getTarget(): Promise<any> { state.targets++; return fn; }
            async function getKey(): Promise<any> { state.keys++; return property; }
            async function run() {
                const remove: any = Reflect.deleteProperty;
                const target = await getTarget(), key = await getKey();
                console.log(remove(target, key), !Object.hasOwn(fn, 'slot'), state.targets, state.keys, state.conversions, fn());
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true true 1 1 1 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReflectDeletionUsesOrdinaryFunctionOwnDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
