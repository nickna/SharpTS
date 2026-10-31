using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DateReflectDelete3217RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-reported", "const value:any=new Date(0);value.marker=7;console.log(Reflect.deleteProperty(value,\"marker\"),Object.hasOwn(value,\"marker\"),value.marker===undefined);", "true false true\n"];
        yield return ["nonconfigurable-no-getter-read", "const value:any=new Date(0);let calls=0;Object.defineProperty(value,\"marker\",{get(){calls++;return 7;},configurable:false});console.log(Reflect.deleteProperty(value,\"marker\"),calls,Object.hasOwn(value,\"marker\"));", "false 0 true\n"];
        yield return ["missing-and-repeat", "const value:any=new Date(0);value.marker=7;console.log(Reflect.deleteProperty(value,\"marker\"),Reflect.deleteProperty(value,\"marker\"),Reflect.deleteProperty(value,\"missing\"));", "true true true\n"];
        yield return ["inherited-property-retained", "const value:any=new Date(0);console.log(Reflect.deleteProperty(value,\"getTime\"),typeof value.getTime,value.getTime());", "true function 0\n"];
        yield return ["own-method-shadow", "const value:any=new Date(0);value.getTime=()=>7;console.log(Reflect.deleteProperty(value,\"getTime\"),value.getTime(),Object.hasOwn(value,\"getTime\"));", "true 0 false\n"];
        yield return ["key-conversion-once", "const value:any=new Date(0);value.marker=7;let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return \"marker\";}};console.log(Reflect.deleteProperty(value,key),calls,Object.hasOwn(value,\"marker\"));", "string\ntrue 1 false\n"];
        yield return ["source-name-control", "const value:any=new(class Date{})();value.marker=7;console.log(Reflect.deleteProperty(value,\"marker\"),Object.hasOwn(value,\"marker\"));", "true false\n"];
        yield return ["original-3200-reflect-control", "const P:any=Date.prototype,replacement={};let reads=0;Object.defineProperty(P,\"constructor\",{get(){reads++;return replacement;},configurable:true});const value=new Date(0);console.log(value.constructor===replacement,reads);console.log(Reflect.deleteProperty(P,\"constructor\"),Object.hasOwn(P,\"constructor\"),P.constructor===Object);", "true 1\ntrue false true\n"];
        yield return ["runtime-any-accessor-override-and-delete", "const P:any=Date.prototype,replacement={};let reads=0;Object.defineProperty(P,\"constructor\",{get(){reads++;return replacement;},configurable:true});const value:any=new Date(0);console.log(value.constructor===replacement,reads);console.log(Reflect.deleteProperty(P,\"constructor\"),Object.hasOwn(P,\"constructor\"),P.constructor===Object);", "true 1\ntrue false true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReflectUsesActualDateOwnDescriptors(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}