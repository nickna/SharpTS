using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DeletedArrayHelperFallbackTests
{
    [Theory, ModeData]
    public void PreserveIssue2316Behavior(ExecutionMode mode)
    {
        const string source = """
            const A: any = Array, helper = A.bind;
            Object.defineProperty(A, "bind", { value: 7, configurable: true });
            console.log(A.bind === 7, delete A.bind, Object.hasOwn(A, "bind"));
            console.log(typeof A.bind, A.bind === helper, A.bind === Function.prototype.bind,
                Reflect.get(A, "bind") === helper);
            """;
        Assert.Equal("true true false\nfunction true true true\n", TestHarness.Run(source, mode));
    }
}
