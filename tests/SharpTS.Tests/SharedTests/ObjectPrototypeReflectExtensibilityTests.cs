using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeReflectExtensibilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["initial-query", "const p:any=Object.prototype;console.log(Reflect.isExtensible(p),Object.isExtensible(p));", "true true\n"];
        yield return ["prevent-refuses-new-property", "const p:any=Object.prototype;console.log(Reflect.preventExtensions(p),Object.isExtensible(p));try{Object.defineProperty(p,'__p2_ext__',{value:7,configurable:true});console.log('added')}catch(e){console.log(e instanceof TypeError,Object.hasOwn(p,'__p2_ext__'))}", "true false\ntrue false\n"];
        yield return ["no-getter-during-transition", "const p:any=Object.prototype;let calls=0;const getter=()=>{calls++;return 7};Object.defineProperty(p,'__p2_ext__',{get:getter,configurable:true});console.log(Reflect.preventExtensions(p),calls,Object.isExtensible(p),Reflect.isExtensible(p));const d:any=Object.getOwnPropertyDescriptor(p,'__p2_ext__');console.log(d.get===getter,d.configurable,d.enumerable,calls);", "true 0 false false\ntrue true false 0\n"];
        yield return ["retained-functions", "const p:any=Object.prototype,prevent:any=Reflect.preventExtensions,query:any=Reflect.isExtensible;console.log(query(p));console.log(prevent(p),query(p),Object.isExtensible(p));", "true\ntrue false false\n"];
        yield return ["object-api-control", "const p:any=Object.prototype;Object.preventExtensions(p);console.log(Reflect.isExtensible(p),Object.isExtensible(p),Reflect.preventExtensions(p));", "false false true\n"];
        yield return ["frozen-control", "const p:any=Object.prototype;Object.freeze(p);console.log(Reflect.isExtensible(p),Object.isExtensible(p),Reflect.preventExtensions(p));", "false false true\n"];
        yield return ["ordinary-target-control", "const p:any={};console.log(Reflect.isExtensible(p),Reflect.preventExtensions(p),Reflect.isExtensible(p),Object.isExtensible(p));", "true true false false\n"];
        yield return ["transparent-proxy-control", "const p:any=Object.prototype,proxy=new Proxy(p,{});console.log(Reflect.isExtensible(proxy),Reflect.preventExtensions(proxy),Reflect.isExtensible(proxy),Object.isExtensible(p));", "true true false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectUsesActualRootExtensibility(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
