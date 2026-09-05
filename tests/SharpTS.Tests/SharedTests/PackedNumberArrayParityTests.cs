using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Guards the interpreter's packed-number array representation and every
/// transition back to the generic object/descriptor model.
/// </summary>
public sealed class PackedNumberArrayParityTests
{
    [Theory, ModeData]
    public void SequentialIndexGrowth_RoundTripsNumbers(ExecutionMode mode)
    {
        const string source = """
            const a: any[] = [];
            for (let i = 0; i < 100; i++) a[i] = i * 3;
            console.log(a.length, a[0], a[37], a[99]);
            """;
        Assert.Equal("100 0 111 297\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonNumberWrite_MaterializesWithoutLosingValues(ExecutionMode mode)
    {
        const string source = """
            const a: any[] = [];
            a[0] = 1; a[1] = 2; a[1] = "x"; a[2] = 3;
            console.log(a.join(","), a.length);
            """;
        Assert.Equal("1,x,3 3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DescriptorWrite_MaterializesAndHonorsWritable(ExecutionMode mode)
    {
        const string source = """
            const a: any[] = [];
            a[0] = 1; a[1] = 2;
            Object.defineProperty(a, "1", { writable: false });
            a[1] = 9;
            console.log(a.join(","));
            """;
        Assert.Equal("1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeleteAndLengthChanges_PreserveHoleSemantics(ExecutionMode mode)
    {
        const string source = """
            const a: any[] = [];
            a[0] = 1; a[1] = 2; a[2] = 3;
            delete a[1];
            a.length = 2;
            a.length = 4;
            console.log([a[0], 1 in a, a[1], 2 in a, 3 in a, a.length].join("|"));
            """;
        Assert.Equal("1|false||false|false|4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void IterationAndMutationHelpers_MaterializeCorrectly(ExecutionMode mode)
    {
        const string source = """
            const a: any[] = [];
            a[0] = 1; a[1] = 2; a[2] = 3;
            const spread = [...a].join(",");
            a.reverse();
            console.log(spread, a.map((x: number) => x * 2).join(","));
            """;
        Assert.Equal("1,2,3 6,4,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncIndexedRead_RoundTripsPackedNumbers(ExecutionMode mode)
    {
        const string source = """
            async function readPacked(): Promise<number> {
                const a: number[] = [];
                for (let i = 0; i < 100; i++) a[i] = i * 3;
                await Promise.resolve();
                return a[37];
            }
            readPacked().then((value: number) => console.log(value));
            """;
        Assert.Equal("111\n", TestHarness.Run(source, mode));
    }
}
