using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParameterDefaultCaptureCellTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["independent-calls-and-live-writes", """
            const later=99;
            function make(read:()=>number=()=>later,later:number=2){
                return [read,(value:number)=>later=value];
            }
            const first:any=make(),second:any=make(undefined,3);
            console.log(first[0](),second[0]());
            first[1](7);console.log(first[0](),second[0]());
            """, "2 3\n7 3\n"];
        yield return ["body-shadow-has-distinct-binding", """
            function make(read:()=>number=()=>later,later:number=2){
                {let later=9;const shadow=()=>later;console.log(shadow());}
                later++;return read;
            }
            console.log(make()());
            """, "9\n3\n"];
        yield return ["default-callback-parameter-shadows-owner-parameter", """
            const later=99;
            function run(read:(value:number)=>number=(later:number)=>later,later:number=2){return read(7);}
            console.log(run());
            """, "7\n"];
        yield return ["early-write-observes-parameter-tdz", """
            function invoke(read:()=>number):number{return read();}
            let bodies=0;
            function run(first:number=invoke(()=>{later=7;return later;}),later:number=2){bodies++;return later;}
            try{run();}catch(error:any){console.log(error.name,bodies);}
            console.log(run(0),bodies);
            """, "ReferenceError 0\n2 1\n"];
        yield return ["later-rest-parameter-shares-live-cell", """
            function run(read:()=>number=()=>rest.length,...rest:number[]){return read();}
            console.log(run(),run(undefined,1,2,3));
            """, "0 3\n"];
        yield return ["body-var-copy-does-not-replace-parameter-cell", """
            function run(read:()=>number=()=>later,later:number=2){var later=8;return [read(),later];}
            console.log(run().join(','));
            """, "2,8\n"];
        yield return ["parameter-property-uses-current-captured-binding", """
            class Parent{constructor(public value:number=2,read:()=>number=()=>value){value+=3;console.log(read());}}
            const Runtime:typeof Parent=Parent;class Child extends Runtime{}
            console.log(new Child().value);
            """, "5\n2\n"];
        yield return ["derived-property-retains-binding-across-super-shadow", """
            class Base{}
            class Parent extends Base{
                constructor(public value:number=2,read:()=>number=()=>value){
                    value+=3;{let value='shadow';super();console.log(value);}console.log(read());
                }
            }
            const Runtime:typeof Parent=Parent;class Child extends Runtime{}
            console.log(new Child().value);
            """, "shadow\n5\n5\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DefaultClosuresKeepActualParameterBindings(string name, string source, string expected, ExecutionMode mode)
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

    [Fact]
    public void CompiledEarlyReadAndWriteDoNotResolveAnOuterBinding()
    {
        const string source = """
            let later=99;let bodies=0;
            function invoke(read:()=>number):number{return read();}
            function read(first:number=invoke(()=>later),later:number=2){bodies++;return first;}
            function write(first:number=invoke(()=>{later=7;return later;}),later:number=2){bodies++;return first;}
            try{read();}catch(error:any){console.log(error.name);}
            try{write();}catch(error:any){console.log(error.name);}
            console.log(later,bodies);
            """;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("ReferenceError\nReferenceError\n99 0\n", output);
    }
}
