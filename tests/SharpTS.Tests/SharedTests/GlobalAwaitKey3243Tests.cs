using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalAwaitKey3243Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["literal-key", "declare var __p2_async_write__:any;async function run(){globalThis['__p2_async_write__']=await Promise.resolve(7);console.log(globalThis['__p2_async_write__']);}run().catch(e=>console.log('error'));", "7\n"];
        yield return ["key-value-once", "declare var __p2_async_write__:any;const log:string[]=[];function key():'__p2_async_write__'{log.push('key');return '__p2_async_write__';}async function value(){log.push('value');return 8;}async function run(){globalThis[key()]=await value();console.log(globalThis['__p2_async_write__'],log.join(','));}run();", "8 key,value\n"];
        yield return ["generator-key", "declare var __p2_async_write__:any;function* run():Generator<number,void,number>{globalThis['__p2_async_write__']=yield 9;console.log(globalThis['__p2_async_write__']);}const it=run();console.log(it.next().value);it.next(8);", "9\n8\n"];
        yield return ["sync-control", "declare var __p2_async_write__:any;globalThis['__p2_async_write__']=7;console.log(globalThis['__p2_async_write__']);", "7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RetainsComputedGlobalReferenceAcrossSuspension(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
