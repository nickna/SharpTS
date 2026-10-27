using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PromiseWithResolversReasonIdentityTests
{
    [Theory, ModeData]
    public void RejectPreservesTheOriginalReason(ExecutionMode mode)
    {
        const string source = """
            const reason = { value: 42 };
            const capability = Promise.withResolvers<any>();
            capability.promise.catch((actual: any) => console.log(actual === reason, actual.value));
            capability.reject(reason);
            """;
        Assert.Equal("true 42\n", TestHarness.Run(source, mode));
    }
}
