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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    #endregion

    #region Promise.catch() Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Resolve_NoArgs(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve();
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    /// <summary>
    /// Regression test: Nested Promise.resolve must flatten correctly.
    /// Previously, this caused double-wrapping: Promise(Task(Promise(Task(value))))
    /// which led to infinite loops in async iterators.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    #endregion

    #region Complex Scenarios

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Any_EmptyArray_ThrowsAggregateError(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                try {
                    await Promise.any([]);
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

    #endregion

    #region Promise Executor Constructor Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
}
