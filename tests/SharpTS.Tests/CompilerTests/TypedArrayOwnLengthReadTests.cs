using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class TypedArrayOwnLengthReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["own-writable-length", "const a:any=new Uint8Array(2);Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});a.length=8;console.log(a.length,Reflect.get(a,'length'));", "8 8\n"];
        yield return ["own-setter-receiver", "const a:any=new Uint8Array(2);let value=3,calls=0,receiver=false;Object.defineProperty(a,'length',{get(){return value},set(v:any){calls++;receiver=this===a;value=v},configurable:true});a.length=8;console.log(value,calls,receiver,Reflect.get(a,'length'));", "8 1 true 8\n"];
        yield return ["physical-element-after-shadow", "const a:any=new Uint8Array(2);a[0]=9;Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});console.log(a[0],a.length,Reflect.get(a,'length'));", "9 7 7\n"];
        yield return ["statically-typed-raw-value", "const a=new Uint8Array(2);Object.defineProperty(a,\"length\",{value:\"own\",configurable:true});console.log(a.length,Reflect.get(a,\"length\"));", "own own\n"];
        yield return ["own-getter-receiver", "const a=new Uint8Array(2);let reads=0;Object.defineProperty(a,\"length\",{get(){reads++;return this===a?7:99},configurable:true});console.log(a.length,Reflect.get(a,\"length\"),reads);", "7 7 2\n"];
        yield return ["own-getterless", "const a=new Uint8Array(2);Object.defineProperty(a,\"length\",{set(v:any){},configurable:true});console.log(a.length===undefined,Reflect.get(a,\"length\")===undefined);", "true true\n"];
        yield return ["deleted-own-reveals-intrinsic", "const a:any=new Uint8Array(2);Object.defineProperty(a,\"length\",{value:7,configurable:true});console.log(delete a.length,a.length,Reflect.get(a,\"length\"));", "true 2 2\n"];
        yield return ["explicit-prototype-data", "const a=new Uint8Array(2);Object.setPrototypeOf(a,{length:5});console.log(a.length,Reflect.get(a,\"length\"));", "5 5\n"];
        yield return ["explicit-null-prototype", "const a=new Uint8Array(2);Object.setPrototypeOf(a,null);console.log(a.length===undefined,Reflect.get(a,\"length\")===undefined);", "true true\n"];
        yield return ["loop-current-length", "const a=new Uint8Array(2);a[0]=9;a[1]=10;Object.defineProperty(a,\"length\",{value:1,configurable:true});let sum=0;for(let i=0;i<a.length;i++)sum+=a[i];console.log(sum,a.length);", "9 1\n"];
        yield return ["intrinsic-over-array-prototype", "(Array.prototype as any).length=4;const a=new Uint8Array(2);console.log(a.length,Reflect.get(a,\"length\"));", "2 2\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DirectReadsObserveCurrentLengthDescriptors(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunCompiled(source));
    }
}
