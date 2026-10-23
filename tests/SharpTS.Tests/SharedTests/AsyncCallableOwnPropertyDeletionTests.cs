using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncCallableOwnPropertyDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            const target: any = [async (first: any, second: any) => first][0];
            target.tag = 7;
            console.log(delete target.length, target.length, delete target.tag, target.tag);
            """, "true 0 true undefined\n"];
        yield return ["metadata-deletion-and-inherited-fallback", """
            async function declaration(first: any, second: any) { return first; }
            const expression: any = async function named(first: any, second: any) { return first; };
            const arrow: any = async (first: any, second: any) => first;
            const key = Symbol("method");
            class Owner { async [key](first: any, second: any) { return first; } }
            const method: any = (new Owner() as any)[key], prototype: any = Function.prototype;
            const savedName = Object.getOwnPropertyDescriptor(prototype, "name")!;
            const savedLength = Object.getOwnPropertyDescriptor(prototype, "length")!;
            function writeLength(target: any) { "use strict"; target.length = 5; }
            try {
                Object.defineProperty(prototype, "name", { value: "inherited-name", configurable: true });
                Object.defineProperty(prototype, "length", { value: 99, configurable: true });
                for (const target of [declaration as any, expression, arrow, method]) {
                    console.log(Object.hasOwn(target, "name"), Object.hasOwn(target, "length"),
                        Object.getOwnPropertyDescriptor(target, "length")!.configurable);
                    console.log(delete target.name, target.name, Reflect.get(target, "name"),
                        Object.hasOwn(target, "name"), Object.getOwnPropertyDescriptor(target, "name") === undefined);
                    console.log(Reflect.deleteProperty(target, "length"), target["length"], Reflect.get(target, "length"),
                        Object.hasOwn(target, "length"), Object.getOwnPropertyDescriptor(target, "length") === undefined);
                    Object.defineProperty(target, "length", { value: 4, writable: true, configurable: true });
                    writeLength(target);
                    console.log(target.length, Object.getOwnPropertyDescriptor(target, "length")!.writable);
                }
            } finally {
                Object.defineProperty(prototype, "name", savedName);
                Object.defineProperty(prototype, "length", savedLength);
            }
            """, "true true true\ntrue inherited-name inherited-name false true\ntrue 99 99 false true\n5 true\ntrue true true\ntrue inherited-name inherited-name false true\ntrue 99 99 false true\n5 true\ntrue true true\ntrue inherited-name inherited-name false true\ntrue 99 99 false true\n5 true\ntrue true true\ntrue inherited-name inherited-name false true\ntrue 99 99 false true\n5 true\n"];
        yield return ["string-symbol-identity-and-delete-parity", """
            async function declaration() {}
            const functions: any[] = [declaration, async function() {}, async () => {}];
            for (const target of functions) {
                const first = Symbol("same"), second = Symbol("same");
                target.slot = 1; target[first] = 2; target[second] = 3; target[String(first)] = 4;
                console.log(delete target["slot"], Reflect.deleteProperty(target, first),
                    Object.hasOwn(target, "slot"), Object.hasOwn(target, first),
                    target[second], target[String(first)]);
                const symbols = Object.getOwnPropertySymbols(target);
                console.log(symbols.length, symbols[0] === second, Object.keys(target).join(","),
                    Object.getOwnPropertyDescriptor(target, first) === undefined,
                    Reflect.getOwnPropertyDescriptor(target, first) === undefined);
                console.log(delete target[second], Reflect.deleteProperty(target, String(first)),
                    target[second], target[String(first)], Object.getOwnPropertySymbols(target).length,
                    delete target.absent, Reflect.deleteProperty(target, first));
            }
            """, "true true false false 3 4\n1 true Symbol(same) true true\ntrue true undefined undefined 0 true true\ntrue true false false 3 4\n1 true Symbol(same) true true\ntrue true undefined undefined 0 true true\ntrue true false false 3 4\n1 true Symbol(same) true true\ntrue true undefined undefined 0 true true\n"];
        yield return ["accessor-deletion-and-nonconfigurable-refusal", """
            async function declaration() {}
            const functions: any[] = [declaration, async function() {}, async () => {}], token = {};
            let reads = 0, writes = 0;
            const getter = function() { reads++; throw token; };
            const setter = function(value: any) { writes++; };
            for (const target of functions) {
                const symbol = Symbol("accessor"), locked = Symbol("locked");
                Object.defineProperty(target, "access", { get: getter, set: setter, configurable: true });
                Object.defineProperty(target, symbol, { get: getter, configurable: true });
                console.log(delete target.access, Reflect.deleteProperty(target, symbol),
                    Object.hasOwn(target, "access"), Object.hasOwn(target, symbol), reads, writes);
                Object.defineProperty(target, "setterOnly", { set: setter, configurable: true });
                console.log(Reflect.deleteProperty(target, "setterOnly"), reads, writes);
                Object.defineProperty(target, "locked", { value: 7, configurable: false });
                Object.defineProperty(target, locked, { get: getter, configurable: false });
                console.log(delete target.locked, Reflect.deleteProperty(target, "locked"), target.locked,
                    delete target[locked], Reflect.deleteProperty(target, locked),
                    Object.getOwnPropertyDescriptor(target, locked)!.get === getter, reads, writes);
            }
            """, "true true false false 0 0\ntrue 0 0\nfalse false 7 false false true 0 0\ntrue true false false 0 0\ntrue 0 0\nfalse false 7 false false true 0 0\ntrue true false false 0 0\ntrue 0 0\nfalse false 7 false false true 0 0\n"];
        yield return ["absent-and-integrity-deletion", """
            function create(kind: number): any {
                if (kind === 0) { async function declaration() { return 7; } return declaration; }
                if (kind === 1) return async function() { return 7; };
                return async () => 7;
            }
            for (const kind of [0, 1, 2]) {
                for (const integrity of ["preventExtensions", "seal", "freeze"]) {
                    const target: any = create(kind), symbol = Symbol("slot");
                    Object.defineProperty(target, "slot", { value: 1, configurable: true });
                    Object.defineProperty(target, symbol, { value: 2, configurable: true });
                    if (integrity === "preventExtensions") Object.preventExtensions(target);
                    else if (integrity === "seal") Object.seal(target); else Object.freeze(target);
                    console.log(integrity, delete target.absent, Reflect.deleteProperty(target, Symbol("absent")),
                        delete target.slot, Reflect.deleteProperty(target, symbol),
                        Object.hasOwn(target, "slot"), Object.hasOwn(target, symbol), typeof target);
                }
            }
            """, "preventExtensions true true true true false false function\nseal true true false false true true function\nfreeze true true false false true true function\npreventExtensions true true true true false false function\nseal true true false false true true function\nfreeze true true false false true true function\npreventExtensions true true true true false false function\nseal true true false false true true function\nfreeze true true false false true true function\n"];
        yield return ["held-callables-retain-awaited-results", """
            async function declaration(value: number) { await Promise.resolve(); return value + 1; }
            const expression: any = async function(value: number) { await Promise.resolve(); return value + 2; };
            const arrow: any = async (value: number) => { await Promise.resolve(); return value + 3; };
            async function run() {
                for (const target of [declaration as any, expression, arrow]) {
                    const held = target, bound = target.bind({});
                    target.tag = 7;
                    const pending = held(10);
                    console.log(delete target.tag, Reflect.deleteProperty(target, "length"),
                        held.tag, Object.hasOwn(held, "length"));
                    console.log(await pending, await bound(20), await Reflect.apply(held, null, [30]));
                }
            }
            run().catch((error: any) => console.log("unexpected", error.name, error.message));
            """, "true true undefined false\n11 21 31\ntrue true undefined false\n12 22 32\ntrue true undefined false\n13 23 33\n"];
        yield return ["receiver-views-share-state-and-arrow-this", """
            const target: any = async function self() { return (self as any).tag; };
            target.tag = 7;
            class Owner {
                value = 30;
                make() { return async (amount: number) => { await Promise.resolve(); return this.value + amount; }; }
            }
            const owner = new Owner(), arrow: any = owner.make();
            arrow.tag = 8;
            async function run() {
                const held = target, bound = target.bind({});
                console.log(await held.call({}), await bound());
                console.log(delete held.tag, target.tag, Object.hasOwn(target, "tag"));
                console.log(await target.call({}), await bound());
                const boundArrow = arrow.bind({ value: 99 });
                console.log(delete arrow.tag, arrow.tag, await boundArrow(3), await arrow.call({ value: 99 }, 4));
            }
            run().catch((error: any) => console.log("unexpected", error.name, error.message));
            """, "7 7\ntrue undefined false\nundefined undefined\ntrue undefined 33 34\n"];
        yield return ["computed-async-method-metadata-and-strict-delete", """
            const key = Symbol("method");
            class Owner { async [key](first: any, second: any) { return first; } }
            const target: any = Object.getOwnPropertyDescriptor(Owner.prototype, key)!.value;
            const name = Object.getOwnPropertyDescriptor(target, "name")!;
            const length = Object.getOwnPropertyDescriptor(target, "length")!;
            console.log(target.name === "[method]", target.length, name.value === "[method]",
                name.writable, name.enumerable, name.configurable,
                length.writable, length.enumerable, length.configurable);
            function rename(fn: any) { "use strict"; fn.name = "changed"; }
            function removeName(fn: any) { "use strict"; return delete fn.name; }
            try { rename(target); } catch (error) { console.log(error instanceof TypeError, target.name === "[method]"); }
            console.log(removeName(target), Object.hasOwn(target, "name"),
                Object.getOwnPropertyDescriptor(target, "name") === undefined, Reflect.get(target, "name") === "");
            """, "true 2 true false false true false false true\ntrue true\ntrue false true true\n"];
        yield return ["strict-absent-and-frozen-string-deletion", """
            async function declaration() {}
            const functions: any[] = [declaration, async function() {}, async () => {}];
            function absentDot(fn: any) { "use strict"; return delete fn.absent; }
            function absentIndex(fn: any) { "use strict"; return delete fn["absent"]; }
            function lockedDot(fn: any) { "use strict"; return delete fn.slot; }
            function lockedIndex(fn: any) { "use strict"; return delete fn["slot"]; }
            for (const target of functions) {
                Object.defineProperty(target, "slot", { value: 1, writable: true, configurable: true });
                Object.freeze(target);
                console.log(absentDot(target), absentIndex(target), Reflect.deleteProperty(target, "absent"),
                    Object.hasOwn(target, "absent"), delete target.slot, Reflect.deleteProperty(target, "slot"));
                try { console.log("dot-result", lockedDot(target)); }
                catch (error) { console.log(error instanceof TypeError); }
                try { console.log("index-result", lockedIndex(target)); }
                catch (error) { console.log(error instanceof TypeError); }
                const descriptor = Object.getOwnPropertyDescriptor(target, "slot")!;
                console.log(descriptor.value, descriptor.writable, descriptor.configurable);
            }
            """, "true true true false false false\ntrue\ntrue\n1 false false\ntrue true true false false false\ntrue\ntrue\n1 false false\ntrue true true false false false\ntrue\ntrue\n1 false false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AsyncCallableDeletionUsesCanonicalOwnPropertyState(
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
