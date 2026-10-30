using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BuiltinConstructorReflectDelete3182RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-configurable", "const owner:any=Promise;owner.marker=7;console.log(Reflect.deleteProperty(owner,\"marker\"),owner.marker===undefined,Object.hasOwn(owner,\"marker\"));", "true true false\n"];
        yield return ["nonconfigurable", "const owner:any=Promise;Object.defineProperty(owner,\"marker\",{value:7,configurable:false});console.log(Reflect.deleteProperty(owner,\"marker\"),owner.marker,Object.hasOwn(owner,\"marker\"));", "false 7 true\n"];
        yield return ["date-overlay", "const owner:any=Date;owner.marker=7;console.log(Reflect.deleteProperty(owner,\"marker\"),owner.marker===undefined,Object.hasOwn(owner,\"marker\"));", "true true false\n"];
        yield return ["native-deletion-and-retained-value", "const owner:any=Date,held=owner.now;console.log(Reflect.deleteProperty(owner,\"now\"),owner.now===undefined,Object.hasOwn(owner,\"now\"),Object.getOwnPropertyDescriptor(owner,\"now\")===undefined,typeof held);", "true true false true function\n"];
        yield return ["nonconfigurable-prototypes", "for(const owner of [Promise,RegExp])console.log(Reflect.deleteProperty(owner,\"prototype\"),Object.hasOwn(owner,\"prototype\"));", "false true\nfalse true\n"];
        yield return ["key-conversion-once", "const owner:any=Promise;owner.marker=7;let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return \"marker\";}};console.log(Reflect.deleteProperty(owner,key),calls,owner.marker===undefined);", "string\ntrue 1 true\n"];
        yield return ["repeated-delete", "const owner:any=Promise;owner.marker=7;console.log(Reflect.deleteProperty(owner,\"marker\"),Reflect.deleteProperty(owner,\"marker\"),Reflect.deleteProperty(owner,\"missing\"));", "true true true\n"];
        yield return ["nonextensible-owner", "const owner:any=Promise;owner.marker=7;Object.preventExtensions(owner);console.log(Reflect.deleteProperty(owner,\"marker\"),Object.hasOwn(owner,\"marker\"),Object.isExtensible(owner));", "true false false\n"];
        yield return ["operator-companion", "const owner:any=Promise;owner.marker=7;console.log(delete owner.marker,owner.marker===undefined,Object.hasOwn(owner,\"marker\"));", "true true false\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReflectDeletesTheActualConstructorStringProperty(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}