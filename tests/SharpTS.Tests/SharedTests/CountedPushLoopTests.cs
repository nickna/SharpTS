using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class CountedPushLoopTests
{
    [Theory, ModeData]
    public void CountedPushReservation_RetainsBoundsAndRuntimeFallbacks(
        ExecutionMode mode)
    {
        var source = """
            function fill(n: number): number {
                const items: { value: number }[] = [];
                for (let i: number = 0; i < n; i++) {
                    items.push({ value: i });
                }
                return items.length + items[items.length - 1].value;
            }
            console.log(fill(3.2));

            const receiver: any = {
                total: 0,
                push: function (value: number): number {
                    this.total = this.total + value;
                    return 0;
                }
            };
            for (let i: number = 0; i < 3; i++) {
                receiver.push(i);
            }
            console.log(receiver.total);
            """;

        Assert.Equal("7\n3\n", TestHarness.Run(source, mode));
    }
}
