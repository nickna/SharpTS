using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3BuiltInConstructorPrototypeChainTests
{
    [Fact]
    public void DeletedNativeNameWritesFollowCustomPrototypeReadonlyDescriptors()
    {
        const string source = """
            const ctor: any = Map;
            delete ctor.name;
            const functions: any = Function.prototype;
            delete functions.name;
            const inherited: any = {};
            Object.defineProperty(inherited, "name", { value: "blocked", writable: false });
            const nearer: any = Object.create(inherited);
            Object.setPrototypeOf(functions, nearer);
            ctor.name = "ignored";
            console.log(Object.hasOwn(ctor, "name"), ctor.name);
            function strictWrite(target: any) { "use strict"; target.name = "ignored"; }
            try { strictWrite(ctor); console.log("accepted"); }
            catch (error: any) { console.log(error instanceof TypeError); }
            console.log(Object.hasOwn(ctor, "name"), ctor.name);
            Object.defineProperty(nearer, "name", { value: "writable", writable: true });
            ctor.name = "created";
            console.log(ctor.name, Object.getOwnPropertyDescriptor(ctor, "name")!.enumerable, inherited.name);
            """;
        Assert.Equal("false blocked\ntrue\nfalse blocked\ncreated true blocked\n",
            TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void DeletedNativeNameWritesCallCustomPrototypeSetterWithConstructorReceiver()
    {
        const string source = """
            const ctor: any = Map;
            delete ctor.name;
            const functions: any = Function.prototype;
            delete functions.name;
            const inherited: any = {};
            let calls = 0;
            let reads = 0;
            let receiver: any;
            let assigned: any;
            Object.defineProperty(inherited, "name", {
                get() { reads++; return "inherited"; },
                set(value: any) { calls++; receiver = this; assigned = value; },
                configurable: true
            });
            Object.setPrototypeOf(functions, Object.create(inherited));
            ctor.name = "changed";
            console.log(calls, receiver === ctor, assigned, Object.hasOwn(ctor, "name"), reads);
            console.log(ctor.name, reads);
            """;
        Assert.Equal("1 true changed false 0\ninherited 1\n", TestHarness.RunInterpreted(source));
    }
}
