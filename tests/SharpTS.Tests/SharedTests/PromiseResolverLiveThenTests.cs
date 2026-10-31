using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseResolverLiveThenTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original","let gets=0,calls=0;const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{get(){gets++;return function(resolve:any){calls++;resolve(9);};}});const q=new Promise(resolve=>resolve(p));console.log(\"sync\",gets,calls);q.then(value=>console.log(\"value\",value,gets,calls));","sync 1 0\nvalue 9 1 1\n"];
        yield return ["getter-abrupt","const marker={};let gets=0;const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{get(){gets++;throw marker;}});const q=new Promise(resolve=>resolve(p));console.log(\"returned\",gets);q.then(()=>console.log(\"wrong\"),error=>console.log(error===marker,gets));","returned 1\ntrue 1\n"];
        yield return ["noncallable-value","const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{value:7});new Promise(resolve=>resolve(p)).then(value=>console.log(value===p));","true\n"];
        yield return ["first-call-wins","let gets=0,calls=0;const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{get(){gets++;return function(resolve:any,reject:any){calls++;resolve(9);reject(99);resolve(10);throw 99;};}});new Promise(resolve=>{resolve(p);resolve(11);throw 99;}).then(value=>console.log(value,gets,calls),()=>console.log(\"wrong\"));","9 1 1\n"];
        yield return ["self-resolution","let resolve:any;const q=new Promise(r=>{resolve=r;});resolve(q);q.catch(error=>console.log(error instanceof TypeError));","true\n"];
        yield return ["fifo","const events:string[]=[];let gets=0,calls=0;const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{get(){gets++;events.push(\"get\");return function(resolve:any){calls++;events.push(\"then\");resolve(9);};}});const q=new Promise(resolve=>resolve(p));events.push(\"after\");queueMicrotask(()=>events.push(\"job\"));q.then(value=>console.log(value,gets,calls,events.join(\",\")));","9 1 1 get,after,then,job\n"];
        yield return ["native-value","new Promise(resolve=>resolve(Promise.resolve(7))).then(value=>console.log(value));","7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ResolvingFunctionsObserveLiveThenOnNativePromiseValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
