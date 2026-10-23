using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveStringLiveMethodTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            const original = String.prototype.indexOf;
            String.prototype.indexOf = function (_needle: string, _position?: number): number { return null as any; };
            function forward(input: string, needle: string, position: number): number { return input.indexOf(needle, position); }
            try { console.log(forward("abc", "b", 0)); }
            finally { String.prototype.indexOf = original; }
            """, "null\n"];
        yield return ["includes-typed-replacement", """
            const prototype: any = String.prototype;
            const original = prototype.includes;
            prototype.includes = function(_needle: string, _position?: number): boolean { return null as any; };
            function forward(input: string, needle: string, position: number): boolean { return input.includes(needle, position); }
            try { console.log(forward("abc", "b", 0)); }
            finally { prototype.includes = original; }
            """, "null\n"];
        yield return ["direct-indexed-and-detached-live-references", """
            const prototype: any = String.prototype, original = prototype.indexOf;
            const replacement = function(_needle: string, _position?: number): any {
                "use strict"; return this === "abc" ? 17 : 23;
            };
            try {
                prototype.indexOf = replacement;
                const input: string = "abc", key: any = "indexOf";
                const held: any = input.indexOf;
                console.log("abc".indexOf("b", 0), input.indexOf("b", 0), (input as any)[key]("b", 0));
                console.log(held === replacement, held.call("abc", "b", 0), held.call("other", "b", 0));
                prototype.indexOf = () => 31;
                console.log(input.indexOf("b", 0), held.call("other", "b", 0));
            } finally { prototype.indexOf = original; }
            """, "17 17 17\ntrue 17 23\n31 23\n"];
        yield return ["getter-receiver-and-lookup-before-arguments", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
            const input = "abc", events: string[] = [];
            let result: any;
            try {
                Object.defineProperty(prototype, "indexOf", {
                    configurable: true,
                    get() {
                        "use strict"; events.push("get:" + (this === input));
                        return function(_needle: string, _position?: number): any {
                            "use strict"; events.push("call:" + (this === input)); return 17;
                        };
                    }
                });
                function receiver() { events.push("receiver"); return input; }
                function first() {
                    events.push("first");
                    Object.defineProperty(prototype, "indexOf", { value: () => 99, writable: true, configurable: true });
                    return "b";
                }
                function second() { events.push("second"); return 0; }
                result = receiver().indexOf(first(), second());
            } finally { Object.defineProperty(prototype, "indexOf", original); }
            console.log(result, events.join(","));
            """, "17 receiver,get:true,first,second,call:true\n"];
        yield return ["throwing-includes-getter-suppresses-arguments", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "includes")!;
            const token = {};
            let calls = 0, same = false;
            try {
                Object.defineProperty(prototype, "includes", { configurable: true, get() { throw token; } });
                function argument() { calls++; return "b"; }
                try { "abc".includes(argument(), 0); }
                catch (error) { same = error === token; }
            } finally { Object.defineProperty(prototype, "includes", original); }
            console.log(same, calls);
            """, "true 0\n"];
        yield return ["noncallable-indexof-still-evaluates-arguments", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
            const events: string[] = [];
            let guest = false;
            try {
                Object.defineProperty(prototype, "indexOf", { value: 5, writable: true, configurable: true });
                function first() { events.push("first"); prototype.indexOf = () => 99; return "b"; }
                function second() { events.push("second"); return 0; }
                try { "abc".indexOf(first(), second()); }
                catch (error) { guest = error instanceof TypeError; }
            } finally { Object.defineProperty(prototype, "indexOf", original); }
            console.log(guest, events.join(","));
            """, "true first,second\n"];
        yield return ["async-includes-getter-with-awaited-argument", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "includes")!;
            const input = "abc", events: string[] = [];
            async function run() {
                let result: any;
                try {
                    Object.defineProperty(prototype, "includes", {
                        configurable: true,
                        get() {
                            "use strict"; events.push("get:" + (this === input));
                            return function(_needle: string, _position?: number): any {
                                "use strict"; events.push("call:" + (this === input)); return null;
                            };
                        }
                    });
                    async function argument() {
                        events.push("argument");
                        await Promise.resolve(0);
                        Object.defineProperty(prototype, "includes", { value: () => true, writable: true, configurable: true });
                        return "b";
                    }
                    result = input.includes(await argument(), 0);
                } finally { Object.defineProperty(prototype, "includes", original); }
                console.log(result, events.join(","));
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """, "null get:true,argument,call:true\n"];
        yield return ["unmodified-and-restored-controls", """
            const prototype: any = String.prototype, originalIndexOf = prototype.indexOf, originalIncludes = prototype.includes;
            const input = "abc";
            console.log(input.indexOf("b", 0), input.includes("b", 0), input.length, input[1]);
            try {
                prototype.indexOf = () => 31;
                prototype.includes = () => false;
                console.log(input.indexOf("b", 0), input.includes("b", 0));
            } finally { prototype.indexOf = originalIndexOf; prototype.includes = originalIncludes; }
            console.log(input.indexOf("b", 0), input.includes("b", 0), input.length, input[1]);
            """, "1 true 3 b\n31 false\n1 true 3 b\n"];
        yield return ["mask-delete-and-inherited-fallback", """
            const prototype: any = String.prototype, object: any = Object.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
            const inherited = Object.getOwnPropertyDescriptor(object, "indexOf");
            const input: any = "abc";
            let calls = 0;
            function argument() { calls++; return "b"; }
            try {
                for (const value of [null, undefined]) {
                    Object.defineProperty(prototype, "indexOf", { value, writable: true, configurable: true });
                    calls = 0;
                    console.log(input.indexOf?.(argument(), 0) === undefined, calls);
                }
                Object.defineProperty(prototype, "indexOf", { get: undefined, set(_value: any) {}, configurable: true });
                calls = 0;
                console.log(input.indexOf?.(argument(), 0) === undefined, calls);
                console.log(delete prototype.indexOf, typeof input.indexOf);
                object.indexOf = function(_needle: string, _position?: number): any { "use strict"; return this === "abc" ? 41 : 0; };
                console.log(input.indexOf("b", 0));
            } finally {
                Object.defineProperty(prototype, "indexOf", original);
                if (inherited) Object.defineProperty(object, "indexOf", inherited); else delete object.indexOf;
            }
            console.log(input.indexOf("b", 0));
            """, "true 0\ntrue 0\ntrue 0\ntrue undefined\n41\n1\n"];
        yield return ["native-identity-detached-and-character-controls", """
            const input: any = "abc";
            const native: any = String.prototype.indexOf, held: any = input.indexOf;
            console.log(held === native);
            try { console.log("detached", held("b", 0)); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(native.call("abc", "b", 0), input["1"], input[1], input.length);
            """, "true\ntrue\n1 b b 3\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
            // Compiler gap: awaited argument verification (#2700).
            if ((string)row[0] != "async-includes-getter-with-awaited-argument")
                yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void PrimitiveStringCallsObserveTheLivePrototypeMethod(
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
