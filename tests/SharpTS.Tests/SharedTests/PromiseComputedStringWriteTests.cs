using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseComputedStringWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            class P extends Promise<number> {}
            const promise: any = new P(resolve => resolve(1));
            const key: string = "value";
            promise[key] = 42;
            console.log(promise[key]);
            """, "42\n"];
        yield return ["intrinsic-subclass-and-ordinary-controls", """
            class P extends Promise<number> {}
            class Ordinary {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2)), new Ordinary(), {}] as any[]) {
                const key: string = "value";
                target.dot = 3;
                target[key] = 42;
                console.log(target.dot, target.value, target[key], Object.hasOwn(target, key));
            }
            """, "3 42 42 true\n3 42 42 true\n3 42 42 true\n3 42 42 true\n"];
        yield return ["reference-order-and-key-coercion", """
            const promise: any = Promise.resolve(1), other: any = Promise.resolve(2);
            const events: string[] = [], token = {};
            let selected = promise;
            function receiver(): any { events.push("receiver"); return selected; }
            const key: any = { [Symbol.toPrimitive](hint: string) { events.push("key:" + hint); return "value"; } };
            function property(): any { events.push("key-expression"); return key; }
            function rhs(): any { events.push("rhs"); selected = other; return 7; }
            console.log(receiver()[property()] = rhs(), promise.value, Object.hasOwn(other, "value"), events.join(","));
            events.length = 0;
            const badKey: any = { [Symbol.toPrimitive](hint: string) { events.push("bad-key:" + hint); throw token; } };
            try { promise[badKey] = rhs(); }
            catch (error) { console.log(error === token, events.join(","), promise.value); }
            events.length = 0;
            function throwingKey(): any { events.push("key-expression"); throw token; }
            try { promise[throwingKey()] = rhs(); }
            catch (error) { console.log(error === token, events.join(","), promise.value); }
            """, "7 7 false receiver,key-expression,rhs,key:string\ntrue rhs,bad-key:string 7\ntrue key-expression 7\n"];
        yield return ["own-accessors-and-getterless-reads", """
            class P extends Promise<number> {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2))] as any[]) {
                let stored = 0, calls = 0, receiver = false;
                Object.defineProperty(target, "value", {
                    configurable: true,
                    get() { return stored; },
                    set(value: number) { calls++; receiver = this === target; stored = value; }
                });
                const key: string = "value";
                console.log(target[key] = 7, target[key], calls, receiver);
                Object.defineProperty(target, "only", {
                    configurable: true,
                    set(value: number) { stored = value; receiver = this === target; }
                });
                const only: string = "only";
                target[only] = 9;
                console.log(stored, receiver, target[only] === undefined);
            }
            """, "7 7 1 true\n9 true true\n7 7 1 true\n9 true true\n"];
        yield return ["inherited-subclass-setter-and-own-shadow", """
            class P extends Promise<number> {
                stored = 0;
                set value(value: number) { this.stored = value; }
                get value() { return this.stored; }
            }
            const target: any = new P(resolve => resolve(1));
            const key: string = "value";
            console.log(target[key] = 7, target.stored, target[key], Object.hasOwn(target, key));
            Object.defineProperty(target, key, { value: 3, writable: true, configurable: true });
            console.log(target[key] = 9, target.stored, target[key], Object.hasOwn(target, key));
            """, "7 7 7 false\n9 7 9 true\n"];
        yield return ["descriptor-and-integrity-guards", """
            class P extends Promise<number> {}
            function write(target: any, key: string, value: number) { "use strict"; target[key] = value; }
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2))] as any[]) {
                Object.defineProperty(target, "locked", { value: 1, writable: false, configurable: false });
                target["locked"] = 2;
                try { write(target, "locked", 3); } catch (error) { console.log(error instanceof TypeError, target.locked); }
                target.open = 4;
                Object.seal(target);
                target["open"] = 5;
                console.log(target.open, Object.getOwnPropertyDescriptor(target, "open")!.writable);
                Object.freeze(target);
                target["open"] = 6;
                target["missing"] = 7;
                try { write(target, "open", 8); } catch (error) { console.log(error instanceof TypeError, target.open); }
                try { write(target, "missing", 9); } catch (error) { console.log(error instanceof TypeError, Object.hasOwn(target, "missing")); }
            }
            """, "true 1\n5 true\ntrue 5\ntrue false\ntrue 1\n5 true\ntrue 5\ntrue false\n"];
        yield return ["frozen-accessor-setter-still-runs", """
            class P extends Promise<number> {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2))] as any[]) {
                let stored = 0, receiver = false;
                Object.defineProperty(target, "value", {
                    get() { return stored; },
                    set(value: number) { stored = value; receiver = this === target; },
                    configurable: true
                });
                Object.freeze(target);
                const key: string = "value";
                console.log(target[key] = 7, target[key], receiver, Object.isFrozen(target));
            }
            """, "7 7 true true\n7 7 true true\n"];
        yield return ["awaited-rhs-capture", """
            async function run() {
                const promise: any = Promise.resolve(1), other: any = Promise.resolve(2), events: string[] = [];
                let selected = promise;
                function receiver(): any { events.push("receiver"); return selected; }
                const key: any = { [Symbol.toPrimitive](hint: string) { events.push("key:" + hint); return "value"; } };
                function property(): any { events.push("key-expression"); return key; }
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                async function assign() { return receiver()[property()] = await pending; }
                const task = assign();
                console.log(events.join(","));
                selected = other; release(7);
                console.log(await task, promise.value, Object.hasOwn(other, "value"), events.join(","));
                console.log(await promise, await other);
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """, "receiver,key-expression\n7 7 false receiver,key-expression,key:string\n1 2\n"];
        yield return ["own-setter-dot-computed-and-flags-before-and-after-freeze", """
            class P extends Promise<number> {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2)), {}] as any[]) {
                let stored = 0, calls = 0, reads = 0, receiver = false;
                const getter = function(this: any) { reads++; return stored; };
                const setter = function(this: any, value: number) { calls++; stored = value; receiver = this === target; };
                Object.defineProperty(target, "value", { get: getter, set: setter, enumerable: true, configurable: true });
                const key: string = "value";
                console.log("open", target.value = 3, target[key] = 4, target.value, calls, receiver, Object.isFrozen(target));
                Object.freeze(target);
                const descriptor = Object.getOwnPropertyDescriptor(target, key)!;
                console.log("frozen", target.value = 7, target[key] = 8, target[key], calls, receiver,
                    Object.isFrozen(target), descriptor.get === getter, descriptor.set === setter,
                    descriptor.enumerable, descriptor.configurable, Object.hasOwn(descriptor, "writable"), reads);
            }
            """, "open 3 4 4 2 true false\nfrozen 7 8 8 4 true true true true true false false 2\nopen 3 4 4 2 true false\nfrozen 7 8 8 4 true true true true true false false 2\nopen 3 4 4 2 true false\nfrozen 7 8 8 4 true true true true true false false 2\n"];
        yield return ["frozen-setter-abrupt-identity-and-no-getter-evaluation", """
            class P extends Promise<number> {}
            function dot(target: any) { "use strict"; return target.value = 7; }
            function indexed(target: any, key: string) { "use strict"; return target[key] = 8; }
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2)), {}] as any[]) {
                const token = {}, events: string[] = [];
                Object.defineProperty(target, "value", {
                    get() { events.push("get"); return 0; },
                    set(value: number) { events.push("set:" + value + ":" + (this === target)); throw token; },
                    enumerable: true, configurable: true
                });
                Object.freeze(target);
                try { dot(target); } catch (error) { console.log(error === token, events.join(",")); }
                events.length = 0;
                try { indexed(target, "value"); } catch (error) { console.log(error === token, events.join(",")); }
                const descriptor = Object.getOwnPropertyDescriptor(target, "value")!;
                console.log(descriptor.enumerable, descriptor.configurable, typeof descriptor.set, Object.isFrozen(target));
            }
            """, "true set:7:true\ntrue set:8:true\ntrue false function true\ntrue set:7:true\ntrue set:8:true\ntrue false function true\ntrue set:7:true\ntrue set:8:true\ntrue false function true\n"];
    }

    public static IEnumerable<object[]> FrozenSetterCases() => Cases().Where(row =>
        (string)row[0] is "frozen-accessor-setter-still-runs" or
            "own-setter-dot-computed-and-flags-before-and-after-freeze" or
            "frozen-setter-abrupt-identity-and-no-getter-evaluation");

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
            // Getterless compiled Promise reads remain #2436.
            if ((string)row[0] != "own-accessors-and-getterless-reads")
                yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedPromiseWritesUseTheExistingPropertySemantics(
        string name, string source, string expected, ExecutionMode mode)
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
