using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UsingHandlerDisposalTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["try", "const log:string[]=[];try{using resource={[Symbol.dispose](){log.push('dispose')}};log.push('body')}finally{log.push('finally')}console.log(log.join(','));", "body,dispose,finally\n"];
        yield return ["catch", "const log:string[]=[];try{throw 1}catch(e){using resource={[Symbol.dispose](){log.push('dispose')}};log.push('body')}console.log(log.join(','));", "body,dispose\n"];
        yield return ["finally", "const log:string[]=[];try{log.push('try')}finally{using resource={[Symbol.dispose](){log.push('dispose')}};log.push('body')}console.log(log.join(','));", "try,body,dispose\n"];
        yield return ["return", "const log:string[]=[];function run(){try{using resource={[Symbol.dispose](){log.push('dispose')}};return 7}finally{log.push('finally')}}console.log(run(),log.join(','));", "7 dispose,finally\n"];
        yield return ["throw", "const log:string[]=[];const marker={};try{using resource={[Symbol.dispose](){log.push('dispose')}};throw marker}catch(e){console.log(e===marker);log.push('catch')}finally{log.push('finally')}console.log(log.join(','));", "true\ndispose,catch,finally\n"];
        yield return ["catch-return", "const log:string[]=[];function run(){try{throw 1}catch(e){using resource={[Symbol.dispose](){log.push('dispose')}};return e}finally{log.push('finally')}}console.log(run(),log.join(','));", "1 dispose,finally\n"];
        yield return ["continue", "const log:string[]=[];for(let i=0;i<2;i++){try{using resource={[Symbol.dispose](){log.push('d'+i)}};log.push('b'+i);continue}finally{log.push('f'+i)}}console.log(log.join(','));", "b0,d0,f0,b1,d1,f1\n"];
        yield return ["nested", "const log:string[]=[];try{using outer={[Symbol.dispose](){log.push('outer')}};{using inner={[Symbol.dispose](){log.push('inner')}};}log.push('body')}finally{log.push('finally')}console.log(log.join(','));", "inner,body,outer,finally\n"];
    }

    public static IEnumerable<object[]> DualCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [.. row, mode];
    }

    [Theory, MemberData(nameof(DualCases))]
    public void DirectHandlerResourcesDisposeAtTheirListBoundary(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
