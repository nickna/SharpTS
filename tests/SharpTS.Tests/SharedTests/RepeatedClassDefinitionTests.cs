using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RepeatedClassDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["function", """
            function make() { return class Named {}; }
            const First = make(), Second = make();
            const first = new First(), second = new Second();
            const fp = Object.getPrototypeOf(first), sp = Object.getPrototypeOf(second);
            console.log(First === Second, fp === sp);
            console.log(fp === First.prototype, sp === Second.prototype);
            console.log(first.constructor === First, second.constructor === Second);
            console.log(first instanceof First, first instanceof Second, second instanceof First, second instanceof Second);
            console.log(First.name, Second.name, First.length, typeof First);
            """, "false false\ntrue true\ntrue true\ntrue false false true\nNamed Named 0 function\n"];
        yield return ["generic", """
            function make() { return class Box<T> { value: T; constructor(value: T) { this.value = value; } }; }
            const First = make(), Second = make();
            const one = new First<number>(7), two = new First<string>("ok"), three = new Second<number>(9);
            const op = Object.getPrototypeOf(one), tp = Object.getPrototypeOf(two), hp = Object.getPrototypeOf(three);
            console.log(op === tp, op === hp, one.constructor === First, two.constructor === First);
            console.log(one.value, two.value, three.value, First.length);
            """, "true false true true\n7 ok 9 1\n"];
        yield return ["field-initialization", """
            function make() { return class Named {
                owner: any = this.constructor;
                prototype: any = Object.getPrototypeOf(this);
            }; }
            const First = make(), Second = make();
            const first = new First(), second = new Second();
            console.log(first.owner === First, second.owner === Second);
            console.log(first.prototype === First.prototype, second.prototype === Second.prototype);
            """, "true true\ntrue true\n"];
        yield return ["loop-closure", """
            const constructors: any[] = [];
            function outer() { return () => class Named {}; }
            const make = outer();
            for (let i = 0; i < 3; i++) constructors.push(make());
            console.log(constructors[0] === constructors[1], constructors[1] === constructors[2]);
            const first = new constructors[0](), last = new constructors[2]();
            console.log(first.constructor === constructors[0], last.constructor === constructors[2]);
            """, "false false\ntrue true\n"];
        yield return ["generator", """
            function* make() { for (let i = 0; i < 2; i++) { yield class Named {}; } }
            const iterator = make();
            const First: any = iterator.next().value, Second: any = iterator.next().value;
            console.log(First === Second);
            const first = new First(), second = new Second();
            console.log(first.constructor === First, second.constructor === Second);
            """, "false\ntrue true\n"];
        yield return ["async", """
            async function make() { await Promise.resolve(0); return class Named {}; }
            async function run() {
                const First = await make(), Second = await make();
                console.log(First === Second);
                const first = new First(), second = new Second();
                console.log(first.constructor === First, second.constructor === Second);
            }
            run();
            """, "false\ntrue true\n"];
        yield return ["reflect", """
            function make() { return class Named { value: number; constructor(value: number) { this.value = value; } }; }
            const First = make(), Second = make();
            const first: any = Reflect.construct(First, [7]), second: any = Reflect.construct(Second, [9]);
            console.log(first.value, second.value, first.constructor === First, second.constructor === Second);
            console.log(first instanceof First, first instanceof Second);
            """, "7 9 true true\ntrue false\n"];
        yield return ["self-and-prototype-properties", """
            function make() { return class Named { self() { return Named; } }; }
            const First = make(), Second = make();
            First.prototype.choice = 7;
            const first: any = new First(), second: any = new Second();
            console.log(first.self() === First, second.self() === Second);
            console.log(first.choice, second.choice);
            """, "true true\n7 undefined\n"];
        yield return ["arguments-and-defaults", """
            function make() { return class Named {
                value: number; count: number;
                constructor(value: number = 7) { this.value = value; this.count = arguments.length; }
            }; }
            const First = make(), Second: any = make();
            const first = new First(), second = new Second(9, 10);
            console.log(first.value, first.count, second.value, second.count, First.length);
            """, "7 0 9 2 0\n"];
        yield return ["bound-construction", """
            function make() { return class Named { value: number; constructor(value: number) { this.value = value; } }; }
            const First = make(), Second = make();
            const Bound: any = First.bind(null, 7);
            const first: any = new Bound();
            console.log(first.value, first.constructor === First, first instanceof First, first instanceof Second);
            """, "7 true true false\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EvaluationsOwnConstructorAndPrototypeIdentity(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }

    [Theory, ModeData]
    public void ModuleFactoryKeepsEvaluationIdentityAcrossCalls(ExecutionMode mode)
        => Assert.Equal("false true true\n", TestHarness.RunModules(new()
        {
            ["factory.ts"] = "export function make() { return class Named {}; }",
            ["main.ts"] = """
                import { make } from './factory';
                const First = make(), Second = make();
                const first = new First(), second = new Second();
                console.log(First === Second, first.constructor === First, second.constructor === Second);
                """
        }, "main.ts", mode));
}
