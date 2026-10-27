using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class StrictInheritedConstructorSymbolSetterTests
{
    [Theory, ModeData]
    public void NonextensibleConstructorsCanInvokeInheritedSymbolSetters(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol("setter"), state = { calls: 0, receiver: false };
            class Base {}
            class Child extends Base { static stored = 0; }
            Object.defineProperty(Base, key, {
                set: function(this: any, value: number) {
                    state.calls++; state.receiver = this === Child; this.stored = value;
                }, configurable: true
            });
            Object.preventExtensions(Child);
            function write(target: any) { "use strict"; target[key] = 7; }
            write(Child);
            console.log(state.calls, state.receiver, Child.stored, Object.hasOwn(Child, key));
            """;
        Assert.Equal("1 true 7 false\n", TestHarness.Run(source, mode));
    }
}
