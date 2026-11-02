using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayProxyOwnKeysTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["physical-and-named", "const a:any=new Uint8Array(2);Object.defineProperty(a,'tag',{value:7,enumerable:true});const p=new Proxy(a,{});console.log(Object.keys(p).join(','),Object.getOwnPropertyNames(p).join(','),Reflect.ownKeys(p).join(','));", "0,1,tag 0,1,tag 0,1,tag\n"];
        yield return ["descriptors", "const a:any=new Uint8Array(2);a[0]=7;Object.defineProperty(a,'tag',{value:9,enumerable:true});const p=new Proxy(a,{});const d=Object.getOwnPropertyDescriptor(p,'0')!;console.log(d.value,d.writable,d.enumerable,d.configurable,Object.getOwnPropertyDescriptor(p,'tag')?.value,Reflect.getOwnPropertyDescriptor(p,'1')?.value);", "7 true true true 9 0\n"];
        yield return ["nested-and-empty", "console.log(Object.keys(new Proxy(new Proxy(new Uint8Array(2),{}),{})).join(','),Reflect.ownKeys(new Proxy(new Uint8Array(0),{})).length);", "0,1 0\n"];
        yield return ["getter-not-read", "const a:any=new Uint8Array(1);let gets=0;Object.defineProperty(a,'probe',{get(){gets++;throw 9},enumerable:true});const p=new Proxy(a,{});console.log(Object.keys(p).join(','),Object.getOwnPropertyNames(p).join(','),Reflect.ownKeys(p).join(','),gets);", "0,probe 0,probe 0,probe 0\n"];
        yield return ["trap-order-and-filter", "const a:any=new Uint8Array(2);Object.defineProperty(a,'hidden',{value:7,configurable:true});const p=new Proxy(a,{ownKeys(){return ['hidden','1','0']}});console.log(Object.keys(p).join(','),Object.getOwnPropertyNames(p).join(','),Reflect.ownKeys(p).join(','));", "1,0 hidden,1,0 hidden,1,0\n"];
        yield return ["nonconfigurable-key-required", "const a:any=new Uint8Array(1);Object.defineProperty(a,'tag',{value:7});const p=new Proxy(a,{ownKeys(){return ['0']}});try{Reflect.ownKeys(p)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["nonextensible-physical-key-required", "const a=new Uint8Array(2);Object.preventExtensions(a);const p=new Proxy(a,{ownKeys(){return ['0']}});try{Reflect.ownKeys(p)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["nonextensible-exact-keys", "const a=new Uint8Array(2);Object.preventExtensions(a);const p=new Proxy(a,{ownKeys(){return ['1','0']}});console.log(Object.keys(p).join(','),Reflect.ownKeys(p).join(','));", "1,0 1,0\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Interpreted(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Theory, MemberData(nameof(Cases))]
    public void Compiled(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
