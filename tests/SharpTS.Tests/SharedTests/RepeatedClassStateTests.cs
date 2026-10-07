using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RepeatedClassStateTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["retained-generic-field-keys", """
            let count = 0;
            function key() { count++; return "key" + count; }
            function make() { return class<T> { [key()] = 5; }; }
            const First = make();
            const early: any = new First<number>();
            const Second = make();
            const late: any = new First<string>();
            const second: any = new Second<number>();
            console.log(First === Second, count);
            console.log(early.key1, late.key1, late.key2, second.key1, second.key2);
            """, "false 2\n5 5 undefined undefined 5\n"];
        yield return ["symbol-field-keys", """
            const left = Symbol("key"), right = Symbol("key");
            function make(key: symbol) { return class { [key] = 7; }; }
            const First = make(left), Second = make(right);
            const first: any = new First(), second: any = new Second();
            console.log(first[left], first[right], second[left], second[right]);
            console.log(Reflect.ownKeys(first)[0] === left, Reflect.ownKeys(second)[0] === right);
            """, "7 undefined undefined 7\ntrue true\n"];
        yield return ["awaited-field-keys", """
            async function make(key: string) { return class { [await Promise.resolve(key)] = 5; }; }
            async function run() {
                const First = await make("left"), Second = await make("right");
                const first: any = new First(), second: any = new Second();
                console.log(first.left, first.right, second.left, second.right);
            }
            run();
            """, "5 undefined undefined 5\n"];
        yield return ["captured-method-values", """
            function make(value: number) { return class { read() { return value; } }; }
            const First = make(7), Second = make(9);
            console.log(new First().read(), new Second().read(), new First().read());
            """, "7 9 7\n"];
        yield return ["inherited-field-keys", """
            function make(key: string) { return class { [key] = 7; }; }
            const First = make("left"), Second = make("right");
            const Child = class extends First {};
            const Other = class extends Second { constructor() { super(); } };
            const first: any = new Child(), second: any = new Other();
            console.log(first.left, first.right, second.left, second.right);
            """, "7 undefined undefined 7\n"];
        yield return ["static-initialization", """
            let count = 0;
            function make() { return class { static value = ++count; }; }
            const First = make(), Second = make();
            console.log(count, First.value, Second.value);
            const first = new First(), second = new Second();
            console.log(count, first.constructor === First, second.constructor === Second);
            """, "2 1 2\n2 true true\n"];
        yield return ["computed-methods-and-accessors", """
            function make(key: string, value: number) {
                return class { [key]() { return value; } get ["get" + key]() { return value + 1; } };
            }
            const First = make("left", 7), Second = make("right", 9);
            const first: any = new First(), second: any = new Second();
            console.log(first.left(), first.getleft, first.right, first.getright);
            console.log(second.right(), second.getright, second.left, second.getleft);
            const later: any = new First();
            console.log(later.left());
            """, "7 8 undefined undefined\n9 10 undefined undefined\n7\n"];
        yield return ["symbol-methods-and-accessors", """
            const left = Symbol("method"), right = Symbol("method"), leftGet = Symbol("getter"), rightGet = Symbol("getter");
            function make(methodKey: symbol, getterKey: symbol, value: number) {
                return class { [methodKey]() { return value; } get [getterKey]() { return value + 1; } };
            }
            const First = make(left, leftGet, 7), Second = make(right, rightGet, 9);
            const first: any = new First(), second: any = new Second();
            console.log(first[left](), first[leftGet], first[right], first[rightGet]);
            console.log(second[right](), second[rightGet], second[left], second[leftGet]);
            """, "7 8 undefined undefined\n9 10 undefined undefined\n"];
        yield return ["static-captures-and-receivers", """
            function make(value: number) {
                return class Named {
                    static value = value;
                    static read() { return value; }
                    static get captured() { return value; }
                    static receiver() { return this; }
                    static self() { return Named; }
                };
            }
            const First = make(7), Second = make(9);
            First.value = 11;
            console.log(First.value, Second.value, First.read(), Second.read(), First.captured, Second.captured);
            console.log(First.receiver() === First, First.self() === First, Second.self() === Second);
            const borrowed = First.receiver;
            const other = {};
            console.log(borrowed.call(other) === other, First.read.call(other));
            """, "11 9 7 9 7 9\ntrue true true\ntrue 7\n"];
        yield return ["key-and-static-source-order", """
            const trace: string[] = [];
            function key(label: string) { trace.push("key:" + label); return label; }
            function value(label: string) { trace.push("init:" + label); return label; }
            function make() {
                return class {
                    static [key("first")] = value("first");
                    static { trace.push("block"); this.block = 3; }
                    static [key("last")] = value("last");
                };
            }
            const First: any = make(), Second: any = make();
            console.log(trace.join(","));
            console.log(First.first, First.last, First.block, Second.first, Second.last, Second.block);
            """, "key:first,key:last,init:first,block,init:last,key:first,key:last,init:first,block,init:last\nfirst last 3 first last 3\n"];
        yield return ["interleaved-await-captures", """
            async function make(key: string, value: number) {
                return class { [await Promise.resolve(key)] = value; read() { return value; } };
            }
            async function run() {
                const one = make("left", 7), two = make("right", 9);
                const First = await one, Second = await two;
                const first: any = new First(), second: any = new Second();
                console.log(first.left, first.right, first.read(), second.left, second.right, second.read());
            }
            run();
            """, "7 undefined 7 undefined 9 9\n"];
        yield return ["abrupt-definition", """
            let count = 0;
            function key(fail: boolean) { if (fail) throw new Error("key"); return "value"; }
            function make(fail: boolean) { return class { static [key(fail)] = ++count; }; }
            const First: any = make(false);
            try { make(true); } catch (error) { console.log(error.message); }
            const Second: any = make(false);
            console.log(count, First.value, Second.value);
            """, "key\n2 1 2\n"];
        yield return ["suspended-method-captures", """
            function make(value: number) {
                return class {
                    *read() { yield value; }
                    async readAsync() { await Promise.resolve(0); return value; }
                    static *readStatic() { yield value; }
                    static async readStaticAsync() { await Promise.resolve(0); return value; }
                };
            }
            async function run() {
                const First = make(7), Second = make(9);
                console.log(new First().read().next().value, new Second().read().next().value);
                console.log(First.readStatic().next().value, Second.readStatic().next().value);
                console.log(await new First().readAsync(), await new Second().readAsync());
                console.log(await First.readStaticAsync(), await Second.readStaticAsync());
            }
            run();
            """, "7 9\n7 9\n7 9\n7 9\n"];
        yield return ["parent-survives-awaited-key", """
            function make(key: string) { return class { [key] = 7; }; }
            const First = make("left");
            async function makeChild() { return class extends First { [await Promise.resolve("right")] = 9; }; }
            async function run() {
                const Child = await makeChild();
                const Second = make("other");
                const child: any = new Child();
                console.log(child.left, child.right, child.other, child instanceof First, child instanceof Second);
            }
            run();
            """, "7 9 undefined true false\n"];
        yield return ["captured-method-closures", """
            function make(value: number) { return class { read() { return () => value; } }; }
            const First = make(7), Second = make(9);
            const first = new First().read(), second = new Second().read();
            console.log(first(), second(), first());
            """, "7 9 7\n"];
        yield return ["live-captured-bindings", """
            function make(value: number) {
                const C = class {
                    read() { return value; }
                    write(next: number) { value = next; }
                    increment() { value++; value += 2; return value; }
                    closure() { return () => value; }
                };
                value += 10;
                return C;
            }
            const First = make(7), Second = make(9);
            const first = new First(), second = new Second(), read = first.closure();
            console.log(first.read(), second.read(), read());
            first.write(21);
            console.log(first.increment(), read(), second.read());
            """, "17 19 17\n24 24 19\n"];
        yield return ["suspended-method-closures", """
            function make(value: number) {
                return class { async read() { await Promise.resolve(0); return () => value; }
                    *values() { yield () => value; } };
            }
            async function run() {
                const First = make(7), Second = make(9);
                const one = await new First().read(), two = await new Second().read();
                console.log(one(), two(), new First().values().next().value());
            }
            run();
            """, "7 9 7\n"];
        yield return ["borrowed-instance-environments", """
            function make(value: number) {
                return class { field = value; read() { return value + this.field; }
                    async readAsync() { await Promise.resolve(0); return value; }
                    *values() { yield value; } };
            }
            async function run() {
                const First = make(7), Second = make(9), other = new Second();
                const first = new First();
                console.log(first.read.call(other), first.read.call({ field: 11 }));
                console.log(await first.readAsync.call(other), first.values.call(other).next().value);
            }
            run();
            """, "16 18\n7 7\n"];
    }

    [Theory, ModeData]
    public void SameNamedModuleFactoriesRetainTheirDefinitionState(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "export function make(value: number) { return class Named { ['left'] = value; static value = value; read() { return value; } }; }",
            ["right.ts"] = "export function make(value: number) { return class Named { ['right'] = value; static value = value; read() { return value; } }; }",
            ["main.ts"] = """
                import { make as left } from './left';
                import { make as right } from './right';
                const First: any = left(7), Second: any = right(9), Third: any = left(11);
                const first: any = new First(), second: any = new Second(), third: any = new Third();
                console.log(first.left, first.right, first.read(), First.value);
                console.log(second.left, second.right, second.read(), Second.value);
                console.log(third.left, third.right, third.read(), Third.value);
                """
        };
        Assert.Equal("7 undefined 7 7\nundefined 9 9 9\n11 undefined 11 11\n", TestHarness.RunModules(files, "main.ts", mode));
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory, MemberData(nameof(Cases))]
    public void DefinitionsRetainKeysCapturesAndInitialization(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
    }
}
