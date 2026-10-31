using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NativeConstructorAddedNamesTests
{
    [Theory, ModeData]
    public void NativeConstructorOwnNamesIncludeAddedDataAndAccessorProperties(ExecutionMode mode)
    {
        const string source = """
            for (const ctor of [Object as any, Array as any, Number as any]) {
                let reads = 0;
                ctor.extra = 1;
                Object.defineProperty(ctor, "hidden", { value: 2 });
                Object.defineProperty(ctor, "accessor", { get() { reads++; return 3; }, configurable: true });
                const names = Object.getOwnPropertyNames(ctor);
                console.log(names.includes("extra"), names.includes("hidden"), names.includes("accessor"), reads);
                delete ctor.extra;
                console.log(Object.getOwnPropertyNames(ctor).includes("extra"));
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true true true 0\nfalse\n", 3)), TestHarness.Run(source, mode));
    }
}
