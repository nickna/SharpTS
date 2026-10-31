using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TimerLexicalBinding3237Tests
{
    [Theory]
    [InlineData("function run(setTimeout:any){setTimeout=()=>8;console.log(setTimeout());}run(()=>7);", true)]
    [InlineData("function run(){const queueMicrotask=()=>13;console.log(queueMicrotask());}run();", true)]
    [InlineData("function run(clearTimeout:any){clearTimeout=(value:number)=>value+1;const result:number=clearTimeout(6);}run(()=>0);", true)]
    [InlineData("function run(setInterval:any){setInterval=()=>9;const value:number=setInterval();}run(()=>0);", true)]
    [InlineData("function run(clearInterval:any){clearInterval=()=>9;const value:number=clearInterval();}run(()=>0);", true)]
    [InlineData("setTimeout();", false)]
    [InlineData("setInterval(7);", false)]
    [InlineData("queueMicrotask();", false)]
    [InlineData("const queueMicrotask=(value:number)=>value;queueMicrotask('bad');", false)]
    public void ChecksTheResolvedBindingBeforeUsingAnIntrinsicSignature(string source, bool valid)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        if (valid) Assert.Empty(diagnostics);
        else Assert.NotEmpty(diagnostics);
    }
}
