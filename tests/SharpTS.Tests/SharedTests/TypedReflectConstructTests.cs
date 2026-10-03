using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedReflectConstructTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "intrinsic-controls", """
            for (const target of [Function.prototype.call, Function.prototype.apply, Function.prototype.bind]) {
                try { Reflect.construct(target, []); console.log(false); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            """, "true\ntrue\ntrue\n" };
        yield return new object[] { "original", """
            class Point{x:number;constructor(x:number){this.x=x;}}const p:any=Reflect.construct(Point,[7]);console.log(p.x,p instanceof Point);for(const value of [null,undefined,{},Function.prototype.call]){try{Reflect.construct(value as any,[]);console.log(false);}catch(error){console.log(error instanceof TypeError);}}
            """, "7 true\ntrue\ntrue\ntrue\ntrue\n" };
        yield return new object[] { "dynamic-control", """
            class Point{x:number;constructor(x:number){this.x=x;}}const p:any=Reflect.construct(Point as any,[7]);console.log(p.x,p instanceof Point);for(const value of [null,undefined,{},Function.prototype.call]){try{Reflect.construct(value as any,[]);console.log(false);}catch(error){console.log(error instanceof TypeError);}}
            
            """, "7 true\ntrue\ntrue\ntrue\ntrue\n" };
        yield return new object[] { "function-type-control", """
            class Point{x:number;constructor(x:number){this.x=x;}}
            function identity<T extends Function>(constructor:T):T{return constructor;}
            const broad:Function=Point;
            const signature:new (x:number)=>Point=Point;
            const a:any=Reflect.construct(identity(Point),[9]);
            const b:any=Reflect.construct(broad,[11]);
            const c:any=Reflect.construct(signature,[13]);
            console.log(a.x,a instanceof Point,b.x,b instanceof Point,c.x,c instanceof Point);
            """, "9 true 11 true 13 true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void TypedConstructorValuesRetainIdentityAndInvalidTargetErrors(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
