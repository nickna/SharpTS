using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ProxySymbolIndexedDeletionTests
{
    [Theory, ModeData]
    public void ComputedSymbolDeletesUseProxyTrapsAndStrictness(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol("direct"), target: any = {}, state = { calls: 0, valid: true };
            const handler = {
                deleteProperty(object: any, actualKey: any) {
                    state.calls++; state.valid = state.valid && this === handler && object === target && actualKey === key;
                    return Reflect.deleteProperty(object, actualKey);
                }
            };
            const proxy: any = new Proxy(target, handler);
            Object.defineProperty(target, key, { value: 3, configurable: true });
            console.log(delete proxy[key], target[key] === undefined, state.calls, state.valid);
            const refuses: any = new Proxy(target, { deleteProperty() { return false; } });
            console.log(delete refuses[key]);
            function strictDelete(object: any) { "use strict"; return delete object[key]; }
            try { strictDelete(refuses); }
            catch (error: any) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("true true 1 true\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }
}
