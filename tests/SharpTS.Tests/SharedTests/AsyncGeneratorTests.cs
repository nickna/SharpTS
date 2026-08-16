using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for async generators (async function*) and async iteration (for await...of).
/// Runs against both interpreter and compiler.
/// Note: Tests use single-arg console.log or string concatenation to avoid
/// a multi-arg console.log limitation in compiled async functions.
/// </summary>
public class AsyncGeneratorTests
{
    #region Basic Async Generator Tests

    [Theory, ModeData]
    public void AsyncGenerator_BasicYield_ReturnsValues(ExecutionMode mode)
    {
        var source = """
            async function* asyncCounter() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                const gen = asyncCounter();
                let result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        // After the last yield, next() reports { value: undefined, done: true } — not null (#481/#540).
        Assert.Equal("1 false\n2 false\n3 false\nundefined true\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ObjectMethod_ReadsThis(ExecutionMode mode)
    {
        // #775/#923: an object async-generator method reads instance state via `this`. The value-call
        // `o.gen()` inside an async function must thread the receiver into the state machine in both
        // modes — #923 fixed the compiled gap (the call site dropped the receiver, so `<>4__this`
        // captured the sloppy global instead of `o`).
        var source = """
            const o = { v: 7, async *gen() { yield this.v; yield this.v + 1; } };
            async function main() {
                const it = o.gen();
                let r = await it.next();
                console.log(r.value);
                r = await it.next();
                console.log(r.value);
            }
            main();
            """;

        Assert.Equal("7\n8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForAwaitOf_AsyncGeneratorObjectMethod_ReadsThis(ExecutionMode mode)
    {
        // #923: inline `for await (... of o.gen())` over an async-generator object method must thread
        // `this` into the state machine. The for-await iterable is evaluated via the same call-site
        // path as the bare value-call, so this exercises the second reported case (compiled mode
        // previously hit null/NRE on `this.v`).
        var source = """
            const o = { v: 42, async *gen() { yield this.v; yield this.v + 1; } };
            async function run() {
                for await (const x of o.gen()) {
                    console.log(x);
                }
            }
            run();
            """;

        Assert.Equal("42\n43\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorFunctionExpression_AsObjectProperty_ReadsThis(ExecutionMode mode)
    {
        // #923: the other `HasOwnThis` lift shape — an `async function*` EXPRESSION assigned to an
        // object property — must also bind `this` to the receiver when called as `o.gen()`.
        var source = """
            const o = { v: 5, gen: async function* () { yield this.v; yield this.v * 2; } };
            async function main() {
                const it = o.gen();
                let r = await it.next();
                console.log(r.value);
                r = await it.next();
                console.log(r.value);
            }
            main();
            """;

        Assert.Equal("5\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGeneratorFunctionExpression_DotCallBindsThis(ExecutionMode mode)
    {
        // #775: an `async function*` expression bound via `.call` reads `this`.
        var source = """
            async function* g() { yield this.x; yield this.x + 1; }
            async function main() {
                const it = (g as any).call({ x: 9 });
                let r = await it.next();
                console.log(r.value);
                r = await it.next();
                console.log(r.value);
            }
            main();
            """;

        Assert.Equal("9\n10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_EmptyGenerator_ReturnsDoneImmediately(ExecutionMode mode)
    {
        var source = """
            async function* empty() {}

            async function main() {
                const gen = empty();
                const result = await gen.next();
                console.log(result.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_WithAwait_AwaitsBeforeYield(ExecutionMode mode)
    {
        var source = """
            async function delay(value: number): Promise<number> {
                return value * 10;
            }

            async function* asyncGen() {
                const a = await delay(1);
                yield a;
                const b = await delay(2);
                yield b;
                const c = await delay(3);
                yield c;
            }

            async function main() {
                for await (const val of asyncGen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_WithParameters_UsesParameters(ExecutionMode mode)
    {
        var source = """
            async function* range(start: number, end: number) {
                for (let i = start; i <= end; i++) {
                    yield i;
                }
            }

            async function main() {
                for await (const n of range(5, 8)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n6\n7\n8\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_SingleYield_Works(ExecutionMode mode)
    {
        var source = """
            async function* single() {
                yield 42;
            }

            async function main() {
                const gen = single();
                const r1 = await gen.next();
                console.log(r1.value + " " + r1.done);
                const r2 = await gen.next();
                console.log(r2.value + " " + r2.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        // The result after the single yield is { value: undefined, done: true } — not null (#481/#540).
        Assert.Equal("42 false\nundefined true\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldInLoop_Works(ExecutionMode mode)
    {
        var source = """
            async function* countdown(start: number) {
                while (start > 0) {
                    yield start;
                    start = start - 1;
                }
            }

            async function main() {
                for await (const n of countdown(3)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n1\n", output);
    }

    #endregion

    #region for await...of Tests

    [Theory, ModeData]
    public void ForAwaitOf_AsyncGenerator_IteratesValues(ExecutionMode mode)
    {
        var source = """
            async function* asyncNumbers() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                for await (const num of asyncNumbers()) {
                    console.log(num);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitOf_AsyncGeneratorWithAwait_IteratesAwaitedValues(ExecutionMode mode)
    {
        var source = """
            async function delay(value: number): Promise<number> {
                return value * 2;
            }

            async function* asyncGen() {
                yield await delay(5);
                yield await delay(10);
                yield await delay(15);
            }

            async function main() {
                for await (const val of asyncGen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitOf_WithBreak_StopsIteration(ExecutionMode mode)
    {
        var source = """
            async function* numbers() {
                yield 0;
                yield 1;
                yield 2;
                yield 3;
                yield 4;
                yield 5;
            }

            async function main() {
                for await (const num of numbers()) {
                    console.log(num);
                    if (num >= 3) break;
                }
                console.log("done");
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n3\ndone\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitOf_WithContinue_SkipsIteration(ExecutionMode mode)
    {
        var source = """
            async function* numbers() {
                yield 1;
                yield 2;
                yield 3;
                yield 4;
                yield 5;
            }

            async function main() {
                for await (const num of numbers()) {
                    if (num % 2 === 0) continue;
                    console.log(num);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n5\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitOf_EmptyAsyncGenerator_NoIterations(ExecutionMode mode)
    {
        var source = """
            async function* empty() {}

            async function main() {
                let count = 0;
                for await (const _ of empty()) {
                    count++;
                }
                console.log("iterations: " + count);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("iterations: 0\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitOf_MultipleLoops_Works(ExecutionMode mode)
    {
        var source = """
            async function* gen() {
                yield 1;
                yield 2;
            }

            async function main() {
                for await (const x of gen()) {
                    console.log("first: " + x);
                }
                for await (const y of gen()) {
                    console.log("second: " + y);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first: 1\nfirst: 2\nsecond: 1\nsecond: 2\n", output);
    }

    // #672: a top-level `for await...of` drives the async-iterator protocol, which requires an async
    // context. SharpTS does not support top-level await, so — like a top-level `await` expression —
    // it must be rejected by the type checker rather than silently degrading to a synchronous
    // `for...of` (which then throws a misleading 'not iterable' runtime error in both modes).
    [Theory, ModeData]
    public void ForAwaitOf_TopLevel_RejectedByTypeChecker(ExecutionMode mode)
    {
        var source = """
            async function* g() { yield 1; yield 2; }
            for await (const x of g()) console.log("top", x);
            """;

        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("'await' is only valid inside an async function.", ex.Message);
    }

    // #672: `for await...of` inside a non-async function is likewise an await outside an async
    // context and must be rejected (TS conformance), not run as a synchronous `for...of`.
    [Theory, ModeData]
    public void ForAwaitOf_InNonAsyncFunction_RejectedByTypeChecker(ExecutionMode mode)
    {
        var source = """
            async function* g() { yield 1; yield 2; }
            function notAsync() {
                for await (const x of g()) console.log(x);
            }
            notAsync();
            """;

        var ex = Assert.Throws<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("'await' is only valid inside an async function.", ex.Message);
    }

    #endregion

    #region Async Generator .return() and .throw() Tests

    [Theory, ModeData]
    public void AsyncGenerator_Return_ClosesGenerator(ExecutionMode mode)
    {
        var source = """
            async function* asyncGen() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                const gen = asyncGen();
                const r1 = await gen.next();
                console.log(r1.value);
                const returnResult = await gen.return(42);
                console.log(returnResult.value + " " + returnResult.done);
                const nextResult = await gen.next();
                console.log(nextResult.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n42 true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_Throw_ThrowsError(ExecutionMode mode)
    {
        var source = """
            async function* asyncGen() {
                yield 1;
                yield 2;
            }

            async function main() {
                const gen = asyncGen();
                const r1 = await gen.next();
                console.log(r1.value);
                try {
                    await gen.throw("Test error");
                } catch (e) {
                    console.log("Caught: " + e);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nCaught: Test error\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ReturnWithoutValue_ReturnsUndefined(ExecutionMode mode)
    {
        // ECMA-262 §27.6.1.3: AsyncGenerator.prototype.return(value) with value absent resolves to
        // { value: undefined, done: true } — an omitted argument is undefined, not null (#618).
        var source = """
            async function* gen() {
                yield 1;
                yield 2;
            }

            async function main() {
                const g = gen();
                await g.next();
                const r = await g.return();
                console.log(r.value + " " + r.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined true\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ThrowWithoutValue_RejectsWithUndefined(ExecutionMode mode)
    {
        // throw() with no argument rejects with undefined, not null (#618).
        var source = """
            async function* gen() { yield 1; yield 2; }
            async function main() {
                const g = gen();
                await g.next();
                try { await g.throw(); } catch (e) { console.log("rejected " + e); }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("rejected undefined\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_BareReturn_CompletesWithUndefined(ExecutionMode mode)
    {
        // A value-less `return;` completes with undefined, not null; `return null;` still reports null (#540).
        var source = """
            async function* bare() { yield 1; return; }
            async function* retNull() { yield 1; return null; }
            async function main() {
                const a = bare();
                await a.next();
                console.log("bare " + (await a.next()).value);
                const b = retNull();
                await b.next();
                console.log("retNull " + (await b.next()).value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bare undefined\nretNull null\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ManualNextOnThrowingGenerator_RejectsCatchably(ExecutionMode mode)
    {
        // A throwing async generator settles its final next() as a rejection (catchable via await),
        // delivering the preceding yields first, rather than propagating unhandled (#566).
        var source = """
            async function* g() { yield 1; throw "b"; }
            async function main() {
                const it = g();
                const r1 = await it.next();
                let err = "none";
                try { await it.next(); } catch (e) { err = "" + e; }
                console.log(r1.value + " err=" + err);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 err=b\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_RejectedAwaitInTry_CaughtByOwnCatch(ExecutionMode mode)
    {
        // A rejected await inside a try in an async generator is caught by that try's catch, which
        // binds the rejection reason; the generator then continues to its next yield (#617).
        var source = """
            async function* g() {
                try { await Promise.reject("boom"); } catch (e: any) { console.log("caught " + e); }
                yield 1;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught boom\nv1\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ThrowInTryAfterYield_CatchBindsValue(ExecutionMode mode)
    {
        // A throw in a flag-based try (one containing a yield) is caught and the catch parameter binds
        // the thrown value, not null — the catch param is stored to its hoisted field (#617/#477 analog).
        // The caught value is *yielded* (not logged) so the assertion is independent of the interpreter's
        // eager-drain side-effect ordering (#564): yielded values keep their order in both modes.
        var source = """
            async function* g() {
                try { yield 0; throw "x"; } catch (e: any) { yield "caught:" + e; }
                yield 1;
            }
            async function main() { for await (const v of g()) console.log(v); }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\ncaught:x\n1\n", output);
    }

    #endregion

    #region yield* Delegation Tests

    [Theory, ModeData]
    public void AsyncGenerator_YieldStar_DelegatesToSyncIterable(ExecutionMode mode)
    {
        var source = """
            async function* asyncGen() {
                yield* [1, 2, 3];
            }

            async function main() {
                for await (const val of asyncGen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldStar_DelegatesToAsyncGenerator(ExecutionMode mode)
    {
        var source = """
            async function* inner() {
                yield "a";
                yield "b";
            }

            async function* outer() {
                yield "start";
                yield* inner();
                yield "end";
            }

            async function main() {
                for await (const val of outer()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("start\na\nb\nend\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldStar_EmptyIterable(ExecutionMode mode)
    {
        var source = """
            async function* gen() {
                yield 1;
                yield* [];
                yield 2;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldStar_NestedDelegation(ExecutionMode mode)
    {
        var source = """
            async function* level1() {
                yield 1;
            }

            async function* level2() {
                yield* level1();
                yield 2;
            }

            async function* level3() {
                yield* level2();
                yield 3;
            }

            async function main() {
                for await (const val of level3()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Return Value Tests

    [Theory, ModeData]
    public void AsyncGenerator_ReturnsValue_IncludedInFinalResult(ExecutionMode mode)
    {
        var source = """
            async function* genWithReturn() {
                yield 1;
                yield 2;
                return "final";
            }

            async function main() {
                const gen = genWithReturn();
                let result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 false\n2 false\nfinal true\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ImplicitReturn_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            async function* gen() {
                yield 1;
            }

            async function main() {
                const g = gen();
                await g.next();
                const r = await g.next();
                console.log(r.value + " " + r.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        // A generator that runs off the end completes with `undefined`, not null (#481/#540).
        Assert.Equal("undefined true\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_EarlyReturn_SkipsRemainingYields(ExecutionMode mode)
    {
        var source = """
            async function* gen(earlyReturn: boolean) {
                yield 1;
                if (earlyReturn) return "early";
                yield 2;
                return "normal";
            }

            async function main() {
                const g = gen(true);
                let r = await g.next();
                console.log(r.value + " " + r.done);
                r = await g.next();
                console.log(r.value + " " + r.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 false\nearly true\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void AsyncGenerator_InNamespace_Works(ExecutionMode mode)
    {
        var source = """
            namespace Utils {
                export async function* counter(max: number) {
                    for (let i = 1; i <= max; i++) {
                        yield i;
                    }
                }
            }

            async function main() {
                for await (const n of Utils.counter(3)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_WithStringYields_Works(ExecutionMode mode)
    {
        var source = """
            async function* greetings() {
                yield "hello";
                yield "world";
            }

            async function main() {
                for await (const s of greetings()) {
                    console.log(s);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nworld\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldingObjects_Works(ExecutionMode mode)
    {
        var source = """
            async function* objects() {
                yield { name: "Alice", age: 30 };
                yield { name: "Bob", age: 25 };
            }

            async function main() {
                for await (const obj of objects()) {
                    console.log(obj.name + " " + obj.age);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice 30\nBob 25\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldingArrays_Works(ExecutionMode mode)
    {
        var source = """
            async function* arrays() {
                yield [1, 2, 3];
                yield [4, 5, 6];
            }

            async function main() {
                for await (const arr of arrays()) {
                    console.log(arr.join(","));
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n4,5,6\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_ConditionalYield_Works(ExecutionMode mode)
    {
        var source = """
            async function* conditional(includeMiddle: boolean) {
                yield 1;
                if (includeMiddle) {
                    yield 2;
                }
                yield 3;
            }

            async function main() {
                console.log("With middle:");
                for await (const n of conditional(true)) {
                    console.log(n);
                }
                console.log("Without middle:");
                for await (const n of conditional(false)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("With middle:\n1\n2\n3\nWithout middle:\n1\n3\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldNull_Works(ExecutionMode mode)
    {
        var source = """
            async function* gen() {
                yield null;
                yield 1;
                yield null;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n1\nnull\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldBoolean_Works(ExecutionMode mode)
    {
        var source = """
            async function* gen() {
                yield true;
                yield false;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_AwaitInYieldExpression_Works(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }

            async function* gen() {
                yield await getValue();
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region yield await Regression Tests

    [Theory, ModeData]
    public void AsyncGenerator_MultipleAwaitsBetweenYields_Works(ExecutionMode mode)
    {
        var source = """
            async function add(a: number, b: number): Promise<number> {
                return a + b;
            }

            async function* gen() {
                const x = await add(1, 2);
                const y = await add(x, 3);
                yield y;
                const z = await add(y, 4);
                yield z;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n10\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_MultipleYieldAwait_InSequence(ExecutionMode mode)
    {
        var source = """
            async function getValue(n: number): Promise<number> {
                return n * 10;
            }

            async function* gen() {
                yield await getValue(1);
                yield await getValue(2);
                yield await getValue(3);
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_MixedYieldAndYieldAwait(ExecutionMode mode)
    {
        var source = """
            async function getValue(n: number): Promise<number> {
                return n * 10;
            }

            async function* gen() {
                yield 1;
                yield await getValue(2);
                yield 3;
                yield await getValue(4);
                yield 5;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n20\n3\n40\n5\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldAwaitThenStandaloneAwait(ExecutionMode mode)
    {
        var source = """
            async function getValue(n: number): Promise<number> {
                return n * 10;
            }

            async function* gen() {
                yield await getValue(1);
                const x = await getValue(2);
                yield x;
                yield await getValue(3);
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldAwaitWithComputation(ExecutionMode mode)
    {
        var source = """
            async function getValue(n: number): Promise<number> {
                return n;
            }

            async function* gen() {
                yield await getValue(5) + 1;
                yield await getValue(10) * 2;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n20\n", output);
    }

    #endregion

    #region Completion / resume value semantics — #481, #540

    [Theory, ModeData]
    public void AsyncGenerator_ResumedYield_EvaluatesToUndefined(ExecutionMode mode)
    {
        // The resumed `yield` expression evaluates to undefined (no value sent), not null (#481, the
        // async analog of #443). The interpreter's lazy async-generator coroutine binds `const r = yield 1`
        // (an earlier eager-drain model threw "Undefined variable 'r'" — fixed by the coroutine rewrite).
        var source = """
            async function* ag() { const r = yield 1; console.log("r=" + r); }
            async function main() { for await (const v of ag()) {} }
            main();
            """;

        Assert.Equal("r=undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldStarCompletion_EvaluatesToUndefined(ExecutionMode mode)
    {
        // The completion value of `yield* inner()` (when the delegate has no explicit return value) is
        // undefined, not null (#481). The interpreter binds `const x = yield* …` via its lazy coroutine.
        var source = """
            async function* inner() { yield 2; yield 3; }
            async function* g() { const x = yield* inner(); console.log("x=" + x); yield 4; }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v2\nv3\nx=undefined\nv4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NextAfterDone_ReportsUndefinedNotStaleReturn(ExecutionMode mode)
    {
        // The return value is delivered exactly once; every later next() reports undefined, not the
        // stale completion value replayed forever (#540, async analog of #499/#480).
        var source = """
            async function* ag() { yield 1; return 42; }
            async function main() {
                const it = ag();
                console.log((await it.next()).value);
                console.log((await it.next()).value);
                console.log((await it.next()).value);
                console.log((await it.next()).value);
            }
            main();
            """;

        Assert.Equal("1\n42\nundefined\nundefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NextAfterReturn_ReportsUndefinedNotLastYielded(ExecutionMode mode)
    {
        // After return(v) delivers { value: v, done: true }, a later next() reports undefined — the
        // last *yielded* value must not replay (#540).
        var source = """
            async function* ag() { yield 1; yield 2; }
            async function main() {
                const it = ag();
                console.log((await it.next()).value);
                console.log(JSON.stringify(await it.return(99)));
                console.log((await it.next()).value);
            }
            main();
            """;

        Assert.Equal("1\n{\"value\":99,\"done\":true}\nundefined\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Re-entrant next() — "already running" guard (#542)

    [Theory, ModeData]
    public void AsyncGenerator_ReentrantNext_RejectsInsteadOfStackOverflow(ExecutionMode mode)
    {
        // An async generator whose body advances itself (a re-entrant next()) is rejected with a
        // catchable TypeError rather than crashing. Compiled mode previously recursed into MoveNextAsync
        // until the stack overflowed; ECMA-262 §27.6.3 queues such a request, but under both modes' drive
        // the queued request could only be served by the body that blocks on it, so the guard rejects
        // (#542). The interpreter's lazy coroutine guards the body's synchronous segment the same way
        // (an earlier eager-drain model returned a non-conformant done result instead). Observed via a
        // `for await…of` consumer, whose next()-drive surfaces the rejection to the enclosing try/catch.
        var source = """
            const h: any = {};
            async function* g() { await h.it.next(); yield 1; }
            h.it = g();
            async function main() {
              try {
                for await (const v of h.it) { console.log("v" + v); }
              } catch (e: any) {
                console.log("caught: " + e.name + " " + e.message);
              }
            }
            main();
            """;

        Assert.Equal("caught: TypeError Async generator is already running\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void AsyncGenerator_ReentrantNext_AfterPendingAwait_Rejects(ExecutionMode mode)
    {
        // Re-entrancy issued *after* a genuinely-pending (setTimeout-backed) await — once the body has
        // suspended and resumed, the guard used to be back to false and the self-advancing next() was
        // silently enqueued behind the body that awaits it, stalling the chain with no output. The
        // running guard now tracks "body synchronously on the stack" across await/yield resume spans
        // (toggled at the AwaitPreservingEnvironment chokepoint), so this rejects with the same
        // catchable TypeError as the synchronous-prefix form above (#771). Interpreter-only: compiled
        // mode still stalls this pathological self-referential pattern (as does Node), recorded as a
        // follow-up tail of #542.
        var source = """
            const h: any = {};
            function later(): Promise<number> { return new Promise(res => setTimeout(() => res(0), 5)); }
            async function* g() { await later(); await h.it.next(); yield 1; }
            h.it = g();
            async function main() {
              try {
                for await (const v of h.it) { console.log("v" + v); }
              } catch (e: any) {
                console.log("caught: " + e.name + " " + e.message);
              }
            }
            main();
            """;

        Assert.Equal("caught: TypeError Async generator is already running\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Genuinely-async awaits and request queuing (#631 / #542)

    [Theory, ModeData]
    public void AsyncGenerator_PendingAwait_ForAwaitOf_YieldsAllValues(ExecutionMode mode)
    {
        // A not-yet-settled (setTimeout-backed) await inside an async generator consumed by for await…of
        // previously hung the compiled program: next() drove MoveNextAsync synchronously via GetResult,
        // blocking the event-loop thread the continuation needed. next() is now truly asynchronous (#631).
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* g() { yield await later(1); yield await later(2); }
            async function main() {
                for await (const v of g()) console.log("v" + v);
            }
            main();
            """;

        Assert.Equal("v1\nv2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_PendingAwait_DirectNext_ResolvesInOrder(ExecutionMode mode)
    {
        // Driving a pending-await async generator by awaiting next() directly. #631 (compiled). Also
        // covered in the interpreter: a second `await it.next()` previously read `it` against a scope an
        // eager-drain model had corrupted ("Only instances and objects have properties"). The interpreter's
        // lazy async-generator coroutine runs the body on demand and preserves the caller's environment
        // across each suspension (#690), so sequential next() calls resolve in order.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* g() { yield await later(7); yield await later(8); }
            async function main() {
                const it = g();
                const a = await it.next();
                const b = await it.next();
                const c = await it.next();
                console.log(a.value + " " + a.done + " " + b.value + " " + b.done + " " + c.value + " " + c.done);
            }
            main();
            """;

        Assert.Equal("7 false 8 false undefined true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_PendingRejection_InTry_ReachesCatch(ExecutionMode mode)
    {
        // A pending (genuinely-async) rejected await must reach the consumer's catch through for await…of.
        // The emitted AsyncGeneratorAwaitContinue no longer short-circuits on the faulted task; it resumes
        // the body so its own resume point re-throws into place (#631, unblocks the pending sub-case of #617).
        var source = """
            function fail(): Promise<number> {
                return new Promise((_res, rej) => setTimeout(() => rej("boom"), 5));
            }
            async function* g() { yield await fail(); }
            async function main() {
                try { for await (const v of g()) console.log("v" + v); }
                catch (e) { console.log("caught " + e); }
            }
            main();
            """;

        Assert.Equal("caught boom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ConcurrentNext_QueuesInOrder(ExecutionMode mode)
    {
        // Two next() calls issued before the first settles must be serviced FIFO (ECMA-262 §27.6.3
        // AsyncGeneratorQueue): compiled mode models it as a task chain (#542); the interpreter's lazy
        // async-generator coroutine enqueues the second request and the body services it after the first
        // yield, so it can no longer race ahead and read a half-populated state as completion (#690).
        // Previously the interpreter threw "Only instances and objects have properties" here (the env-leak
        // symptom of an eager-drain model) and could not service concurrent next() at all.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* g() { yield await later(1); yield await later(2); }
            async function main() {
                const it: any = g();
                const [a, b] = await Promise.all([it.next(), it.next()]);
                console.log(a.value + " " + a.done + " " + b.value + " " + b.done);
            }
            main();
            """;

        Assert.Equal("1 false 2 false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ForAwaitBody_WritesOuterBinding(ExecutionMode mode)
    {
        // #689: a `let`/`const` declared before a `for await…of` over a (genuinely-async) async generator
        // must remain visible inside the loop body. An eager-drain model repointed the shared environment
        // at the generator's closure and held it across the body's awaits, so the loop body resolved `out`
        // against the wrong scope and threw "Undefined variable 'out'". The interpreter's lazy coroutine
        // preserves the caller's environment across each suspension, so the body reaches the enclosing
        // scope in both modes.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* g() { yield await later(1); yield await later(2); }
            async function main() {
                let out = "";
                for await (const v of g()) out += v;
                console.log(out);
            }
            main();
            """;

        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_DirectNext_PreservesCallerScope(ExecutionMode mode)
    {
        // #690 (env-leak symptom): after the first `await it.next()` drives the generator, bindings
        // declared before it (here `it` itself and the outer `tag`) must still resolve in the caller's
        // scope. Previously a second `it.next()` read `it` against a leaked generator closure and threw
        // "Only instances and objects have properties", and an outer local read back as undefined. The
        // interpreter's lazy coroutine preserves the caller's environment across each await suspension.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* g() { yield await later(1); yield await later(2); }
            async function main() {
                let tag = "T";
                const it = g();
                const a = await it.next();
                const b = await it.next();
                console.log(tag + " " + a.value + " " + b.value);
            }
            main();
            """;

        Assert.Equal("T 1 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ForAwaitBody_OuterBinding_SurvivesNestedAwaitBody(ExecutionMode mode)
    {
        // #689 hardening: a generator body whose await is nested inside a delegated expression
        // (`yield dbl(await later(n))`) suspends through the interpreter's general async-expression path.
        // The interpreter's lazy coroutine evaluates the yielded expression with the ambient environment
        // preserved across that await (like any async function), so the loop body still reaches the outer
        // `out` binding regardless of the body shape.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            function dbl(n: number): number { return n * 2; }
            async function* g() { yield dbl(await later(1)); yield dbl(await later(2)); }
            async function main() {
                let out = "";
                for await (const v of g()) out += v;
                console.log(out);
            }
            main();
            """;

        Assert.Equal("24\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_DirectNext_OuterBinding_SurvivesNestedAwaitBody(ExecutionMode mode)
    {
        // #752: the direct `await it.next()` form of the nested-await body shape
        // (`yield dbl(await later(n))`). An eager-drain model leaked the generator's closure into the
        // shared environment for this shape, so the outer `tag` read back as undefined ("T 2" became
        // "undefined 2"). The interpreter's lazy coroutine preserves the environment across the nested
        // await like any async function, so the caller binding survives even when driven by direct next().
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            function dbl(n: number): number { return n * 2; }
            async function* g() { yield dbl(await later(1)); yield dbl(await later(2)); }
            async function main() {
                let tag = "T";
                const it = g();
                const a = await it.next();
                console.log(tag + " " + a.value);
            }
            main();
            """;

        Assert.Equal("T 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ForAwaitOf_OverPendingAsyncGenerator_SuspendsNotBlocks(ExecutionMode mode)
    {
        // A `for await…of` INSIDE an async generator, consuming a genuinely-async (setTimeout-backed)
        // source, must suspend on the inner iterator's next() instead of blocking — the compiled sibling
        // deadlocked/crashed this stream-transform shape (#697, the async-generator sibling of #631). The
        // interpreter now drives it natively: its lazy async-generator body runs the for-await through the
        // real async-iterator protocol, fixing the prior "Cannot iterate over non-iterable value" (#717).
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* src() { yield await later(1); yield await later(2); }
            async function* transform() { for await (const x of src()) yield x * 10; }
            async function main() {
                for await (const v of transform()) console.log("v" + v);
            }
            main();
            """;

        Assert.Equal("v10\nv20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ForAwaitOf_BreakAwaitsReturn(ExecutionMode mode)
    {
        // Breaking out of a `for await…of` inside an async generator must await the inner iterator's
        // return() (the suspension-based cleanup path) and stop consuming — here only the first
        // transformed value is produced before the break (#697). The interpreter now drives this natively
        // (#717), closing the inner iterator on the break via AsyncIteratorClose.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* src() { yield await later(1); yield await later(2); yield await later(3); }
            async function* transform() {
                for await (const x of src()) { if (x === 2) break; yield x * 10; }
            }
            async function main() {
                for await (const v of transform()) console.log("v" + v);
                console.log("end");
            }
            main();
            """;

        Assert.Equal("v10\nend\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ForAwaitOf_InsideTryFinally_RunsFinally(ExecutionMode mode)
    {
        // A `for await…of` inside a try in an async generator suspends; the finally still runs after the
        // loop drains. Compiled mode must take its flag-based try path (resume labels would be illegal
        // BranchIntoTry targets on the real-IL try path — the async-gen analog of the #631 ContainsAwait
        // pitfall). The interpreter now drives the same shape natively via its lazy coroutine (#717).
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* src() { yield await later(1); yield await later(2); }
            async function* transform() {
                try { for await (const x of src()) yield x * 10; }
                finally { console.log("finally"); }
            }
            async function main() {
                for await (const v of transform()) console.log("v" + v);
            }
            main();
            """;

        Assert.Equal("v10\nv20\nfinally\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_YieldStar_DelegatesToPendingAsyncGenerator(ExecutionMode mode)
    {
        // yield* delegating to a genuinely-async (setTimeout-backed) async generator must drive the
        // delegate via its next() and suspend, not block on a synchronous MoveNextAsync GetResult — which
        // produced no output at all in compiled mode (#688, sibling of #631). The delegate's
        // `p + (await …)` body also exercises the live-spill-across-await persistence the async-generator
        // emitter previously lacked (the #400 analog), so the parameter prefix survives the suspension.
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* inner(p: string) { yield p + (await later(1)); yield p + (await later(2)); }
            async function* outer() { yield* inner("a"); }
            async function main() {
                for await (const v of outer()) console.log(v);
                console.log("done");
            }
            main();
            """;

        Assert.Equal("a1\na2\ndone\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_ValueLiveAcrossAwait_IsPreserved(ExecutionMode mode)
    {
        // A value live on the IL evaluation stack across an await inside an async generator — here the
        // parameter `p`, the left operand of `p + (await …)` — must be spilled to a state-machine field
        // and restored on resume (the async-generator analog of #400). The async-gen emitter previously
        // never enabled persistent spills, so `p` was wiped by the MoveNextAsync re-entry and the result
        // was "1"/"2" instead of "a1"/"a2".
        var source = """
            function later(n: number): Promise<number> {
                return new Promise(res => setTimeout(() => res(n), 5));
            }
            async function* g(p: string) { yield p + (await later(1)); yield p + (await later(2)); }
            async function main() {
                for await (const v of g("a")) console.log(v);
            }
            main();
            """;

        Assert.Equal("a1\na2\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Sent Value Tests (next(v) — issue #473)

    [Theory, ModeData]
    public void AsyncGenerator_NextWithSentValue_PlainYield(ExecutionMode mode)
    {
        // The resumed `yield` expression evaluates to the value passed to next(v) (ECMA-262 §27.6.3.6).
        var source = """
            async function* gen() {
                const r = yield 1;
                yield r + 10;
            }
            async function main() {
                const g = gen();
                const first = await g.next();
                console.log(first.value);
                const second = await g.next(42);
                console.log(second.value);
            }
            main();
            """;

        Assert.Equal("1\n52\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_FirstNextIgnoresSentValue(ExecutionMode mode)
    {
        // The first next() call always ignores its sent value — yield evaluates to undefined
        // (ECMA-262 §27.6.3.6 step 1: "If value is not present, let value be undefined").
        var source = """
            async function* gen() {
                const r = yield 1;
                yield String(r);
            }
            async function main() {
                const g = gen();
                await g.next("ignored");
                const second = await g.next("hello");
                console.log(second.value);
            }
            main();
            """;

        Assert.Equal("hello\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NextWithSentValue_Accumulator(ExecutionMode mode)
    {
        // Sum accumulates sent values across multiple next(v) calls.
        var source = """
            async function* accumulator() {
                let sum = 0;
                while (true) {
                    const n = yield sum;
                    sum = sum + (n as number);
                }
            }
            async function main() {
                const g = accumulator();
                await g.next();
                const a = await g.next(10);
                console.log(a.value);
                const b = await g.next(20);
                console.log(b.value);
                const c = await g.next(5);
                console.log(c.value);
            }
            main();
            """;

        Assert.Equal("10\n30\n35\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NextWithSentValue_NullAndUndefined(ExecutionMode mode)
    {
        // next(null) delivers null; bare next() delivers undefined (not null).
        var source = """
            async function* gen() {
                const a = yield 1;
                const b = yield 2;
                yield String(a) + " " + String(b);
            }
            async function main() {
                const g = gen();
                await g.next();
                await g.next(null);
                const r = await g.next();
                console.log(r.value);
            }
            main();
            """;

        Assert.Equal("null undefined\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region #924: generator expression capturing an enclosing async function's local

    // #924 (the async analog of #534): an async-generator function EXPRESSION that closes over an
    // enclosing ASYNC function's local was an unbound forward reference in BOTH modes. The
    // GeneratorArrowLifter rewrites it to `const g = __genArrow_0;` and APPENDS the lifted
    // `async function* __genArrow_0(){…}` at the body END (so the source-order type checker sees `let y`
    // first, #522); the reference to `g` then precedes the lifted declaration. The interpreter now
    // hoists function declarations in async bodies (Interpreter.Async.ExecuteBlockAsync), and the
    // compiler now hoists the function-scope forwarding binding for a PLAIN/ASYNC (non-generator)
    // encloser — where the forwarding arrow reads its captures live at call time.
    [Theory, ModeData]
    public void AsyncGeneratorExpression_CapturesAsyncFunctionLocal_IsCallable(ExecutionMode mode)
    {
        var source = """
            async function outer(): Promise<number[]> {
                let y = 5;
                const g = async function* () { yield y; yield y + 1; };
                const out: number[] = [];
                for await (const v of g()) out.push(v);
                return out;
            }
            outer().then((r: number[]) => console.log(r.join(",")));
            """;
        Assert.Equal("5,6\n", TestHarness.Run(source, mode));
    }

    // The same forward-reference gap applied to a SYNC generator expression nested in an async function.
    [Theory, ModeData]
    public void SyncGeneratorExpression_CapturesAsyncFunctionLocal_IsCallable(ExecutionMode mode)
    {
        var source = """
            async function outer(): Promise<number[]> {
                let y = 5;
                const g = function* () { yield y; yield y + 1; };
                return [...g()];
            }
            outer().then((r: number[]) => console.log(r.join(",")));
            """;
        Assert.Equal("5,6\n", TestHarness.Run(source, mode));
    }

    // #945 (generator-encloser analog of #924): an async-generator EXPRESSION whose enclosing function is
    // itself a top-level/module-level `async function*` now runs in BOTH modes. The lifted forwarding
    // binding is hoisted to the generator body top, and its read-only forwarded capture (`y`) is routed
    // through the generator's #674 function display class so the hoisted forwarder reads it LIVE — not a
    // stale by-value snapshot. (Previously interpreter-only + a compiled-fails-cleanly guard.)
    [Theory, ModeData]
    public void AsyncGeneratorExpression_CapturesAsyncGeneratorLocal(ExecutionMode mode)
    {
        var source = """
            async function* outer() {
                let y = 3;
                const g = async function* () { yield y; yield y + 1; };
                for await (const v of g()) yield v;
            }
            async function main() {
                const out: number[] = [];
                for await (const v of outer()) out.push(v);
                console.log(out.join(","));
            }
            main();
            """;
        Assert.Equal("3,4\n", TestHarness.Run(source, mode));
    }

    // #945: directly pins the compiled fix for the async-generator encloser — the lifted
    // `async function* __genArrow_N` is appended at the enclosing async-generator body's END, so its
    // forwarding binding must be hoisted above the earlier `const g = …` reference AND its forwarded
    // capture routed through the function DC. Before the fix this miscompiled to a runtime
    // "object is not a function".
    [Fact]
    public void AsyncGeneratorExpression_CapturesAsyncGeneratorLocal_CompiledHoistsForwardReference()
    {
        var source = """
            async function* outer() {
                let y = 3;
                const g = async function* () { yield y; yield y + 1; };
                for await (const v of g()) yield v;
            }
            async function main() {
                const out: number[] = [];
                for await (const v of outer()) out.push(v);
                console.log(out.join(","));
            }
            main();
            """;
        Assert.Equal("3,4\n", TestHarness.RunCompiled(source));
    }

    // #945 decline: a class GENERATOR METHOD encloser (here an async-generator instance method) stays
    // unsupported in compiled mode — its state machine wires no function DC for read-only captures, so
    // hoisting the forwarding binding would read a stale by-value snapshot. It must fail CLEANLY (compile
    // error), never miscompile to a wrong value. The interpreter runs the nested declaration natively.
    [Fact]
    public void AsyncGeneratorExpression_CapturesAsyncGeneratorMethodLocal_CompiledDeclinesCleanly()
    {
        var source = """
            class C {
                async *m() {
                    let y = 3;
                    const g = async function* () { yield y; yield y + 1; };
                    for await (const v of g()) yield v;
                }
            }
            async function main() {
                const out: number[] = [];
                for await (const v of new C().m()) out.push(v);
                console.log(out.join(","));
            }
            main();
            """;
        Assert.Equal("3,4\n", TestHarness.Run(source, ExecutionMode.Interpreted));
        string compiled;
        try { compiled = TestHarness.RunCompiled(source); }
        catch { compiled = "<compile-or-runtime-error>"; }
        // A clean failure is acceptable; the correct "3,4\n" is acceptable; a wrong value is not.
        Assert.True(compiled == "<compile-or-runtime-error>" || compiled == "3,4\n", $"unexpected: {compiled}");
    }

    #endregion
}
