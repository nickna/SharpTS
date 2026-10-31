using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NullDataDescriptorMergeTests
{
    [Theory, ModeData]
    public void AttributeOnlyDefinitionsPreserveExistingNullValues(ExecutionMode mode)
    {
        const string source = """
            class C { value: any = 1; }
            const instance: any = new C();
            Object.defineProperty(instance, "value", { value: null });
            console.log(instance.value === null, Object.getOwnPropertyDescriptor(instance, "value")!.value === null);
            Object.defineProperty(instance, "value", { enumerable: false });
            console.log(instance.value === null, Object.getOwnPropertyDescriptor(instance, "value")!.value === null);
            const object: any = {};
            Object.defineProperty(object, "value", { value: null, configurable: true });
            Object.defineProperty(object, "value", { enumerable: false });
            console.log(object.value === null);
            """;
        Assert.Equal("true true\ntrue true\ntrue\n", TestHarness.Run(source, mode));
    }
}
