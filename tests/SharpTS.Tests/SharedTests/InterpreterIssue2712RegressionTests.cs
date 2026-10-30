using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2712RegressionTests
{
    [Theory, ModeData]
    public void OriginalIssueSourceRemainsCorrect(ExecutionMode mode)
    {
        const string source = "class C {}\nconst prototype: any = C.prototype;\nconst proxy: any = new Proxy(prototype, {});\nprototype.extra = 7;\nconsole.log(\"reflect\", Reflect.deleteProperty(proxy, \"extra\"), Object.hasOwn(prototype, \"extra\"), prototype.extra);\nprototype.extra = 8;\nconsole.log(\"syntax\", delete proxy.extra, Object.hasOwn(prototype, \"extra\"), prototype.extra);\nprototype.extra = 9;\nconsole.log(\"direct\", delete prototype.extra, Object.hasOwn(prototype, \"extra\"), prototype.extra);\nObject.defineProperty(prototype, \"fixed\", { value: 11, configurable: false });\nconsole.log(\"fixed\", Reflect.deleteProperty(proxy, \"fixed\"), Object.hasOwn(prototype, \"fixed\"), prototype.fixed);\nconst record: any = { extra: 12 };\nconst recordProxy: any = new Proxy(record, {});\nconsole.log(\"record\", Reflect.deleteProperty(recordProxy, \"extra\"), Object.hasOwn(record, \"extra\"), record.extra);\n";
        Assert.Equal("reflect true false undefined\nsyntax true false undefined\ndirect true false undefined\nfixed false true 11\nrecord true false undefined\n", TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["strict-nested-and-traps", "class C{}const prototype:any=C.prototype;Object.defineProperty(prototype,\"fixed\",{value:7,configurable:false});const proxy:any=new Proxy(new Proxy(prototype,{}),{});console.log(Reflect.deleteProperty(proxy,\"missing\"),Reflect.deleteProperty(proxy,\"fixed\"));function remove(target:any){\"use strict\";try{delete target.fixed;console.log(\"accepted\");}catch(error){console.log(error instanceof TypeError);}}remove(proxy);prototype.extra=8;let calls=0;const trapped:any=new Proxy(prototype,{deleteProperty(target:any,key:any){calls++;return false;}});console.log(Reflect.deleteProperty(trapped,\"extra\"),prototype.extra,calls);", "true false\ntrue\nfalse 8 1\n"];
        yield return ["symbol-control", "class C{}const prototype:any=C.prototype;const symbol=Symbol(\"key\");prototype[symbol]=7;const proxy:any=new Proxy(prototype,{});console.log(Reflect.deleteProperty(proxy,symbol),Object.hasOwn(prototype,symbol));Object.defineProperty(prototype,symbol,{value:8,configurable:false});console.log(Reflect.deleteProperty(proxy,symbol),prototype[symbol]);", "true false\nfalse 8\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
