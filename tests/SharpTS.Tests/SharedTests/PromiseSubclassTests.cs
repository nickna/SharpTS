using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for guest classes extending the built-in Promise (#242).
/// Runs against both interpreter and compiler.
/// </summary>
public class PromiseSubclassTests
{
    [Theory, ModeData]
    public void ExtendsPromise_ExecutorConstructionAndBrand(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const p = new MyPromise<number>((resolve, reject) => resolve(41));
                console.log(p instanceof MyPromise);
                console.log(p instanceof Promise);
                console.log(await p);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n41\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_MethodsAndFields(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {
                label: string = "mine";
                tag(): string { return "MyPromise:" + this.label; }
            }
            async function main() {
                const p = new MyPromise<number>((resolve) => resolve(1));
                console.log(p.label);
                console.log(p.tag());
                p.label = "updated";
                console.log(p.tag());
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("mine\nMyPromise:mine\nMyPromise:updated\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_StaticSideInheritance_Resolve(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const p = MyPromise.resolve(1);
                console.log(p instanceof MyPromise);
                console.log(p instanceof Promise);
                console.log(await p);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n1\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_ThenReturnsSubclass(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const p = new MyPromise<number>((resolve) => resolve(41));
                const q = p.then(v => v + 1);
                console.log(q instanceof MyPromise);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n42\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_RejectAndCatch(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const p = MyPromise.reject(new Error("boom")).catch((e: any) => "caught:" + e.message);
                console.log(p instanceof MyPromise);
                console.log(await p);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ncaught:boom\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_UserConstructorWithSuper(ExecutionMode mode)
    {
        var source = """
            class Tracked<T> extends Promise<T> {
                created: string;
                constructor(executor: (resolve: (v: T) => void, reject: (r: any) => void) => void) {
                    super(executor);
                    this.created = "yes";
                }
            }
            async function main() {
                const t = new Tracked<string>((resolve) => resolve("v"));
                console.log(t.created);
                console.log(t instanceof Tracked, t instanceof Promise);
                console.log(await t);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\ntrue true\nv\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_TransitiveSubclass(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            class Deeper<T> extends MyPromise<T> {}
            async function main() {
                const t = new Deeper<number>((resolve) => resolve(7));
                console.log(t instanceof Deeper);
                console.log(await t);
                const d = Deeper.resolve("x");
                console.log(d instanceof Deeper);
                console.log(d instanceof MyPromise);
                console.log(d instanceof Promise);
                console.log(await d);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n7\ntrue\ntrue\ntrue\nx\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_Combinators(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const all = MyPromise.all([MyPromise.resolve(3), Promise.resolve(4)]);
                console.log(all instanceof MyPromise);
                const r = await all;
                console.log(r[0] + r[1]);
                const raced = MyPromise.race([MyPromise.resolve("first")]);
                console.log(raced instanceof MyPromise);
                console.log(await raced);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n7\ntrue\nfirst\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_WithResolvers(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const wr = MyPromise.withResolvers();
                wr.resolve(9);
                console.log(await wr.promise);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_DynamicDispatch_ThenKeepsBrand(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const p: any = MyPromise.resolve(5);
                const r = p.then((v: number) => v * 2);
                console.log(r instanceof MyPromise);
                console.log(await r);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n10\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_BasePromiseUnaffected(ExecutionMode mode)
    {
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const q = Promise.resolve(7);
                console.log(q instanceof MyPromise);
                console.log(q instanceof Promise);
                console.log(await q.then(v => v * 2));
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n14\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_TopLevelStaticThen(ExecutionMode mode)
    {
        // #309: the exact repro — inherited static + derived `then` dispatched
        // from the top-level (synchronous) program body, NOT inside an async
        // function. The async-body path (state machine) already worked; the
        // main ILEmitter.EmitCall path had drifted and omitted the derived
        // Promise-static block, so `MyP.resolve(1)` threw
        // "TypeError: undefined is not a function" in compiled mode.
        var source = """
            class MyP extends Promise<number> {}
            MyP.resolve(1).then(v => console.log("got", v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got 1\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_TopLevelRejectCatch(ExecutionMode mode)
    {
        // #309 sibling: reject/catch on a non-generic subclass from the
        // top-level body.
        var source = """
            class MyP extends Promise<number> {}
            MyP.reject("boom").catch(e => console.log("caught", e));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught boom\n", output);
    }

    [Theory, ModeData]
    public void ExtendsPromise_ConstructorIdentity(ExecutionMode mode)
    {
        // ECMA-262 §27.2.5.1 / #221: subclass instances report the subclass
        // as .constructor; plain promises report Promise.
        var source = """
            class MyPromise<T> extends Promise<T> {}
            async function main() {
                const p = MyPromise.resolve(1);
                console.log(p.constructor === MyPromise);
                console.log(p.constructor === Promise);
                const q = Promise.resolve(1);
                console.log(q.constructor === Promise);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    #region SpeciesConstructor (#221) — then/catch/finally consult @@species

    [Theory, ModeData]
    public void Species_ThenRedirectsToPlainPromise(ExecutionMode mode)
    {
        // ECMA-262 §27.2.5.4 / §7.3.22: then builds its result through
        // SpeciesConstructor(promise, %Promise%) = promise.constructor[@@species].
        // A subclass whose @@species returns Promise gets a PLAIN promise from
        // then — not an instance of the subclass.
        var source = """
            class MyP extends Promise<number> {
                static get [Symbol.species]() { return Promise; }
            }
            async function main() {
                const q = MyP.resolve(1).then(v => v + 1);
                console.log(q instanceof MyP);
                console.log(q instanceof Promise);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n2\n", output);
    }

    [Theory, ModeData]
    public void Species_DefaultIsReceiverClass(ExecutionMode mode)
    {
        // No @@species override: the inherited Promise[@@species] returns the
        // constructor itself, so then stays subclass-typed.
        var source = """
            class MyP extends Promise<number> {}
            async function main() {
                const q = MyP.resolve(1).then(v => v + 1);
                console.log(q instanceof MyP);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n2\n", output);
    }

    [Theory, ModeData]
    public void Species_ThenRedirectsToOtherSubclass(ExecutionMode mode)
    {
        // @@species may name a different Promise subclass; then constructs
        // through it.
        var source = """
            class Other extends Promise<number> {}
            class MyP extends Promise<number> {
                static get [Symbol.species]() { return Other; }
            }
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof Other);
                console.log(q instanceof MyP);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n1\n", output);
    }

    [Theory, ModeData]
    public void Species_UndefinedFallsBackToPromise(ExecutionMode mode)
    {
        // SpeciesConstructor: an @@species of undefined/null defaults to
        // %Promise% (ECMA-262 §7.3.22 step 3-4).
        var source = """
            class MyP extends Promise<number> {
                static get [Symbol.species]() { return undefined as any; }
            }
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof MyP);
                console.log(q instanceof Promise);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Species_CatchAndFinallyFollowSpecies(ExecutionMode mode)
    {
        // catch (= then(undefined, onRejected)) and finally also route their
        // result through SpeciesConstructor.
        var source = """
            class MyP extends Promise<number> {
                static get [Symbol.species]() { return Promise; }
            }
            async function main() {
                const c = MyP.reject("x").catch(e => 0);
                console.log(c instanceof MyP);
                const f = MyP.resolve(1).finally(() => {});
                console.log(f instanceof MyP);
                console.log(await f);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n1\n", output);
    }

    [Theory, ModeData]
    public void Species_CombinatorsUseReceiverConstructorDirectly(ExecutionMode mode)
    {
        // Static methods (resolve/all/race) build through the receiver
        // constructor C directly (e.g. §27.2.4.1 NewPromiseCapability(C)), NOT
        // C[@@species] — so even with @@species → Promise they stay subclass-typed.
        var source = """
            class MyP extends Promise<number> {
                static get [Symbol.species]() { return Promise; }
            }
            async function main() {
                console.log(MyP.resolve(1) instanceof MyP);
                console.log(MyP.all([1, 2]) instanceof MyP);
                console.log(MyP.race([1, 2]) instanceof MyP);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Species_GenericSubclassOverride(ExecutionMode mode)
    {
        // A GENERIC Promise subclass with a @@species override resolves in both
        // modes. The static accessor is registered under the open generic
        // definition (MyP`1) while the receiver's runtime type is closed
        // (MyP<object>); compiled mode reconciles the two (#351).
        var source = """
            class MyP<T> extends Promise<T> {
                static get [Symbol.species]() { return Promise; }
            }
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof MyP);
                console.log(q instanceof Promise);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Species_GenericSubclassRedirectsToOtherGenericSubclass(ExecutionMode mode)
    {
        // A generic subclass whose @@species names ANOTHER generic Promise
        // subclass: then must construct through that (also generic) species. In
        // compiled mode the species value is the open generic definition
        // (Other`1) and is closed before construction (#351).
        var source = """
            class Other<T> extends Promise<T> {}
            class MyP<T> extends Promise<T> {
                static get [Symbol.species]() { return Other; }
            }
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof Other);
                console.log(q instanceof MyP);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n1\n", output);
    }

    [Theory, ModeData]
    public void Species_GenericSubclassStaticReadResolvesOverride(ExecutionMode mode)
    {
        // The guest-visible static read `(MyP as any)[Symbol.species]` of a
        // generic subclass: the class reference is the open generic definition
        // (MyP`1), and reading the static @@species getter must close it before
        // reflective Invoke rather than crashing / returning undefined (#351).
        var source = """
            class MyP<T> extends Promise<T> {
                static get [Symbol.species]() { return Promise; }
            }
            console.log((MyP as any)[Symbol.species] === Promise);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Species_ExpandoOverrideRedirectsToPlainPromise(ExecutionMode mode)
    {
        // A dynamically-assigned static @@species ((C as any)[Symbol.species] =
        // Promise, #262) — with no declared `static get [Symbol.species]()` — is
        // consulted by then in BOTH modes (#349). Compiled mode previously read
        // only the declared-accessor registry; it now also consults the per-Type
        // symbol-expando dict.
        var source = """
            class MyP extends Promise<number> {}
            (MyP as any)[Symbol.species] = Promise;
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof MyP);
                console.log(q instanceof Promise);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n1\n", output);
    }

    [Theory, ModeData]
    public void Species_ExpandoOverrideRedirectsToOtherSubclass(ExecutionMode mode)
    {
        // The expando @@species may name another Promise subclass; then must
        // construct its result through it, exactly as a declared getter would.
        var source = """
            class Other extends Promise<number> {}
            class MyP extends Promise<number> {}
            (MyP as any)[Symbol.species] = Other;
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof Other);
                console.log(q instanceof MyP);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n1\n", output);
    }

    [Theory, ModeData]
    public void Species_ExpandoOverrideInheritedThroughSubclass(ExecutionMode mode)
    {
        // An expando @@species set on a base class is visible on a subclass
        // receiver (#265 base-chain walk): Sub.resolve(...).then(...) follows the
        // base's @@species. Here Base's expando → Promise, so Sub's then result
        // is a plain promise, not a Sub/Base instance.
        var source = """
            class Base extends Promise<number> {}
            (Base as any)[Symbol.species] = Promise;
            class Sub extends Base {}
            async function main() {
                const q = Sub.resolve(1).then(v => v);
                console.log(q instanceof Sub);
                console.log(q instanceof Base);
                console.log(q instanceof Promise);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\ntrue\n1\n", output);
    }

    [Theory, ModeData]
    public void Species_ExpandoOverrideOnGenericSubclass(ExecutionMode mode)
    {
        // An expando @@species on a GENERIC subclass: the assignment keys the
        // open generic definition (MyP`1) while the then-receiver's runtime type
        // is closed (MyP<object>). Compiled mode reconciles the two via
        // SymbolRegistryKey (the same open-vs-closed bridge the declared-accessor
        // path uses, #351) so the expando is found in both modes (#349).
        var source = """
            class MyP<T> extends Promise<T> {}
            (MyP as any)[Symbol.species] = Promise;
            async function main() {
                const q = MyP.resolve(1).then(v => v);
                console.log(q instanceof MyP);
                console.log(q instanceof Promise);
                console.log(await q);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n1\n", output);
    }

    #endregion

    #region General NewPromiseCapability (#349) — non-Promise species + thenable await

    [Theory, ModeData]
    public void Species_NonPromiseConstructor_BuildsThroughIt(ExecutionMode mode)
    {
        // ECMA-262 §27.2.5.4 step 7 / §27.2.4.5: when @@species resolves to a
        // constructor that is NOT %Promise% or a Promise subclass, then/catch/
        // finally build the result through `new S(executor)` (general
        // NewPromiseCapability) and adopt the captured capability — the result is
        // an instance of S, not a Promise (#349). A `then` member makes it a
        // thenable, which `await` adopts back to the settled value.
        var source = """
            class Thenable {
                _value: any = undefined;
                _settled: boolean = false;
                _cb: any = undefined;
                constructor(executor: (res: any, rej: any) => void) {
                    executor(
                        (v: any) => { this._value = v; this._settled = true; if (this._cb) this._cb(v); },
                        (_e: any) => {});
                }
                then(onF: any, _onR?: any) {
                    if (this._settled) onF(this._value); else this._cb = onF;
                }
            }
            class P extends Promise<number> {
                static get [Symbol.species]() { return Thenable as any; }
            }
            async function main() {
                const r: any = P.resolve(10).then((x: number) => x + 1);
                console.log(r instanceof Thenable);
                console.log(r instanceof Promise);
                console.log(await r);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n11\n", output);
    }

    [Theory, ModeData]
    public void Species_NonPromiseConstructor_AdoptsRejection(ExecutionMode mode)
    {
        // The captured capability's reject is driven when the source task faults
        // (here a catch handler that rethrows): the species instance records the
        // rejection reason, unwrapped to the guest value (not an AggregateException).
        var source = """
            class Box {
                err: any = undefined;
                ok: any = undefined;
                constructor(ex: (res: any, rej: any) => void) {
                    ex((v: any) => { this.ok = v; }, (e: any) => { this.err = e; });
                }
            }
            class Q extends Promise<number> {
                static get [Symbol.species]() { return Box as any; }
            }
            const q: any = Q.reject("boom").catch((e: string) => { throw "rethrow:" + e; });
            console.log(q instanceof Box);
            setTimeout(() => { console.log(q.err, q.ok); }, 30);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nrethrow:boom undefined\n", output);
    }

    [Theory, ModeData]
    public void Species_ExpandoNonPromiseConstructor_BuildsThroughIt(ExecutionMode mode)
    {
        // The general NewPromiseCapability path is reached through an EXPANDO
        // @@species ((C as any)[Symbol.species] = S, #262) just as through a
        // declared getter.
        var source = """
            class Thenable {
                v: any = undefined;
                settled: boolean = false;
                cb: any = undefined;
                constructor(executor: (res: any, rej: any) => void) {
                    executor(
                        (x: any) => { this.v = x; this.settled = true; if (this.cb) this.cb(x); },
                        (_e: any) => {});
                }
                then(onF: any) { if (this.settled) onF(this.v); else this.cb = onF; }
            }
            class P extends Promise<number> {}
            (P as any)[Symbol.species] = Thenable;
            async function main() {
                const r: any = P.resolve(5).then((x: number) => x * 2);
                console.log(r instanceof Thenable);
                console.log(r instanceof Promise);
                console.log(await r);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n10\n", output);
    }

    [Theory, ModeData]
    public void Await_AdoptsPlainThenable(ExecutionMode mode)
    {
        // `await` on an ordinary object whose `then` is callable adopts it
        // (ECMA-262 await → PromiseResolve), independent of any Promise species.
        var source = """
            const resolving = { then(onF: any, _onR: any) { onF("hello"); } };
            const rejecting = { then(_onF: any, onR: any) { onR("nope"); } };
            async function main() {
                console.log(await resolving);
                try {
                    await rejecting;
                } catch (e) {
                    console.log("caught:" + e);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\ncaught:nope\n", output);
    }

    [Theory, ModeData]
    public void Await_NonThenableObjectPassesThrough(ExecutionMode mode)
    {
        // An object WITHOUT a callable `then` is not a thenable: `await` returns
        // it unchanged rather than hanging or adopting.
        var source = """
            async function main() {
                const o = { value: 42, then: 7 };
                const r: any = await o;
                console.log(r.value);
                console.log(r.then);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n7\n", output);
    }

    #endregion

    #region SpeciesConstructor IsConstructor (#390) — non-constructor throws, function species constructs

    [Theory, ModeData]
    public void Species_NonConstructor_ThrowsTypeErrorSynchronously(ExecutionMode mode)
    {
        // ECMA-262 §7.3.22 SpeciesConstructor step 5: if C[@@species] is neither
        // undefined/null nor a constructor (IsConstructor is false), throw
        // TypeError. The read happens in the SpeciesConstructor pre-step of
        // then/catch/finally, so the throw is SYNCHRONOUS out of the call (#390).
        var source = """
            class MyPromise extends Promise<number> {
                static get [Symbol.species]() { return 42 as any; }
            }
            const p = MyPromise.resolve(1);
            let threw = false;
            try {
                p.then(x => x);
            } catch (e) {
                threw = true;
                console.log(e instanceof TypeError);
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Species_ExpandoNonConstructor_ThrowsTypeError(ExecutionMode mode)
    {
        // The IsConstructor check applies to an expando @@species
        // ((C as any)[Symbol.species] = nonCtor, #262) just as to a declared
        // getter: a string is not a constructor → TypeError (#390).
        var source = """
            class MyPromise extends Promise<number> {}
            (MyPromise as any)[Symbol.species] = "not a constructor";
            let threw = false;
            try {
                MyPromise.resolve(1).then(x => x);
            } catch (e) {
                threw = true;
                console.log(e instanceof TypeError);
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Species_PlainFunctionConstructor_BuildsThroughNewProtocol(ExecutionMode mode)
    {
        // A @@species that resolves to a plain FUNCTION (constructible via the
        // `new` protocol rather than a class) is a valid constructor: then builds
        // the result through `new S(executor)` and adopts the captured capability,
        // exactly like a class species (#390). The result is an instance of S.
        // The thenable defers an unsettled callback (storing it for the capability
        // to drive on settlement) so the observed value is independent of the
        // microtask/macrotask boundary the two modes settle on (the benign #390
        // timing note). The executor's resolve closes over a local `self`, not
        // `this` — arrows inside a function constructed via the dynamic `new`
        // protocol mis-capture `this` in compiled mode (a pre-existing bug
        // unrelated to species resolution, filed separately).
        var source = """
            function Cap(this: any, executor: (res: any, rej: any) => void) {
                const self: any = this;
                self.tag = "cap";
                self.value = undefined;
                self.settled = false;
                self.cb = undefined;
                executor(
                    (v: any) => { self.value = v; self.settled = true; if (self.cb) self.cb(v); },
                    (_e: any) => {});
            }
            (Cap as any).prototype.then = function (onF: any) {
                if (this.settled) onF(this.value); else this.cb = onF;
            };
            class P extends Promise<number> {
                static get [Symbol.species]() { return Cap as any; }
            }
            async function main() {
                const r: any = P.resolve(5).then((x: number) => x * 2);
                console.log(r instanceof Cap);
                console.log(r.tag);
                console.log(r instanceof Promise);
                const v = await r;
                console.log(v);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ncap\nfalse\n10\n", output);
    }

    #endregion

    #region Poisoned constructor getter (#350) — then/catch/finally throw synchronously

    [Theory, ModeData]
    public void PoisonedConstructor_ThenThrowsSynchronously(ExecutionMode mode)
    {
        // ECMA-262 §27.2.5.4 step 3 resolves SpeciesConstructor(promise, %Promise%)
        // via §7.3.22 step 1 = Get(promise, "constructor") BEFORE PerformPromiseThen
        // (a ReturnIfAbrupt). An own `constructor` getter installed via
        // Object.defineProperty that throws must therefore make `then` throw
        // SYNCHRONOUSLY — not return a rejected promise. test262
        // built-ins/Promise/prototype/then/ctor-poisoned.js.
        var source = """
            const p = new Promise<number>(function () {});
            Object.defineProperty(p, "constructor", {
                get() { throw new Error("poisoned"); }
            });
            let threw = false;
            try {
                p.then(v => v);
            } catch (e) {
                threw = true;
                console.log((e as Error).message);
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("poisoned\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PoisonedConstructor_CatchThrowsSynchronously(ExecutionMode mode)
    {
        // catch delegates to then, so the same synchronous SpeciesConstructor
        // read applies.
        var source = """
            const p = new Promise<number>(function () {});
            Object.defineProperty(p, "constructor", {
                get() { throw new Error("poisoned-catch"); }
            });
            let threw = false;
            try {
                p.catch(() => 0);
            } catch (e) {
                threw = true;
                console.log((e as Error).message);
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("poisoned-catch\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PoisonedConstructor_FinallyThrowsSynchronously(ExecutionMode mode)
    {
        // finally also resolves SpeciesConstructor for its result promise.
        var source = """
            const p = new Promise<number>(function () {});
            Object.defineProperty(p, "constructor", {
                get() { throw new Error("poisoned-finally"); }
            });
            let threw = false;
            try {
                p.finally(() => {});
            } catch (e) {
                threw = true;
                console.log((e as Error).message);
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("poisoned-finally\ntrue\n", output);
    }

    [Theory, ModeData]
    public void PoisonedConstructor_DoesNotAffectNormalThen(ExecutionMode mode)
    {
        // Regression guard: a promise WITHOUT a poisoned `constructor` accessor
        // still resolves through %Promise% and `then` works normally.
        var source = """
            async function main() {
                const r = await Promise.resolve(1).then(v => v + 10);
                console.log(r);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    [Theory, ModeData]
    public void PoisonedConstructor_OwnAccessorWinsOverSubclassConstructor(ExecutionMode mode)
    {
        // An own `constructor` accessor on a Promise SUBCLASS instance shadows the
        // synthetic `constructor` (which otherwise reports the subclass), so its
        // poisoned getter still fires from then.
        var source = """
            class MyP extends Promise<number> {}
            const p = new MyP((resolve) => resolve(1));
            Object.defineProperty(p, "constructor", {
                get() { throw new Error("poisoned-sub"); }
            });
            let threw = false;
            try {
                p.then(v => v);
            } catch (e) {
                threw = true;
                console.log((e as Error).message);
            }
            console.log(threw);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("poisoned-sub\ntrue\n", output);
    }

    #endregion

}
