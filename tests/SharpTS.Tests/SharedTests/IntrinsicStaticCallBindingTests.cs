using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class IntrinsicStaticCallBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            function forward(Number: { parseInt(value: string, radix: number): number }): number {
                return Number.parseInt("7", 10);
            }
            console.log(forward({ parseInt: () => null as any }));
            """, "null\n"];
        yield return ["number-and-math-live-member-replacements", """
            const number: any = Number, math: any = Math;
            const parse = number.parseInt, min = math.min, max = math.max;
            let results: any[] = [];
            try {
                number.parseInt = () => 17;
                math.min = () => 21;
                math.max = () => 25;
                const numberAlias = number, mathAlias = math;
                const held = Number.parseInt;
                results = [Number.parseInt("7", 10), Math.min(7, 8), Math.max(1, 2),
                    numberAlias.parseInt("7", 10), mathAlias.max(1, 2), held("7", 10)];
            } finally { number.parseInt = parse; math.min = min; math.max = max; }
            console.log(results.join(","));
            console.log(Number.parseInt("12", 10), Math.min(1, 2), Math.max(1, 2));
            """, "17,21,25,17,25,17\n12 1 2\n"];
        yield return ["math-parameter-shadow", """
            function low(Math: any) { return Math.min(7, 8); }
            function high(Math: any) { return Math.max(1, 2); }
            console.log(low({ min: () => null }), high({ max: () => 42 }));
            """, "null 42\n"];
        yield return ["lexical-block-capture-and-object-alias", """
            function outer() {
                const Number = { tag: "number", parseInt(value: string, radix: number) { return this.tag + value; } };
                const Math = { tag: "math", max(left: number, right: number) { return this.tag + right; } };
                function read() { return Number.parseInt("7", 10) + ":" + Math.max(1, 2); }
                const numberAlias = Number, mathAlias = Math;
                console.log(read(), numberAlias.parseInt("8", 10), mathAlias.max(1, 3));
                {
                    const Math = { min(left: number, right: number) { return "block"; } };
                    console.log(Math.min(7, 8));
                }
                console.log(Math.max(1, 4));
            }
            outer();
            """, "number7:math2 number8 math3\nblock\nmath4\n"];
        yield return ["async-shadow-and-captured-alias", """
            async function forward(Number: any, Math: any) {
                await Promise.resolve(0);
                const numberAlias = Number, mathAlias = Math;
                console.log(Number.parseInt("7", 10), Math.min(7, 8),
                    numberAlias.parseInt("8", 10), mathAlias.max(1, 2));
            }
            async function outer() {
                const Number = { parseInt(value: string, radix: number) { return "local" + value; } };
                const Math = { max(left: number, right: number) { return "local" + right; } };
                async function read() { await Promise.resolve(0); console.log(Number.parseInt("9", 10), Math.max(1, 3)); }
                await read();
            }
            async function run() {
                await forward({ parseInt: (value: string) => "parameter" + value }, { min: () => 31, max: () => 32 });
                await outer();
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """, "parameter7 31 parameter8 32\nlocal9 local3\n"];
        yield return ["getter-before-arguments-and-original-receiver", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, "max")!;
            const events: string[] = [];
            let result: any;
            try {
                Object.defineProperty(target, "max", {
                    configurable: true,
                    get() {
                        events.push("get:" + (this === target));
                        return function(left: number, right: number) {
                            events.push("call:" + (this === target));
                            return left + right;
                        };
                    }
                });
                function first() {
                    events.push("first");
                    Object.defineProperty(target, "max", { value: () => 99, writable: true, configurable: true });
                    return 4;
                }
                function second() { events.push("second"); return 5; }
                result = Math.max(first(), second());
            } finally { Object.defineProperty(target, "max", original); }
            console.log(result, events.join(","));
            console.log(Math.max(1, 2));
            """, "9 get:true,first,second,call:true\n2\n"];
        yield return ["throwing-lookup-suppresses-arguments", """
            const target: any = Number;
            const original = Object.getOwnPropertyDescriptor(target, "parseInt")!;
            const token = {};
            let calls = 0, same = false;
            try {
                Object.defineProperty(target, "parseInt", { configurable: true, get() { throw token; } });
                function first() { calls++; return "7"; }
                function second() { calls++; return 10; }
                try { Number.parseInt(first(), second()); }
                catch (error) { same = error === token; }
            } finally { Object.defineProperty(target, "parseInt", original); }
            console.log(same, calls);
            """, "true 0\n"];
        yield return ["noncallable-lookup-retained-through-arguments", """
            const target: any = Number;
            const original = Object.getOwnPropertyDescriptor(target, "parseInt")!;
            const events: string[] = [];
            let guest = false;
            try {
                Object.defineProperty(target, "parseInt", { value: 5, writable: true, configurable: true });
                function first() { events.push("first"); target.parseInt = () => 99; return "7"; }
                function second() { events.push("second"); return 10; }
                try { Number.parseInt(first(), second()); }
                catch (error) { guest = error instanceof TypeError; }
            } finally { Object.defineProperty(target, "parseInt", original); }
            console.log(guest, events.join(","));
            """, "true first,second\n"];
        yield return ["async-getter-before-awaited-argument-and-receiver", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, "min")!;
            const events: string[] = [];
            async function run() {
                let result: any;
                try {
                    Object.defineProperty(target, "min", {
                        configurable: true,
                        get() {
                            events.push("get:" + (this === target));
                            return function(left: number, right: number) {
                                events.push("call:" + (this === target));
                                return left + right;
                            };
                        }
                    });
                    async function argument() {
                        events.push("argument");
                        await Promise.resolve(0);
                        Object.defineProperty(target, "min", { value: () => 99, writable: true, configurable: true });
                        return 4;
                    }
                    result = Math.min(await argument(), 5);
                } finally { Object.defineProperty(target, "min", original); }
                console.log(result, events.join(","));
                console.log(Math.min(1, 2));
            }
            run().catch((error: any) => console.log("unexpected", error && error.name));
            """, "9 get:true,argument,call:true\n1\n"];
        yield return ["math-alias-and-explicit-reflect-receiver", """
            const target: any = Math, alternate = { tag: "alternate" };
            const original = Object.getOwnPropertyDescriptor(target, "max")!;
            let reads = 0, direct = false, reflected = false, identity = false;
            const getter = function(this: any) { reads++; return this; };
            try {
                Object.defineProperty(target, "max", { configurable: true, get: getter });
                direct = target.max === target;
                reflected = Reflect.get(target, "max", alternate) === alternate;
                identity = Object.getOwnPropertyDescriptor(target, "max")!.get === getter;
            } finally { Object.defineProperty(target, "max", original); }
            console.log(direct, reflected, identity, reads);
            """, "true true true 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CallsRespectTheResolvedBindingAndLiveMemberBeforeArguments(
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
