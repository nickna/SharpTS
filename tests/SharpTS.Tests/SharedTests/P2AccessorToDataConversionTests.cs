using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2AccessorToDataConversionTests
{
    [Theory, ModeData]
    public void WritableOnlyDefinitionConvertsAccessorToUndefinedData(ExecutionMode mode)
    {
        const string source = """
            const obj: any = {};
            let reads = 0;
            Object.defineProperty(obj, "accessor", { get() { reads++; return null; }, enumerable: true, configurable: true });
            Object.defineProperty(obj, "accessor", { writable: true });
            const descriptor: any = Object.getOwnPropertyDescriptor(obj, "accessor");
            console.log(obj.accessor === undefined, "value" in descriptor, "get" in descriptor, descriptor.writable, descriptor.enumerable, descriptor.configurable, reads);
            obj.accessor = 7;
            console.log(obj.accessor);
            Object.defineProperty(obj, "accessor", { get() { return 9; }, configurable: true });
            Object.defineProperty(obj, "accessor", { enumerable: false });
            console.log(obj.accessor);
            """;
        Assert.Equal("true true false true true true 0\n7\n9\n", TestHarness.Run(source, mode));
    }
}
