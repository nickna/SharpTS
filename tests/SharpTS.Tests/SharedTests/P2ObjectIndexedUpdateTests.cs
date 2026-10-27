using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ObjectIndexedUpdateTests
{
    [Theory, ModeData]
    public void OrdinaryRecordIndexedUpdatesUsePropertySemantics(ExecutionMode mode)
    {
        const string source = """
            const holder: any = { item: 1 };
            console.log(holder["item"]++, holder.item);
            console.log(++holder["item"], holder.item);
            console.log(holder["item"]--, holder.item);
            console.log(--holder["item"], holder.item);
            let reads = 0, writes = 0, receivers = 0, keys = 0;
            Object.defineProperty(holder, "accessor", {
                get() { reads++; return "5"; },
                set(value: number) { writes++; this.item = value; }
            });
            function receiver() { receivers++; return holder; }
            function key() { keys++; return "accessor"; }
            console.log(receiver()[key()]++, holder.item, reads, writes, receivers, keys);
            """;
        Assert.Equal("1 2\n3 3\n3 2\n1 1\n5 6 1 1 1 1\n", TestHarness.Run(source, mode));
    }
}
