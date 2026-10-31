using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class GlobalComputedKey3245Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["key-coercion-after-rhs", "declare var __p2_async_write__:any;const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '__p2_async_write__';}};async function value(){log.push('value');return 8;}async function run(){globalThis[key as '__p2_async_write__']=await value();console.log(globalThis['__p2_async_write__'],log.join(','));}run();", "8 value,string\n"];
        yield return ["abrupt-key-after-rhs", "declare var __p2_async_write__:any;const log:string[]=[];const marker={};const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);throw marker;}};async function value(){log.push('value');return 8;}async function run(){try{globalThis[key as '__p2_async_write__']=await value();}catch(e){console.log(e===marker,log.join(','));}}run();", "true value,string\n"];
        yield return ["sync-original-3245", "declare var __p2_sync_key__:any;const log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return '__p2_sync_key__';}};function value(){log.push('value');return 7;}globalThis[key as '__p2_sync_key__']=value();console.log(globalThis['__p2_sync_key__'],log.join(','));", "7 value,string\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ConvertsActualGlobalKeyAfterRhsOnce(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
