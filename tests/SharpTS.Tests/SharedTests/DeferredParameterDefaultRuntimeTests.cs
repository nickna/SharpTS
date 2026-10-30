using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DeferredParameterDefaultRuntimeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["later-parameter-and-outer-shadow", """
            const later='outer';
            class Example{constructor(public read:()=>number=()=>later,later:number=2){later++;}}
            console.log(new Example().read());
            """, "3\n"];
        yield return ["later-object-and-array-binding", """
            class ObjectCase{constructor(public read:()=>number=()=>later,{later}:{later:number}={later:2}){}}
            class ArrayCase{constructor(public read:()=>number=()=>later,[later]:[number]=[3]){}}
            console.log(new ObjectCase().read(),new ArrayCase().read());
            """, "2 3\n"];
        yield return ["ordinary-callable-forms", """
            function declaration(read:()=>number=()=>later,later:number=2){return read();}
            const arrow=(read:()=>number=()=>later,later:number=3)=>read();
            const expression=function(read:()=>number=()=>later,later:number=4){return read();};
            class Owner{method(read:()=>number=()=>later,later:number=5){return read();}}
            console.log(declaration(),arrow(),expression(),new Owner().method());
            """, "2 3 4 5\n"];
        yield return ["indirect-early-invocation-observes-tdz", """
            function invoke(read:()=>number):number{return read();}
            class Early{constructor(public value:number=invoke(()=>later),later:number=2){}}
            try{new Early();}catch(error:any){console.log(error.name);}
            """, "ReferenceError\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DefaultsCaptureTheParameterEnvironment(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
