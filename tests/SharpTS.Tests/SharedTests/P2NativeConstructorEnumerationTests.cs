using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NativeConstructorEnumerationTests
{
    [Theory, ModeData]
    public void NativeConstructorEnumerablePropertiesAppearInObjectEnumeration(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = Map;
            ctor.extra = 1;
            let reads = 0;
            Object.defineProperty(ctor, "visible", { get() { reads++; return 2; }, enumerable: true, configurable: true });
            Object.defineProperty(ctor, "hidden", { value: 3 });
            console.log(Object.keys(ctor).join(","), reads);
            console.log(Object.values(ctor).join(","), reads);
            console.log(JSON.stringify(Object.entries(ctor)), reads);
            console.log(Object.getOwnPropertyDescriptor(ctor, "extra")!.enumerable);
            delete ctor.extra;
            console.log(Object.keys(ctor).join(","));
            """;
        Assert.Equal("extra,visible 0\n1,2 1\n[[\"extra\",1],[\"visible\",2]] 2\ntrue\nvisible\n", TestHarness.Run(source, mode));
        Assert.Equal("0\n", TestHarness.Run("console.log(Object.keys(Map).length);", mode));
    }
}
