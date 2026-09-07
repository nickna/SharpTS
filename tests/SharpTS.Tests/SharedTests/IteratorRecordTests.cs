using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class IteratorRecordTests
{
    [Theory, ModeData]
    public void SumPreciseCapturesNextBeforeItReplacesItself(ExecutionMode mode)
    {
        const string source = """
            let index = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() {
                    this.next = () => ({ value: 9, done: true });
                    return { value: index++, done: index > 3 };
                }
            };
            console.log(Math.sumPrecise(iterator));
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpreadCapturesNextBeforeItReplacesItself(ExecutionMode mode)
    {
        const string source = """
            let index = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() {
                    this.next = () => ({ value: 9, done: true });
                    return { value: index++, done: index > 3 };
                }
            };
            console.log([...iterator].join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GenericIteratorRemainsStandalone()
    {
        const string source = """
            function run(n: number): number {
                let current: number = 0;
                const iterator: any = {
                    [Symbol.iterator]() { return this; },
                    next() { return { value: current++, done: current > n }; }
                };
                const alias: any = iterator;
                alias.next = alias.next;
                let sum = 0;
                for (const value of iterator) sum = sum + value;
                return sum;
            }
            console.log(run(10));
            """;
        Assert.Equal("45\n", TestHarness.RunCompiledStandalone(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Theory, ModeData]
    public void NextIsCapturedOnceAndReacquiredForEachLoop(ExecutionMode mode)
    {
        const string source = """
            let reads = 0;
            let current = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                get next() {
                    reads++;
                    return function() {
                        if (this !== iterator) throw new Error("receiver");
                        return { value: current++, done: current > 3 };
                    };
                }
            };
            let sum = 0;
            for (const value of iterator) sum += value;
            for (const value of iterator) sum += value;
            console.log(sum, reads);
            """;
        Assert.Equal("3 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReplacingNextDuringIterationDoesNotReplaceCapturedMethod(ExecutionMode mode)
    {
        const string source = """
            let current = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() { return { value: current++, done: current > 3 }; }
            };
            let sum = 0;
            for (const value of iterator) {
                sum += value;
                iterator.next = () => ({ value: 999, done: true });
            }
            console.log(sum);
            console.log(iterator.next().value);
            """;
        Assert.Equal("3\n999\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReplacementBeforeAcquisitionIsHonored(ExecutionMode mode)
    {
        const string source = """
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() { throw new Error("old method"); }
            };
            const alias: any = iterator;
            let current = 0;
            alias.next = () => ({ value: current++, done: current > 3 });
            let sum = 0;
            for (const value of iterator) sum += value;
            console.log(sum);
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StepFailureDoesNotCloseIterator(ExecutionMode mode)
    {
        const string source = """
            let closes = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() { throw new Error("step"); },
                return() { closes++; return {}; }
            };
            try { for (const value of iterator) {} }
            catch (e: any) { console.log(e.message); }
            console.log(closes);
            """;
        Assert.Equal("step\n0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DonePrecedesValueAndCompletedValueIsNotRead(ExecutionMode mode)
    {
        const string source = """
            let step = 0;
            let log = "";
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() {
                    step++;
                    return {
                        get done() { log += "d"; return step > 1; },
                        get value() { log += "v"; return 5; }
                    };
                }
            };
            let sum = 0;
            for (const value of iterator) sum += value;
            console.log(sum, log);
            """;
        Assert.Equal("5 dvd\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrimitiveResultIsTypeErrorWithoutClose(ExecutionMode mode)
    {
        const string source = """
            let closes = 0;
            const iterator: any = {
                [Symbol.iterator]() { return this; },
                next() { return 1; },
                return() { closes++; return {}; }
            };
            try { for (const value of iterator) { break; } }
            catch (e: any) { console.log(e.name); }
            console.log(closes);
            """;
        Assert.Equal("TypeError\n0\n", TestHarness.Run(source, mode));
    }
}
