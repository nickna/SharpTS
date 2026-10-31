using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class HoistedBlockFunctionBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-readonly", "const run=async()=>{const n=100;{const n=3;function f(){return n}await Promise.resolve(0);console.log(f(),n)}console.log(n)};run();", "3 3\n100\n"];
        yield return ["original-write", "const run=async()=>{let n=100;{let n=3;function f(){n++;return n}await Promise.resolve(0);console.log(f(),n)}console.log(n)};run();", "4 4\n100\n"];
        yield return ["parameter-shadow", "const run=async(n:number)=>{{function f(){return n}const n=3;await Promise.resolve(0);console.log(f(),n)}console.log(n)};run(100);", "3 3\n100\n"];
        yield return ["sibling-blocks", "const run=async()=>{const n=100;{function f(){return n}const n=3;await Promise.resolve(0);console.log(f(),n)}{function g(){return n}const n=7;await Promise.resolve(0);console.log(g(),n)}console.log(n)};run();", "3 3\n7 7\n100\n"];
        yield return ["nested-blocks", "const run=async()=>{const n=100;{const n=7;{function f(){return n}const n=3;await Promise.resolve(0);console.log(f(),n)}console.log(n)}console.log(n)};run();", "3 3\n7\n100\n"];
        yield return ["escaped-function", "const run=async()=>{const n=100,values:any[]=[];{function f(){return n}const n=3;values.push(f)}await Promise.resolve(0);console.log(values[0](),n)};run();", "3 100\n"];
        yield return ["outer-closure-keeps-owner", "const run=async()=>{const n=100,outer=()=>n;{function f(){return n}const n=3;await Promise.resolve(0);console.log(outer(),f(),n)}console.log(n)};run();", "100 3 3\n100\n"];
        yield return ["var-same-binding-control", "const run=async(n:number)=>{{function f(){return n}var n=3;await Promise.resolve(0);console.log(f(),n)}console.log(n)};run(100);", "3 3\n3\n"];
        yield return ["switch-existing-predeclare-control", "const run=async()=>{const n=100;switch(1){case 1:function f(){return n}const n=3;await Promise.resolve(0);console.log(f(),n);break}console.log(n)};run();", "3 3\n100\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void HoistedFunctionsResolveTheBlockBinding(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
