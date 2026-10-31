using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseSubclassConstructorParentTests
{
    [Theory, ModeData]
    public void NativePromiseParentsExposeCanonicalGuestValues(ExecutionMode mode)
    {
        const string source = """
            class C extends Promise<any> {}
            console.log(Object.getPrototypeOf(C) === Promise);
            console.log(Object.getPrototypeOf(C.prototype) === Promise.prototype);
            """;
        Assert.Equal("true\ntrue\n", TestHarness.Run(source, mode));
    }
}
