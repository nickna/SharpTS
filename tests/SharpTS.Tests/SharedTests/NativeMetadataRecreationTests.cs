using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NativeMetadataRecreationTests
{
    [Theory, ModeData]
    public void DeletedMetadataAssignmentsFollowInheritedDescriptors(ExecutionMode mode)
    {
        const string source = """
            const ctor: any = Map;
            function strictWrite(target: any, value: any) { "use strict"; target.name = value; }
            delete ctor.name;
            ctor.name = "ignored";
            console.log(Object.hasOwn(ctor, "name"));
            try { strictWrite(ctor, "ignored"); }
            catch (error: any) { console.log(error instanceof TypeError); }
            Object.defineProperty(Function.prototype, "name", { writable: true });
            ctor.name = "recreated";
            const descriptor: any = Object.getOwnPropertyDescriptor(ctor, "name");
            console.log(Object.hasOwn(ctor, "name"), ctor.name, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            delete ctor.name;
            let receiver: any;
            Object.defineProperty(Function.prototype, "name", { set(value: any) { receiver = this; console.log(value); }, configurable: true });
            strictWrite(ctor, "setter");
            console.log(receiver === ctor, Object.hasOwn(ctor, "name"));
            """;
        Assert.Equal("false\ntrue\ntrue recreated true true true\nsetter\ntrue false\n", TestHarness.Run(source, mode));
    }
}
