using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassOwnKeyOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "class_fields", """
            class Box{value:number;constructor(value:number){this.value=value;}read(){return this.value;}}const b:any=new Box(2);b["value"]=7;b["extra"]=9;console.log(b.value,b.read(),b.extra,"value" in b,"missing" in b);console.log(Object.keys(b).join(","));
            """, "7 7 9 true false\nvalue,extra\n" };
        yield return new object[] { "class_expression", """
            const Box=class{value:number=2;read(){return this.value;}};const b:any=new Box();b["value"]=6;console.log(b.value,b.read(),"value" in b,"absent" in b,Object.keys(b).join(","));
            """, "6 6 true false value\n" };
        yield return new object[] { "inherited_fields", """
            class Base{a:number=2;}class Child extends Base{b:number=3;}const c:any=new Child();c["a"]=5;c["b"]=7;console.log(c.a,c.b,"a" in c,"b" in c,Object.keys(c).join(","));
            """, "5 7 true true a,b\n" };
        yield return new object[] { "generic_class_fields", """
            class Box<T>{value:T;constructor(value:T){this.value=value;}get(){return this.value;}}const a:any=new Box<number>(3);const b:any=new Box<string>("text");a["value"]=5;b["value"]="next";console.log(a.value,a.get(),b.value,b.get(),"value" in a);
            """, "5 5 next next true\n" };
        yield return new object[] { "multiple-fields-and-values", """
            class Box{z:number=1;a:number=2;}const b:any=new Box();b.extra=3;b.later=4;b.z=5;console.log(Object.keys(b).join(","));console.log(Object.values(b).join(","));console.log(Object.entries(b).map(p=>p.join(":")).join(","));console.log(Object.getOwnPropertyNames(b).join(","));console.log(Reflect.ownKeys(b).join(","));
            """, "z,a,extra,later\n5,2,3,4\nz:5,a:2,extra:3,later:4\nz,a,extra,later\nz,a,extra,later\n" };
        yield return new object[] { "expression-extra", """
            const Box=class{value:number=2;other:string="text";};const b:any=new Box();b.extra=9;b.value=7;console.log(Object.keys(b).join(","),Object.values(b).join(","));
            """, "value,other,extra 7,text,9\n" };
        yield return new object[] { "inherited-extra", """
            class Base{a:number=2;}class Child extends Base{b:number=3;}const c:any=new Child();c.a=5;c.extra=9;c.b=7;console.log(Object.keys(c).join(","),Object.values(c).join(","));
            """, "a,b,extra 5,7,9\n" };
        yield return new object[] { "numeric-indices", """
            class Box{z:number=1;a:number=2;}const b:any=new Box();b["10"]=10;b.extra=3;b["2"]=2;b["01"]=1;console.log(Object.keys(b).join(","));console.log(Object.getOwnPropertyNames(b).join(","));
            """, "2,10,z,a,extra,01\n2,10,z,a,extra,01\n" };
        yield return new object[] { "symbols-and-hidden-fields", """
            class Box{value:number=2;}const b:any=new Box();const s=Symbol("s");b[s]=8;b.extra=9;Object.defineProperty(b,"value",{enumerable:false});console.log(Object.keys(b).join(","));console.log(Object.getOwnPropertyNames(b).join(","));const keys=Reflect.ownKeys(b);console.log(keys.length,keys[0],keys[1],keys[2]===s);
            """, "extra\nvalue,extra\n3 value extra true\n" };
        yield return new object[] { "generic-extra", """
            class Box<T>{value:T;constructor(value:T){this.value=value;}}const b:any=new Box<string>("text");b.extra=9;b.value="next";console.log(Object.keys(b).join(","),Object.values(b).join(","));
            """, "value,extra next,9\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void DeclaredClassFieldsPrecedeLaterDynamicProperties(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
