using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class StrictInheritedProxySymbolSetTests
{
    [Theory, ModeData]
    public void NonextensibleReceiversStillEnterInheritedProxySetTraps(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const fn: any = values, key = Symbol("key"), events: string[] = [];
            Object.setPrototypeOf(Object.getPrototypeOf(fn), new Proxy({}, {
                set(target: any, property: any, value: any, receiver: any) {
                    events.push("set");
                    console.log(property === key, value === 7, receiver === fn);
                    return true;
                }
            }));
            Object.preventExtensions(fn);
            function assignStrict() { "use strict"; fn[key] = 7; }
            assignStrict();
            console.log(events.join(","), Object.hasOwn(fn, key));
            """;
        Assert.Equal("true true true\nset false\n", TestHarness.Run(source, mode));
    }
}
