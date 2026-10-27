using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ReflectSetPrimitiveReceiverTests
{
    [Theory, ModeData]
    public void DataPropertiesRejectPrimitiveReceivers(ExecutionMode mode)
    {
        const string source = """
            const target: any = { value: 1 };
            const receivers: any[] = [null, undefined, 7, true, "text", 1n, Symbol("receiver")];
            for (const receiver of receivers) {
                console.log(Reflect.set(target, "value", 2, receiver), target.value);
            }
            let observed: any;
            Object.defineProperty(target, "accessor", { set(value: any) { "use strict"; observed = this; } });
            console.log(Reflect.set(target, "accessor", 2, null), observed === null);
            console.log(Reflect.set(target, "accessor", 2, undefined), observed === undefined);
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("false 1\n", 7)) + "true true\ntrue true\n", TestHarness.Run(source, mode));
    }
}
