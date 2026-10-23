using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorLiveSuperTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["explicit-live-parent-and-restoration", """
            const trace: string[] = [];
            class First { first = 1; constructor(value: number) { trace.push("first:" + value); } }
            class Second { second = 2; constructor(value: number) { trace.push("second:" + value); console.log(new.target === Derived); } }
            class Derived extends First { child = 3; constructor(value: number) { super(value); trace.push("derived"); } }
            const owner: any = Derived;
            Object.setPrototypeOf(owner, Second);
            const changed: any = new Derived(7);
            console.log(changed.first === undefined, changed.second, changed.child,
                Object.getPrototypeOf(changed) === Derived.prototype, Object.getPrototypeOf(Derived.prototype) === First.prototype,
                changed instanceof First, changed instanceof Second);
            Object.setPrototypeOf(owner, First);
            const restored: any = new Derived(8);
            console.log(restored.first, restored.second === undefined, restored.child, Object.getPrototypeOf(restored) === Derived.prototype);
            console.log(trace.join(","));
            """, "true\ntrue 2 3 true true true false\n1 true 3 true\nsecond:7,derived,first:8,derived\n"];
        yield return ["implicit-expression-live-parent", """
            const trace: string[] = [];
            class First { first = 1; constructor(value: number) { trace.push("first:" + value); } }
            class Second { second = 2; constructor(value: number) { trace.push("second:" + value); } }
            const Derived: any = class extends First { child = 3; };
            Object.setPrototypeOf(Derived, Second);
            const changed: any = new Derived(4);
            console.log(changed.first === undefined, changed.second, changed.child,
                Object.getPrototypeOf(changed) === Derived.prototype,
                Object.getPrototypeOf(Derived.prototype) === First.prototype, changed instanceof First, changed instanceof Second);
            Object.setPrototypeOf(Derived, First);
            const restored: any = new Derived(5);
            console.log(restored.first, restored.second === undefined, restored.child, trace.join(","));
            """, "true 2 3 true true true false\n1 true 3 second:4,first:5\n"];
        yield return ["derived-nonconstructor-parent-and-base-kind", """
            const trace: string[] = [];
            class Base { constructor(value?: number) { trace.push("base"); } }
            class Explicit extends Base { constructor() { super((trace.push("argument"), 1)); } }
            class Implicit extends Base {}
            const parents: any[] = [null, {}];
            for (const parent of parents) {
                for (const owner of [Explicit, Implicit]) {
                    Object.setPrototypeOf(owner, parent);
                    try { new owner(); } catch (error) { console.log(error instanceof TypeError); }
                }
            }
            Object.setPrototypeOf(Base, null);
            const value: any = new Base();
            console.log(Object.getPrototypeOf(value) === Base.prototype, value instanceof Base, trace.join(","));
            """, "true\ntrue\ntrue\ntrue\ntrue true argument,argument,base\n"];
        yield return ["static-super-live-home-and-borrowed-receiver", """
            class First {
                static value = 1;
                static get watched() { return (this as any).tag + 10; }
            }
            class Derived extends First {
                static tag = 2;
                static read() { return [super.value, super.watched, this.tag].join(","); }
                static arrow() { return () => super.value; }
            }
            class Borrow { static tag = 7; }
            const owner: any = Derived, read: any = owner.read, arrow = owner.arrow();
            console.log(owner.read(), read.call(Borrow), arrow());
            Object.setPrototypeOf(owner, { value: 3, get watched() { return (this as any).tag + 20; } });
            console.log(owner.read(), read.call(Borrow), arrow());
            Object.setPrototypeOf(owner, null);
            try { owner.read(); } catch (error) { console.log(error instanceof TypeError); }
            try { arrow(); } catch (error) { console.log(error instanceof TypeError); }
            Object.setPrototypeOf(owner, First);
            console.log(owner.read(), arrow());
            """, "1,12,2 1,17,7 1\n3,22,2 3,27,7 3\ntrue\ntrue\n1,12,2 1\n"];
        yield return ["array-leaf-to-ordinary-parent", """
            class Plain { value = 5; }
            class Values extends Array<any> {}
            const owner: any = Values;
            Object.setPrototypeOf(owner, Plain);
            const value: any = new owner(2);
            console.log(Array.isArray(value), Object.hasOwn(value, "length"), value.value,
                Object.getPrototypeOf(value) === Values.prototype, value instanceof Values, value instanceof Array);
            """, "false false 5 true true true\n"];
        yield return ["ordinary-leaf-to-array-parent", """
            class Plain {}
            class Native extends Array<any> { value = 7; }
            class Derived extends Plain { child = 9; }
            const owner: any = Derived;
            Object.setPrototypeOf(owner, Native);
            const value: any = new owner(2);
            console.log(Array.isArray(value), Object.hasOwn(value, "length"), value.length, value.value, value.child,
                Object.getPrototypeOf(value) === Derived.prototype, value instanceof Derived, value instanceof Array);
            """, "true true 2 7 9 true true false\n"];
        yield return ["function-and-proxy-parent", """
            class Original { constructor(value?: number) {} }
            class Derived extends Original { child = 3; constructor(value: number) { super(value); } }
            function Parent(this: any, value: number) { this.parent = value; console.log("function", (new.target as any) === Derived, value); }
            const owner: any = Derived;
            Object.setPrototypeOf(owner, Parent);
            const ordinary: any = new owner(5);
            console.log(ordinary.parent, ordinary.child, Object.getPrototypeOf(ordinary) === Derived.prototype);
            const replacement: any = { from: "proxy" };
            let calls = 0;
            const proxy = new Proxy(Parent, { construct(target, args, newTarget) {
                calls++; console.log("trap", target === Parent, args[0], newTarget === Derived); return replacement;
            } });
            Object.setPrototypeOf(owner, proxy);
            const result: any = new owner(7);
            console.log(result === replacement, result.from, result.child, result.parent === undefined,
                Object.getPrototypeOf(result) === Object.prototype, calls);
            """, "function true 5\n5 3 true\ntrap true 7 true\ntrue proxy 3 true true 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SuperUsesTheLiveConstructorPrototype(string name, string source, string expected, ExecutionMode mode)
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
