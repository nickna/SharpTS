using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayPhysicalReflectGetTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reduced-read-only-original", "const a:any=new Uint8Array([5]);Object.setPrototypeOf(a,null);console.log(a[0],Reflect.get(a,'0'));", "5 5\n"];
        yield return ["default-prototype", "const a:any=new Uint8Array([5]);console.log(a[0],Reflect.get(a,\"0\"));", "5 5\n"];
        yield return ["ordinary-prototype", "const a:any=new Uint8Array([5]);Object.setPrototypeOf(a,{});console.log(a[0],Reflect.get(a,\"0\"));", "5 5\n"];
        yield return ["first-last-and-numeric-key", "const a:any=new Int16Array([-2,7]);Object.setPrototypeOf(a,null);console.log(Reflect.get(a,\"0\"),Reflect.get(a,1));", "-2 7\n"];
        yield return ["float-values-preserve-identity", "const a:any=new Float64Array([NaN,-0,Infinity]);Object.setPrototypeOf(a,null);console.log(Number.isNaN(Reflect.get(a,\"0\")),Object.is(Reflect.get(a,\"1\"),-0),Reflect.get(a,\"2\"));", "true true Infinity\n"];
        yield return ["clamped-storage", "const a:any=new Uint8ClampedArray([300]);Object.setPrototypeOf(a,null);console.log(Reflect.get(a,\"0\"));", "255\n"];
        yield return ["bigint-signed", "const a:any=new BigInt64Array([-7n]);Object.setPrototypeOf(a,null);console.log(String(Reflect.get(a,\"0\")));", "-7\n"];
        yield return ["bigint-unsigned", "const a:any=new BigUint64Array([9n]);Object.setPrototypeOf(a,null);console.log(String(Reflect.get(a,\"0\")));", "9\n"];
        yield return ["own-index-before-inherited-getter", "const a:any=new Uint8Array([8]),proto:any={};let calls=0;Object.defineProperty(proto,\"0\",{get(){calls++;throw 7}});Object.setPrototypeOf(a,proto);console.log(Reflect.get(a,\"0\"),calls);", "8 0\n"];
        yield return ["own-index-before-proxy-prototype", "const a:any=new Uint8Array([8]);let calls=0;const proto:any=new Proxy({},{get(){calls++;throw 7}});Object.setPrototypeOf(a,proto);console.log(Reflect.get(a,\"0\"),calls);", "8 0\n"];
        yield return ["physical-read-ignores-ordinary-receiver", "const a:any=new Uint8Array([5]);Object.setPrototypeOf(a,null);let calls=0;const receiver:any={};Object.defineProperty(receiver,\"0\",{get(){calls++;return 99}});console.log(Reflect.get(a,\"0\",receiver),calls);", "5 0\n"];
        yield return ["physical-and-length-receiver-control", "const a:any=new Uint8Array([5]),receiver:any=new Uint8Array(3);console.log(Reflect.get(a,\"0\",receiver),Reflect.get(a,\"length\",receiver));", "5 3\n"];
        yield return ["key-conversion-once", "const a:any=new Uint8Array([5]);Object.setPrototypeOf(a,null);let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return \"0\"}};console.log(Reflect.get(a,key),calls);", "string\n5 1\n"];
        yield return ["key-conversion-abrupt", "const a:any=new Uint8Array([5]),marker={};let calls=0;const key:any={[Symbol.toPrimitive](){calls++;throw marker}};try{Reflect.get(a,key)}catch(e){console.log(e===marker)}console.log(a[0],calls);", "true\n5 1\n"];
        yield return ["uint32-storage", "const a:any=new Uint32Array([4294967295]);Object.setPrototypeOf(a,null);console.log(Reflect.get(a,\"0\"));", "4294967295\n"];
        yield return ["ordinary-own-accessor-receiver-control", "const a:any=new Uint8Array([5]),receiver={};Object.defineProperty(a,\"tag\",{get(){return this===receiver?7:8}});console.log(Reflect.get(a,\"tag\",receiver));", "7\n"];
        yield return ["symbol-accessor-receiver-control", "const a:any=new Uint8Array([5]),receiver={},key=Symbol(\"tag\");Object.defineProperty(a,key,{get(){return this===receiver?7:8}});console.log(Reflect.get(a,key,receiver));", "7\n"];
        yield return ["ordinary-object-control", "const a:any={\"0\":7};console.log(Reflect.get(a,\"0\"));", "7\n"];
        yield return ["view-byte-offset", "const backing=new Uint8Array([1,2,3,4]);const a:any=new Uint8Array(backing.buffer,1,2);Object.setPrototypeOf(a,null);console.log(Reflect.get(a,\"0\"),Reflect.get(a,\"1\"));", "2 3\n"];
        yield return ["own-length-shadow-control", "const a:any=new Uint8Array([5]);Object.defineProperty(a,\"length\",{value:99});Object.setPrototypeOf(a,null);console.log(Reflect.get(a,\"0\"),Reflect.get(a,\"length\"));", "5 99\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PhysicalOwnValuesPrecedePrototypeLookup(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
