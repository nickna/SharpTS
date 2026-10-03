using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedReflectApplyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            function sum(a:number,b:number){return a+b;}const proxy:any=new Proxy(sum,{apply(t:any,r:any,args:any[]){return (Reflect.apply(t,r,args) as number)+1;}});console.log(typeof proxy,proxy(2,3));
            """, "function 6\n" };
        yield return new object[] { "direct-control", """
            const proxy:any=new Proxy(function(a:number,b:number){return a+b;},{apply(t:any,r:any,args:any[]){return args[0]+args[1]+1;}});console.log(typeof proxy,proxy(2,3));
            """, "function 6\n" };
        yield return new object[] { "typed-and-any-targets", """
            function sum(a:number,b:number){return a+b;}
            const args:[number,number]=[2,3];
            const typed:number=Reflect.apply(sum,undefined,args);
            const dynamic:any=sum;
            const asserted:number=Reflect.apply(dynamic,undefined,args) as number;
            console.log(typed,asserted);
            """, "5 5\n" };
        yield return new object[] { "readonly-and-explicit", """
            function label(a:number,b:number):string{return 'value'+(a+b);}
            const args:readonly [number,number]=[2,3];
            const inferred:string=Reflect.apply(label,undefined,args);
            const explicit:string=Reflect.apply<undefined,[number,number],string>(label,undefined,args);
            console.log(inferred,explicit);
            """, "value5 value5\n" };
        yield return new object[] { "optional-and-rest", """
            function optional(a:number,b?:number):string{return 'value'+(a+(b??0));}
            function rest(a:number,...values:number[]):string{return 'count'+values.length;}
            const one:[number]=[2];
            const many:[number,number,number]=[2,3,4];
            const first:string=Reflect.apply(optional,undefined,one);
            const second:string=Reflect.apply(rest,undefined,many);
            console.log(first,second);
            """, "value2 count2\n" };
        yield return new object[] { "tuple-rest-target", """
            function label(...pair:[number,number]):string{return 'value'+(pair[0]+pair[1]);}
            const args:[number,number]=[2,3];
            const result:string=Reflect.apply(label,undefined,args);
            console.log(result);
            """, "value5\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ReflectApplyTargetsRetainInvocationAndReturnValues(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
