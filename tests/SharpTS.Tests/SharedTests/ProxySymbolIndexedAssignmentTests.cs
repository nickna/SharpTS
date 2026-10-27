using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ProxySymbolIndexedAssignmentTests
{
    [Theory, ModeData]
    public void IndexedProxySetTrapsReceiveSymbolsAndOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol("key"), target: any = {};
            const proxy: any = new Proxy(target, {
                set(actual: any, property: any, value: any, receiver: any) {
                    console.log(property === key, typeof property, receiver === proxy, value);
                    return true;
                }
            });
            proxy[key] = 1;
            """;
        Assert.Equal("true symbol true 1\n", TestHarness.Run(source, mode));
    }
}
