using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("TimerTests")]
public class TimerLexicalShadowRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function run(){const setTimeout=()=>7;const clearImmediate=()=>9;console.log(setTimeout(),clearImmediate());}run();", "7 9\n", "original"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function run(){const setTimeout=()=>1;const clearTimeout=()=>2;const setInterval=()=>3;const clearInterval=()=>4;console.log(setTimeout(),clearTimeout(),setInterval(),clearInterval());}run();", "1 2 3 4\n", "four-local-bindings"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function run(setTimeout:()=>number){console.log(setTimeout());}run(()=>7);", "7\n", "parameter"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function run(setTimeout:any){setTimeout=()=>8;console.log(setTimeout());}run(()=>7);", "8\n", "overwritten-parameter"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function outer(){const setTimeout=()=>9;function run(){return setTimeout();}console.log(run());}outer();", "9\n", "captured"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"class C{run(){const setInterval=()=>10;console.log(setInterval());}}new C().run();", "10\n", "method-local"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"async function run(setTimeout:()=>number){await 0;console.log(setTimeout());}run(()=>11);", "11\n", "async-parameter"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function*run(){const setInterval=()=>12;yield 0;console.log(setInterval());}const g=run();g.next();g.next();", "12\n", "generator-capture"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function run(){const queueMicrotask=()=>13;console.log(queueMicrotask());}run();", "13\n", "microtask-shadow"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const h=setTimeout(()=>console.log(""bad""),10000);clearTimeout(h);queueMicrotask(()=>console.log(""micro""));setTimeout(()=>console.log(42),0);", "micro\n42\n", "unshadowed-timer"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DirectCallsHonorActualLexicalBindings(ExecutionMode mode, string source, string expected, string name)
    {
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ImportedSourceTimerNameRetainsItsBinding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = "export const setTimeout=()=>14;",
            ["main.ts"] = "import {setTimeout} from './lib';console.log(setTimeout());"
        };
        Assert.Equal("14\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}