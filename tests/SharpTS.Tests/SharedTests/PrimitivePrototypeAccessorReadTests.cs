using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeAccessorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-existing-accessor-no-eager-get", "const p:any=String.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});console.log(calls,p.held,calls,Object.getOwnPropertyDescriptor(p,'held')!.get===get,Object.isExtensible(p));", "0 7 1 true false\n"];
        yield return ["number-existing-accessor-no-eager-get", "const p:any=Number.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});console.log(calls,p.held,calls,Object.getOwnPropertyDescriptor(p,'held')!.get===get,Object.isExtensible(p));", "0 7 1 true false\n"];
        yield return ["boolean-existing-accessor-no-eager-get", "const p:any=Boolean.prototype;let calls=0;const get=()=>{calls++;return 7};Object.defineProperty(p,'held',{get,configurable:true});Object.preventExtensions(p);Object.defineProperty(p,'held',{enumerable:true});console.log(calls,p.held,calls,Object.getOwnPropertyDescriptor(p,'held')!.get===get,Object.isExtensible(p));", "0 7 1 true false\n"];
        yield return ["string-getter-before-after-reflect", "const p:any=String.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;return 7},configurable:true});console.log(p.held,calls);Object.preventExtensions(p);console.log(p.held,calls,Reflect.get(p,'held'),calls);", "7 1\n7 2 7 3\n"];
        yield return ["number-getter-before-after-reflect", "const p:any=Number.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;return 7},configurable:true});console.log(p.held,calls);Object.preventExtensions(p);console.log(p.held,calls,Reflect.get(p,'held'),calls);", "7 1\n7 2 7 3\n"];
        yield return ["boolean-getter-before-after-reflect", "const p:any=Boolean.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;return 7},configurable:true});console.log(p.held,calls);Object.preventExtensions(p);console.log(p.held,calls,Reflect.get(p,'held'),calls);", "7 1\n7 2 7 3\n"];
        yield return ["string-read-routes-receiver-abrupt", "const p:any=String.prototype;let calls=0;const marker={};Object.defineProperty(p,'held',{get:function(this:any){calls++;return this===p?7:99},configurable:true});const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return 'held'}};console.log(p.held,p['held'],p[key],calls);Object.defineProperty(p,'held',{get(){throw marker},configurable:true});try{p.held}catch(e){console.log(e===marker)}Object.defineProperty(p,'held',{get:undefined,set(){calls++},configurable:true});console.log(p.held===undefined,calls);", "string\n7 7 7 3\ntrue\ntrue 3\n"];
        yield return ["string-numeric-symbol-surfaces", "const p:any=String.prototype;let calls=0;Object.defineProperty(p,'0',{get(){calls++;return 7},configurable:true});const symbol=Symbol('held');Object.defineProperty(p,symbol,{get:function(this:any){calls++;return this===p?9:99},configurable:true});let conversions=0;const key:any={[Symbol.toPrimitive](){conversions++;return symbol}};console.log(p[0],p[symbol],p[key],calls,conversions,p[Symbol('held')]===undefined);", "7 9 9 3 1 true\n"];
        yield return ["string-own-shadow-inherited-receiver", "const p:any=String.prototype;let calls=0;Object.defineProperty(Object.prototype,'__p2_inherited__',{get:function(this:any){calls++;return this===p?9:99},configurable:true});Object.defineProperty(p,'__p2_inherited__',{value:undefined,configurable:true});console.log(p.__p2_inherited__===undefined,calls);delete p.__p2_inherited__;console.log(p.__p2_inherited__,calls);delete (Object.prototype as any).__p2_inherited__;", "true 0\n9 1\n"];
        yield return ["string-method-identity-borrow", "const p:any=String.prototype,f=p.toString;console.log(f===p.toString,f.call('x'));", "true x\n"];
        yield return ["number-read-routes-receiver-abrupt", "const p:any=Number.prototype;let calls=0;const marker={};Object.defineProperty(p,'held',{get:function(this:any){calls++;return this===p?7:99},configurable:true});const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return 'held'}};console.log(p.held,p['held'],p[key],calls);Object.defineProperty(p,'held',{get(){throw marker},configurable:true});try{p.held}catch(e){console.log(e===marker)}Object.defineProperty(p,'held',{get:undefined,set(){calls++},configurable:true});console.log(p.held===undefined,calls);", "string\n7 7 7 3\ntrue\ntrue 3\n"];
        yield return ["number-own-shadow-inherited-receiver", "const p:any=Number.prototype;let calls=0;Object.defineProperty(Object.prototype,'__p2_inherited__',{get:function(this:any){calls++;return this===p?9:99},configurable:true});Object.defineProperty(p,'__p2_inherited__',{value:undefined,configurable:true});console.log(p.__p2_inherited__===undefined,calls);delete p.__p2_inherited__;console.log(p.__p2_inherited__,calls);delete (Object.prototype as any).__p2_inherited__;", "true 0\n9 1\n"];
        yield return ["number-method-identity-borrow", "const p:any=Number.prototype,f=p.toString;console.log(f===p.toString,f.call(7));", "true 7\n"];
        yield return ["boolean-read-routes-receiver-abrupt", "const p:any=Boolean.prototype;let calls=0;const marker={};Object.defineProperty(p,'held',{get:function(this:any){calls++;return this===p?7:99},configurable:true});const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return 'held'}};console.log(p.held,p['held'],p[key],calls);Object.defineProperty(p,'held',{get(){throw marker},configurable:true});try{p.held}catch(e){console.log(e===marker)}Object.defineProperty(p,'held',{get:undefined,set(){calls++},configurable:true});console.log(p.held===undefined,calls);", "string\n7 7 7 3\ntrue\ntrue 3\n"];
        yield return ["boolean-own-shadow-inherited-receiver", "const p:any=Boolean.prototype;let calls=0;Object.defineProperty(Object.prototype,'__p2_inherited__',{get:function(this:any){calls++;return this===p?9:99},configurable:true});Object.defineProperty(p,'__p2_inherited__',{value:undefined,configurable:true});console.log(p.__p2_inherited__===undefined,calls);delete p.__p2_inherited__;console.log(p.__p2_inherited__,calls);delete (Object.prototype as any).__p2_inherited__;", "true 0\n9 1\n"];
        yield return ["boolean-method-identity-borrow", "const p:any=Boolean.prototype,f=p.toString;console.log(f===p.toString,f.call(true));", "true true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReadsCurrentDescriptorWithOriginalReceiver(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
