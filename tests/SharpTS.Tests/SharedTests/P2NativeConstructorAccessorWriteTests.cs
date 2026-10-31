using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NativeConstructorAccessorWriteTests
{
    [Theory, ModeData]
    public void GetterOnlyOwnNativeConstructorPropertiesRejectStrictWrites(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = Map;
            let reads = 0;
            Object.defineProperty(ctor, "name", { get() { reads++; return "getter"; }, configurable: true });
            ctor.name = "ignored";
            function write(target: any) { "use strict"; target.name = "changed"; }
            try { write(ctor); console.log("accepted"); }
            catch (error: any) { console.log(error instanceof TypeError); }
            console.log(reads, ctor.name, reads);
            console.log(Object.getOwnPropertyDescriptor(ctor, "name")!.set === undefined);
            let received: any;
            Object.defineProperty(ctor, "name", { set(value: any) { received = value; }, configurable: true });
            write(ctor);
            console.log(received, ctor.name);
            """;
        Assert.Equal("true\n0 getter 1\ntrue\nchanged getter\n", TestHarness.Run(source, mode));
    }
}
