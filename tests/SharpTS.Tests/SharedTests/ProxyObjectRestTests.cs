using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ProxyObjectRestTests
{
    [Theory, ModeData]
    public void RestCopiesProxyKeysUsingDescriptorsAndGet(ExecutionMode mode)
    {
        Assert.Equal("{\"value\":9}\nkeys,descriptor:value,get:value\n", TestHarness.Run("""
            const events: string[] = [];
            const proxy: any = new Proxy({ value: 7, excluded: 2 }, {
                ownKeys(target: any) { events.push('keys'); return ['value', 'excluded']; },
                getOwnPropertyDescriptor(target: any, key: any) {
                    events.push('descriptor:' + key);
                    return Object.getOwnPropertyDescriptor(target, key);
                },
                get(target: any, key: any, receiver: any) {
                    events.push('get:' + key);
                    return key === 'value' ? 9 : Reflect.get(target, key, receiver);
                }
            });
            const { excluded, ...rest } = proxy;
            events.splice(0, 1);
            console.log(JSON.stringify(rest));
            console.log(events.join(','));
            """, mode));
    }
}
