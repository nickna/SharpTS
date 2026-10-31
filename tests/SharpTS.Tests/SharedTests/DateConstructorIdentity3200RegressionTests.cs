using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DateConstructorIdentity3200RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-identity", "console.log(Date.prototype.constructor===Date);", "true\n"];
        yield return ["instance-and-prototype", "const value=new Date(0);console.log(Date.prototype.constructor===Date,value.constructor===Date);", "true true\n"];
        yield return ["mutation-through-prototype-constructor", "const P:any=Date.prototype;P.constructor.now=()=>7;console.log(Date.now());", "7\n"];
        yield return ["own-constructor-descriptor", "const d:any=Object.getOwnPropertyDescriptor(Date.prototype,\"constructor\");console.log(d.value===Date,d.writable,d.enumerable,d.configurable);", "true true false true\n"];
        yield return ["captured-constructor-new", "const C:any=Date.prototype.constructor;const value=new C(0);console.log(value.getTime(),C===Date);", "0 true\n"];
        yield return ["inherited-getter-receiver", "const P:any=Date.prototype;let receiver:any;Object.defineProperty(P,\"constructor\",{get(){receiver=this;return Date;},configurable:true});const value=new Date(0);console.log(value.constructor===Date,receiver===value);", "true true\n"];
        yield return ["runtime-any-instance-and-prototype", "const value:any=new Date(0);console.log(Date.prototype.constructor===Date,value.constructor===Date);", "true true\n"];
        yield return ["runtime-any-inherited-getter-receiver", "const P:any=Date.prototype;let receiver:any;Object.defineProperty(P,\"constructor\",{get(){receiver=this;return Date;},configurable:true});const value:any=new Date(0);console.log(value.constructor===Date,receiver===value);", "true true\n"];
        yield return ["operator-override-and-delete-companion", "const P:any=Date.prototype,replacement={};let reads=0;Object.defineProperty(P,\"constructor\",{get(){reads++;return replacement;},configurable:true});const value=new Date(0);console.log(value.constructor===replacement,reads);console.log(delete P.constructor,Object.hasOwn(P,\"constructor\"),P.constructor===Object);", "true 1\ntrue false true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ActualOwnConstructorUsesCanonicalDate(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}