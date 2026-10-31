using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseSettledValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["own-noncallable-wins","let gets=0;class P extends (Promise as any){}const p:any=P.resolve(7);Object.defineProperty(P.prototype,\"then\",{get(){gets++;throw 99;},configurable:true});Object.defineProperty(p,\"then\",{value:7,configurable:true});Promise.resolve(p).then(v=>console.log(v===p,gets));","true 0\n"];
        yield return ["then-own-value","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});Promise.resolve(p).then(v=>console.log(v===p));","true\n"];
        yield return ["reaction-pass-through","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});Promise.resolve(p).then().then(v=>console.log(v===p));","true\n"];
        yield return ["reaction-return","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});Promise.resolve(1).then(()=>p).then(v=>console.log(v===p));","true\n"];
        yield return ["await-final-value","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});async function run(){console.log(await Promise.resolve(p)===p);}run();","true\n"];
        yield return ["catch-final-value","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});Promise.resolve(p).catch(()=>99).then(v=>console.log(v===p));","true\n"];
        yield return ["finally-final-value","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});Promise.resolve(p).finally(()=>{}).then(v=>console.log(v===p));","true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ObservesFinalFulfillmentWithoutRepeatingResolution(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
