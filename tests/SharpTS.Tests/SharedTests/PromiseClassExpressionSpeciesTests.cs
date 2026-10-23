using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseClassExpressionSpeciesTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["explicit-promise-expression-default-species-issue", """
            const P = class extends Promise<number> {
                constructor(executor: any) { super(executor); }
            };
            const p: any = new P((resolve: any) => resolve(7));
            p.then((value: number) => console.log(value));
            """, "7\n"];
        yield return ["default-expression-declaration-and-intrinsic-species-identity", """
            const Expression = class extends Promise<number> { constructor(executor: any) { super(executor); } };
            class Declaration extends Promise<number> {}
            const expression: any = new Expression((resolve: any) => resolve(7));
            const declaration: any = new Declaration(resolve => resolve(8));
            const intrinsic: any = Promise.resolve(9);
            const first = expression.then((value: number) => value + 1);
            const second = declaration.then((value: number) => value + 1);
            const third = intrinsic.then((value: number) => value + 1);
            console.log(first instanceof Expression, first.constructor === Expression, first !== expression);
            console.log(second instanceof Declaration, second.constructor === Declaration, second !== declaration);
            console.log(third instanceof Promise, third.constructor === Promise, third !== intrinsic);
            Promise.all([first, second, third]).then(values => console.log(values.join(",")));
            """, "true true true\ntrue true true\ntrue true true\n8,9,10\n"];
        yield return ["inherited-expression-species-receiver-custom-and-null-fallback", """
            const state = { reads: 0, same: true };
            let selected: any, current: any;
            const Other = class extends Promise<number> { constructor(executor: any) { super(executor); } };
            const Base = class extends Promise<number> {
                constructor(executor: any) { super(executor); }
                static get [Symbol.species](): any { state.reads++; state.same = state.same && this === current; return selected; }
            };
            const Derived = class extends Base {};
            current = Derived; selected = Other;
            const source: any = new Derived((resolve: any) => resolve(7));
            const custom = source.then((value: number) => value + 1);
            console.log(custom instanceof Other, custom instanceof Derived, state.reads, state.same);
            selected = null;
            const plain = source.then((value: number) => value + 2);
            console.log(plain instanceof Promise, plain instanceof Derived, plain.constructor === Promise, state.reads, state.same);
            Promise.all([custom, plain]).then(values => console.log(values.join(",")));
            """, "true false 1 true\ntrue false true 2 true\n8,9\n"];
        yield return ["expression-constructor-getter-once-and-abrupt-before-reaction", """
            const P = class extends Promise<number> { constructor(executor: any) { super(executor); } };
            const source: any = new P((resolve: any) => resolve(7)), token = {};
            const state = { constructors: 0, species: 0, handlers: 0 };
            let fail = false;
            Object.defineProperty(P, Symbol.species, {
                get() { state.species++; return P; }, configurable: true
            });
            Object.defineProperty(source, "constructor", {
                get() { state.constructors++; if (fail) throw token; return P; }, configurable: true
            });
            const result = source.then((value: number) => { state.handlers++; return value + 1; });
            console.log(state.constructors, state.species, state.handlers, result instanceof P);
            fail = true;
            try { source.then(() => { state.handlers += 10; }); }
            catch (error) { console.log(error === token, state.constructors, state.species, state.handlers); }
            result.then((value: number) => console.log(value, state.handlers));
            """, "1 1 0 true\ntrue 2 1 0\n8 1\n"];
        yield return ["repeated-expression-definitions-retain-captures-and-nominal-child-species", """
            const events: string[] = [];
            function make(label: string) {
                return class extends Promise<number> {
                    constructor(executor: any) { super(executor); events.push(label); }
                };
            }
            const P = make("P"), Q = make("Q");
            class Child extends P {}
            const p: any = new P((resolve: any) => resolve(7)), q: any = new Q((resolve: any) => resolve(8));
            const child: any = new Child((resolve: any) => resolve(9));
            events.length = 0;
            const first = p.then((value: number) => value + 1), second = q.then((value: number) => value + 1);
            const third = child.then((value: number) => value + 1);
            console.log(events.join(","), first.constructor === P, second.constructor === Q, third.constructor === Child);
            console.log(first instanceof P, second instanceof Q, third instanceof Child);
            Promise.all([first, second, third]).then(values => console.log(values.join(",")));
            """, "P,Q,P true true true\ntrue true true\n8,9,10\n"];
        yield return ["repeated-expression-definitions-retain-own-captures", """
            const events: string[] = [];
            function make(label: string) {
                return class extends Promise<number> {
                    constructor(executor: any) { super(executor); events.push(label); }
                };
            }
            const P = make("P"), Q = make("Q");
            const p: any = new P((resolve: any) => resolve(7)), q: any = new Q((resolve: any) => resolve(8));
            events.length = 0;
            const first = p.then((value: number) => value + 1), second = q.then((value: number) => value + 1);
            console.log(events.join(","), first.constructor === P, second.constructor === Q);
            console.log(first instanceof P, second instanceof Q);
            Promise.all([first, second]).then(values => console.log(values.join(",")));
            """, "P,Q true true\ntrue true\n8,9\n"];
        yield return ["expression-over-nominal-species-own-override-and-inherited-receiver", """
            const events: string[] = [];
            let expected: any;
            class Base extends Promise<number> {
                static get [Symbol.species](): any { events.push("base:" + (this === expected)); return Promise; }
            }
            const Override = class extends Base {
                static get [Symbol.species](): any { events.push("own:" + (this === expected)); return Promise; }
            };
            const Inherited = class extends Base {};
            expected = Override;
            const own: any = new Override(resolve => resolve(7)).then(value => value + 1);
            console.log("own", events.join(","), own.constructor === Promise, own instanceof Override);
            events.length = 0; expected = Inherited;
            const inherited: any = new Inherited(resolve => resolve(8)).then(value => value + 1);
            console.log("inherited", events.join(","), inherited.constructor === Promise, inherited instanceof Inherited);
            Promise.all([own, inherited]).then(values => console.log(values.join(",")));
            """, "own own:true true false\ninherited base:true true false\n8,9\n"];
        yield return ["expression-undefined-throwing-and-invalid-species", """
            const P = class extends Promise<number> {};
            const source: any = new P(resolve => resolve(7)), token = {};
            const state = { reads: 0, handlers: 0 };
            let selected: any = undefined;
            Object.defineProperty(P, Symbol.species, {
                get() { state.reads++; if (selected === token) throw token; return selected; }, configurable: true
            });
            const result = source.then((value: number) => { state.handlers++; return value + 1; });
            console.log("undefined", result.constructor === Promise, result instanceof P, state.reads, state.handlers);
            selected = token;
            try { source.then(() => { state.handlers += 10; }); }
            catch (error) { console.log("throw", error === token, state.reads, state.handlers); }
            selected = 1;
            try { source.then(() => { state.handlers += 100; }); }
            catch (error) { console.log("invalid", error instanceof TypeError, state.reads, state.handlers); }
            result.then((value: number) => console.log(value, state.handlers));
            """, "undefined true false 1 0\nthrow true 2 0\ninvalid true 3 0\n8 1\n"];
        yield return ["repeated-expression-symbol-species-remains-definition-local", """
            const events: string[] = [];
            function make(label: string) {
                return class extends Promise<number> {
                    static get [Symbol.species](): any { events.push(label); return this; }
                };
            }
            const P = make("P"), Q = make("Q");
            const p: any = new P(resolve => resolve(7)), q: any = new Q(resolve => resolve(8));
            const first = p.then((value: number) => value + 1), second = q.then((value: number) => value + 1);
            console.log(events.join(","), first.constructor === P, second.constructor === Q);
            Object.defineProperty(P, Symbol.species, {
                get() { events.push("override-P"); return Promise; }, configurable: true
            });
            events.length = 0;
            const third = p.then((value: number) => value + 2), fourth = q.then((value: number) => value + 2);
            console.log(events.join(","), third.constructor === Promise, fourth.constructor === Q);
            Promise.all([first, second, third, fourth]).then(values => console.log(values.join(",")));
            """, "P,Q true true\noverride-P,Q true true\n8,9,9,10\n"];
        yield return ["native-promise-species-masks-function-prototype-pollution", """
            const prototype: any = Function.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, Symbol.species), token = {};
            const state = { reads: 0 };
            try {
                Object.defineProperty(prototype, Symbol.species, {
                    get() { state.reads++; throw token; }, configurable: true
                });
                const P = class extends Promise<number> {};
                const source: any = new P(resolve => resolve(7));
                const result = source.then((value: number) => value + 1);
                console.log(state.reads, result instanceof P, result.constructor === P);
                result.then((value: number) => console.log(value, state.reads));
            } finally {
                if (original) Object.defineProperty(prototype, Symbol.species, original);
                else delete prototype[Symbol.species];
            }
            """, "0 true true\n8 0\n"];
    }

    // Only the restored saved row adds the historical oracle's final CRLF.
    // Keep Cases and every previously saved row object unchanged.
    public static IEnumerable<object[]> SavedCases() => Cases().Select(row =>
        (string)row[0] == "repeated-expression-definitions-retain-captures-and-nominal-child-species"
            ? new object[] { row[0], ((string)row[1]).ReplaceLineEndings("\r\n") + "\r\n", row[2] }
            : row).Concat(
        ClassExpressionDeclareFieldErasureTests.Cases().Where(row =>
            (string)row[0] == "native-promise-subclass-preserves-state"));

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PromiseExpressionReactionsRetainTheGuestSpeciesConstructor(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
