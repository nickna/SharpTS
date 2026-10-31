using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BuiltInPrototypePreventExtensions3332Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-prevent-delete-object", "const p:any=String.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;try{Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true false true false false\n"];
        yield return ["number-prevent-delete-object", "const p:any=Number.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;try{Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true false true false false\n"];
        yield return ["boolean-prevent-delete-object", "const p:any=Boolean.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;try{Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true false true false false\n"];
        yield return ["array-prevent-delete-object", "const p:any=Array.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;try{Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true false true false false\n"];
        yield return ["string-existing-constructor-partial", "const p:any=String.prototype;const old=p.constructor;Object.preventExtensions(p);Object.defineProperty(p,'constructor',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(d.value===old,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true false false true false\n"];
        yield return ["string-existing-method-assignment", "const p:any=String.prototype;const fn=()=>7;Object.preventExtensions(p);p.toString=fn;const d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(p.toString===fn,d.value===fn,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true true true false true false\n"];
        yield return ["string-reflect-prevention-fresh", "const p:any=String.prototype;console.log(Reflect.isExtensible(p),Reflect.preventExtensions(p));let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p),Reflect.isExtensible(p));", "true true\ntrue false false false\n"];
        yield return ["string-proxy-prevention-fresh", "const p:any=String.prototype;const proxy=new Proxy(p,{});console.log(Object.preventExtensions(proxy)===proxy);let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true\ntrue false false\n"];
        yield return ["number-existing-constructor-partial", "const p:any=Number.prototype;const old=p.constructor;Object.preventExtensions(p);Object.defineProperty(p,'constructor',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(d.value===old,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true false false true false\n"];
        yield return ["number-existing-method-assignment", "const p:any=Number.prototype;const fn=()=>7;Object.preventExtensions(p);p.toString=fn;const d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(p.toString===fn,d.value===fn,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true true true false true false\n"];
        yield return ["number-reflect-prevention-fresh", "const p:any=Number.prototype;console.log(Reflect.isExtensible(p),Reflect.preventExtensions(p));let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p),Reflect.isExtensible(p));", "true true\ntrue false false false\n"];
        yield return ["number-proxy-prevention-fresh", "const p:any=Number.prototype;const proxy=new Proxy(p,{});console.log(Object.preventExtensions(proxy)===proxy);let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true\ntrue false false\n"];
        yield return ["boolean-existing-constructor-partial", "const p:any=Boolean.prototype;const old=p.constructor;Object.preventExtensions(p);Object.defineProperty(p,'constructor',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(d.value===old,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true false false true false\n"];
        yield return ["boolean-existing-method-assignment", "const p:any=Boolean.prototype;const fn=()=>7;Object.preventExtensions(p);p.toString=fn;const d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(p.toString===fn,d.value===fn,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true true true false true false\n"];
        yield return ["boolean-reflect-prevention-fresh", "const p:any=Boolean.prototype;console.log(Reflect.isExtensible(p),Reflect.preventExtensions(p));let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p),Reflect.isExtensible(p));", "true true\ntrue false false false\n"];
        yield return ["boolean-proxy-prevention-fresh", "const p:any=Boolean.prototype;const proxy=new Proxy(p,{});console.log(Object.preventExtensions(proxy)===proxy);let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true\ntrue false false\n"];
        yield return ["array-existing-constructor-partial", "const p:any=Array.prototype;const old=p.constructor;Object.preventExtensions(p);Object.defineProperty(p,'constructor',{writable:false});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(d.value===old,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true false false true false\n"];
        yield return ["array-existing-method-assignment", "const p:any=Array.prototype;const fn=()=>7;Object.preventExtensions(p);p.toString=fn;const d=Object.getOwnPropertyDescriptor(p,'toString')!;console.log(p.toString===fn,d.value===fn,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "true true true false true false\n"];
        yield return ["array-existing-accessor-no-eager-get", "const p:any=Array.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});console.log(calls,p.held,calls,Object.getOwnPropertyDescriptor(p,'held')!.get===get,Object.isExtensible(p));", "0 7 1 true false\n"];
        yield return ["array-reflect-prevention-fresh", "const p:any=Array.prototype;console.log(Reflect.isExtensible(p),Reflect.preventExtensions(p));let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p),Reflect.isExtensible(p));", "true true\ntrue false false false\n"];
        yield return ["array-proxy-prevention-fresh", "const p:any=Array.prototype;const proxy=new Proxy(p,{});console.log(Object.preventExtensions(proxy)===proxy);let caught=false;try{Object.defineProperty(p,'fresh',{value:7})}catch(e){caught=e instanceof TypeError}console.log(caught,Object.prototype.hasOwnProperty.call(p,'fresh'),Object.isExtensible(p));", "true\ntrue false false\n"];
        yield return ["string-existing-accessor-descriptor-preserved", "const p:any=String.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});const d=Object.getOwnPropertyDescriptor(p,'held')!;console.log(calls,d.get===get,d.enumerable,d.configurable,Object.isExtensible(p));", "0 true true true false\n"];
        yield return ["number-existing-accessor-descriptor-preserved", "const p:any=Number.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});const d=Object.getOwnPropertyDescriptor(p,'held')!;console.log(calls,d.get===get,d.enumerable,d.configurable,Object.isExtensible(p));", "0 true true true false\n"];
        yield return ["boolean-existing-accessor-descriptor-preserved", "const p:any=Boolean.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});const d=Object.getOwnPropertyDescriptor(p,'held')!;console.log(calls,d.get===get,d.enumerable,d.configurable,Object.isExtensible(p));", "0 true true true false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PreventionUsesTheOwnedPrototypeStore(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
