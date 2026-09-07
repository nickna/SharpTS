using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OptimizationBindingParityTests
{
    private const string Source = """
        function containers(): void {
            let map = new Map<number, number>();
            map.set(1, 2);
            let data = new Int32Array(1);
            data[0] = 3;
            {
                const map = new Map<number, number>();
                map.set(8, 9);
                const data = new Int32Array(1);
                data[0] = 11;
                console.log(map.size + data[0]);
            }
            function nested(): number {
                const map = new Map<number, number>();
                map.set(1, 6);
                const data = new Int32Array(1);
                data[0] = 7;
                return map.size + data[0];
            }
            console.log(nested());
            [map] = [new Map<number, number>()];
            map.set(4, 5);
            ({ data } = { data: new Int32Array(1) });
            data[0] = 9;
            let sum: number = 0;
            for (const entry of map) sum += entry[0] + entry[1];
            console.log(sum + data[0]);
            const capture = (): number => map.size + data[0];
            console.log(capture());
        }
        async function promises(): Promise<void> {
            let chain: Promise<number> = Promise.resolve(1);
            {
                let chain: Promise<number> = Promise.resolve(40);
                chain = chain.then((n: number): number => n + 2);
                console.log(await chain);
            }
            const nested = (): Promise<number> => {
                let chain: Promise<number> = Promise.resolve(2);
                chain = chain.then((n: number): number => n + 3);
                return chain;
            };
            console.log(await nested());
            [chain] = [Promise.resolve(3)];
            chain = chain.then((n: number): number => n + 4);
            const capture = (): Promise<number> => chain;
            console.log(await capture());
        }
        containers();
        promises();
        """;

    private const string Expected = "12\n8\n18\n10\n42\n5\n7\n";

    [Theory, ModeData]
    public void ShadowedNestedCapturedAndDestructuredBindings_PreserveOutput(ExecutionMode mode) =>
        Assert.Equal(Expected, TestHarness.Run(Source, mode));

    [Fact]
    public void ShadowedNestedCapturedAndDestructuredBindings_PreserveStandaloneOutput() =>
        Assert.Equal(Expected, TestHarness.RunCompiledStandalone(Source));
}
