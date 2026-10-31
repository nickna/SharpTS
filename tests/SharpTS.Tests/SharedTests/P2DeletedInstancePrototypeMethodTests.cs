using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2DeletedInstancePrototypeMethodTests
{
    [Theory, ModeData]
    public void InstanceReadsRespectDeletedLivePrototypeMethods(ExecutionMode mode)
    {
        const string source = """
            class Owner { method() { return 1; } }
            const instance: any = new Owner(), prototype: any = Owner.prototype;
            console.log(typeof instance.method);
            delete prototype.method;
            console.log(instance.method === undefined, "method" in instance);
            prototype.method = () => 3;
            console.log(instance.method());
            """;
        Assert.Equal("function\ntrue false\n3\n", TestHarness.Run(source, mode));
    }
}
