using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateMethodValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-property-write-and-call", """
            class Box {
                value = 3;
                read() { return this.value; }
                #change() { this.value = this.value + 1; return this.read(); }
                extract() { return this.#change; }
            }
            const box = new Box(), change = box.extract();
            const object = { value: 7, read() { return this.value + 10; } };
            console.log(change.call(object), object.value, box.value);
            """, "18 8 3\n"];
        yield return ["real-suspension-and-receiver-order", """
            let reads = 0;
            class Box {
                value = 3;
                #read(amount = 2) { return this.value + amount; }
                async #later() { await new Promise(resolve => setTimeout(() => resolve(0), 1)); return this.value; }
                async extract(object: any) { return (await Promise.resolve(object)).#read; }
                later() { return this.#later; }
            }
            function receiver(object: any) { reads++; return object; }
            async function run() {
                const box = new Box(), read = await box.extract(receiver(box)), later = box.later();
                console.log(reads, read === await box.extract(box));
                console.log(read.call({ value: 7 }, await new Promise(resolve => setTimeout(() => resolve(5), 1))));
                console.log(await later.call({ value: 11 }));
            }
            run().catch(error => console.log("error", error));
            """, "1 true\n12\n11\n"];
        yield return ["generic-static-values", """
            class Box<T> {
                static #value = 7;
                static async #read() { await Promise.resolve(0); return this.#value; }
                static extract() { return Box.#read; }
            }
            class Other { static #value = 9; }
            async function run() {
                const read = Box.extract();
                console.log(read === Box.extract(), await read.call(Box));
                try { await read.call(Other); } catch (error) { console.log(error instanceof TypeError); }
            }
            run().catch(error => console.log("error", error));
            """, "true 7\ntrue\n"];
        yield return ["repeated-static-private-state", """
            function make(value: number) {
                return class {
                    static #value = value;
                    static #read() { return this.#value; }
                    static extract() { return this.#read; }
                };
            }
            const First = make(3), Second = make(7), one = First.extract(), two = Second.extract();
            console.log(one === First.extract(), one === two, one.call(First), two.call(Second));
            try { one.call(Second); } catch (error) { console.log(error instanceof TypeError); }
            """, "true false 3 7\ntrue\n"];
        yield return ["repeated-class-evaluations", """
            function make(value: number) {
                return class Named {
                    #value = value;
                    #read() { return this.#value; }
                    #captured() { return value; }
                    extract(object: any) { return object.#read; }
                    captured() { return this.#captured; }
                    call() { return this.#read(); }
                };
            }
            const First = make(3), Second = make(7), first = new First(), second = new Second();
            const read = first.extract(first), other = second.extract(second);
            console.log(read === first.extract(new First()), read === other, read.call(first), first.call());
            console.log(first.captured().call(second), second.captured().call({}));
            try { first.extract(second); } catch (error) { console.log(error instanceof TypeError); }
            try { read.call(second); } catch (error) { console.log(error instanceof TypeError); }
            """, "true false 3 3\n3 7\ntrue\ntrue\n"];
        yield return ["closures-and-static-suspension", """
            class Box {
                value = 3;
                #read(): () => number { return () => this.value; }
                async extract(object: any) { await Promise.resolve(0); return () => object.#read; }
                static async #later() { await Promise.resolve(0); return this; }
                static *#values() { yield this; }
                static async *#more() { await Promise.resolve(0); yield this; }
                static values() { return [this.#later, this.#values, this.#more]; }
            }
            async function run() {
                const first = new Box(), read = (await first.extract(first))();
                console.log(read.call({ value: 11 })());
                const values: any = Box.values();
                const later = values[0], stream = values[1], more = values[2];
                console.log(await later() === undefined, stream().next().value === undefined);
                console.log((await more().next()).value === undefined);
            }
            run().catch(error => console.log("error", error));
            """, "11\ntrue true\ntrue\n"];
        yield return ["identity-and-receivers", """
            class Box {
                value: number;
                constructor(value: number) { this.value = value; }
                #read(): number { return this.value; }
                extract() { return this.#read; }
            }
            const first = new Box(3), second = new Box(7), read = first.extract();
            console.log(read === first.extract(), read === second.extract());
            console.log(read.call(second), read.apply({ value: 11 }, []), read.bind(second)());
            console.log(read.name, read.length);
            try { new (read as any)(); } catch (error) { console.log(error instanceof TypeError); }
            """, "true true\n7 11 7\n#read 0\ntrue\n"];
        yield return ["generic-identity", """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                #read(): T { return this.value; }
                extract() { return this.#read; }
            }
            const first = new Box<number>(3), second = new Box<string>("seven"), read = first.extract();
            console.log(read === first.extract(), (read as any) === second.extract());
            console.log(read.call(second), read.call({ value: 11 }));
            """, "true true\nseven 11\n"];
        yield return ["brands-and-strict-this", """
            let reads = 0;
            class Box {
                #value = 3;
                #read() { return this.#value; }
                #receiver() { return this; }
                extract(object: any) { return object.#read; }
                receiver() { return this.#receiver; }
            }
            const first = new Box(), read = first.extract(first);
            console.log(read.call(first));
            try { first.extract((reads++, {})); } catch (error) { console.log(error instanceof TypeError, reads); }
            try { read.call({}); } catch (error) { console.log(error instanceof TypeError); }
            try { read.call(null); } catch (error) { console.log(error instanceof TypeError); }
            const receiver = first.receiver();
            console.log(receiver() === undefined, receiver.call(null) === null);
            """, "3\ntrue 1\ntrue\ntrue\ntrue true\n"];
        yield return ["static-and-defaults", """
            class Box {
                static value = 7;
                static #read(first = 2, ...rest: any[]) { return this.value + first + rest.length; }
                static extract(object: any) { return object.#read; }
            }
            const read = Box.extract(Box);
            console.log(read === Box.extract(Box), read.name, read.length);
            console.log(read.call({ value: 11 }), read.apply({ value: 11 }, [3, 4, 5]));
            try { Box.extract({}); } catch (error) { console.log(error instanceof TypeError); }
            """, "true #read 0\n13 16\ntrue\n"];
        yield return ["suspended-method-kinds", """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                async #read() { await Promise.resolve(0); return this.value; }
                *#values() { yield this.value; }
                async *#later() { await Promise.resolve(0); yield this.value; }
                extract() { return [this.#read, this.#values, this.#later]; }
            }
            async function run() {
                const first = new Box<number>(3), second = new Box<string>("seven");
                const values: any = first.extract(), other: any = second.extract();
                console.log(values[0] === other[0], values[1] === other[1], values[2] === other[2]);
                console.log(await values[0].call({ value: 11 }));
                console.log(values[1].call(second).next().value);
                console.log((await values[2].call({ value: 13 }).next()).value);
            }
            run();
            """, "true true true\n11\nseven\n13\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PrivateMethodsHaveCanonicalUnboundValues(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
    }

    [Theory, ModeData]
    public void ModulePrivateMethodValuesBelongToTheirDeclaration(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "export class Box<T> { #read() { return 3; } extract() { return this.#read; } }",
            ["right.ts"] = "export class Box<T> { #read() { return 7; } extract() { return this.#read; } }",
            ["main.ts"] = """
                import { Box as Left } from './left';
                import { Box as Right } from './right';
                const left = new Left<number>().extract(), other = new Left<string>().extract(), right = new Right<number>().extract();
                console.log(left === other, left === right, left(), right());
                """
        };
        Assert.Equal("true false 3 7\n", TestHarness.RunModules(files, "main.ts", mode));
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory]
    [InlineData("class Box { #read() {} } new Box().#read;")]
    [InlineData("class Box { #read() {} replace() { this.#read = () => {}; } }")]
    [InlineData("class Box { extract() { return this.#missing; } }")]
    public void InvalidPrivateMethodAccessIsRejected(string source)
    {
        Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Throws<TypeCheckException>(() => TestHarness.RunCompiled(source));
    }
}
