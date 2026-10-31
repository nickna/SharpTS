using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PromiseWithResolversSubclassCapabilityTests
{
    [Theory, ModeData]
    public void InheritedWithResolversUsesTheSelectedConstructorResult(ExecutionMode mode)
    {
        const string source = """
            const replacement: any = { value: 43 };
            class Returned extends Promise<any> {
                constructor(executor: any) { super(executor); return replacement; }
            }
            console.log(Returned.withResolvers().promise === replacement);
            class Derived extends Promise<any> {}
            const capability: any = Derived.withResolvers();
            console.log(capability.promise instanceof Derived, typeof capability.resolve, typeof capability.reject);
            capability.promise.then((value: any) => console.log(value));
            capability.resolve(42);
            """;
        Assert.Equal("true\ntrue function function\n42\n", TestHarness.Run(source, mode));
    }
}
