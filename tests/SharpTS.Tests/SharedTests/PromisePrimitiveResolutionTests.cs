using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromisePrimitiveResolutionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["undefined","Promise.resolve(1).finally(()=>undefined).then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["null","Promise.resolve(1).finally(()=>null).then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["number","Promise.resolve(1).finally(()=>7).then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["string","Promise.resolve(1).finally(()=>\"text\").then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["boolean","Promise.resolve(1).finally(()=>false).then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["bigint","Promise.resolve(1).finally(()=>1n).then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["symbol","Promise.resolve(1).finally(()=>Symbol(\"value\")).then(value=>console.log(value),error=>console.log(\"error\",error.name));","1\n"];
        yield return ["undefined-chain","Promise.resolve().then(value=>console.log(value===undefined)).then(()=>console.log(\"continued\"));","true\ncontinued\n"];
        yield return ["number-prototype","let gets=0;Object.defineProperty(Number.prototype,\"then\",{get(){gets++;throw 99;},configurable:true});Promise.resolve(1).finally(()=>2).then(value=>console.log(value,gets),()=>console.log(\"wrong\"));","1 0\n"];
        yield return ["string-prototype","let gets=0;Object.defineProperty(String.prototype,\"then\",{get(){gets++;throw 99;},configurable:true});Promise.resolve(\"first\").finally(()=>\"next\").then(value=>console.log(value,gets),()=>console.log(\"wrong\"));","first 0\n"];
        yield return ["boxed-object","let gets=0;const value:any=Object(7);Object.defineProperty(value,\"then\",{get(){gets++;return function(resolve:any){resolve(9);};}});Promise.resolve(1).finally(()=>value).then(value=>console.log(value,gets),()=>console.log(\"wrong\"));","1 1\n"];
        yield return ["callable-object","let gets=0;const value:any=()=>{};Object.defineProperty(value,\"then\",{get(){gets++;return function(resolve:any){resolve(9);};}});Promise.resolve(1).finally(()=>value).then(value=>console.log(value,gets),()=>console.log(\"wrong\"));","1 1\n"];
        yield return ["explicit-undefined-chain","Promise.resolve(undefined).then(value=>console.log(value===undefined)).then(()=>console.log(\"continued\"));","true\ncontinued\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OnlyObjectsRequireThenLookup(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
