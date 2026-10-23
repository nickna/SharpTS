using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FunctionIntrinsicPrototypeMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["prototype-apis-and-callability", """
            const prototype: any = Function.prototype;
            const original = Object.getPrototypeOf(prototype);
            const call = prototype.call, apply = prototype.apply, bind = prototype.bind;
            const hasInstance = prototype[Symbol.hasInstance];
            console.log(Object.isExtensible(prototype), original === Object.prototype,
                Reflect.getPrototypeOf(prototype) === Object.prototype);
            const custom = { marker: 7 };
            console.log(Object.setPrototypeOf(prototype, custom) === prototype,
                Object.getPrototypeOf(prototype) === custom, Reflect.getPrototypeOf(prototype) === custom);
            console.log(prototype.marker, typeof prototype, prototype(1, 2) === undefined,
                prototype.call === call, prototype.apply === apply, prototype.bind === bind,
                prototype[Symbol.hasInstance] === hasInstance);
            console.log(Reflect.setPrototypeOf(prototype, null), Object.getPrototypeOf(prototype) === null,
                Reflect.getPrototypeOf(prototype) === null, prototype.marker, prototype.hasOwnProperty);
            console.log(prototype() === undefined, prototype.call === call);
            console.log(Object.setPrototypeOf(prototype, original) === prototype,
                Reflect.getPrototypeOf(prototype) === original, typeof prototype.hasOwnProperty);
            """, "true true true\ntrue true true\n7 function true true true true true\ntrue true true undefined undefined\ntrue true\ntrue true function\n"];
        yield return ["inherited-reads-receivers-and-presence", """
            const prototype: any = Function.prototype, custom: any = {};
            const data = Symbol("data"), receiver = Symbol("receiver"), masked = Symbol("masked");
            let reads = 0;
            custom.value = 7; custom[data] = 8; custom.present = undefined;
            Object.defineProperty(custom, "receiver", { get() { reads++; return (this as any).marker; } });
            Object.defineProperty(custom, receiver, { get() { reads++; return (this as any).marker; } });
            Object.defineProperty(custom, "masked", { get() { reads++; return 99; } });
            Object.defineProperty(custom, masked, { get() { reads++; return 99; } });
            Object.defineProperty(prototype, "masked", { set(value: any) {}, configurable: true });
            Object.defineProperty(prototype, masked, { set(value: any) {}, configurable: true });
            prototype.marker = 10;
            function ordinary() {}
            const arrow: any = () => {};
            (ordinary as any).marker = 11; arrow.marker = 12;
            Object.setPrototypeOf(prototype, custom);
            for (const target of [prototype, ordinary, arrow]) {
                console.log("receiver" in target, receiver in target, "present" in target,
                    Reflect.has(target, "receiver"), Reflect.has(target, receiver), reads);
                console.log(target.value, target[data], target.receiver, target[receiver],
                    target.masked, target[masked]);
            }
            console.log(Reflect.get(ordinary, "receiver", { marker: 20 }),
                Reflect.get(prototype, receiver, { marker: 21 }), reads);
            Object.setPrototypeOf(prototype, null);
            console.log((ordinary as any).value, (ordinary as any)[data], "receiver" in ordinary,
                receiver in ordinary, "present" in ordinary);
            """, "true true true true true 0\n7 8 10 10 undefined undefined\ntrue true true true true 2\n7 8 11 11 undefined undefined\ntrue true true true true 4\n7 8 12 12 undefined undefined\n20 21 8\nundefined undefined false false false\n"];
        yield return ["deleted-intrinsics-reveal-live-parent", """
            const prototype: any = Function.prototype;
            const original = Object.getPrototypeOf(prototype);
            const descriptors: any = Object.getOwnPropertyDescriptors(prototype), output: string[] = [];
            function record(...values: any[]) { output.push(values.map(String).join(" ")); }
            const custom = { bind: "inherited-bind", name: "inherited-name", length: 17, toString: "inherited-string" };
            Object.setPrototypeOf(prototype, custom);
            record(prototype.name === "", prototype.length, typeof prototype.bind, typeof prototype.toString);
            record(delete prototype.name, delete prototype.length, delete prototype.bind, delete prototype.toString);
            record(Object.hasOwn(prototype, "name"), Object.hasOwn(prototype, "length"),
                Object.hasOwn(prototype, "bind"), Object.hasOwn(prototype, "toString"));
            record(prototype.name, prototype.length, prototype.bind, prototype.toString,
                Reflect.get(prototype, "bind"));
            function ordinary() {}
            record((ordinary as any).bind, (ordinary as any).toString);
            Object.setPrototypeOf(prototype, null);
            record(prototype.name, prototype.length, prototype.bind, prototype.toString);
            Object.setPrototypeOf(prototype, original);
            record(prototype.bind, typeof prototype.toString, prototype() === undefined);
            Object.defineProperty(prototype, "name", descriptors.name);
            Object.defineProperty(prototype, "length", descriptors.length);
            Object.defineProperty(prototype, "bind", descriptors.bind);
            Object.defineProperty(prototype, "toString", descriptors.toString);
            for (const line of output) console.log(line);
            """, "true 0 function function\ntrue true true true\nfalse false false false\ninherited-name 17 inherited-bind inherited-string inherited-bind\ninherited-bind inherited-string\nundefined undefined undefined undefined\nundefined function true\n"];
        yield return ["inherited-writes-and-explicit-receiver", """
            const prototype: any = Function.prototype, custom: any = {}, key = Symbol("setter");
            Object.defineProperty(custom, "value", {
                get() { return (this as any).stored; },
                set(value: any) { (this as any).stored = value; }
            });
            Object.defineProperty(custom, key, {
                get() { return (this as any).symbolStored; },
                set(value: any) { (this as any).symbolStored = value; }
            });
            Object.defineProperty(custom, "readonly", { value: 1, writable: false });
            Object.defineProperty(custom, "getterOnly", { get() { return 2; } });
            Object.setPrototypeOf(prototype, custom);
            prototype.value = 3; prototype["value"] = 4; prototype[key] = 5;
            console.log(prototype.stored, prototype.symbolStored, Object.hasOwn(prototype, "value"),
                Object.hasOwn(prototype, key));
            const receiver: any = {};
            console.log(Reflect.set(prototype, "value", 6, receiver), Reflect.set(prototype, key, 7, receiver),
                receiver.stored, receiver.symbolStored, Object.hasOwn(receiver, "value"), Object.hasOwn(receiver, key));
            function ordinary() {}
            const native: any = Math.max;
            for (const target of [ordinary as any, native]) {
                target.value = 8; target[key] = 9;
                console.log(target.stored, target.symbolStored, Object.hasOwn(target, "value"), Object.hasOwn(target, key));
            }
            console.log(Reflect.set(prototype, "readonly", 10), Reflect.set(prototype, "getterOnly", 10),
                prototype.readonly, prototype.getterOnly);
            Object.defineProperty(ordinary, "value", { value: 12, writable: false });
            console.log(Reflect.set(ordinary, "value", 13), (ordinary as any).value, (ordinary as any).stored);
            """, "4 5 false false\ntrue true 6 7 false false\n8 9 false false\n8 9 false false\nfalse false 1 2\nfalse 12 8\n"];
        yield return ["cycle-and-validation-guards", """
            const prototype: any = Function.prototype;
            console.log(Reflect.setPrototypeOf(prototype, prototype));
            try { Object.setPrototypeOf(prototype, prototype); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Reflect.setPrototypeOf(prototype, prototype.toString),
                Reflect.setPrototypeOf(prototype, Object.prototype.hasOwnProperty));
            function ordinary() {}
            console.log(Reflect.setPrototypeOf(prototype, ordinary), Object.getPrototypeOf(prototype) === Object.prototype);
            const custom: any = {}, child: any = {};
            Object.setPrototypeOf(prototype, custom);
            Object.setPrototypeOf(child, prototype);
            console.log(Reflect.setPrototypeOf(custom, child), Object.getPrototypeOf(custom) === Object.prototype);
            try { Object.setPrototypeOf(custom, child); } catch (error) { console.log(error instanceof TypeError); }
            try { Object.setPrototypeOf(prototype, 1 as any); } catch (error) { console.log(error instanceof TypeError); }
            try { Reflect.setPrototypeOf(prototype, 1 as any); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.getPrototypeOf(prototype) === custom, Object.getPrototypeOf(child) === prototype);
            """, "false\ntrue\nfalse false\nfalse true\nfalse true\ntrue\ntrue\ntrue\ntrue true\n"];
        yield return ["integrity-and-proxy-forwarding", """
            const prototype: any = Function.prototype, custom = {};
            let reads = 0, writes = 0;
            const proxy: any = new Proxy(prototype, {
                getPrototypeOf(target: any) { reads++; return Reflect.getPrototypeOf(target); },
                setPrototypeOf(target: any, next: any) { writes++; return Reflect.setPrototypeOf(target, next); }
            });
            console.log(Object.setPrototypeOf(proxy, custom) === proxy, writes,
                Object.getPrototypeOf(proxy) === custom, reads, proxy() === undefined);
            console.log(Reflect.setPrototypeOf(proxy, null), writes, Reflect.getPrototypeOf(proxy) === null, reads);
            Object.preventExtensions(prototype);
            console.log(Object.setPrototypeOf(prototype, null) === prototype, Reflect.setPrototypeOf(prototype, null),
                Object.isExtensible(prototype));
            console.log(Reflect.setPrototypeOf(proxy, custom), writes, Reflect.getPrototypeOf(prototype) === null);
            try { Object.setPrototypeOf(prototype, custom); } catch (error) { console.log(error instanceof TypeError); }
            Object.seal(prototype);
            console.log(Object.isSealed(prototype), Reflect.setPrototypeOf(prototype, null),
                Reflect.setPrototypeOf(prototype, custom));
            Object.freeze(prototype);
            console.log(Object.isFrozen(prototype), Object.isSealed(prototype), Object.setPrototypeOf(prototype, null) === prototype,
                Reflect.setPrototypeOf(prototype, custom), prototype() === undefined);
            """, "true 1 true 1 true\ntrue 2 true 2\ntrue true false\nfalse 3 true\ntrue\ntrue true false\ntrue true true false true\n"];
        yield return ["absent-callable-metadata-and-inherited-write", """
            "use strict";
            const prototype: any = Function.prototype, custom: any = { prototype: 7, length: 21 };
            Object.setPrototypeOf(prototype, custom);
            const arrow: any = () => {};
            const ordinary: any = function (value: any) {};
            console.log(Object.hasOwn(arrow, "prototype"), arrow.prototype, arrow["prototype"],
                Reflect.get(arrow, "prototype"));
            const nativeToString: any = prototype.toString, nativeHasOwn: any = Object.prototype.hasOwnProperty;
            console.log(nativeToString.prototype, nativeHasOwn.prototype);
            console.log(delete ordinary.length, delete prototype.length);
            console.log(ordinary.length, prototype.length, arrow.length);
            ordinary.length = 22;
            prototype.length = 23;
            const descriptor: any = Object.getOwnPropertyDescriptor(ordinary, "length");
            console.log(ordinary.length, prototype.length, arrow.length, Object.hasOwn(ordinary, "length"),
                descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "false 7 7 7\n7 7\ntrue true\n21 21 0\n22 23 0 true true true true\n"];
        yield return ["symbol-constants-stay-values-through-parent-mutation", """
            const prototype: any = Function.prototype;
            const original = Object.getPrototypeOf(prototype);
            const iterator = Symbol.iterator, hasInstance = Symbol.hasInstance;
            console.log(typeof iterator, typeof hasInstance,
                Reflect.get(Symbol, "iterator") === iterator, Reflect.get(Symbol, "hasInstance") === hasInstance);
            Object.setPrototypeOf(prototype, {});
            console.log(Symbol.iterator === iterator, Symbol.hasInstance === hasInstance,
                Reflect.get(Symbol, "iterator") === iterator, Reflect.get(Symbol, "hasInstance") === hasInstance,
                prototype[hasInstance] === prototype[Symbol.hasInstance]);
            Object.setPrototypeOf(prototype, original);
            console.log(Symbol.iterator === iterator, Symbol.hasInstance === hasInstance,
                Reflect.get(Symbol, "iterator") === iterator, Reflect.get(Symbol, "hasInstance") === hasInstance);
            """, "symbol symbol true true\ntrue true true true true\ntrue true true true\n"];
        yield return ["virtual-constructor-assignment-preserves-descriptors", """
            const prototype: any = Function.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "constructor")!;
            try {
                prototype.constructor = 1;
                let descriptor = Object.getOwnPropertyDescriptor(prototype, "constructor")!;
                console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
                console.log(delete prototype.constructor, Object.hasOwn(prototype, "constructor"));
                prototype.constructor = 2;
                descriptor = Object.getOwnPropertyDescriptor(prototype, "constructor")!;
                console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            } finally {
                Object.defineProperty(prototype, "constructor", original);
            }
            """, "1 true false true\ntrue false\n2 true true true\n"];
    }

    public static IEnumerable<object[]> ParityCases()
        => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void FunctionIntrinsicUsesItsSelectedInternalPrototype(
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
