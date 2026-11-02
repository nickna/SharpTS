using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArraySymbolDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["identity-and-order", "const a:any=new Uint8Array(2),s=Symbol('s'),t=Symbol('t');Object.defineProperty(a,s,{value:7,enumerable:true});Object.defineProperty(a,'name',{value:8,enumerable:true});Object.defineProperty(a,t,{get(){throw 9},enumerable:true});const k=Reflect.ownKeys(a);console.log(k.length,k[0],k[1],k[2],k[3]===s,k[4]===t,Object.keys(a).join(','),Object.getOwnPropertyNames(a).join(','));", "5 0 1 name true true 0,1,name 0,1,name\n"];
        yield return ["reflect-definition", "const a:any=new Uint8Array(1),s=Symbol('s');console.log(Reflect.defineProperty(a,s,{value:7,writable:true,enumerable:true,configurable:true}),Object.getOwnPropertyDescriptor(a,s)?.value,Reflect.getOwnPropertyDescriptor(a,s)?.writable,Object.getOwnPropertySymbols(a)[0]===s,Object.hasOwn(a,s),a[s]);", "true 7 true true true 7\n"];
        yield return ["same-description", "const a:any=new Uint8Array(0),s=Symbol('x'),t=Symbol('x');Object.defineProperty(a,s,{value:7});Object.defineProperty(a,t,{value:8});Object.defineProperty(a,'Symbol(x)',{value:9});console.log(a[s],a[t],Object.getOwnPropertyDescriptor(a,'Symbol(x)')?.value,Object.getOwnPropertySymbols(a).length,Reflect.ownKeys(a)[0]);", "7 8 9 2 Symbol(x)\n"];
        yield return ["accessor-receiver", "const a:any=new Uint8Array(0),s=Symbol('s');let gets=0,sets=0;Object.defineProperty(a,s,{get(){gets++;console.log(this===a);return 7},set(v){sets++;console.log(this===a,v)},enumerable:true,configurable:true});console.log(Object.keys(a).length,Reflect.ownKeys(a)[0]===s,gets);console.log(a[s]);a[s]=9;console.log(gets,sets);", "0 true 0\ntrue\n7\ntrue 9\n1 1\n"];
        yield return ["omitted-value", "const a:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:7,writable:true,configurable:true});Object.defineProperty(a,s,{enumerable:true});console.log(a[s],Object.getOwnPropertyDescriptor(a,s)?.enumerable);", "7 true\n"];
        yield return ["nonconfigurable-refusal", "const a:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:7});console.log(Reflect.defineProperty(a,s,{value:8}),Object.getOwnPropertyDescriptor(a,s)?.value,Reflect.deleteProperty(a,s));try{Object.defineProperty(a,s,{value:8})}catch(e){console.log(e instanceof TypeError)}", "false 7 false\ntrue\n"];
        yield return ["prevent-extensions", "const a:any=new Uint8Array(0),s=Symbol('s'),t=Symbol('t');Object.defineProperty(a,s,{value:7,writable:true,configurable:true});Object.preventExtensions(a);console.log(Reflect.defineProperty(a,t,{value:8}),Reflect.defineProperty(a,s,{value:9}),a[s],Object.getOwnPropertySymbols(a).length);", "false true 9 1\n"];
        yield return ["seal-and-freeze", "const a:any=new Uint8Array(0),b:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:7,writable:true,configurable:true});Object.seal(a);Object.defineProperty(b,s,{value:8,writable:true,configurable:true});Object.freeze(b);console.log(Object.getOwnPropertyDescriptor(a,s)?.configurable,Object.getOwnPropertyDescriptor(a,s)?.writable,Reflect.deleteProperty(a,s),Object.getOwnPropertyDescriptor(b,s)?.writable,Reflect.defineProperty(b,s,{value:9}),b[s]);", "false true false false false 8\n"];
        yield return ["deletion-recreation-order", "const a:any=new Uint8Array(0),s=Symbol('s'),t=Symbol('t');Object.defineProperty(a,s,{value:7,configurable:true});Object.defineProperty(a,t,{value:8,configurable:true});console.log(Reflect.deleteProperty(a,s));Object.defineProperty(a,s,{value:9});const k=Object.getOwnPropertySymbols(a);console.log(k[0]===t,k[1]===s,a[s]);", "true\ntrue true 9\n"];
        yield return ["transparent-proxy-symbols", "const a:any=new Uint8Array(1),s=Symbol('s');Object.defineProperty(a,s,{value:7,enumerable:true});const p=new Proxy(a,{});const k=Reflect.ownKeys(p);console.log(k.length,k[0],k[1]===s,Object.getOwnPropertySymbols(p)[0]===s,Object.getOwnPropertyDescriptor(p,s)?.value,p[s]);", "2 0 true true 7 7\n"];
        yield return ["proxy-nonconfigurable-symbol-required", "const a:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:7});const p=new Proxy(a,{ownKeys(){return []}});try{Reflect.ownKeys(p)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["descriptor-snapshot", "const a:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:7,writable:true});const d=Object.getOwnPropertyDescriptor(a,s)!;d.value=9;console.log(Object.getOwnPropertyDescriptor(a,s)?.value,Reflect.ownKeys(Object.getOwnPropertyDescriptors(a))[0]===s);", "7 true\n"];
        yield return ["undefined-is-present", "const a:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:undefined});console.log(s in a,Reflect.has(a,s),Object.hasOwn(a,s),a[s]===undefined);", "true true true true\n"];
        yield return ["presence-does-not-read-getter", "const a:any=new Uint8Array(0),s=Symbol('s');let gets=0;Object.defineProperty(a,s,{get(){gets++;throw 9}});console.log(s in a,Reflect.has(a,s),Object.hasOwn(a,s),gets);", "true true true 0\n"];
        yield return ["compound-and-logical", "const a:any=new Uint8Array(0),s=Symbol('s');Object.defineProperty(a,s,{value:7,writable:true});console.log(a[s]+=2,a[s]++,a[s]);a[s]&&=12;console.log(a[s]);", "9 9 10\n12\n"];
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
