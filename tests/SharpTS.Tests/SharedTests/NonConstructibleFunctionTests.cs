using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NonConstructibleFunctionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "branch-original", """
            function attempt(fn:any){try{new fn();console.log("constructed");}catch(e:any){console.log("threw",e.name,e instanceof TypeError);}}attempt((x:number)=>x);attempt(async function(){});attempt(function*(){});
            """, "threw TypeError true\nthrew TypeError true\nthrew TypeError true\n" };
        yield return new object[] { "boolean-original", """
            function attempt(fn:any){try{new fn();return false;}catch(e){return e instanceof TypeError;}}console.log(attempt((x:number)=>x),attempt(async function(){}),attempt(function*(){}));
            """, "true true true\n" };
        yield return new object[] { "function-kinds", """
            function reject(fn:any){try{new fn();return false;}catch(e:any){return e.name==='TypeError'&&e instanceof TypeError;}}
            const arrow:any=()=>1;
            const asyncArrow:any=async()=>1;
            async function asyncFn(){}
            function* generator(){}
            async function* asyncGenerator(){}
            const functions:any[]=[arrow,asyncArrow,asyncFn,generator,asyncGenerator];
            for(const fn of functions){
                const bound:any=fn.bind({});
                const again:any=bound.bind({});
                console.log(reject(fn),reject(bound),reject(again));
            }
            function Value(this:any){this.value=7;}
            const C:any=Value;const first:any=C.bind({});const second:any=first.bind({});
            console.log(new C().value,new first().value,new second().value);
            """, "true true true\ntrue true true\ntrue true true\ntrue true true\ntrue true true\n7 7 7\n" };
        yield return new object[] { "reflect-controls", """
            function targetRejected(fn:any){try{Reflect.construct(fn,[]);return false;}catch(e){return e instanceof TypeError;}}
            function newTargetRejected(fn:any){try{Reflect.construct(function(){},[],fn);return false;}catch(e){return e instanceof TypeError;}}
            const functions:any[]=[()=>1,async function(){},function*(){},async function*(){}];
            for(const fn of functions){
                const bound:any=fn.bind({});
                console.log(targetRejected(fn),newTargetRejected(fn),targetRejected(bound),newTargetRejected(bound));
            }
            """, "true true true true\ntrue true true true\ntrue true true true\ntrue true true true\n" };
        yield return new object[] { "evaluation", """
            let effects=0;
            function argument(){effects++;return 1;}
            const token:any={failure:true};
            const arrow:any=()=>{throw token;};
            try{new arrow(argument());}catch(e){console.log(e instanceof TypeError,effects);}
            function Ordinary(this:any){this.value=2;}
            const C:any=Ordinary;console.log(new C().value,effects);
            """, "true 1\n2 1\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    public static IEnumerable<object[]> CompiledCases()
    {
        foreach (var row in Cases()) yield return row;
        yield return new object[] { "prototype-preservation", """
            function* generator(){}
            async function* asyncGenerator(){}
            async function asyncFn(){}
            const G:any=generator;const AG:any=asyncGenerator;const AF:any=asyncFn;
            const arrow:any=()=>1;
            console.log(G.prototype!==undefined,AG.prototype!==undefined,arrow.prototype===undefined,AF.prototype===undefined);
            """, "true true true true\n" };
    }

    [Fact]
    public void CompiledConstructionPolicyPreservesGeneratorPrototypes()
    {
        var row = CompiledCases().Last();
        Assert.Equal((string)row[2], TestHarness.Run((string)row[1], ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(ParityCases))]
    public void DynamicConstructionRejectsNonConstructorsWithGuestTypeError(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
