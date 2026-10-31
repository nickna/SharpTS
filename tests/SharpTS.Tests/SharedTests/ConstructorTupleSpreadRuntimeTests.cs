using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ConstructorTupleSpreadRuntimeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["generic-elements-and-single-evaluation", """
            let reads=0;
            function args():readonly [number]{reads++;return [42] as const;}
            class Box<T>{constructor(public value:T){}}
            const explicit=new Box<number>(...args());
            const inferred=new Box(...[43] as const);
            console.log(explicit.value,inferred.value,reads);
            """, "42 43 1\n"];
        yield return ["fixed-slots-and-multiple-tuples", """
            class Pair{constructor(public value:number,public name:string){}}
            const pair=new Pair(...[1] as const,...['x'] as const);
            console.log(pair.value,pair.name);
            """, "1 x\n"];
        yield return ["optional-and-default-slots", """
            class Pair{constructor(public value:number,public name:string='default'){}}
            const args:readonly [number,string?]=[1];
            const pair=new Pair(...args);
            console.log(pair.value,pair.name);
            """, "1 default\n"];
        yield return ["empty-tuple-and-implicit-constructor", """
            class Empty{}
            console.log(new Empty(...[] as const) instanceof Empty);
            """, "true\n"];
        yield return ["tuple-rest-constructor", """
            class Pair{constructor(...args:[number,string]){console.log(args[0],args[1]);}}
            new Pair(...[1,'x'] as const);
            """, "1 x\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorsReceiveExpandedTupleValues(string name, string source, string expected, ExecutionMode mode)
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
