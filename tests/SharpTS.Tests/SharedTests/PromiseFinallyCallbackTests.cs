using SharpTS.Tests.Infrastructure;
using Xunit;
namespace SharpTS.Tests.SharedTests;
public sealed class PromiseFinallyCallbackTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-number","Promise.resolve(1).finally(()=>2).then(value=>console.log(value));","1\n"];
        yield return ["original-undefined","Promise.resolve(1).finally(()=>undefined).then(value=>console.log(value));","1\n"];
        yield return ["string","Promise.resolve(1).finally(()=>'ignored').then(value=>console.log(value));","1\n"];
        yield return ["object","Promise.resolve(1).finally(()=>({ignored:2})).then(value=>console.log(value));","1\n"];
        yield return ["boolean","Promise.resolve(1).finally(()=>true).then(value=>console.log(value));","1\n"];
        yield return ["null-callback","Promise.resolve(1).finally(null).then(value=>console.log(value));","1\n"];
        yield return ["undefined-callback","Promise.resolve(1).finally(undefined).then(value=>console.log(value));","1\n"];
        yield return ["omitted-callback","Promise.resolve(1).finally().then(value=>console.log(value));","1\n"];
        yield return ["promise-result-discarded","const result:Promise<number>=Promise.resolve(1).finally(()=>Promise.resolve('ignored'));result.then(value=>console.log(value));","1\n"];
        yield return ["promise-rejection-adopted","const marker={};Promise.resolve(1).finally(()=>Promise.reject(marker)).then(()=>console.log('unexpected'),reason=>console.log(reason===marker));","true\n"];
        yield return ["callback-throw-adopted","const marker={};Promise.resolve(1).finally(()=>{throw marker}).then(()=>console.log('unexpected'),reason=>console.log(reason===marker));","true\n"];
        yield return ["original-rejection-preserved","Promise.reject('original').finally(()=>2).catch(reason=>console.log(reason));","original\n"];
        yield return ["optional-parameter","Promise.resolve(1).finally((unused?:number)=>2).then(value=>console.log(value));","1\n"];
        yield return ["nullable-callback-domain","const cleanup:(()=>number)|null=()=>2;const result:Promise<number>=Promise.resolve(1).finally(cleanup);result.then(value=>console.log(value));","1\n"];
        yield return ["thenable-adopted","Promise.resolve(1).finally(()=>({then(resolve:(value:number)=>void){console.log('then');resolve(99)}})).then(value=>console.log(value));","then\n1\n"];
    }
    public static IEnumerable<object[]> Modes()=>Cases().SelectMany(row=>new[]{ExecutionMode.Interpreted,ExecutionMode.Compiled}.Select(mode=>new object[]{row[0],row[1],row[2],mode}));
    [Theory,MemberData(nameof(Modes))]
    public void CallbackResultsAreIgnoredWhileRejectionAdoptionIsRetained(string name,string source,string expected,ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected,TestHarness.Run(source,mode));
    }
}
