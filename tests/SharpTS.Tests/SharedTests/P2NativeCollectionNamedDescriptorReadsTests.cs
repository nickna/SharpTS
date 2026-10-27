using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NativeCollectionNamedDescriptorReadsTests
{
    [Theory, ModeData]
    public void OwnDataAndAccessorDescriptorsShadowNativeCollectionMembers(ExecutionMode mode)
    {
        const string source = """
            for (const value of [new Set<number>([1]), new Map<string, number>([['a', 1]])]) {
                Object.defineProperty(value, 'size', { value: 9, configurable: true });
                Object.defineProperty(value, 'marker', { get() { return this === value; }, configurable: true });
                Object.defineProperty(value, 'missing', { set(next: any) {}, configurable: true });
                const current: any = value;
                console.log(current.size, current.marker, current.missing === undefined);
                delete current.size;
                console.log(current.size);
            }
            """;
        Assert.Equal("9 true true\n1\n9 true true\n1\n", TestHarness.Run(source, mode));
    }
}
