using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorOwnPropertyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            function* values() {}
            async function* asyncValues() {}
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                Object.defineProperty(iterator, 'extra', { value: 7, configurable: true });
                console.log(iterator.extra, Object.hasOwn(iterator, 'extra'));
            }
            """, "7 true\n7 true\n"];
        yield return ["delete-semantics", """
            function* values() {}
            async function* asyncValues() {}
            function remove(target: any) { 'use strict'; return delete target.missing; }
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                Object.freeze(iterator);
                console.log(delete iterator.missing, delete iterator.next, remove(iterator),
                    Reflect.deleteProperty(new Proxy(iterator, {}), 'missing'));
            }
            """, "true true true true\ntrue true true true\n"];
        yield return ["delete-key-coercion", """
            function* values() {}
            async function* asyncValues() {}
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory(), symbol = Symbol('key'), marker = {}, state = { calls: 0 };
                iterator[symbol] = 7;
                const key: any = { [Symbol.toPrimitive]() { state.calls++; return symbol; } };
                console.log(delete iterator[key], Object.hasOwn(iterator, symbol), state.calls);
                const throwing: any = { [Symbol.toPrimitive]() { throw marker; } };
                try { delete iterator[throwing]; }
                catch (error) { console.log(error === marker); }
            }
            """, "true false 1\ntrue\ntrue false 1\ntrue\n"];
        yield return ["compound-and-updates", """
            function* values() {}
            async function* asyncValues() {}
            const symbol = Symbol('counter');
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                const state = { value: 2, gets: 0, sets: 0, keys: 0, rhs: 0, valid: true };
                Object.defineProperty(iterator, symbol, {
                    get() { state.gets++; state.valid = state.valid && this === iterator; return state.value; },
                    set(value: any) { state.sets++; state.valid = state.valid && this === iterator; state.value = value; }
                });
                // Explicit any avoids the independent Symbol-index checker gap (#2367).
                function key(): any { state.keys++; return symbol; }
                iterator[key()] += 3;
                iterator[key()] ||= ++state.rhs;
                console.log(iterator[key()]++, ++iterator[key()], iterator[symbol],
                    state.gets, state.sets, state.keys, state.rhs, state.valid);
            }
            """, string.Concat(Enumerable.Repeat("5 7 7 5 3 4 0 true\n", 2))];
        yield return ["dot-updates", """
            function* values() {}
            async function* asyncValues() {}
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                iterator.count = 1;
                iterator.count += 2;
                console.log(iterator.count++, ++iterator.count, iterator.count);
            }
            """, "3 5 5\n3 5 5\n"];
        yield return ["descriptors-and-state", """
            const state = { starts: 0, gets: 0, sets: 0, valid: true, stored: 0 };
            function* values() { state.starts++; yield 3; }
            async function* asyncValues() { state.starts++; yield 4; }
            const symbol = Symbol('extra');
            async function run() {
                for (const factory of [values, asyncValues]) {
                    const iterator: any = factory(), prototype = Object.getPrototypeOf(iterator);
                    Object.defineProperty(iterator, 'extra', { value: 7, configurable: true });
                    Object.defineProperty(iterator, symbol, {
                        get() { state.gets++; state.valid = state.valid && this === iterator; return state.stored; },
                        set(value: any) { state.sets++; state.valid = state.valid && this === iterator; state.stored = value; },
                        enumerable: true, configurable: true
                    });
                    iterator.extra = 9;
                    iterator[symbol] = 8;
                    const descriptor: any = Reflect.getOwnPropertyDescriptor(iterator, 'extra');
                    console.log(typeof iterator, iterator.extra, descriptor.writable, descriptor.enumerable,
                        descriptor.configurable, Object.getPrototypeOf(iterator) === prototype);
                    const accessor: any = Object.getOwnPropertyDescriptor(iterator, symbol);
                    console.log(typeof accessor.get, typeof accessor.set, iterator[symbol],
                        Object.hasOwn(iterator, symbol), symbol in iterator, state.valid);
                    const result = await iterator.next();
                    console.log(result.value, result.done, state.starts, state.gets, state.sets);
                    await iterator.return();
                }
            }
            run();
            """, "object 7 false false true true\nfunction function 8 true true true\n3 false 1 1 1\n"
                + "object 7 false false true true\nfunction function 8 true true true\n4 false 2 2 2\n"];
        yield return ["keys-copy-and-delete", """
            function* values() {}
            async function* asyncValues() {}
            const symbol = Symbol('extra');
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                Object.defineProperty(iterator, 'visible', { value: 1, writable: true, enumerable: true, configurable: true });
                iterator[symbol] = 2;
                Object.defineProperty(iterator, 'hidden', { value: 3, configurable: true });
                console.log(Object.keys(iterator).join(','), Object.getOwnPropertyNames(iterator).join(','),
                    Object.getOwnPropertySymbols(iterator)[0] === symbol, Reflect.ownKeys(iterator).length);
                console.log(iterator.hasOwnProperty('hidden'), iterator.propertyIsEnumerable('hidden'),
                    iterator.propertyIsEnumerable(symbol), 'next' in iterator, Object.hasOwn(iterator, 'next'));
                const assigned: any = Object.assign({}, iterator), spread: any = { ...iterator };
                console.log(assigned.visible, assigned[symbol], spread.visible, spread[symbol], Object.hasOwn(spread, 'hidden'));
                const keys: string[] = [];
                for (const key in iterator) keys.push(key);
                console.log(keys.join(','), delete iterator.visible, delete iterator[symbol],
                    Reflect.deleteProperty(iterator, 'hidden'), Reflect.ownKeys(iterator).length);
            }
            """, string.Concat(Enumerable.Repeat("visible visible,hidden true 3\ntrue false true true false\n"
                + "1 2 1 2 false\nvisible true true true 0\n", 2))];
        yield return ["rest-symbol", """
            function* values() {}
            async function* asyncValues() {}
            const symbol = Symbol('extra');
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                Object.defineProperty(iterator, 'visible', { value: 1, enumerable: true });
                Object.defineProperty(iterator, 'hidden', { value: 3 });
                iterator[symbol] = 2;
                const { visible, ...rest } = iterator;
                console.log(visible, rest[symbol], Object.hasOwn(rest, 'visible'), Object.hasOwn(rest, 'hidden'));
            }
            """, "1 2 false false\n1 2 false false\n"];
        yield return ["all-descriptors", """
            function* values() {}
            async function* asyncValues() {}
            const symbol = Symbol('extra');
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory();
                iterator.visible = 1;
                iterator[symbol] = 2;
                Object.defineProperty(iterator, 'hidden', { value: 3 });
                const descriptors: any = Object.getOwnPropertyDescriptors(iterator);
                console.log(descriptors.visible.value, descriptors[symbol].value, descriptors.hidden.enumerable);
            }
            """, "1 2 false\n1 2 false\n"];
        yield return ["integrity-and-proxy", """
            function* values() {}
            async function* asyncValues() {}
            for (const factory of [values, asyncValues]) {
                const iterator: any = factory(), proxy: any = new Proxy(iterator, {});
                console.log(Reflect.defineProperty(proxy, 'extra', { value: 1, writable: true, configurable: true }),
                    proxy.extra, Reflect.set(proxy, 'extra', 2), iterator.extra, Reflect.ownKeys(proxy).join(','));
                Object.preventExtensions(iterator);
                iterator.newKey = 1;
                console.log(Object.isExtensible(iterator), Reflect.isExtensible(proxy),
                    Reflect.defineProperty(iterator, 'newKey', { value: 1 }), Reflect.set(iterator, 'newKey', 1),
                    Reflect.set(iterator, 'extra', 3), Object.hasOwn(iterator, 'newKey'));
                Object.seal(iterator);
                console.log(Object.isSealed(iterator), Reflect.deleteProperty(proxy, 'extra'), proxy.extra);
                Object.freeze(iterator);
                console.log(Object.isFrozen(iterator), Reflect.set(proxy, 'extra', 4), iterator.extra,
                    Reflect.getOwnPropertyDescriptor(proxy, 'extra')!.writable);
                try { Reflect.ownKeys(new Proxy(iterator, { ownKeys() { return []; } })); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            """, string.Concat(Enumerable.Repeat("true 1 true 2 extra\nfalse false false false true false\ntrue false 3\ntrue false 3 false\ntrue\n", 2))];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        // Compiled generator string assignments are lost (#2576).
        // Compiled indexed deletion also stringifies object-to-Symbol keys (#2579).
        ((string)row[0] is "all-descriptors" or "dot-updates" or "delete-key-coercion"
            ? new[] { ExecutionMode.Interpreted }
            : new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void GeneratorIteratorsKeepOrdinaryOwnDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        string output;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, compiledOutput) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            output = compiledOutput;
        }
        else
        {
            output = TestHarness.Run(source, mode);
        }
        Assert.Equal(expected, output);
    }
}
