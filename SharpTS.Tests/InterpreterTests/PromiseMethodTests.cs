using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for Promise methods: .then(), .catch(), .finally(),
/// Promise.all(), Promise.race(), Promise.resolve(), Promise.reject()
/// </summary>
public class PromiseMethodTests
{
    #region Promise.then() Tests

    [Fact]
    public void Then_BasicChaining()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Then_MultipleChains()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void Then_ReturnsPromise_Flattens()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Then_WithOnRejected()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught: error\n", output);
    }

    #endregion

    #region Promise.catch() Tests

    [Fact]
    public void Catch_HandlesRejection()
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.reject("something went wrong");
                let result = await p.catch((err: string): string => "handled: " + err);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("handled: something went wrong\n", output);
    }

    [Fact]
    public void Catch_PassesThroughResolved()
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve(42);
                let result = await p.catch((err: string): number => -1);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Catch_AfterThen()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Promise.finally() Tests

    [Fact]
    public void Finally_RunsOnResolved()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\ntrue\n", output);
    }

    [Fact]
    public void Finally_DoesNotAlterValue()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Promise.all() Tests

    [Fact]
    public void All_ResolvesAllPromises()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void All_EmptyArray()
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.all([]);
                console.log(results.length);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void All_WithNonPromises()
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.all([1, 2, 3]);
                console.log(results[0] + results[1] + results[2]);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", output);
    }

    #endregion

    #region Promise.race() Tests

    [Fact]
    public void Race_FirstResolvedWins()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("first\n", output);
    }

    [Fact]
    public void Race_NonPromiseWins()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("immediate\n", output);
    }

    #endregion

    #region Promise.resolve() Tests

    [Fact]
    public void Resolve_WrapsValue()
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve(42);
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Resolve_NoDoubleWrap()
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

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n", output);
    }

    /// <summary>
    /// Regression test: Nested Promise.resolve must flatten correctly.
    /// Previously, this caused double-wrapping: Promise(Task(Promise(Task(value))))
    /// which led to infinite loops in async iterators.
    /// </summary>
    [Fact]
    public void Resolve_NestedPromiseResolve_Flattens()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    /// <summary>
    /// Regression test: Triple-nested Promise.resolve must flatten correctly.
    /// </summary>
    [Fact]
    public void Resolve_TripleNestedPromise_Flattens()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }

    /// <summary>
    /// Regression test: Promise.resolve with object containing done:true.
    /// This is the exact pattern that caused infinite loops in async iterators
    /// when the iterator result object was double-wrapped.
    /// </summary>
    [Fact]
    public void Resolve_IteratorResultObject_NotDoubleWrapped()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\ntrue\n", output);
    }

    /// <summary>
    /// Regression test: Async function returning Promise.resolve should not double-wrap.
    /// </summary>
    [Fact]
    public void Resolve_FromAsyncFunction_NotDoubleWrapped()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Promise.reject() Tests

    [Fact]
    public void Reject_CreatesRejectedPromise()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void Chaining_ThenCatchFinally()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("25\nthen1 then2 finally\n", output);
    }

    [Fact]
    public void MultipleThenOnSamePromise()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n15\n", output);
    }

    #endregion

    #region Promise.allSettled() Tests

    [Fact]
    public void AllSettled_AllResolve()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("fulfilled\n1\nfulfilled\n2\n", output);
    }

    [Fact]
    public void AllSettled_SomeReject()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("fulfilled\n1\nrejected\nerror\nfulfilled\n3\n", output);
    }

    [Fact]
    public void AllSettled_EmptyArray()
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.allSettled([]);
                console.log(results.length);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void AllSettled_WithNonPromises()
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.allSettled([1, 2, 3]);
                console.log(results[0].status);
                console.log(results[0].value);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("fulfilled\n1\n", output);
    }

    #endregion

    #region Promise.any() Tests

    [Fact]
    public void Any_FirstResolves()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("first\n", output);
    }

    [Fact]
    public void Any_NonPromiseWins()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("immediate\n", output);
    }

    [Fact]
    public void Any_FirstFulfilledAfterRejection()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("success\n", output);
    }

    [Fact]
    public void Any_AllReject_ThrowsAggregateError()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught\nAggregateError\n", output);
    }

    [Fact]
    public void Any_EmptyArray_ThrowsAggregateError()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught\n", output);
    }

    #endregion
}
