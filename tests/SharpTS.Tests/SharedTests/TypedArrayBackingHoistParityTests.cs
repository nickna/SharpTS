using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>Observable parity coverage for the #1481 compiled backing-storage specialization.</summary>
public sealed class TypedArrayBackingHoistParityTests
{
    [Theory, ModeData]
    public void ExactNumericLoops_PreserveNarrowingAndAccumulation(ExecutionMode mode)
    {
        const string source = """
            function exercise(n: number): number {
                const ints = new Int32Array(n);
                const floats = new Float64Array(n);
                for (let i: number = 0; i < n; i++) {
                    ints[i] = i * 3.75;
                    floats[i] = i * 0.5;
                }
                for (let i: number = 0; i < n; i++) floats[i] += ints[i];
                let sum: number = 0;
                for (let i: number = 0; i < ints.length; i++) {
                    sum = sum + ints[i] + floats[i];
                }
                return sum;
            }
            console.log(exercise(6));
            """;

        Assert.Equal("115.5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AliasedView_RemainsObservableThroughFallback(ExecutionMode mode)
    {
        const string source = """
            const original = new Uint8Array(4);
            const alias = original.subarray(1, 3);
            for (let i: number = 0; i < original.length; i++) original[i] = i * 10 + 1;
            console.log(alias[0]);
            console.log(alias[1]);
            alias[0] = 99;
            console.log(original[1]);
            """;

        Assert.Equal("11\n21\n99\n", TestHarness.Run(source, mode));
    }
}
