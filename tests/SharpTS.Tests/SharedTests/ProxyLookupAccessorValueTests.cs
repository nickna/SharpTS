using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ProxyLookupAccessorValueTests
{
    [Theory, ModeData]
    public void ProxyDescriptorAccessorValuesAreReturnedWithoutInvocation(ExecutionMode mode)
    {
        Assert.Equal("true true 0\n2\n", TestHarness.Run("""
            let calls = 0;
            let traps = 0;
            const getter: any = function() { calls++; return 3; };
            const setter: any = function(value: any) { calls++; };
            const proxy: any = new Proxy({}, {
                getOwnPropertyDescriptor(object: any, key: any) {
                    traps++;
                    return key === 'key' ? { get: getter, set: setter, configurable: true } : undefined;
                }
            });
            console.log(Object.prototype.__lookupGetter__.call(proxy, 'key') === getter,
                Object.prototype.__lookupSetter__.call(proxy, 'key') === setter, calls);
            console.log(traps);
            """, mode));
    }
}
