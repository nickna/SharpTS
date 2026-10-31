using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("TimerTests")]
public class TimerFunctionValueRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const timer:any=setTimeout;console.log(typeof timer);", "function\n", "original"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const start:any=setTimeout;start((a:any,b:any)=>console.log(a,b),0,7,""ok"");", "7 ok\n", "timeout-arguments"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const start:any=setTimeout;start(()=>console.log(""fired""));", "fired\n", "timeout-missing-delay"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const start:any=setTimeout;const cancel:any=clearTimeout;const h=start(()=>console.log(""bad""),10000);cancel(h);console.log(""done"");", "done\n", "cancel-timeout"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const start:any=setInterval;const cancel:any=clearInterval;const h=start((x:any)=>{console.log(x);cancel(h);},1,9);", "9\n", "interval-arguments"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function setTimeout(){return 7;}const timer:any=setTimeout;console.log(timer());", "7\n", "function-shadow"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function run(setTimeout:()=>number){const timer:any=setTimeout;console.log(timer());}run(()=>8);", "8\n", "parameter-shadow"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"async function run(){await 0;const timer:any=setTimeout;timer(()=>console.log(""async""),0);}run();", "async\n", "async-value"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"function*run(){yield 1;const timer:any=setTimeout;timer(()=>console.log(""generator""),0);}const g=run();g.next();g.next();", "generator\n", "generator-value"];
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [mode, @"const g:any=globalThis;const original=setTimeout;g.setTimeout=()=>7;const value:any=setTimeout;console.log(value===g.setTimeout,value());g.setTimeout=original;", "true 7\n", "replacement-read"];
        foreach (var mode in new[] { ExecutionMode.Compiled })
            yield return [mode, @"const g:any=globalThis;console.log(setTimeout===g.setTimeout,clearTimeout===g.clearTimeout);const d:any=Object.getOwnPropertyDescriptor(g,""setTimeout"");console.log(d.value===setTimeout,d.writable,d.enumerable,d.configurable);", "true true\ntrue true true true\n", "global-four-identity"];
    }

    [Theory]
    [CompiledOnlyData]
    public void QualifiedImmediateValuesInvokeAndCancel(ExecutionMode mode)
    {
        // Bare-name checker/interpreter registration is the separate #3233 repair.
        const string source = """
            const g:any=globalThis;
            const start:any=g.setImmediate;
            const cancel:any=g.clearImmediate;
            const removed=start(()=>console.log('bad'));
            cancel(removed);
            start((a:any,b:any)=>console.log(a,b),3,'now');
            """;
        Assert.Equal("3 now\n", TestHarness.Run(source, mode));
    }
    [Theory]
    [CompiledOnlyData]
    public void RealGlobalDescriptorPreservesDeletionAndRecreation(ExecutionMode mode)
    {
        const string source = """
            const g:any=globalThis;
            const original=setTimeout;
            console.log(delete g.setTimeout,g.setTimeout===undefined,Object.getOwnPropertyDescriptor(g,'setTimeout')===undefined);
            g.setTimeout=original;
            const value:any=setTimeout;
            console.log(value===original);
            value(()=>console.log('restored'),0);
            """;
        Assert.Equal("true true true\ntrue\nrestored\n", TestHarness.Run(source, mode));
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ActualGlobalTimerValuesPreserveInvocationAndBindingState(ExecutionMode mode, string source, string expected, string name)
    {
        Assert.False(string.IsNullOrEmpty(name));
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}