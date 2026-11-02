using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayOwnKeysTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["physical-and-named-own-keys", "const a:any=new Uint8Array(2);let gets=0;Object.defineProperty(a,'probe',{get(){gets++;return 9},enumerable:true,configurable:true});Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});console.log(Object.getOwnPropertyNames(a).join(','),Reflect.ownKeys(a).join(','),Object.keys(a).join(','),gets);", "0,1,probe,length 0,1,probe,length 0,1,probe 0\n"];
        yield return ["zero-elements", "const a:any=new Uint8Array(0);console.log(Object.keys(a).length,Object.getOwnPropertyNames(a).length,Reflect.ownKeys(a).length);", "0 0 0\n"];
        yield return ["all-numeric-element-kinds", "function keys(a:any){return Object.keys(a).join(\",\")+\"/\"+Object.getOwnPropertyNames(a).join(\",\")+\"/\"+Reflect.ownKeys(a).join(\",\")}console.log(keys(new Int8Array(2)),keys(new Uint8Array(2)),keys(new Uint8ClampedArray(2)),keys(new Int16Array(2)),keys(new Uint16Array(2)),keys(new Int32Array(2)),keys(new Uint32Array(2)),keys(new Float32Array(2)),keys(new Float64Array(2)));", "0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1 0,1/0,1/0,1\n"];
        yield return ["bigint-element-kinds", "const a=new BigInt64Array(2),b=new BigUint64Array(1);console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"),Reflect.ownKeys(b).join(\",\"));", "0,1 0,1 0\n"];
        yield return ["subview-physical-indices", "const a=new Uint8Array(new ArrayBuffer(8),3,2);console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"),Reflect.ownKeys(a).join(\",\"));", "0,1 0,1 0,1\n"];
        yield return ["zero-explicit-view", "const a:any=new Int32Array(new ArrayBuffer(9),8,0);Object.defineProperty(a,\"tag\",{value:7,enumerable:true});console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"),Reflect.ownKeys(a).join(\",\"));", "tag tag tag\n"];
        yield return ["length-getter-not-read", "const a:any=new Uint8Array(2);let gets=0;Object.defineProperty(a,\"length\",{get(){gets++;throw 9},enumerable:true,configurable:true});console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"),Reflect.ownKeys(a).join(\",\"),gets);", "0,1,length 0,1,length 0,1,length 0\n"];
        yield return ["nonenumerable-and-recreated-names", "const a:any=new Uint8Array(2);Object.defineProperty(a,\"hidden\",{value:7,configurable:true});Object.defineProperty(a,\"tail\",{value:8,enumerable:true,configurable:true});delete a.hidden;Object.defineProperty(a,\"hidden\",{value:9,enumerable:true,configurable:true});console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"));", "0,1,tail,hidden 0,1,tail,hidden\n"];
        yield return ["symbols-follow-strings", "const a:any=new Uint8Array(2),s=Symbol(\"s\"),t=Symbol(\"t\");Object.defineProperty(a,s,{value:7,enumerable:true});Object.defineProperty(a,\"name\",{value:8,enumerable:true});Object.defineProperty(a,t,{get(){throw 9},enumerable:true});const k=Reflect.ownKeys(a);console.log(k.length,k[0],k[1],k[2],k[3]===s,k[4]===t,Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"));", "5 0 1 name true true 0,1,name 0,1,name\n"];
        yield return ["throwing-named-getter-not-read", "const a:any=new Int16Array(1);Object.defineProperty(a,\"probe\",{get(){throw 7},enumerable:true});console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"),Reflect.ownKeys(a).join(\",\"));", "0,probe 0,probe 0,probe\n"];
        yield return ["prevented-elements-remain-own", "const a:any=new Uint8Array(2);Object.defineProperty(a,\"name\",{value:7,enumerable:true});Object.preventExtensions(a);console.log(Object.keys(a).join(\",\"),Object.getOwnPropertyNames(a).join(\",\"),Reflect.ownKeys(a).join(\",\"));", "0,1,name 0,1,name 0,1,name\n"];
        yield return ["transparent-proxy-ownkeys", "const a:any=new Uint8Array(2);Object.defineProperty(a,\"tag\",{value:7,enumerable:true});const p=new Proxy(a,{});console.log(Object.keys(p).join(\",\"),Object.getOwnPropertyNames(p).join(\",\"),Reflect.ownKeys(p).join(\",\"));", "0,1,tag 0,1,tag 0,1,tag\n"];
        yield return ["explicit-proxy-key-order", "const a:any=new Uint8Array(2);Object.defineProperty(a,\"tag\",{value:7,enumerable:true,configurable:true});const p=new Proxy(a,{ownKeys(){return [\"tag\",\"1\",\"0\"]}});console.log(Object.getOwnPropertyNames(p).join(\",\"),Reflect.ownKeys(p).join(\",\"));", "tag,1,0 tag,1,0\n"];
        yield return ["ordinary-array-and-record-control", "console.log(Object.keys([7,8]).join(\",\"),Object.getOwnPropertyNames([7,8]).join(\",\"),Object.keys({b:7,a:8}).join(\",\"));", "0,1 0,1,length b,a\n"];
        yield return ["physical-descriptor", "const a=new Uint8Array(2);a[0]=7;const d=Object.getOwnPropertyDescriptor(a,\"0\");console.log(Boolean(d),d?.value,d?.writable,d?.enumerable,d?.configurable,Object.prototype.hasOwnProperty.call(a,\"0\"));", "true 7 true true true true\n"];
        yield return ["transparent-proxy-keys", "const a=new Uint8Array(2);console.log(Object.keys(new Proxy(a,{})).join(\",\"));", "0,1\n"];
        yield return ["invalid-physical-own-keys", "const a=new Uint8Array(2);const names=[\"-0\",\"-1\",\"2\",\"1.5\",\"4294967295\",\"NaN\",\"Infinity\"];let absent=true;for(const key of names){if(Object.getOwnPropertyDescriptor(a,key)!==undefined||Object.hasOwn(a,key))absent=false}console.log(absent,Object.hasOwn(a,\"0\"),Object.hasOwn(a,\"1\"));", "true true true\n"];
        yield return ["noncanonical-named-slot", "const a:any=new Uint8Array(2);Object.defineProperty(a,\"01\",{value:7,enumerable:true,configurable:true});console.log(Object.getOwnPropertyDescriptor(a,\"01\")?.value,Object.hasOwn(a,\"01\"),Object.keys(a).join(\",\"));", "7 true 0,1,01\n"];
        yield return ["bigint-physical-descriptor", "const a=new BigInt64Array(1);a[0]=7n;const d=Object.getOwnPropertyDescriptor(a,\"0\")!;console.log(d.value===7n,d.writable,d.enumerable,d.configurable,Object.hasOwn(a,\"0\"));", "true true true true true\n"];
        yield return ["ordinary-descendant-not-own", "const a=new Uint8Array(2),child=Object.create(a);console.log(Object.getOwnPropertyDescriptor(child,\"0\")===undefined,Object.hasOwn(child,\"0\"),Object.hasOwn(a,\"0\"));", "true false true\n"];
        yield return ["descriptor-object-key-once", "const a=new Uint8Array(1);a[0]=7;let calls=0,hint=\"\";const key:any={[Symbol.toPrimitive](h:string){calls++;hint=h;return \"0\"}};console.log(Object.getOwnPropertyDescriptor(a,key)?.value,calls,hint);", "7 1 string\n"];
        yield return ["fresh-descriptor-snapshot", "const a=new Uint8Array(1);a[0]=7;const d=Object.getOwnPropertyDescriptor(a,\"0\")!;d.value=99;d.enumerable=false;const current=Object.getOwnPropertyDescriptor(a,\"0\")!;console.log(current.value,current.enumerable,Object.keys(a).join(\",\"));", "7 true 0\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompiledKeysIncludePhysicalElementsBeforeNamedProperties(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpretedKeysIncludePhysicalElementsBeforeNamedProperties(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
