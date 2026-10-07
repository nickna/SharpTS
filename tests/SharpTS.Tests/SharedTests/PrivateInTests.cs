using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateInTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["instance-brand", """
            class Base { #field = 1; #method() {} static has(value: any) { console.log(#field in value, #method in value); } }
            class Derived extends Base { #field = 2; }
            class Other { #field = 3; }
            const value = new Base();
            Base.has(value); Base.has(new Derived()); Base.has(new Other());
            Base.has({ '#field': 1 }); Base.has(new Proxy(value, {}));
            """, "true true\ntrue true\nfalse false\nfalse false\nfalse false\n"];
        yield return ["static-brand", """
            class Box<T> { static #field = 1; static #method() {} static has(value: any) { console.log(#field in value, #method in value); } }
            class Derived extends Box<number> {}
            const Alias = Box; Box.has(Box); Box.has(Alias); Box.has(Derived); Box.has(new Box<number>()); Box.has(() => {});
            """, "true true\ntrue true\nfalse false\nfalse false\nfalse false\n"];
        yield return ["operand-once-and-primitives", """
            class Box { #field = 1; static has(value: any) { return #field in value; } }
            let count = 0;
            function next() { count++; return new Box(); }
            console.log(Box.has(next()), count);
            let errors = 0;
            for (const value of [null, undefined, 0, false, "", 1n, Symbol("x")]) {
                try { Box.has(value); } catch (error) { if (error instanceof TypeError) errors++; }
            }
            console.log(errors);
            """, "true 1\n7\n"];
        yield return ["instance-initialization-order", """
            class Box {
                first = console.log(#later in this, #early in this, #method in this);
                #early = console.log(#later in this, #early in this, #method in this);
                middle = console.log(#later in this, #early in this);
                #later;
                #method() {}
                last = console.log(#later in this, #early in this);
            }
            new Box();
            """, "false false true\nfalse false true\nfalse true\ntrue true\n"];
        yield return ["static-initialization-order", """
            class Box {
                static first = console.log(#later in this, #method in this);
                static #early = console.log(#early in this, #later in this);
                static middle = console.log(#early in this, #later in this);
                static #later;
                static #method() {}
                static last = console.log(#early in this, #later in this);
            }
            """, "false true\nfalse false\ntrue false\ntrue true\n"];
        yield return ["premature-static-private-writes", """
            let calls = 0;
            function next() { calls++; return 7; }
            class Box<T> {
                static first = Box.probe();
                static #later = 2;
                static probe() {
                    let threw = false;
                    try { this.#later = next(); } catch (error) { threw = error instanceof TypeError; }
                    console.log(threw, #later in this, calls);
                    try { this.#later = (() => { throw new Error("rhs"); })(); }
                    catch (error) { console.log(error.message); }
                }
                static after() {
                    const value = this.#later = next();
                    console.log(value, this.#later, #later in this, calls);
                }
            }
            Box.after();
            """, "true false 1\nrhs\n7 7 true 2\n"];
        yield return ["fresh-premature-static-private-writes", """
            let calls = 0;
            function next() { calls++; return 7; }
            function make(value: number) { return class Named {
                static first = Named.probe();
                static #later = value;
                static probe() {
                    let threw = false;
                    try { this.#later = next(); } catch (error) { threw = error instanceof TypeError; }
                    console.log(threw, #later in this, calls);
                }
                static after() {
                    console.log(this.#later, #later in this);
                    this.#later = next();
                    console.log(this.#later, #later in this, calls);
                }
            }; }
            const First = make(3), Second = make(5);
            First.after(); Second.after();
            """, "true false 1\ntrue false 2\n3 true\n7 true 3\n5 true\n7 true 4\n"];
        yield return ["fresh-definition-brands", """
            function make() { return class {
                #field = 1; #method() {}
                static #value = 2; static #staticMethod() {}
                static has(object: any) { return #value in object && #staticMethod in object; }
                has(object: any) { return #field in object && #method in object; }
            }; }
            const First = make(), Second = make(), one = new First(), two = new Second();
            console.log(one.has(one), one.has(two), two.has(two), two.has(one));
            console.log(First.has(First), First.has(Second), Second.has(Second), Second.has(First));
            """, "true false true false\ntrue false true false\n"];
        yield return ["closures-and-suspension", """
            class Box<T> {
                #field = 1;
                probe() { return (object: any) => #field in object; }
                async later(object: any) { return #field in await new Promise<any>(resolve => setTimeout(() => resolve(object), 1)); }
                *values(object: any) { yield #field in object; return #field in (yield object); }
                async *delayed(object: any) { yield #field in await Promise.resolve(object); }
            }
            async function run() {
                const box = new Box<number>(), other = new Box<string>(), probe = box.probe();
                console.log(probe(box), probe(other), probe({}));
                console.log(await box.later(box), await box.later({}));
                const it = box.values(box); console.log(it.next().value); it.next(); console.log(it.next({}).value);
                console.log((await box.delayed(box).next()).value);
            }
            run();
            """, "true true false\ntrue false\ntrue\nfalse\ntrue\n"];
        yield return ["expression-field-order", """
            const Box = class {
                first = console.log(#field in this, #method in this);
                #field = console.log(#field in this);
                #method() {}
                last = console.log(#field in this);
            };
            new Box();
            """, "false true\nfalse\ntrue\n"];
        yield return ["nested-lexical-names-and-shadowing", """
            class Outer {
                #outer = 1; #method() {} #same = 2; static #static = 3;
                make() { return class Inner {
                    #same() {}
                    has(value: any) { return #outer in value && #method in value; }
                    shadows(value: any) { return #same in value; }
                    hasStatic(value: any) { return #static in value; }
                }; }
            }
            const outer = new Outer(), Inner = outer.make(), inner = new Inner();
            console.log(inner.has(outer), inner.has(inner), inner.shadows(outer), inner.shadows(inner));
            console.log(inner.hasStatic(Outer), inner.hasStatic(Inner));
            """, "true false false true\ntrue false\n"];
        yield return ["nested-fresh-lexical-owners", """
            function make() { return class Outer {
                #outer = 1; static #static = 2;
                make() { return class Middle {
                    make() { return class Inner {
                        has(value: any) { return #outer in value; }
                        hasStatic(value: any) { return #static in value; }
                    }; }
                }; }
            }; }
            const First = make(), Second = make(), first = new First(), second = new Second();
            const Middle = first.make(), Inner = new Middle().make(), inner = new Inner();
            console.log(inner.has(first), inner.has(second));
            console.log(inner.hasStatic(First), inner.hasStatic(Second));
            """, "true false\ntrue false\n"];
        yield return ["derived-expression-private-brands", """
            function make() { return class Base {
                #field = 1; #method() {}
                has(value: any) { return #field in value && #method in value; }
            }; }
            const First = make(), Second = make();
            const Child = class extends First {};
            const child = new Child();
            console.log(child.has(child), new First().has(child), new Second().has(child));
            """, "true true false\n"];
        yield return ["generic-static-closure-and-suspension", """
            class Box<T> {
                static #field = 1; static #method() {}
                static probe() { return (value: any) => #field in value && #method in value; }
                static async later(value: any) { return #field in await Promise.resolve(value); }
                static *values(value: any) { yield #field in value; return #method in (yield value); }
                static async *delayed(value: any) { yield #field in await Promise.resolve(value); }
            }
            class Child extends Box<number> {}
            async function run() {
                const probe = Box.probe(); console.log(probe(Box), probe(Child), probe({}));
                console.log(await Box.later(Box), await Box.later(Child));
                const it = Box.values(Box); console.log(it.next().value); it.next(); console.log(it.next(Child).value);
                console.log((await Box.delayed(Box).next()).value);
            }
            run();
            """, "true false false\ntrue false\ntrue\nfalse\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void BrandChecksMatchAcrossEngines(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
        // Proxy explicitly requires the deployed runtime; verification/run above
        // supplies it. The other cases also enforce dependency-free deployment.
        if (name != "instance-brand") Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
    }

    [Theory, ModeData]
    public void ModuleNamesKeepTheirLexicalBrands(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["left.ts"] = "export class Box<T> { #field = 1; static has(x: any) { return #field in x; } }",
            ["right.ts"] = "export class Box<T> { #field = 1; static has(x: any) { return #field in x; } }",
            ["main.ts"] = """
                import { Box as Left } from './left'; import { Box as Right } from './right';
                console.log(Left.has(new Left<number>()), Left.has(new Right<number>()), Right.has(new Right<string>()));
                """
        };
        Assert.Equal("true false true\n", TestHarness.RunModules(files, "main.ts", mode));
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Theory]
    [InlineData("class Box { #field = 1; has() { return #field in 1; } }")]
    [InlineData("class Box { has(x: any) { return #missing in x; } }")]
    [InlineData("const x = #field in {};")]
    public void InvalidNamesOrPrimitiveTypesAreRejected(string source)
    {
        Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Throws<TypeCheckException>(() => TestHarness.RunCompiled(source));
    }
}
