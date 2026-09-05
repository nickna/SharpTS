using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArrayQueueTests
{
    [Theory, ModeData]
    public void Queue_NumericKeysDoNotTruncateFractionalOrHugeIndices(ExecutionMode mode)
    {
        const string source = """
            function run(): void {
                const values: number[] = [];
                values.unshift(8);
                console.log(values[0.5] === undefined, values[-1] === undefined);
                console.log(values[4294967296] === undefined, values[NaN] === undefined);
                console.log(Number.isNaN(1 + values[0.5]), values[0]);
            }
            run();
            """;
        Assert.Equal("true true\ntrue true\ntrue 8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Queue_ReusesStorageAndPreservesLogicalIndices(ExecutionMode mode)
    {
        const string source = """
            function run(): void {
                const values: number[] = [];
                for (let i: number = 0; i < 100; i++) values.push(i);
                let sum: number = 0;
                for (let i: number = 0; i < 1000; i++) {
                    sum = sum + values.shift();
                    values.push(i + 100);
                    values.unshift(-1, -2);
                    sum = sum + values.shift() + values.shift() + 3;
                }
                console.log(sum, values.length, values[0], values[99]);
                values[0] = 7;
                values[99] = 8;
                console.log(values.shift(), values[98]);
                while (values.length > 0) values.shift();
                console.log(values.shift() === undefined);
                values.push(5);
                console.log(values.shift(), values.length);
            }
            run();
            """;
        Assert.Equal("499500 100 1000 1099\n7 8\ntrue\n5 0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Queue_ExtendingWritesPreserveHolesAndBooleans(ExecutionMode mode)
    {
        const string source = """
            function run(): void {
                const values: number[] = [];
                values.push(2, 3);
                console.log(values.shift());
                values[4] = 9;
                console.log(values.length, values[1] === undefined, values[4]);
                console.log(values.shift(), values.shift() === undefined);
                const flags: boolean[] = [];
                flags.unshift(false, true);
                flags[3] = false;
                console.log(flags.shift(), flags.shift(), flags.shift() === undefined, flags.shift());
                console.log(flags.shift() === undefined);
            }
            run();
            """;
        Assert.Equal("2\n5 true 9\n3 true\nfalse true true false\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Queue_EvaluatesArgumentsBeforeMutationAndPreservesNumericEmpty(ExecutionMode mode)
    {
        const string source = """
            function run(): void {
                const values: number[] = [];
                values.push(10);
                console.log(values.unshift(values.length, values.length));
                console.log(values.shift(), values.shift(), values.shift());
                values.push(20);
                console.log(values.push(values.length, values.length));
                console.log(values.shift(), values.shift(), values.shift());
                console.log(Number.isNaN(1 + values.shift()));
                console.log(values.shift() === undefined);
            }
            run();
            """;
        Assert.Equal("3\n1 1 10\n3\n20 1 1\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Queue_FallsBackAfterShapeChanges(ExecutionMode mode)
    {
        const string source = """
            const values: any[] = [];
            values.push(1, 2, 3);
            console.log(values.shift());
            delete values[0];
            Object.defineProperty(Array.prototype, "0", { configurable: true, value: 42, writable: true });
            console.log(values.shift(), values.shift());
            delete (Array.prototype as any)[0];
            values.push(5, 6);
            Object.defineProperty(values, "1", { configurable: true, get(): number { return 9; } });
            console.log(values.shift(), values[0]);
            const fixed: number[] = [];
            fixed.push(1);
            Object.defineProperty(fixed, "length", { writable: false });
            try { fixed.unshift(2); } catch (e) { console.log(e instanceof TypeError); }
            """;
        Assert.Equal("1\n42 3\n5 9\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Queue_UnshiftObservesInheritedTailSetter(ExecutionMode mode)
    {
        const string source = """
            const values: any[] = [];
            values.unshift(3);
            let seen: number = 0;
            Object.defineProperty(Array.prototype, "1", {
                configurable: true,
                set(value: number): void { seen = value; }
            });
            console.log(values.unshift(2), seen, values[0], values[1] === undefined);
            delete (Array.prototype as any)[1];
            """;
        Assert.Equal("2 3 2 true\n", TestHarness.Run(source, mode));
    }
}
