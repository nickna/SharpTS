using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Promise methods: .then(), .catch(), .finally(),
/// Promise.all(), Promise.race(), Promise.resolve(), Promise.reject(),
/// Promise.allSettled(), Promise.any(), and Promise executor constructor.
/// </summary>
public class PromiseMethodTests
{
    #region Promise.then() Tests

    [Theory, ModeData]
    public void Then_BasicChaining(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function main(): Promise<void> {
                let p = getValue();
                let result = await p.then((x: number): number => x * 2);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void Then_MultipleChains(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 5;
            }
            async function main(): Promise<void> {
                let result = await getValue()
                    .then((x: number): number => x + 1)
                    .then((x: number): number => x * 2)
                    .then((x: number): number => x + 3);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void Then_ReturnsPromise_Flattens(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function double(x: number): Promise<number> {
                return x * 2;
            }
            async function main(): Promise<void> {
                let result = await getValue().then((x: number): Promise<number> => double(x));
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void Then_WithOnRejected(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.reject("error");
                let result = await p.then(
                    (x: string): string => "success: " + x,
                    (err: string): string => "caught: " + err
                );
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught: error\n", output);
    }

    [Theory, ModeData]
    public void Then_PassesValueThrough(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(42).then((x: number): number => x);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // Per ECMA-262 the handler arguments to then/catch/finally are all
    // optional, so a zero-argument call is legal and acts as a pass-through
    // (then/catch) or no-op (finally). Previously the interpreter registered
    // these with minArity 1 and threw "expects 1-2 arguments but got 0" (#382).

    [Theory, ModeData]
    public void Then_ZeroArgs_PassesValueThrough(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(5).then();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void Catch_ZeroArgs_PassesValueThrough(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(7).catch();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void Finally_ZeroArgs_PassesValueThrough(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(9).finally();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9\n", output);
    }

    #endregion

    #region Promise.catch() Tests

    [Theory, ModeData]
    public void Catch_HandlesRejection(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.reject("something went wrong");
                let result = await p.catch((err: string): string => "handled: " + err);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("handled: something went wrong\n", output);
    }

    [Theory, ModeData]
    public void Catch_PassesThroughResolved(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve(42);
                let result = await p.catch((err: string): number => -1);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Catch_AfterThen(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.reject("error");
                let result = await p
                    .then((x: number): number => x * 2)
                    .catch((err: string): number => 99);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Promise.finally() Tests

    [Theory, ModeData]
    public void Finally_RunsOnResolved(ExecutionMode mode)
    {
        var source = """
            let ran: boolean = false;
            async function main(): Promise<void> {
                let result = await Promise.resolve(42).finally((): void => {
                    ran = true;
                });
                console.log(result);
                console.log(ran);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Finally_DoesNotAlterValue(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(42).finally((): number => {
                    return 999;
                });
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Promise.all() Tests

    [Theory, ModeData]
    public void All_ResolvesAllPromises(ExecutionMode mode)
    {
        var source = """
            async function a(): Promise<number> { return 1; }
            async function b(): Promise<number> { return 2; }
            async function c(): Promise<number> { return 3; }
            async function main(): Promise<void> {
                let results = await Promise.all([a(), b(), c()]);
                console.log(results[0]);
                console.log(results[1]);
                console.log(results[2]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void All_EmptyArray(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.all([]);
                console.log(results.length);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void All_WithNonPromises(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.all([1, 2, 3]);
                console.log(results[0] + results[1] + results[2]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    #endregion

    #region Promise.race() Tests

    [Theory, ModeData]
    public void Race_FirstResolvedWins(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve("first");
                let p2 = Promise.resolve("second");
                let result = await Promise.race([p1, p2]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n", output);
    }

    [Theory, ModeData]
    public void Race_NonPromiseWins(ExecutionMode mode)
    {
        var source = """
            async function slow(): Promise<string> {
                return "slow";
            }
            async function main(): Promise<void> {
                let result = await Promise.race(["immediate", slow()]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("immediate\n", output);
    }

    #endregion

    #region Promise.resolve() Tests

    [Theory, ModeData]
    public void Resolve_WrapsValue(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve(42);
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Resolve_NoDoubleWrap(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function main(): Promise<void> {
                let p = getValue();
                let p2 = Promise.resolve(p);
                let result = await p2;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Resolve_NoArgs()
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve();
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("undefined\n", output);
    }

    /// <summary>
    /// Regression test: Nested Promise.resolve must flatten correctly.
    /// Previously, this caused double-wrapping: Promise(Task(Promise(Task(value))))
    /// which led to infinite loops in async iterators.
    /// </summary>
    [Theory, ModeData]
    public void Resolve_NestedPromiseResolve_Flattens(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let innerPromise = Promise.resolve(42);
                let outerPromise = Promise.resolve(innerPromise);
                let result = await outerPromise;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    /// <summary>
    /// Regression test: Triple-nested Promise.resolve must flatten correctly.
    /// </summary>
    [Theory, ModeData]
    public void Resolve_TripleNestedPromise_Flattens(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve(100);
                let p2 = Promise.resolve(p1);
                let p3 = Promise.resolve(p2);
                let result = await p3;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    /// <summary>
    /// Regression test: Promise.resolve with object containing done:true.
    /// This is the exact pattern that caused infinite loops in async iterators
    /// when the iterator result object was double-wrapped.
    /// </summary>
    [Theory, ModeData]
    public void Resolve_IteratorResultObject_NotDoubleWrapped(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let iterResult = { value: 42, done: true };
                let p = Promise.resolve(iterResult);
                let result = await p;
                console.log(result.value);
                console.log(result.done);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\ntrue\n", output);
    }

    /// <summary>
    /// Regression test: Async function returning Promise.resolve should not double-wrap.
    /// </summary>
    [Theory, ModeData]
    public void Resolve_FromAsyncFunction_NotDoubleWrapped(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return await Promise.resolve(99);
            }
            async function main(): Promise<void> {
                let result = await getValue();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Promise.reject() Tests

    [Theory, ModeData]
    public void Reject_CreatesRejectedPromise(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.reject("error message");
                try {
                    await p;
                    console.log("should not reach");
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\n", output);
    }

    [Fact]
    public void ResolveAndReject_OmittedValuesAreUndefined()
    {
        var source = """
            async function main(): Promise<void> {
                console.log((await Promise.resolve()) === undefined);
                try {
                    await Promise.reject();
                } catch (reason) {
                    console.log(reason === undefined);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Theory, ModeData]
    public void Chaining_ThenCatchFinally(ExecutionMode mode)
    {
        var source = """
            let log: string = "";
            async function main(): Promise<void> {
                let result = await Promise.resolve(10)
                    .then((x: number): number => {
                        log = log + "then1 ";
                        return x * 2;
                    })
                    .then((x: number): number => {
                        log = log + "then2 ";
                        return x + 5;
                    })
                    .catch((err: string): number => {
                        log = log + "catch ";
                        return -1;
                    })
                    .finally((): void => {
                        log = log + "finally";
                    });
                console.log(result);
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("25\nthen1 then2 finally\n", output);
    }

    [Theory, ModeData]
    public void MultipleThenOnSamePromise(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve(10);
                let r1 = await p.then((x: number): number => x * 2);
                let r2 = await p.then((x: number): number => x + 5);
                console.log(r1);
                console.log(r2);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n15\n", output);
    }

    #endregion

    #region Promise.allSettled() Tests

    [Theory, ModeData]
    public void AllSettled_AllResolve(ExecutionMode mode)
    {
        var source = """
            async function a(): Promise<number> { return 1; }
            async function b(): Promise<number> { return 2; }
            async function main(): Promise<void> {
                let results = await Promise.allSettled([a(), b()]);
                console.log(results[0].status);
                console.log(results[0].value);
                console.log(results[1].status);
                console.log(results[1].value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("fulfilled\n1\nfulfilled\n2\n", output);
    }

    [Theory, ModeData]
    public void AllSettled_SomeReject(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve(1);
                let p2 = Promise.reject("error");
                let p3 = Promise.resolve(3);
                let results = await Promise.allSettled([p1, p2, p3]);
                console.log(results[0].status);
                console.log(results[0].value);
                console.log(results[1].status);
                console.log(results[1].reason);
                console.log(results[2].status);
                console.log(results[2].value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("fulfilled\n1\nrejected\nerror\nfulfilled\n3\n", output);
    }

    [Theory, ModeData]
    public void AllSettled_EmptyArray(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.allSettled([]);
                console.log(results.length);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void AllSettled_WithNonPromises(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.allSettled([42, "hello"]);
                console.log(results[0].status);
                console.log(results[0].value);
                console.log(results[1].status);
                console.log(results[1].value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("fulfilled\n42\nfulfilled\nhello\n", output);
    }

    #endregion

    #region Promise.any() Tests

    [Theory, ModeData]
    public void Any_FirstResolves(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve("first");
                let p2 = Promise.resolve("second");
                let result = await Promise.any([p1, p2]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n", output);
    }

    [Theory, ModeData]
    public void Any_NonPromiseWins(ExecutionMode mode)
    {
        var source = """
            async function slow(): Promise<string> {
                return "slow";
            }
            async function main(): Promise<void> {
                let result = await Promise.any(["immediate", slow()]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("immediate\n", output);
    }

    [Theory, ModeData]
    public void Any_FirstFulfilledAfterRejection(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.reject("error1");
                let p2 = Promise.resolve("success");
                let p3 = Promise.reject("error2");
                let result = await Promise.any([p1, p2, p3]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("success\n", output);
    }

    [Theory, ModeData]
    public void Any_AllReject_ThrowsAggregateError(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.reject("error1");
                let p2 = Promise.reject("error2");
                try {
                    await Promise.any([p1, p2]);
                    console.log("should not reach");
                } catch (e) {
                    console.log("caught");
                    console.log(e.name);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\nAggregateError\n", output);
    }

    [Theory, ModeData]
    public void Any_EmptyArray_ThrowsAggregateError(ExecutionMode mode)
    {
        // ECMA-262 §27.2.4.3: empty iterable rejects with an AggregateError
        // whose errors is []. Compiled mode used to reject with a raw BCL
        // Exception whose name read "Exception" (#220).
        var source = """
            async function main(): Promise<void> {
                try {
                    await Promise.any([]);
                    console.log("should not reach");
                } catch (e: any) {
                    console.log("caught");
                    console.log(e.name);
                    console.log(Array.isArray(e.errors) ? e.errors.length : "no errors");
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\nAggregateError\n0\n", output);
    }

    [Theory, ModeData]
    public void Any_RejectionIsInstanceofAggregateError(ExecutionMode mode)
    {
        // #232: the combinator's rejection must be the same representation
        // `new AggregateError()` produces, so instanceof checks hold.
        var source = """
            async function main(): Promise<void> {
                try {
                    await Promise.any([Promise.reject("e1")]);
                } catch (e: any) {
                    console.log(e instanceof AggregateError);
                    console.log(e instanceof Error);
                }
                try {
                    await Promise.any([]);
                } catch (e: any) {
                    console.log(e instanceof AggregateError, e instanceof Error);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue true\n", output);
    }

    [Theory, ModeData]
    public void Any_ErrorsCarryGuestRejectionValues(ExecutionMode mode)
    {
        // #232: e.errors must hold what each promise rejected with — the
        // guest values themselves, not host exception wrapper messages.
        var source = """
            async function thrower(): Promise<number> {
                throw new Error("boom");
            }
            async function main(): Promise<void> {
                try {
                    await Promise.any([Promise.reject(new TypeError("t1")), thrower()]);
                } catch (e: any) {
                    console.log(e.errors.length);
                    console.log(e.errors[0] instanceof TypeError, e.errors[0].message);
                    console.log(e.errors[1] instanceof Error, e.errors[1].message);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\ntrue t1\ntrue boom\n", output);
    }

    [Theory, ModeData]
    public void AllSettled_ReasonCarriesGuestValue(ExecutionMode mode)
    {
        // #232 (adjacent): allSettled's rejected outcome must carry the guest
        // rejection value, including guest throws from async functions.
        var source = """
            async function thrower(): Promise<number> {
                throw new RangeError("r1");
            }
            async function main(): Promise<void> {
                const results: any = await Promise.allSettled([thrower()]);
                console.log(results[0].status);
                console.log(results[0].reason instanceof RangeError, results[0].reason.message);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("rejected\ntrue r1\n", output);
    }

    [Theory, CompiledOnlyData]
    public void InstanceofError_InsideAsyncFunction(ExecutionMode mode)
    {
        // #232 root cause in compiled mode: state-machine bodies (async
        // functions) resolved built-in constructor identifiers to null, so
        // EVERY `x instanceof Error/Map/Date` inside async was false.
        var source = """
            async function main(): Promise<void> {
                const e: any = new Error("x");
                console.log(e instanceof Error);
                const t: any = new TypeError("x");
                console.log(t instanceof TypeError, t instanceof Error);
                const m: any = new Map();
                console.log(m instanceof Map);
                const d: any = new Date();
                console.log(d instanceof Date);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Promise Executor Constructor Tests

    [Theory, ModeData]
    public void ExecutorConstructor_ImmediateResolve(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    resolve(42);
                });
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_ImmediateReject(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    reject("something went wrong");
                });
                try {
                    await p;
                    console.log("should not reach");
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_ResolveWithObject(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<{name: string}>((resolve, reject) => {
                    resolve({ name: "test" });
                });
                let result = await p;
                console.log(result.name);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_ResolveWithUndefined(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<void>((resolve, reject) => {
                    resolve();
                });
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_ExecutorThrows(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    throw new Error("executor error");
                });
                try {
                    await p;
                    console.log("should not reach");
                } catch (e) {
                    console.log("caught executor error");
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught executor error\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_OnlyFirstSettlementCounts(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    resolve(1);
                    resolve(2);
                    reject("error");
                });
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_ChainWithThen(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    resolve(10);
                });
                let result = await p.then((x: number): number => x * 2);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_ChainWithCatch(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    reject("error");
                });
                let result = await p.catch((e: string): number => 99);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_UseInPromiseAll(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = new Promise<number>((resolve, reject) => resolve(1));
                let p2 = new Promise<number>((resolve, reject) => resolve(2));
                let p3 = new Promise<number>((resolve, reject) => resolve(3));
                let results = await Promise.all([p1, p2, p3]);
                console.log(results[0] + results[1] + results[2]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void ExecutorConstructor_UseInPromiseRace(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = new Promise<string>((resolve, reject) => resolve("first"));
                let p2 = new Promise<string>((resolve, reject) => resolve("second"));
                let result = await Promise.race([p1, p2]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n", output);
    }

    #endregion

    #region promise.constructor (#221 SpeciesConstructor increment)

    /// <summary>
    /// ECMA-262 §27.2.5.1: Promise.prototype.constructor is %Promise%, so
    /// <c>promise.constructor === Promise</c> must hold by identity. This is
    /// the observable subset of #221's SpeciesConstructor work — species
    /// dispatch itself stays unobservable until guest classes can subclass
    /// Promise (#233) and Symbol is a first-class value (#234).
    /// </summary>
    [Theory, ModeData]
    public void PromiseConstructorProperty_IsPromiseGlobal(ExecutionMode mode)
    {
        var source = """
            const p = Promise.resolve(1);
            console.log((p as any).constructor === Promise);
            console.log(typeof (p as any).constructor);
            async function f(): Promise<number> { return 2; }
            const q: any = f();
            console.log(q.constructor === Promise);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfunction\ntrue\n", output);
    }

    [Fact]
    public void PromisePrototype_IsAnOrdinaryMutableObject()
    {
        var source = """
            const proto: any = Promise.prototype;
            const original = proto.then;
            console.log(Object.hasOwn(proto, "then"));
            console.log(delete proto.then);
            console.log(Object.hasOwn(proto, "then"));
            proto["then"] = original;
            console.log(Object.hasOwn(proto, "then"));
            console.log(Object.getPrototypeOf(proto) === Object.prototype);
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("true\ntrue\nfalse\ntrue\ntrue\n", output);
    }

    [Fact]
    public void PromiseMethods_AreInheritedByIdentityAndBindAtCallTime()
    {
        var source = """
            async function main(): Promise<void> {
                const p: any = Promise.resolve(3);
                console.log(p.then === Promise.prototype.then);
                console.log(p.catch === Promise.prototype.catch);
                console.log(p.finally === Promise.prototype.finally);
                console.log(await p.then((value: number) => value + 1));
            }
            main();
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("true\ntrue\ntrue\n4\n", output);
    }

    #endregion

    #region Executor rejection reason

    /// <summary>
    /// The executor reject callback must hand the guest value through
    /// unchanged — the compiled $PromiseRejectCallback used to re-wrap it as
    /// new Exception(reason.ToString()), so catch handlers saw a host string
    /// ("Error: nope") instead of the rejected error object (#232 family).
    /// </summary>
    [Theory, ModeData]
    public void ExecutorReject_PreservesGuestReason(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const p = new Promise<number>((resolve, reject) => { reject(new TypeError("raw")); });
                const v = await p.catch((e: any) => (e instanceof TypeError) + ":" + e.message);
                console.log(v);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true:raw\n", output);
    }

    /// <summary>
    /// Resolving a pending promise with another (already-settled) promise must
    /// adopt the inner promise's value and still run the outer's <c>.then</c>
    /// reaction (ECMA-262 resolve-with-thenable). The interpreter adopted the
    /// inner promise on a thread-pool continuation, so the event loop could exit
    /// before the adoption settled and the reaction was dropped — a load-sensitive
    /// flake (Test262 Promise/resolve-thenable-deferred flipped Pass/Fail). The
    /// adoption now runs as an event-loop callback, so the reaction always fires.
    /// Interpreter-only — compiled mode emits its own event loop (it has the same
    /// resolve-with-thenable gap, tracked separately).
    /// </summary>
    [Fact]
    public void ResolveWithThenable_Deferred_RunsReactionWithInnerValue()
    {
        var mode = ExecutionMode.Interpreted;
        var source = """
            const value: any = { tag: "inner" };
            let resolve: any;
            const thenable = new Promise((r: any) => r(value));
            const promise = new Promise((res: any) => { resolve = res; });
            promise.then((v: any) => {
                console.log(v === value ? "same" : "different");
            }, () => {
                console.log("rejected");
            });
            resolve(thenable);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("same\n", output);
    }

    /// <summary>
    /// The await form of resolve-with-thenable: awaiting a promise that was
    /// resolved with another promise yields the inner value (flattening through
    /// the same event-loop adoption path). Interpreter-only (see above).
    /// </summary>
    [Fact]
    public void ResolveWithThenable_Awaited_YieldsInnerValue()
    {
        var mode = ExecutionMode.Interpreted;
        var source = """
            async function main(): Promise<void> {
                let resolve: any;
                const inner = new Promise((r: any) => r(42));
                const outer = new Promise((res: any) => { resolve = res; });
                resolve(inner);
                console.log(await outer);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion
}
