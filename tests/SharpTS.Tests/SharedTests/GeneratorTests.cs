using SharpTS.Diagnostics.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Shared generator tests that run against both interpreter and compiler.
/// These tests verify core generator functionality that should work identically in both modes.
/// </summary>
public class GeneratorTests
{
    #region For...Of Integration

    [Theory, ModeData]
    public void Generator_ForOfLoop_IteratesAllValues(ExecutionMode mode)
    {
        var source = """
            function* nums() {
                yield 10;
                yield 20;
                yield 30;
            }

            for (let n of nums()) {
                console.log(n);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void Generator_WithParameters_UsesParameters(ExecutionMode mode)
    {
        var source = """
            function* range(start: number, end: number) {
                for (let i = start; i <= end; i++) {
                    yield i;
                }
            }

            for (let x of range(5, 8)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n6\n7\n8\n", output);
    }

    #endregion

    #region For...In Integration (#547)

    [Theory, ModeData]
    public void Generator_ForInLoop_YieldsAllKeys(ExecutionMode mode)
    {
        // A for...in whose body yields must continue past the first key: the key list and index
        // are hoisted to state-machine fields so they survive the MoveNext re-entry (#547).
        var source = """
            function* inGen() {
                const obj = { a: 1, b: 2, c: 3 };
                for (const k in obj) { yield k; }
            }
            console.log([...inGen()].join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a,b,c\n", output);
    }

    [Theory, ModeData]
    public void Generator_ForInLoop_TwoYieldsPerKey_AccumulatesAcrossSuspension(ExecutionMode mode)
    {
        // Two yields per iteration force the loop to re-enter mid-body; the index must persist (#547).
        var source = """
            function* kv() {
                const obj: any = { x: 10, y: 20 };
                for (const k in obj) { yield k; yield obj[k]; }
            }
            console.log([...kv()].join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x,10,y,20\n", output);
    }

    [Theory, ModeData]
    public void Generator_ForInLoop_Nested_YieldsCartesianProduct(ExecutionMode mode)
    {
        // Nested for...in loops, each with its own hoisted key-list/index fields.
        var source = """
            function* nested() {
                const a = { p: 0, q: 0 };
                const b = { m: 0, n: 0 };
                for (const i in a) { for (const j in b) { yield i + j; } }
            }
            console.log([...nested()].join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("pm,pn,qm,qn\n", output);
    }

    #endregion

    #region Yield* Delegation

    [Theory, ModeData]
    public void Generator_YieldStarArray_DelegatesCorrectly(ExecutionMode mode)
    {
        var source = """
            function* withArray() {
                yield* [1, 2, 3];
            }

            for (let x of withArray()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStarGenerator_DelegatesCorrectly(ExecutionMode mode)
    {
        var source = """
            function* inner() {
                yield 2;
                yield 3;
            }

            function* outer() {
                yield 1;
                yield* inner();
                yield 4;
            }

            for (let x of outer()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStar_ForwardsSentValue(ExecutionMode mode)
    {
        // ECMA-262 §14.4.14: the value passed to the outer's next(v) is forwarded
        // into the delegated iterator's next(v), so the inner yield resumes with it
        // rather than undefined (issue #476).
        var source = """
            function* inner() {
                const a = yield 1;
                console.log("inner got", a);
            }
            function* outer() {
                yield* inner();
            }
            const it = outer();
            it.next();
            it.next(99);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner got 99\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStar_ForwardsAcrossYields_AndYieldsCompletionValue(ExecutionMode mode)
    {
        // Each next(v) feeds the inner's successive yields, and the delegate's
        // return value becomes the value of the `yield*` expression.
        var source = """
            function* inner() {
                const a = yield "a";
                const b = yield "b";
                return `inner:${a},${b}`;
            }
            function* outer() {
                const ret = yield* inner();
                console.log("ret", ret);
                yield "after";
            }
            const it = outer();
            console.log(it.next().value);
            console.log(it.next(1).value);
            console.log(it.next(2).value);
            console.log(it.next(3).done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\nb\nret inner:1,2\nafter\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStar_Nested_ForwardsSentValue(ExecutionMode mode)
    {
        // A resume value must thread through every level of nested delegation.
        var source = """
            function* a() {
                const x = yield 1;
                console.log("a got", x);
            }
            function* b() { yield* a(); }
            function* c() { yield* b(); }
            const it = c();
            it.next();
            it.next(42);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a got 42\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStar_InExpression_ForwardsSentValue(ExecutionMode mode)
    {
        // yield* as a sub-expression (operand-spill path) still forwards the resume
        // value and yields the delegate's return value into the surrounding expression.
        var source = """
            function* inner() {
                const x = yield 1;
                return x + 10;
            }
            function* outer() {
                const v = "R:" + (yield* inner());
                console.log(v);
            }
            const it = outer();
            it.next();
            it.next(5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("R:15\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStar_CustomIterator_ForwardsNextSentValue(ExecutionMode mode)
    {
        // ECMA-262 §14.4.14: the value passed to the outer's next(v) must be forwarded
        // to a non-generator custom iterator's next(v) as well (issue #503).
        var source = """
            function makeEcho() {
              let started = false;
              return {
                [Symbol.iterator]() { return this; },
                next(v: any) {
                  const out = started ? `got:${v}` : "start";
                  started = true;
                  return { value: out, done: false };
                },
              };
            }
            function* outer() { yield* makeEcho() as any; }
            const it = outer();
            console.log(it.next().value);
            console.log(it.next(42).value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("start\ngot:42\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStar_CustomIterator_ForwardsMultipleSentValues(ExecutionMode mode)
    {
        // Each successive next(v) on the outer must be forwarded to the custom iterator's next(v).
        var source = """
            function makeEcho() {
              let started = false;
              return {
                [Symbol.iterator]() { return this; },
                next(v: any) {
                  const out = started ? `got:${v}` : "start";
                  started = true;
                  return { value: out, done: false };
                },
              };
            }
            function* outer() { yield* makeEcho() as any; }
            const it = outer();
            console.log(it.next().value);
            console.log(it.next(1).value);
            console.log(it.next(2).value);
            console.log(it.next(3).value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("start\ngot:1\ngot:2\ngot:3\n", output);
    }

    #endregion

    #region Control Flow

    [Theory, ModeData]
    public void Generator_WhileLoop_YieldsMultipleTimes(ExecutionMode mode)
    {
        var source = """
            function* countdown(n: number) {
                while (n > 0) {
                    yield n;
                    n--;
                }
            }

            for (let x of countdown(3)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n1\n", output);
    }

    [Theory, ModeData]
    public void Generator_IfStatement_ConditionalYield(ExecutionMode mode)
    {
        var source = """
            function* conditionalYield(includeSecond: boolean) {
                yield 1;
                if (includeSecond) {
                    yield 2;
                }
                yield 3;
            }

            console.log("With second:");
            for (let x of conditionalYield(true)) {
                console.log(x);
            }

            console.log("Without second:");
            for (let x of conditionalYield(false)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("With second:\n1\n2\n3\nWithout second:\n1\n3\n", output);
    }

    #endregion

    #region Closures

    [Theory, ModeData]
    public void Generator_Closure_CapturesVariables(ExecutionMode mode)
    {
        var source = """
            function* makeSequence(multiplier: number) {
                for (let i = 1; i <= 3; i++) {
                    yield i * multiplier;
                }
            }

            for (let x of makeSequence(10)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    #endregion

    #region Iterator Protocol (.next())

    [Theory, ModeData]
    public void Generator_BasicYield_ReturnsValues(ExecutionMode mode)
    {
        var source = """
            function* counter() {
                yield 1;
                yield 2;
                yield 3;
            }

            let gen = counter();
            console.log(gen.next().value);
            console.log(gen.next().value);
            console.log(gen.next().value);
            console.log(gen.next().done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Generator_EmptyGenerator_ReturnsDoneImmediately(ExecutionMode mode)
    {
        var source = """
            function* empty() {}

            let gen = empty();
            let result = gen.next();
            console.log(result.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Generator_UncaughtThrowClosesIterator(ExecutionMode mode)
    {
        var source = """
            let runs = 0;
            function* fail() {
                runs++;
                throw new Error("boom");
            }

            const gen = fail();
            try { gen.next(); } catch (_) {}
            const result = gen.next();
            console.log(result.value === undefined, result.done, runs);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true true 1\n", output);
    }

    [Theory, ModeData]
    public void Generator_IteratorResult_HasCorrectStructure(ExecutionMode mode)
    {
        var source = """
            function* single() {
                yield 42;
            }

            let gen = single();
            let first = gen.next();
            let second = gen.next();

            console.log("First value:", first.value);
            console.log("First done:", first.done);
            console.log("Second done:", second.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("First value: 42\nFirst done: false\nSecond done: true\n", output);
    }

    [Theory, ModeData]
    public void Generator_MultipleInstances_IndependentState(ExecutionMode mode)
    {
        var source = """
            function* counter() {
                yield 1;
                yield 2;
                yield 3;
            }

            let gen1 = counter();
            let gen2 = counter();

            console.log(gen1.next().value);
            console.log(gen2.next().value);
            console.log(gen1.next().value);
            console.log(gen2.next().value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n2\n2\n", output);
    }

    #endregion

    #region Resume Values (next(v)) — issue #452

    [Theory, ModeData]
    public void Generator_NextWithValue_DeliveredToYield(ExecutionMode mode)
    {
        // ECMA-262 §27.5.3.3: `yield expr` evaluates to the argument of the
        // resuming next(v). The first next() only starts the body.
        var source = """
            function* g() {
                const x = yield 1;
                console.log("got", x);
                const y = yield 2;
                console.log("got", y);
                return 99;
            }
            const it = g();
            console.log("first", it.next().value);
            console.log("r1", it.next(42).value);
            console.log("r2", it.next(43).value);
            console.log("done", it.next().done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first 1\ngot 42\nr1 2\ngot 43\nr2 99\ndone true\n", output);
    }

    [Theory, ModeData]
    public void Generator_AccumulatorPattern_UsesSentValues(ExecutionMode mode)
    {
        // The classic two-way generator: each next(n) feeds the running total.
        var source = """
            function* adder() {
                let total = 0;
                while (true) {
                    const n = yield total;
                    total += n;
                }
            }
            const a = adder();
            console.log(a.next().value);
            console.log(a.next(5).value);
            console.log(a.next(10).value);
            console.log(a.next(3).value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n5\n15\n18\n", output);
    }

    [Theory, ModeData]
    public void Generator_AccumulatorPattern_CompoundAssignAcrossYield(ExecutionMode mode)
    {
        // #497: the same two-way accumulator, but the running total is read *before* the yield
        // as the LHS of `+=`, so it is loop-carried across the suspension. The sibling test
        // Generator_AccumulatorPattern_UsesSentValues sidesteps this by reading `total` after
        // the yield. Pre-fix the compiled state machine left `total` in an IL local that the
        // MoveNext re-entry wiped, so each resume recomputed `0 + n` (5, 10, 3) instead of the
        // running total. The analyzer now hoists loop-body locals of any yielding loop.
        var source = """
            function* adder() {
                let total = 0;
                while (true) {
                    total += yield total;
                }
            }
            const a = adder();
            console.log(a.next().value);
            console.log(a.next(5).value);
            console.log(a.next(10).value);
            console.log(a.next(3).value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n5\n15\n18\n", output);
    }

    [Theory, ModeData]
    public void Generator_AccumulatorPattern_CompoundAssignAcrossYield_DoWhile(ExecutionMode mode)
    {
        // #497: do-while is one of the loop forms that lacked loop-body hoisting pre-fix.
        var source = """
            function* adder() {
                let total = 0;
                do {
                    total += yield total;
                } while (true);
            }
            const a = adder();
            console.log(a.next().value);
            console.log(a.next(5).value);
            console.log(a.next(10).value);
            console.log(a.next(3).value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n5\n15\n18\n", output);
    }

    [Theory, ModeData]
    public void Generator_AccumulatorPattern_CompoundAssignAcrossYield_For(ExecutionMode mode)
    {
        // #497: an unbounded for(;;) likewise lacked loop-body hoisting pre-fix.
        var source = """
            function* adder() {
                let total = 0;
                for (;;) {
                    total += yield total;
                }
            }
            const a = adder();
            console.log(a.next().value);
            console.log(a.next(5).value);
            console.log(a.next(10).value);
            console.log(a.next(3).value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n5\n15\n18\n", output);
    }

    [Theory, ModeData]
    public void Generator_LoopCarriedLocal_ReadBeforeYield_SurvivesAcrossIterations(ExecutionMode mode)
    {
        // #497 (general shape): a counted for-loop whose induction-adjacent local is read in the
        // loop body before the yield. The accumulator must persist across the loop back-edge.
        var source = """
            function* g() {
                let sum = 0;
                for (let i = 0; i < 3; i++) {
                    sum = sum + (yield sum);
                }
                return sum;
            }
            const it = g();
            console.log(it.next().value);    // 0
            console.log(it.next(10).value);  // 10
            console.log(it.next(20).value);  // 30
            console.log(it.next(30).value);  // 60 (return)
            console.log(it.next().done);     // true
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n10\n30\n60\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Generator_BareNext_ResumesWithUndefined(ExecutionMode mode)
    {
        var source = """
            function* g() {
                const x = yield 1;
                console.log(x, typeof x);
            }
            const it = g();
            it.next();
            it.next();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined undefined\n", output);
    }

    [Theory, ModeData]
    public void Generator_NextWithNull_ResumesWithNull(ExecutionMode mode)
    {
        // next(null) must deliver null (typeof "object"), distinct from a bare
        // next() which resumes with undefined.
        var source = """
            function* g() {
                const x = yield 1;
                console.log(x, typeof x);
            }
            const it = g();
            it.next();
            it.next(null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null object\n", output);
    }

    [Theory, ModeData]
    public void Generator_ForOf_ResumesYieldWithUndefined(ExecutionMode mode)
    {
        // for...of drives the generator without passing a value, so a yield used
        // as an expression resolves to undefined (not null).
        var source = """
            function* g() {
                const x = yield 10;
                console.log("resumed", x, typeof x);
            }
            for (const v of g()) {
                console.log("yielded", v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("yielded 10\nresumed undefined undefined\n", output);
    }

    [Theory, ModeData]
    public void Generator_SentValue_UsedInExpression(ExecutionMode mode)
    {
        var source = """
            function* g() {
                const doubled = (yield 1) * 2;
                console.log("doubled", doubled);
            }
            const it = g();
            it.next();
            it.next(21);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("doubled 42\n", output);
    }

    [Theory, ModeData]
    public void Generator_InstanceMethod_NextWithValue_AndThis(ExecutionMode mode)
    {
        var source = """
            class Echo {
                prefix: string = "msg:";
                *run() {
                    const first = yield "ready";
                    console.log(this.prefix + first);
                    const second = yield "more";
                    console.log(this.prefix + second);
                }
            }
            const g = new Echo().run();
            console.log(g.next().value);
            g.next("hello");
            g.next("world");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ready\nmsg:hello\nmsg:world\n", output);
    }

    #endregion

    #region Yield* with String

    [Theory, ModeData]
    public void Generator_YieldStarString_DelegatesCharacters(ExecutionMode mode)
    {
        var source = """
            function* chars() {
                yield* "hi";
            }

            for (let c of chars()) {
                console.log(c);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("h\ni\n", output);
    }

    #endregion

    #region Yield* with Map and Set

    [Theory, ModeData]
    public void Generator_YieldStarMap_DelegatesEntries(ExecutionMode mode)
    {
        var source = """
            function* mapIter() {
                let m = new Map<string, number>();
                m.set("a", 1);
                m.set("b", 2);
                yield* m;
            }

            for (let pair of mapIter()) {
                console.log(pair[0] + ":" + pair[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a:1\nb:2\n", output);
    }

    [Theory, ModeData]
    public void Generator_YieldStarSet_DelegatesValues(ExecutionMode mode)
    {
        var source = """
            function* setIter() {
                let s = new Set<number>();
                s.add(100);
                s.add(200);
                yield* s;
            }

            for (let v of setIter()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }

    #endregion

    #region return()/throw() resume suspended generator (ECMA-262 §27.5.3.4) — issue #478

    // Run in both modes: the interpreter implemented this in #478; the compiled analog (an external
    // return()/throw() injecting an abrupt completion at the suspended yield, so active try/finally/
    // catch run) landed in #526. They double as a cross-mode parity guard.

    [Theory, ModeData]
    public void Generator_Return_RunsFinally_AndReportsValue(ExecutionMode mode)
    {
        // The repro from issue #478: return(v) on a suspended generator resumes it as an
        // abrupt completion, running the active finally block, then settles { value, done }.
        var source = """
            function* g() {
                try {
                    yield 1;
                    yield 2;
                } finally {
                    console.log("finally ran");
                }
            }
            const it = g();
            const a = it.next();
            console.log(a.value, a.done);
            const b = it.return(99);
            console.log(b.value, b.done);
            const c = it.next();
            console.log(c.value, c.done);
            """;

        var output = TestHarness.Run(source, mode);
        // Node: 1 false / finally ran / 99 true / undefined true
        Assert.Equal("1 false\nfinally ran\n99 true\nundefined true\n", output);
    }

    [Theory, ModeData]
    public void Generator_Throw_CaughtByTryCatch_Continues(ExecutionMode mode)
    {
        // throw(e) injects the error at the yield point; an enclosing catch handles it and
        // the generator keeps running (and may yield again).
        var source = """
            function* g() {
                try {
                    yield 1;
                    yield 2;
                } catch (e) {
                    console.log("caught " + e);
                    yield 99;
                }
            }
            const it = g();
            console.log(it.next().value);
            const r = it.throw("boom");
            console.log(r.value, r.done);
            const done = it.next();
            console.log(done.value, done.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ncaught boom\n99 false\nundefined true\n", output);
    }

    [Theory, ModeData]
    public void Generator_Throw_RunsFinally_ThenPropagates(ExecutionMode mode)
    {
        // throw(e) with only a finally (no catch): the finally runs, then the error propagates
        // to the throw() caller.
        var source = """
            function* g() {
                try {
                    yield 1;
                } finally {
                    console.log("finally C");
                }
            }
            const it = g();
            it.next();
            try {
                it.throw("boomC");
            } catch (e) {
                console.log("outer caught " + e);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("finally C\nouter caught boomC\n", output);
    }

    [Theory, ModeData]
    public void Generator_Return_RunsNestedFinallyInnerToOuter(ExecutionMode mode)
    {
        var source = """
            function* g() {
                try {
                    try {
                        yield 1;
                    } finally {
                        console.log("inner");
                    }
                } finally {
                    console.log("outer");
                }
            }
            const it = g();
            it.next();
            const r = it.return(5);
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner\nouter\n5 true\n", output);
    }

    [Theory, ModeData]
    public void Generator_Return_FinallyThatYields_DefersCompletion(ExecutionMode mode)
    {
        // A finally that yields suspends the pending return; the return value is delivered
        // only once the finally completes on a later next().
        var source = """
            function* g() {
                try {
                    yield 1;
                } finally {
                    yield 99;
                }
            }
            const it = g();
            it.next();
            const a = it.return(5);
            console.log(a.value, a.done);
            const b = it.next();
            console.log(b.value, b.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99 false\n5 true\n", output);
    }

    [Theory, ModeData]
    public void Generator_Return_FinallyReturnOverridesValue(ExecutionMode mode)
    {
        // A finally that returns its own value overrides the value passed to return().
        var source = """
            function* g() {
                try {
                    yield 1;
                } finally {
                    return 7;
                }
            }
            const it = g();
            it.next();
            const r = it.return(99);
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7 true\n", output);
    }

    [Theory, ModeData]
    public void Generator_Return_FinallyThrowOverridesReturn(ExecutionMode mode)
    {
        // A finally that throws overrides the pending return with the thrown error.
        var source = """
            function* g() {
                try {
                    yield 1;
                } finally {
                    throw "finally-err";
                }
            }
            const it = g();
            it.next();
            try {
                it.return(5);
            } catch (e) {
                console.log("caught " + e);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught finally-err\n", output);
    }

    [Theory, ModeData]
    public void Generator_ReturnAndThrow_OnNotStarted_DoNotRunBody(ExecutionMode mode)
    {
        // return()/throw() on a generator that hasn't started close it without running the
        // body, so the finally never runs (ECMA-262 §27.5.3.4).
        var source = """
            function* g() {
                try {
                    yield 1;
                } finally {
                    console.log("should NOT run");
                }
            }
            const a = g();
            const r = a.return(8);
            console.log(r.value, r.done);
            const b = g();
            try {
                b.throw("x");
            } catch (e) {
                console.log("threw " + e);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8 true\nthrew x\n", output);
    }

    [Theory, ModeData]
    public void Generator_NextAfterCompletion_ReportsUndefined(ExecutionMode mode)
    {
        // Once a generator finishes, its completion value is delivered exactly once; further
        // next() calls report { value: undefined, done: true }.
        var source = """
            function* g() {
                yield 1;
                return 42;
            }
            const it = g();
            it.next();
            const done = it.next();
            console.log(done.value, done.done);
            const after = it.next();
            console.log(after.value, after.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42 true\nundefined true\n", output);
    }

    [Theory, ModeData]
    public void Generator_UncaughtThrowInBody_PropagatesToNext(ExecutionMode mode)
    {
        // An uncaught throw inside the body surfaces to the next() caller rather than being
        // swallowed (regression guard for the worker's abnormal-completion handling).
        var source = """
            function* g() {
                yield 1;
                throw "bodyboom";
            }
            const it = g();
            it.next();
            try {
                it.next();
            } catch (e) {
                console.log("caught " + e);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught bodyboom\n", output);
    }

    [Theory, ModeData]
    public void Generator_BareReturn_ReportsUndefinedValue(ExecutionMode mode)
    {
        // A no-argument return() resumes with undefined.
        var source = """
            function* g() {
                yield 1;
                yield 2;
            }
            const it = g();
            it.next();
            const r = it.return();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined true\n", output);
    }

    #endregion

    #region yield* forwards return()/throw() to the delegated iterator (ECMA-262 §14.4.14) — issue #514

    // Run in both modes: the interpreter implemented yield* forwarding of return()/throw() in #514;
    // the compiled EmitYieldStar gained the same forwarding (driving the delegate via return()/throw()
    // instead of next()) once the compiled injection landed in #526.

    [Theory, ModeData]
    public void YieldStar_Return_ForwardsToInnerFinally_ThenOuterReturns(ExecutionMode mode)
    {
        // The repro from #514: return(v) on the outer while suspended inside yield* must run the
        // delegated generator's finally, then the outer returns with the delegate's value.
        var source = """
            function* inner() {
                try { yield 1; yield 2; } finally { console.log("inner finally"); }
            }
            function* outer() { yield* inner(); }
            const it = outer();
            console.log(it.next().value);
            const r = it.return(42);
            console.log(r.value, r.done);
            """;

        // Node: 1 / inner finally / 42 true
        Assert.Equal("1\ninner finally\n42 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Throw_NoInnerCatch_RunsInnerFinally_ThenPropagates(ExecutionMode mode)
    {
        // throw(e) is forwarded to the delegate; with only a finally (no catch) the delegate's
        // finally runs, then the error propagates out of yield* to the outer's caller.
        var source = """
            function* inner() {
                try { yield 1; } finally { console.log("inner finally"); }
            }
            function* outer() { yield* inner(); }
            const it = outer();
            console.log(it.next().value);
            try { it.throw("boom"); } catch (e) { console.log("caught " + e); }
            """;

        Assert.Equal("1\ninner finally\ncaught boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Throw_CaughtByInner_KeepsDelegating(ExecutionMode mode)
    {
        // The delegate catches the injected error and yields again; the outer stays in the
        // delegation, so the next() after that resumes the delegate to completion.
        var source = """
            function* inner() {
                try { yield 1; } catch (e) { console.log("inner caught " + e); yield 99; }
            }
            function* outer() { yield* inner(); }
            const it = outer();
            console.log(it.next().value);
            console.log(it.throw("boom").value);
            console.log(it.next().done);
            """;

        Assert.Equal("1\ninner caught boom\n99\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Throw_CaughtByInnerThenReturns_OuterContinuesNormally(ExecutionMode mode)
    {
        // The delegate catches the error and returns: per §14.4.14 step b.5 the yield* evaluates
        // to the delegate's value as a NORMAL completion, so the outer continues past yield*.
        var source = """
            function* inner() {
                try { yield 1; } catch (e) { return "recovered"; }
            }
            function* outer() {
                const x = yield* inner();
                console.log("after yield* " + x);
                yield "outer";
            }
            const it = outer();
            console.log(it.next().value);
            const r = it.throw("boom");
            console.log(r.value, r.done);
            """;

        Assert.Equal("1\nafter yield* recovered\nouter false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Return_InnerFinallyYields_DefersCompletion(ExecutionMode mode)
    {
        // The delegate's finally itself yields: the outer suspends there (reporting the
        // finally's yielded value, not done). A later next() drives the finally to completion;
        // the delegate's deferred return value (7) then resurfaces as the yield* result — a
        // NORMAL completion — so the outer continues and `x` is 7. With nothing after yield*,
        // the outer then falls off its end and completes with undefined.
        var source = """
            function* inner() {
                try { yield 1; } finally { yield "from finally"; }
            }
            function* outer() {
                const x = yield* inner();
                console.log("x=" + x);
            }
            const it = outer();
            console.log(it.next().value);
            const a = it.return(7);
            console.log(a.value, a.done);
            const b = it.next();
            console.log(b.value, b.done);
            """;

        Assert.Equal("1\nfrom finally false\nx=7\nundefined true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Return_InnerFinallyReturnOverridesValue(ExecutionMode mode)
    {
        // A finally in the delegate that returns its own value overrides the value passed to the
        // outer's return(); the outer returns the delegate's overriding value.
        var source = """
            function* inner() {
                try { yield 1; } finally { return 99; }
            }
            function* outer() { yield* inner(); }
            const it = outer();
            console.log(it.next().value);
            const r = it.return(7);
            console.log(r.value, r.done);
            """;

        Assert.Equal("1\n99 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Return_RunsInnerThenOuterFinally(ExecutionMode mode)
    {
        // When the outer also wraps the yield* in try/finally, the abrupt return runs the
        // delegate's finally first, then unwinds the outer's own finally (inner before outer).
        var source = """
            function* inner() {
                try { yield 1; } finally { console.log("inner finally"); }
            }
            function* outer() {
                try { yield* inner(); } finally { console.log("outer finally"); }
            }
            const it = outer();
            console.log(it.next().value);
            const r = it.return(5);
            console.log(r.value, r.done);
            """;

        Assert.Equal("1\ninner finally\nouter finally\n5 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Nested_Return_ReachesInnermostFinally(ExecutionMode mode)
    {
        // Two levels of delegation (outer → middle → inner): return() must thread through both
        // delegations and run the innermost finally first, then the middle's.
        var source = """
            function* inner() {
                try { yield 1; } finally { console.log("inner finally"); }
            }
            function* middle() {
                try { yield* inner(); } finally { console.log("middle finally"); }
            }
            function* outer() { yield* middle(); }
            const it = outer();
            console.log(it.next().value);
            const r = it.return(3);
            console.log(r.value, r.done);
            """;

        Assert.Equal("1\ninner finally\nmiddle finally\n3 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Nested_Throw_RunsAllFinallys_ThenPropagates(ExecutionMode mode)
    {
        // throw() through two levels of delegation with no catch anywhere: each finally runs
        // (innermost first), then the error surfaces to the throw() caller.
        var source = """
            function* inner() {
                try { yield 1; } finally { console.log("inner finally"); }
            }
            function* middle() {
                try { yield* inner(); } finally { console.log("middle finally"); }
            }
            function* outer() { yield* middle(); }
            const it = outer();
            console.log(it.next().value);
            try { it.throw("kaboom"); } catch (e) { console.log("caught " + e); }
            """;

        Assert.Equal("1\ninner finally\nmiddle finally\ncaught kaboom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Return_InnerWithoutFinally_TerminatesCleanly(ExecutionMode mode)
    {
        // return() forwarded to a delegate that has no finally: the delegate just closes, the
        // outer returns the value, and the statement after yield* never runs.
        var source = """
            function* inner() { yield 1; yield 2; }
            function* outer() { yield* inner(); yield 3; }
            const it = outer();
            console.log(it.next().value);
            const r = it.return(11);
            console.log(r.value, r.done);
            console.log(it.next().done);
            """;

        Assert.Equal("1\n11 true\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Return_ForwardsIntoGeneratorExpressionDelegate(ExecutionMode mode)
    {
        // The delegate is a generator function EXPRESSION (a distinct runtime type from a
        // declaration); return() forwarding must reach its finally too.
        var source = """
            const inner = function* () {
                try { yield 1; } finally { console.log("inner finally"); }
            };
            function* outer() { yield* inner(); }
            const it = outer();
            console.log(it.next().value);
            const r = it.return(8);
            console.log(r.value, r.done);
            """;

        Assert.Equal("1\ninner finally\n8 true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_Return_FromGeneratorExpressionOuter(ExecutionMode mode)
    {
        // The OUTER is a generator function expression delegating to a declaration; the suspend
        // side of the delegation loop is the expression-generator runtime type here.
        var source = """
            function* inner() {
                try { yield 1; } finally { console.log("inner finally"); }
            }
            const outer = function* () { yield* inner(); };
            const it = outer();
            console.log(it.next().value);
            const r = it.return(8);
            console.log(r.value, r.done);
            """;

        Assert.Equal("1\ninner finally\n8 true\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Generator function EXPRESSION closes over block-scoped outer variables — issue #522

    // A generator function expression (`const g = function*() {...}`) is lifted to a top-level
    // declaration so the IL pipeline can handle it (GeneratorArrowLifter). The lift must not move
    // the body ahead of the block-scoped (let/const) bindings it closes over, or the type checker
    // (which checks bodies in source order) rejects the reference as "Undefined variable".

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverOuterLet(ExecutionMode mode)
    {
        // The exact repro from issue #522.
        var source = """
            let x: number = 1;
            const g = function*() { yield x; yield x + 1; };
            for (const v of g()) console.log(v);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverOuterConst(ExecutionMode mode)
    {
        var source = """
            const base = 10;
            const g = function*() { yield base; yield base * 2; };
            for (const v of g()) console.log(v);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory, ModeData]
    public void GeneratorExpression_CapturesVariableNotSnapshot(ExecutionMode mode)
    {
        // The closure binds the variable, so a mutation between definition and iteration is observed.
        var source = """
            let c: number = 5;
            const g = function*() { yield c; };
            c = 99;
            for (const v of g()) console.log(v);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void GeneratorExpression_NestedInFunction_ClosesOverModuleLet(ExecutionMode mode)
    {
        // A generator expression nested inside another function may still close over module-scope
        // bindings; the lift carries it to module scope, where `base` is visible.
        var source = """
            let baseVal: number = 100;
            function make() {
                const g = function*() { yield baseVal; };
                return g;
            }
            for (const v of make()()) console.log(v);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region Generator function EXPRESSION in call/IIFE position — issue #488

    // A generator function expression invoked inline as an IIFE establishes a generator context.
    // The GeneratorArrowLifter must descend through the grouped callee so the expression is lifted
    // (otherwise the type checker rejects its `yield` because the generator context is never set).

    [Theory, ModeData]
    public void GeneratorExpression_IifeCallPosition(ExecutionMode mode)
    {
        // The exact repro from issue #488.
        var source = """
            const it = (function* () { yield 1; })();
            console.log(it.next().value);
            """;

        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_IifeSpreadIntoArray(ExecutionMode mode)
    {
        var source = """
            const arr = [...(function* () { yield 1; yield 2; yield 3; })()];
            console.log(arr.join(","));
            """;

        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Generator function EXPRESSION inside loop/try/switch/labeled bodies — issue #634

    // GeneratorArrowLifter.RewriteStmt must descend into for / for-of / for-in / do-while /
    // try-catch-finally / switch / labeled statement bodies, or a generator function expression
    // declared there is never lifted and its `yield` is rejected at type-check time.

    [Theory, ModeData]
    public void GeneratorExpression_InsideForLoop(ExecutionMode mode)
    {
        // The exact repro from issue #634.
        var source = """
            for (let k = 0; k < 1; k++) {
              const g = function* () { yield 99; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideForOf(ExecutionMode mode)
    {
        // The generator yields a constant (not the loop variable): #634 is about the lifter reaching
        // the for-of body, not closure capture. Capturing the block-scoped loop variable is handled
        // separately (#678) — see the "closes over a block-scoped binding" region below.
        var source = """
            for (const n of [10, 20]) {
              const g = function* () { yield 1; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("1\n1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideForIn(ExecutionMode mode)
    {
        var source = """
            const obj = { a: 1 };
            for (const k in obj) {
              const g = function* () { yield 2; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideLoopClosesOverModuleScope(ExecutionMode mode)
    {
        // A generator expression inside a loop body may still close over a MODULE-scope binding;
        // the lift carries it to module scope, where `factor` is visible (both modes).
        var source = """
            const factor = 7;
            for (const n of [1, 2]) {
              const g = function* () { yield factor; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("7\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideDoWhile(ExecutionMode mode)
    {
        var source = """
            let i = 0;
            do {
              const g = function* () { yield 7; };
              console.log(g().next().value);
              i++;
            } while (i < 1);
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideTryCatchFinally(ExecutionMode mode)
    {
        var source = """
            try {
              const g = function* () { yield 1; };
              console.log(g().next().value);
            } catch (e) {
              const g = function* () { yield 2; };
              console.log(g().next().value);
            } finally {
              const g = function* () { yield 3; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("1\n3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideSwitchCase(ExecutionMode mode)
    {
        var source = """
            switch (1) {
              case 1: {
                const g = function* () { yield 42; };
                console.log(g().next().value);
                break;
              }
              default:
                console.log("none");
            }
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_InsideLabeledStatement(ExecutionMode mode)
    {
        var source = """
            outer: {
              const g = function* () { yield 5; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Generator function EXPRESSION closing over an enclosing function's local — issue #534

    // A generator expression that closes over an enclosing FUNCTION local: the GeneratorArrowLifter
    // relocates it to the end of that function's body (keeping the local in lexical scope) rather than
    // to module scope. The interpreter runs that nested declaration natively. On the compile path the
    // NestedFunctionLifter lambda-lifts it (the captured local becomes a leading parameter forwarded by
    // an arrow); because the lift is appended at body end, the forwarding binding is HOISTED to the body
    // top so the earlier reference resolves (#534). These run in BOTH modes. The decline cases below
    // (this/arguments, rest/default params, self-recursion) instead stay nested and fail cleanly in
    // compiled mode with "Yield not supported in this context", never a miscompile.

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverFunctionLocal(ExecutionMode mode)
    {
        // The exact repro from issue #534.
        var source = """
            function outer() {
              let y = 5;
              const g = function*() { yield y; };
              return [...g()];
            }
            console.log(outer());
            """;

        Assert.Equal("[5]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverFunctionParameter(ExecutionMode mode)
    {
        var source = """
            function make(seed: number) {
              const g = function*() { yield seed; yield seed * 2; };
              return [...g()];
            }
            console.log(make(3));
            """;

        Assert.Equal("[3, 6]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverLocalCapturesByReference(ExecutionMode mode)
    {
        // Closures bind the variable, so a mutation before iteration is observed (not a snapshot). In
        // compiled mode the lambda-lift forwarding arrow reads the captured local live at call time, so
        // hoisting it to the body top still observes the mutation.
        var source = """
            function outer() {
              let c = 1;
              const g = function*() { yield c; };
              c = 99;
              return [...g()];
            }
            console.log(outer());
            """;

        Assert.Equal("[99]\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GeneratorExpression_ClosesOverFunctionLocal_CompiledHoistsForwardReference()
    {
        // Directly pins the #534 compile-path fix: the lifted `function* __genArrow_N` is appended at
        // the enclosing body's END, so its lambda-lift forwarding binding must be hoisted above the
        // earlier `const g = …` reference. Before the fix this miscompiled to a runtime
        // "Undefined variable '__genArrow_0'".
        var source = """
            function outer() {
              let y = 5;
              const g = function*() { yield y; };
              return [...g()];
            }
            console.log(outer());
            """;

        Assert.Equal("[5]\n", TestHarness.RunCompiled(source));
    }

    // Decline cases: a capturing generator expression the lambda-lift cannot faithfully forward stays
    // nested and fails cleanly in compiled mode ("Yield not supported in this context"), never a
    // miscompile. The interpreter runs all of them natively.

    [Fact]
    public void GeneratorExpression_ClosesOverLocal_WithRestParam_CompiledFailsCleanly()
    {
        var source = """
            function outer() {
              let y = 5;
              const g = function*(...rest: number[]) { yield y; };
              return [...g()];
            }
            console.log(outer());
            """;

        Assert.Equal("[5]\n", TestHarness.Run(source, ExecutionMode.Interpreted));
        var ex = Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Yield not supported", ex.Message);
    }

    [Fact]
    public void GeneratorExpression_ClosesOverLocal_WithDefaultParam_CompiledFailsCleanly()
    {
        var source = """
            function outer() {
              let y = 5;
              const g = function*(x: number = 2) { yield y + x; };
              return [...g()];
            }
            console.log(outer());
            """;

        Assert.Equal("[7]\n", TestHarness.Run(source, ExecutionMode.Interpreted));
        var ex = Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Yield not supported", ex.Message);
    }

    [Fact]
    public void GeneratorExpression_ClosesOverLocal_UsingArguments_CompiledFailsCleanly()
    {
        var source = """
            function outer() {
              let y = 5;
              const g = function*() { yield y; yield arguments.length; };
              return [...g(1, 2)];
            }
            console.log(outer());
            """;

        // The interpreter's generator-expression `arguments` binding is a separate existing gap; this
        // test pins the compiler's conservative decline only.
        var ex = Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Yield not supported", ex.Message);
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverLocal_UsingThis_ThreadsDynamicThis(ExecutionMode mode)
    {
        // #775: a generator function expression that closes over an enclosing-function local AND uses
        // `this` lambda-lifts and threads its own dynamic `this`. Called plainly (`g()`), `this` is the
        // sloppy-mode receiver (globalThis), so the body simply yields two values — length 2.
        var source = """
            function outer(this: any) {
              let y = 5;
              const g = function*() { yield y; yield this; };
              return [...g()].length;
            }
            console.log(outer.call({}));
            """;

        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverLocal_UsingThis_DotCallBindsReceiver(ExecutionMode mode)
    {
        // #775: the generator captures an enclosing-function local (`mult`) AND reads `this` via a bound
        // receiver (`.call`). Both the captured local and the dynamic receiver must resolve.
        var source = """
            function outer() {
              let mult = 3;
              const g = function*() { yield this.base * mult; yield this.base + mult; };
              return [...(g as any).call({ base: 10 })];
            }
            console.log(outer().join(","));
            """;

        Assert.Equal("30,13\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Generator expression capturing an enclosing GENERATOR's local — issue #945

    // #945 (generator-encloser analog of #534/#924): a generator expression closing over a local of an
    // enclosing top-level/module-level (or nested-in-plain-function) `function*` now runs in BOTH modes.
    // The lifted forwarding binding is hoisted to the generator body top and its read-only forwarded
    // capture is routed through the generator's function display class, so the hoisted forwarder reads
    // it LIVE. The same storage path now covers class generator methods, including static and async.

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverGeneratorLocal(ExecutionMode mode)
    {
        // The exact repro from issue #945.
        var source = """
            function* outer() {
              let y = 5;
              const g = function*() { yield y; yield y + 1; };
              yield* g();
            }
            console.log([...outer()]);
            """;

        Assert.Equal("[5, 6]\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GeneratorExpression_ClosesOverGeneratorLocal_CompiledHoistsForwardReference()
    {
        // Directly pins the #945 compile-path fix: before the fix the appended `function* __genArrow_N`'s
        // forwarding binding was not hoisted for a generator encloser, miscompiling to a runtime
        // "object is not a function".
        var source = """
            function* outer() {
              let y = 5;
              const g = function*() { yield y; };
              yield* g();
            }
            console.log([...outer()]);
            """;

        Assert.Equal("[5]\n", TestHarness.RunCompiled(source));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverGeneratorLocal_CapturesByReference(ExecutionMode mode)
    {
        // The forwarder reads the captured local LIVE through the shared function DC, so a write-capturing
        // arrow that mutates `y` before iteration is observed (here 1 + 10 + 20 = 31) — pinning that the
        // write-capture and the forwarder's read share one DC field.
        var source = """
            function* outer() {
              let y = 1;
              const g = function*() { yield y; };
              [10, 20].forEach(n => { y += n; });
              yield* g();
            }
            console.log([...outer()]);
            """;

        Assert.Equal("[31]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverGeneratorLocal_MultipleForwarders(ExecutionMode mode)
    {
        // Two generator expressions each capturing a distinct enclosing-generator local → two DC fields.
        var source = """
            function* outer() {
              let a = 5;
              let b = 100;
              const g1 = function*() { yield a; };
              const g2 = function*() { yield b; };
              yield* g1();
              yield* g2();
            }
            console.log([...outer()]);
            """;

        Assert.Equal("[5, 100]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverGeneratorLocal_NestedInPlainFunction(ExecutionMode mode)
    {
        // The enclosing generator is itself nested in a plain function. NestedFunctionLifter relocates it
        // to module top level, where it wires a function DC — so the encloser is still hoist-safe.
        var source = """
            function host() {
              function* outer() {
                let y = 7;
                const g = function*() { yield y; };
                yield* g();
              }
              return [...outer()];
            }
            console.log(host());
            """;

        Assert.Equal("[7]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverInstanceGeneratorMethodLocal(ExecutionMode mode)
    {
        // The forwarding closure is created at method entry, then reads y after its mutation through the
        // shared generator-method display class.
        var source = """
            class C {
              *m() {
                let y = 5;
                const g = function*() { yield y; };
                y = 9;
                yield* g();
              }
            }
            console.log([...new C().m()]);
            """;

        Assert.Equal("[9]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_ClosesOverStaticGeneratorMethodParameter(ExecutionMode mode)
    {
        // Static method parameters start at IL argument zero; seed the captured parameter into the live
        // display-class cell before MoveNext and observe a later mutation.
        var source = """
            class C {
              static *m(y: number) {
                const g = function*() { yield y; };
                y += 4;
                yield* g();
              }
            }
            console.log([...C.m(7)]);
            """;

        Assert.Equal("[11]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpressions_ClassExpressionMethods_AllKindsUseIndependentLiveStorage(ExecutionMode mode)
    {
        // A ternary forces NestedFunctionLifter to reach the class expression through a non-trivial
        // expression path. Same-named static/instance methods must receive distinct DC keys. The static
        // parameter cases also pin argument offset zero for both iterator families.
        var source = """
            const C = true ? class {
              *m(seed: number) {
                const g = function*() { yield seed; };
                seed += 1;
                yield* g();
              }
              static *m(seed: number) {
                const g = function*() { yield seed; };
                seed += 2;
                yield* g();
              }
              async *am(seed: number) {
                const g = async function*() { yield seed; };
                seed += 3;
                for await (const value of g()) yield value;
              }
              static async *am(seed: number) {
                const g = async function*() { yield seed; };
                seed += 4;
                for await (const value of g()) yield value;
              }
            } : class {};

            async function main() {
              const values: number[] = [];
              values.push(new C().m(10).next().value);
              values.push(C.m(10).next().value);
              for await (const value of new C().am(10)) values.push(value);
              for await (const value of C.am(10)) values.push(value);
              console.log(values.join(","));
            }
            main();
            """;

        Assert.Equal("11,12,13,14\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GeneratorExpression_NamedSelfRecursive_ClosesOverGeneratorLocal_CompiledFailsCleanly()
    {
        // A NAMED self-recursive generator expression that also captures an enclosing local is declined by
        // the lambda-lift (self-recursion can't be forwarded). The compiled path must be a clean error or
        // the correct value — never a wrong value. (The interpreter's handling of this self-recursive form
        // is a separate pre-existing limitation, so only the compiled "never wrong" guarantee is pinned.)
        var source = """
            function* outer() {
              let n = 2;
              const g = function* rec(k: number) { if (k > 0) { yield n; yield* rec(k - 1); } };
              yield* g(2);
            }
            console.log([...outer()]);
            """;

        var ex = Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Yield not supported", ex.Message);
    }

    #endregion

    #region Generator function expression / object generator method dynamic `this` — issue #775

    [Theory, ModeData]
    public void ObjectGeneratorMethod_ReadsThis(ExecutionMode mode)
    {
        // The canonical case from the issue: an object generator method reads instance state via `this`.
        var source = """
            const o = { v: 5, *gen() { yield this.v; yield this.v + 1; } };
            for (const x of o.gen()) console.log(x);
            """;

        Assert.Equal("5\n6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorFunctionExpression_DotCallBindsThis(ExecutionMode mode)
    {
        var source = """
            function* g() { yield this.x; }
            const o = { x: 9 };
            for (const v of (g as any).call(o)) console.log(v);
            """;

        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorFunctionExpression_AssignedAsMethod_BindsThis(ExecutionMode mode)
    {
        var source = """
            const o: any = { v: 42 };
            o.gen = function*() { yield this.v; };
            console.log([...o.gen()].join(","));
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ObjectSymbolIteratorGenerator_ReadsThis(ExecutionMode mode)
    {
        // The canonical MDN iterator pattern: a `[Symbol.iterator]` generator reading `this`.
        var source = """
            const o = {
              vals: [10, 20],
              [Symbol.iterator]: function*() { for (const v of this.vals) yield v; }
            };
            for (const x of o) console.log(x);
            """;

        Assert.Equal("10\n20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NamedGeneratorFunctionExpression_ReadsThis(ExecutionMode mode)
    {
        var source = """
            const o: any = { base: 100 };
            o.gen = function* gen() { yield this.base; yield this.base + 1; };
            console.log([...o.gen()].join(","));
            """;

        Assert.Equal("100,101\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PlainGeneratorFunctionExpressionCall_ThisIsSloppyGlobal(ExecutionMode mode)
    {
        // A `function*` expression called plainly (no receiver) binds `this` to the sloppy-mode global
        // (consistent with a non-generator function expression), not an unbound-variable error. Both
        // back ends model sloppy `this` as a globalThis object → typeof is "object".
        var source = """
            const g = function*() { yield typeof this; };
            console.log([...g()].join(","));
            """;

        Assert.Equal("object\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void PlainGeneratorFunctionExpressionCall_ThisIsUndefinedInStrictMode_Compiled()
    {
        var source = """
            const g = function*() { "use strict"; yield typeof this; };
            console.log([...g()].join(","));
            """;

        Assert.Equal("undefined\n", TestHarness.RunCompiled(source));
    }

    #endregion

    #region Generator function EXPRESSION closing over a BLOCK-scoped binding — issue #678

    // A generator expression that closes over a block-scoped binding (a for/for-of/for-in loop
    // variable, a catch parameter, or a let/const declared in a nested block or switch body) cannot be
    // lifted to a top-level declaration — the lift target sits outside that block, so the captured name
    // would be out of scope. The GeneratorArrowLifter leaves it in place as an expression; the
    // interpreter runs it natively (SharpTSGenerator) and the type checker establishes the generator
    // context directly. These run interpreted-only: the compiler has no generator-expression IL path
    // for a generator that captures a block-scoped binding and reports a clear "Yield not supported in
    // this context" error (the lift target sits outside the block, so unlike #534's enclosing-FUNCTION
    // local — which is now lambda-lifted and runs in both modes — this cannot be lowered; asserted by
    // the *_CompiledRejectsClearly test).

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesForOfLoopVariable(ExecutionMode mode)
    {
        // The exact repro from issue #678.
        var source = """
            for (const n of [10, 20]) {
              const g = function* () { yield n; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("10\n20\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesNestedBlockLet(ExecutionMode mode)
    {
        // The second repro from issue #678: a let in a nested block within a function.
        var source = """
            function outer() {
              {
                let y = 5;
                const g = function* () { yield y; };
                return [...g()];
              }
            }
            console.log(outer());
            """;

        Assert.Equal("[5]\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesForLetLoopVariable(ExecutionMode mode)
    {
        var source = """
            for (let k = 1; k <= 2; k++) {
              const g = function* () { yield k * 10; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("10\n20\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesForInKey(ExecutionMode mode)
    {
        var source = """
            const obj = { a: 1, b: 2 };
            for (const key in obj) {
              const g = function* () { yield key; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("a\nb\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesCatchParameter(ExecutionMode mode)
    {
        var source = """
            try {
              throw "boom";
            } catch (e) {
              const g = function* () { yield e; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("boom\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesSwitchCaseBinding(ExecutionMode mode)
    {
        var source = """
            switch (1) {
              case 1: {
                const local = 99;
                const g = function* () { yield local; };
                console.log(g().next().value);
                break;
              }
            }
            """;

        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_LoopVariableCapturedPerIteration(ExecutionMode mode)
    {
        // Each iteration's for-of binding is distinct, so a generator created in one iteration captures
        // that iteration's value — not the last one. Confirms the in-place generator closes over the
        // per-iteration binding (it is not hoisted to a single shared slot).
        var source = """
            const gens: (() => Generator<number>)[] = [];
            for (const n of [1, 2, 3]) {
              gens.push(function* () { yield n; });
            }
            console.log(gens.map(g => g().next().value).join(","));
            """;

        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_CapturesBlockScopedAndModuleScoped(ExecutionMode mode)
    {
        // A capture mixing a block-scoped loop variable with a module-scoped const still works: the
        // presence of a block-scoped capture keeps the generator in place, and the module binding
        // remains visible via the closure.
        var source = """
            const factor = 100;
            for (const n of [2]) {
              const g = function* () { yield n * factor; };
              console.log(g().next().value);
            }
            """;

        Assert.Equal("200\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_NamedSelfRecursionCapturingLoopVariable(ExecutionMode mode)
    {
        // #678 + #679 together: a NAMED generator expression that both closes over a loop variable and
        // recurses by its own name. The in-place native path binds the self-name in the call scope.
        var source = """
            for (const base of [10]) {
              const g = function* count(n: number): Generator<number> {
                if (n > 0) { yield base + n; yield* count(n - 1); }
              };
              console.log([...g(2)].join(","));
            }
            """;

        Assert.Equal("12,11\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GeneratorExpression_BlockScopedCapture_CompiledRejectsClearly()
    {
        // The compiler has no generator-expression IL path for a generator that closes over a
        // block-scoped binding (it cannot be lifted out of its block, and nested-generator lowering is
        // incomplete, #501). It must FAIL FAST with a clear message rather than crash or silently
        // misbehave — matching the #534 enclosing-function-local decline cases (this/rest/recursion).
        var source = """
            for (const n of [1, 2]) {
              const g = function* () { yield n; };
              console.log(g().next().value);
            }
            """;

        var ex = Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Yield not supported", ex.Message);
    }

    #endregion

    #region ASYNC generator function EXPRESSION closing over a BLOCK-scoped binding — issue #734

    // The async analog of #678: an `async function*() {}` expression that closes over a block-scoped
    // binding (loop variable, catch parameter, nested-block let/const) likewise cannot be lifted, so the
    // GeneratorArrowLifter leaves it in place. EvaluateArrowFunction dispatches IsAsync && IsGenerator to
    // a SharpTSAsyncArrowGeneratorFunction so the interpreter runs it natively (previously it was
    // mishandled as a plain async function, leaving the capture out of scope — "Undefined variable").
    // Interpreted-only: the compiler has no generator-expression IL path and reports a clear
    // "Yield not supported in this context" error (asserted by the *_CompiledRejectsClearly test).

    [Theory, InterpretedOnlyData]
    public void AsyncGeneratorExpression_CapturesForOfLoopVariable(ExecutionMode mode)
    {
        // The exact repro from issue #734.
        var source = """
            async function main() {
              for (const n of [10]) {
                const g = async function* () { yield n; };
                const out: number[] = [];
                for await (const v of g()) out.push(v);
                console.log(out.join(','));
              }
            }
            main();
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void AsyncGeneratorExpression_CapturesNestedBlockLet(ExecutionMode mode)
    {
        // A let in a nested block captured by an in-place async generator expression, driven by direct
        // next() (which the lazy coroutine resolves asynchronously).
        var source = """
            async function main() {
              {
                let y = 5;
                const g = async function* () { yield y; yield y + 1; };
                const it = g();
                console.log((await it.next()).value + "," + (await it.next()).value);
              }
            }
            main();
            """;

        Assert.Equal("5,6\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void AsyncGeneratorExpression_CapturesLoopVariablePerIteration(ExecutionMode mode)
    {
        // Each iteration's for-of binding is distinct, so an async generator created in one iteration
        // captures that iteration's value — confirming the in-place generator closes over the
        // per-iteration binding rather than a single shared slot.
        var source = """
            async function main() {
              let out = "";
              for (const n of [1, 2, 3]) {
                const g = async function* () { yield n * 10; };
                for await (const v of g()) out += v + ",";
              }
              console.log(out);
            }
            main();
            """;

        Assert.Equal("10,20,30,\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AsyncGeneratorExpression_BlockScopedCapture_CompiledRejectsClearly()
    {
        // The compiler has no generator-expression IL path for an async generator that closes over a
        // block-scoped binding; it must FAIL FAST with a clear message rather than crash (matching the
        // sync #678 case).
        var source = """
            async function main() {
              for (const n of [1, 2]) {
                const g = async function* () { yield n; };
                for await (const v of g()) console.log(v);
              }
            }
            main();
            """;

        var ex = Assert.Throws<CompileException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Yield not supported", ex.Message);
    }

    #endregion

    #region ASYNC generator function EXPRESSION closing over an enclosing async function's local — issue #924

    // The async analog of #534: an `async function*() {}` expression that closes over an enclosing
    // ASYNC FUNCTION local. The GeneratorArrowLifter relocates it to the end of that function's body
    // and replaces the use with a forward reference. The interpreter now hoists function declarations
    // in async bodies (ExecuteBlockAsync), matching the sync path, so the appended declaration is in
    // scope. On the compile path the NestedFunctionLifter lambda-lifts it; because an async function
    // routes a nested arrow's captures through a shared function display class (read live at call
    // time, like a plain function), the forwarding binding is HOISTED to the body top so the earlier
    // reference resolves — the async analog of the plain-function #534 hoist. These run in BOTH modes.

    [Theory, ModeData]
    public void AsyncGeneratorExpression_ClosesOverAsyncFunctionLocal(ExecutionMode mode)
    {
        // The exact repro from issue #924.
        var source = """
            async function outer() {
              let y = 5;
              const g = async function* () { yield y; };
              const out: number[] = [];
              for await (const v of g()) out.push(v);
              return out;
            }
            async function main() { console.log(await outer()); }
            main();
            """;

        Assert.Equal("[5]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorExpression_ClosesOverAsyncFunctionParameter(ExecutionMode mode)
    {
        var source = """
            async function make(seed: number) {
              const g = async function* () { yield seed; yield seed * 2; };
              const out: number[] = [];
              for await (const v of g()) out.push(v);
              return out;
            }
            async function main() { console.log(await make(3)); }
            main();
            """;

        Assert.Equal("[3, 6]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorExpression_ClosesOverLocalCapturesByReference(ExecutionMode mode)
    {
        // Closures bind the variable, so a mutation before iteration is observed (not a snapshot). In
        // compiled mode the lambda-lift forwarding arrow reads the captured local live at call time
        // through the async function's shared display class, so hoisting it to the body top still
        // observes the mutation — pinning by-reference semantics (a regression to a by-value snapshot
        // would yield 1).
        var source = """
            async function outer() {
              let c = 1;
              const g = async function* () { yield c; };
              c = 99;
              const out: number[] = [];
              for await (const v of g()) out.push(v);
              return out;
            }
            async function main() { console.log(await outer()); }
            main();
            """;

        Assert.Equal("[99]\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AsyncGeneratorExpression_ClosesOverAsyncFunctionLocal_CompiledHoistsForwardReference()
    {
        // Directly pins the #924 compile-path fix: the lifted `async function* __genArrow_N` is
        // appended at the enclosing async body's END, so its lambda-lift forwarding binding must be
        // hoisted above the earlier `const g = …` reference. Before the fix this miscompiled to an
        // unbound `__genArrow_0` (the gate declined to hoist for any state-machine encloser).
        var source = """
            async function outer() {
              let y = 5;
              const g = async function* () { yield y; };
              const out: number[] = [];
              for await (const v of g()) out.push(v);
              return out;
            }
            async function main() { console.log(await outer()); }
            main();
            """;

        Assert.Equal("[5]\n", TestHarness.RunCompiled(source));
    }

    [Theory, ModeData]
    public void SyncGeneratorExpression_InAsyncFunction_ClosesOverLocal(ExecutionMode mode)
    {
        // A SYNC generator expression closing over a local of an enclosing ASYNC function — the same
        // async-function encloser as #924, fixed by the same interpreter hoist and compiled gate relax.
        var source = """
            async function outer() {
              let y = 7;
              const g = function* () { yield y; yield y + 1; };
              const out: number[] = [];
              for (const v of g()) out.push(v);
              return out;
            }
            async function main() { console.log(await outer()); }
            main();
            """;

        Assert.Equal("[7, 8]\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Named generator function EXPRESSION self-reference — issue #679

    // A NAMED generator function expression can call itself by its own name for recursion. The
    // GeneratorArrowLifter renames the lifted declaration to the synthetic __genArrow_N and discards
    // the original name, so it injects `const <name> = __genArrow_N;` at the top of the body to keep
    // the self-reference bound (skipped when a parameter or body-level binding shadows the name).

    [Theory, ModeData]
    public void GeneratorExpression_NamedSelfRecursion(ExecutionMode mode)
    {
        // The exact repro from issue #679.
        var source = """
            const g = function* countdown(n: number): Generator<number> {
              if (n > 0) { yield n; yield* countdown(n - 1); }
            };
            console.log([...g(3)]);
            """;

        Assert.Equal("[3, 2, 1]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_NamedSelfRecursion_ClosesOverModuleConst(ExecutionMode mode)
    {
        // Self-reference plus a capture of a module-scope binding: both must resolve.
        var source = """
            const step = 1;
            const g = function* down(n: number): Generator<number> {
              if (n > 0) { yield n; yield* down(n - step); }
            };
            console.log([...g(3)]);
            """;

        Assert.Equal("[3, 2, 1]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_NamedSelfReference_ParameterShadowsName(ExecutionMode mode)
    {
        // A parameter named the same as the function expression shadows the self-name (the self-binding
        // must NOT be injected): `foo` refers to the parameter inside the body.
        var source = """
            const a = function* foo(foo: number) { yield foo; yield foo + 1; };
            console.log([...a(10)]);
            """;

        Assert.Equal("[10, 11]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_NamedSelfReference_BodyLetShadowsName(ExecutionMode mode)
    {
        // A body-level `let` of the same name shadows the self-name (no self-binding injected).
        var source = """
            const b = function* bar() { let bar = 7; yield bar; };
            console.log([...b()]);
            """;

        Assert.Equal("[7]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorExpression_NamedSelfReference_NestedBlockDoesNotShadow(ExecutionMode mode)
    {
        // A nested-block binding of the same name shadows only within that block, so the outer
        // self-call still resolves to the function. Compiled generators now give the nested-block
        // redeclaration its own slot instead of leaking it onto the hoisted field (#711).
        var source = """
            const d = function* rec(n: number): Generator<number> {
              { const rec = 0; if (n < -100) yield rec; }
              if (n > 0) { yield n; yield* rec(n - 1); }
            };
            console.log([...d(2)]);
            """;

        Assert.Equal("[2, 1]\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Block-scope shadowing in compiled generators (#711) and void operator (#712)

    [Theory, ModeData]
    public void Generator_NestedBlockConstShadow_DoesNotLeakToOuter(ExecutionMode mode)
    {
        // A const in a nested block that shadows an outer body-level const must get its own slot
        // instead of clobbering the outer binding's hoisted state-machine field (#711).
        var source = """
            function* g(): Generator<number> {
              const r = 100;
              { const r = 0; if (false) yield r; }
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[100]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedBlockShadow_LiveAcrossYield_GetsOwnField(ExecutionMode mode)
    {
        // The inner shadow is itself read after a yield, so it must hoist to its OWN field, distinct
        // from the outer binding's field — both must survive the suspension independently (#711).
        var source = """
            function* g(): Generator<number> {
              const r = 100;
              {
                const r = 0;
                yield r;
                yield r + 1;
              }
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[0, 1, 100]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedBlockLetShadow_ReassignedAcrossYield(ExecutionMode mode)
    {
        // A let shadow that is compound-assigned across yields keeps its own value, separate from
        // the outer binding (#711).
        var source = """
            function* g(): Generator<number> {
              let r = 7;
              {
                let r = 1;
                r += 1;
                yield r;
                r += 10;
                yield r;
              }
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[2, 12, 7]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedBlockShadowsParameter(ExecutionMode mode)
    {
        // An inner block const may shadow a (hoisted) parameter without clobbering it (#711).
        var source = """
            function* g(r: number): Generator<number> {
              { const r = 99; yield r; }
              yield r;
            }
            console.log([...g(5)]);
            """;

        Assert.Equal("[99, 5]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_DeeplyNestedShadows_EachGetOwnSlot(ExecutionMode mode)
    {
        // Each nesting level that re-declares the name resolves to its own binding (#711).
        var source = """
            function* g(): Generator<number> {
              const r = 1;
              { const r = 2; { const r = 3; yield r; } yield r; }
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[3, 2, 1]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_VoidOperator_InBody(ExecutionMode mode)
    {
        // `void expr` inside a compiled generator body must evaluate the operand for its side effects
        // and yield undefined, rather than failing to compile (#712).
        var source = """
            function* g(): Generator<number> {
              const r = 5;
              void r;
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[5]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_VoidOperator_WithYieldOperand_Suspends(ExecutionMode mode)
    {
        // `void (yield x)` must still suspend at the inner yield (the operand can suspend), then
        // discard its result (#712).
        var source = """
            function* g(): Generator<number> {
              void (yield 1);
              yield 2;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[1, 2]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedBlockShadow_CapturedByArrow_DoesNotLeak(ExecutionMode mode)
    {
        // An inner block const that shadows an outer body-level const AND is read by a nested arrow
        // must get its own slot; the arrow reads the inner value while the outer binding keeps its own
        // (#767, the residual closure-capture case left open by #711). Compiled previously yielded
        // [5, 5] because the inner binding was left un-renamed to avoid desyncing the name-keyed capture.
        var source = """
            function* g(): Generator<number> {
              const r = 100;
              {
                const r = 5;
                const f = () => r;
                yield f();
              }
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[5, 100]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_OuterBindingCapturedByArrow_StaysCorrect(ExecutionMode mode)
    {
        // The arrow captures the OUTER binding (declared before the shadowing block); the inner shadow
        // is renamed but the capture must still resolve to the un-renamed outer storage (#767 soundness:
        // pivoting only fires for captures that bind to a renamed shadow).
        var source = """
            function* g(): Generator<number> {
              const r = 100;
              const cap = () => r;
              { const r = 5; yield r; }
              yield cap();
            }
            console.log([...g()]);
            """;

        Assert.Equal("[5, 100]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_TwoArrows_CaptureInnerAndOuterShadows(ExecutionMode mode)
    {
        // Two arrows in distinct scopes each capture a different binding of the same name. Each arrow's
        // captured field must be sourced from the binding it lexically refers to (#767, per-arrow pivot).
        var source = """
            function* g(): Generator<number> {
              const r = 1;
              const outer = () => r;
              {
                const r = 2;
                const inner = () => r;
                yield inner();
                yield outer();
              }
              yield r;
            }
            console.log([...g()]);
            """;

        Assert.Equal("[2, 1, 1]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedBlockShadow_WriteCapturedByArrow_DoesNotLeak(ExecutionMode mode)
    {
        // An inner block let that shadows an outer body-level const AND is WRITTEN by a nested arrow must
        // get its own shared function-DC slot; the arrow's read/write land on the inner binding while the
        // outer keeps its own (#838, the write-capture residual left open by #767/#674). Compiled
        // previously yielded 6,6,6 because the inner shadow was left un-renamed and collided with the
        // outer binding on the single name-keyed $functionDC field.
        var source = """
            function* mut(): Generator<number> {
              const r = 100;
              {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                yield f();
                yield r;
              }
              yield r;
            }
            console.log([...mut()].join(","));
            """;

        Assert.Equal("6,6,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedBlockShadow_WriteCapturedByArrow_IncrementForms(ExecutionMode mode)
    {
        // The arrow mutates the renamed write-shadow through ++ and += (distinct hand-rolled DC store
        // sites from a plain assignment); all must target the shadow's renamed field, not the outer one (#838).
        var source = """
            function* mut(): Generator<number> {
              const r = 100;
              {
                let r = 5;
                const f = () => { r++; r += 2; return r; };
                yield f();
                yield r;
              }
              yield r;
            }
            console.log([...mut()].join(","));
            """;

        Assert.Equal("8,8,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_TwoWriteShadows_CapturedByDistinctArrows(ExecutionMode mode)
    {
        // Two independent nested-block write-shadows, each written by its own arrow, must each get their
        // own renamed DC field without cross-contamination, and the outer bindings stay untouched (#838).
        var source = """
            function* g(): Generator<number> {
              const r = 100; const s = 200;
              { let r = 5; const f = () => { r = r + 1; return r; }; yield f(); yield r; }
              { let s = 9; const h = () => { s = s + 1; return s; }; yield h(); yield s; }
              yield r; yield s;
            }
            console.log([...g()].join(","));
            """;

        Assert.Equal("6,6,10,10,100,200\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NonShadowWriteCapture_StillShared(ExecutionMode mode)
    {
        // Regression guard: the classic #674 write-capture WITHOUT shadowing must keep sharing the outer
        // binding's storage (no rename, name-keyed DC) after the #838 rename-aware DC change.
        var source = """
            function* g(): Generator<number> {
              let sum = 0;
              const add = (n: number) => { sum += n; };
              add(5); yield sum;
              add(3); yield sum;
            }
            console.log([...g()].join(","));
            """;

        Assert.Equal("5,8\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Optional-chain string-method yield short-circuit parity (#709)

    [Theory, ModeData]
    public void Generator_OptionalChainStringMethod_OnNonString_ShortCircuitsBeforeYield(ExecutionMode mode)
    {
        // o?.substring(...) on a non-string object lacking the method short-circuits to undefined
        // WITHOUT evaluating the yield argument, so the generator completes on the first next() in
        // both modes (#709 — the generator manifestation of the #627 await parity).
        var source = """
            function* gen() {
              const o: any = { foo: 1 };
              const r = o?.substring((yield 1) as any, 4);
              return r;
            }
            const g = gen();
            const a = g.next();
            const b = g.next(99);
            console.log(a.value, a.done, b.value, b.done);
            """;

        Assert.Equal("undefined true undefined true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_OptionalChainStringMethod_OnString_RunsYield(ExecutionMode mode)
    {
        // On a genuine string receiver the method exists, so the yield argument DOES run and the
        // generator suspends — the short-circuit must not over-trigger (#709).
        var source = """
            function* gen() {
              const s: any = "hello";
              const r = s?.substring((yield 1) as any, 4);
              return r;
            }
            const g = gen();
            const a = g.next();
            const b = g.next(0);
            console.log(a.value, a.done, b.value, b.done);
            """;

        Assert.Equal("1 false hell true\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Re-entrant next()/return()/throw() — "already running" (ECMA-262 §27.5.3.3) — issues #515, #521

    // ECMA-262 §27.5.3.3 (GeneratorValidate): calling next/return/throw on a generator whose state
    // is `executing` throws a TypeError ("Generator is already running"). The only way to reach
    // that state from a guest call is re-entrancy — the body advancing itself. Before the fix the
    // interpreter's thread-coroutine deadlocked (#515) and the compiled state machine recursed back
    // into MoveNext and overflowed the stack (#521; return/throw silently corrupted state). Both are
    // now guarded.
    //
    // The interpreted and compiled cases are tested from separate sources rather than shared:
    //   * Interpreted tests use the `let it; it = g()` self-reference form.
    //   * Compiled tests resolve the receiver through a live object property (`h.it`), because
    //     compiled generators capture closure variables by value (#541) — a self-assigned `it` is
    //     snapshotted as `undefined`, so the `let it; it = g()` form never reaches the guard.
    //   * `instanceof TypeError` now holds for an error caught inside a compiled generator body too
    //     (#543, fixed): the compiled re-entrancy test below asserts it, as does
    //     GeneratorErrorIdentityTests for the runtime "not a function" TypeError.
    // The yield*-delegation case doesn't rely on the captured self-reference (the inner generator is
    // created after the outer is assigned), so it runs in both modes from a single source.

    [Theory, InterpretedOnlyData]
    public void Generator_ReentrantNext_ThrowsTypeErrorThenResumes(ExecutionMode mode)
    {
        // The re-entrant next() throws a catchable TypeError; once caught, the generator is still
        // suspended-able and resumes normally (the guard must not corrupt its running state).
        var source = """
            let it: any;
            function* g() {
                try { it.next(); }
                catch (e: any) { console.log(e instanceof TypeError, e.name, e.message); }
                yield 1;
            }
            it = g();
            const r = it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true TypeError Generator is already running\n1 false\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Generator_ReentrantNext_UncaughtPropagatesToResumingCaller(ExecutionMode mode)
    {
        // An uncaught re-entrant next() completes the generator abnormally and the TypeError
        // surfaces to the outer next() that resumed it — matching Node's uncaught behavior.
        var source = """
            let it: any;
            function* g() { it.next(); yield 1; }
            it = g();
            try { it.next(); }
            catch (e: any) { console.log("outer caught", e.message); }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("outer caught Generator is already running\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Generator_ReentrantReturn_ThrowsTypeErrorThenResumes(ExecutionMode mode)
    {
        var source = """
            let it: any;
            function* g() {
                try { it.return(0); }
                catch (e: any) { console.log("return ->", e.message); }
                yield 1;
            }
            it = g();
            const r = it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("return -> Generator is already running\n1 false\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Generator_ReentrantThrow_ThrowsTypeErrorThenResumes(ExecutionMode mode)
    {
        // The "already running" guard takes precedence over the injected throw(e): the caller's
        // error never reaches the body — it gets a TypeError instead.
        var source = """
            let it: any;
            function* g() {
                try { it.throw("boom"); }
                catch (e: any) { console.log("throw ->", e.message); }
                yield 1;
            }
            it = g();
            const r = it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("throw -> Generator is already running\n1 false\n", output);
    }

    [Theory, ModeData]
    public void Generator_ReentrantThroughYieldStar_ThrowsTypeError(ExecutionMode mode)
    {
        // The outer generator is still `executing` while it delegates via yield*, so an inner
        // generator that calls the outer's next() must observe "already running". This case doesn't
        // depend on the captured self-reference, so it runs in both modes (#515 interp + #521 compiled).
        var source = """
            let outer: any;
            function* inner() {
                try { outer.next(); }
                catch (e: any) { console.log("deleg ->", e.message); }
                yield 5;
            }
            function* g() { yield* inner(); }
            outer = g();
            const r = outer.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("deleg -> Generator is already running\n5 false\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void GeneratorExpression_ReentrantNext_ThrowsTypeError(ExecutionMode mode)
    {
        // A generator function *expression* that closes over a block-scoped binding stays in place and
        // runs natively (it is NOT lifted to a declaration, #678); it produces a SharpTSGenerator like
        // any other generator, so the re-entrancy guard (ECMA-262 §27.5.3.3) applies. The `let it`
        // capture forces the in-place native path.
        var source = """
            {
                let it: any;
                const g = function*() {
                    try { it.next(); }
                    catch (e: any) { console.log("expr ->", e.message); }
                    yield 7;
                };
                it = g();
                const r = it.next();
                console.log(r.value, r.done);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("expr -> Generator is already running\n7 false\n", output);
    }

    // --- Compiled variants (#521): genuine re-entrancy via a live object-property receiver,
    // so the guard is reached despite the by-value closure capture (#541). ---

    [Theory, CompiledOnlyData]
    public void Generator_ReentrantNext_Compiled_ThrowsTypeErrorThenResumes(ExecutionMode mode)
    {
        // The re-entrant next() throws a catchable TypeError; once caught, the generator is still
        // suspended-able and resumes normally (the guard must not corrupt its running state). The
        // in-body catch also satisfies `instanceof TypeError` (#543, fixed).
        var source = """
            const h: any = {};
            function* g() {
                try { h.it.next(); }
                catch (e: any) { console.log(e instanceof TypeError, e.name, e.message); }
                yield 1;
            }
            h.it = g();
            const r = h.it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true TypeError Generator is already running\n1 false\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Generator_ReentrantNext_Compiled_UncaughtPropagatesToResumingCaller(ExecutionMode mode)
    {
        // An uncaught re-entrant next() completes the generator abnormally and the TypeError
        // surfaces to the outer next() that resumed it. Caught outside the body, so instanceof works.
        var source = """
            const h: any = {};
            function* g() { h.it.next(); yield 1; }
            h.it = g();
            try { h.it.next(); }
            catch (e: any) { console.log(e instanceof TypeError, e.name, e.message); }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true TypeError Generator is already running\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Generator_ReentrantReturn_Compiled_ThrowsTypeErrorThenResumes(ExecutionMode mode)
    {
        var source = """
            const h: any = {};
            function* g() {
                try { h.it.return(0); }
                catch (e: any) { console.log("return ->", e.message); }
                yield 1;
            }
            h.it = g();
            const r = h.it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("return -> Generator is already running\n1 false\n", output);
    }

    [Theory, CompiledOnlyData]
    public void Generator_ReentrantThrow_Compiled_ThrowsTypeErrorThenResumes(ExecutionMode mode)
    {
        // The "already running" guard takes precedence over the injected throw(e): the caller's
        // error never reaches the body — it gets a TypeError instead.
        var source = """
            const h: any = {};
            function* g() {
                try { h.it.throw("boom"); }
                catch (e: any) { console.log("throw ->", e.message); }
                yield 1;
            }
            h.it = g();
            const r = h.it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("throw -> Generator is already running\n1 false\n", output);
    }

    [Theory, CompiledOnlyData]
    public void GeneratorExpression_ReentrantNext_Compiled_ThrowsTypeError(ExecutionMode mode)
    {
        // A generator function *expression* uses the same state-machine builder; its guard is
        // exercised here. `var` (not `const`) sidesteps an unrelated scoping bug for generator
        // function expressions that close over block-scoped variables.
        var source = """
            var h: any = {};
            var g = function*() {
                try { h.it.next(); }
                catch (e: any) { console.log("expr ->", e.message); }
                yield 7;
            };
            h.it = g();
            var r = h.it.next();
            console.log(r.value, r.done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("expr -> Generator is already running\n7 false\n", output);
    }

    #endregion
}
