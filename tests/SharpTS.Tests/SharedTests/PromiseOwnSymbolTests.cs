using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseOwnSymbolTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-own", "const key=Symbol(\"key\"),p:any=Promise.resolve(1);Object.defineProperty(p,key,{value:9,configurable:true});console.log(p[key],Object.getOwnPropertySymbols(p).length,Object.getOwnPropertySymbols(p)[0]===key);", "9 1 true\n"];
        yield return ["own-shadows-live-prototype", "const key=Symbol(\"key\"),p:any=Promise.resolve(1),proto:any={get[key](){return this===p?7:99;}};Object.setPrototypeOf(p,proto);Object.defineProperty(p,key,{value:9,configurable:true});console.log(p[key]);delete p[key];console.log(p[key]);", "9\n7\n"];
        yield return ["distinct-symbol-assignment", "const a=Symbol(\"same\"),b=Symbol(\"same\"),p:any=Promise.resolve(1);p[a]=7;p[b]=9;const d:any=Object.getOwnPropertyDescriptor(p,a);console.log(p[a],p[b],d.value,d.writable,d.enumerable,d.configurable,Object.getOwnPropertySymbols(p)[0]===a,Object.getOwnPropertySymbols(p)[1]===b);console.log(delete p[a],p[a]===undefined,p[b]);", "7 9 7 true true true true true\ntrue true 9\n"];
        yield return ["accessor-receivers", "const key=Symbol(\"key\"),p:any=Promise.resolve(1),other:any={value:11};let reads=0;const get=function(this:any){reads++;return this.value;},set=function(this:any,value:any){this.value=value;};Object.defineProperty(p,key,{get,set,enumerable:true,configurable:true});const d:any=Object.getOwnPropertyDescriptor(p,key);console.log(d.get===get,d.set===set,reads,Object.getOwnPropertySymbols(p)[0]===key);console.log(Reflect.get(p,key,other),Reflect.set(p,key,13,other),other.value,reads);", "true true 0 true\n11 true 13 1\n"];
        yield return ["setter-only-own", "const key=Symbol(\"key\"),p:any=Promise.resolve(1);let reads=0,writes=0;Object.setPrototypeOf(p,{get[key](){reads++;return 7;}});Object.defineProperty(p,key,{set(value:any){writes=value;},configurable:true});p[key]=9;console.log(p[key]===undefined,reads,writes);delete p[key];console.log(p[key],reads);", "true 0 9\n7 1\n"];
        yield return ["integrity", "const key=Symbol(\"key\"),fresh=Symbol(\"fresh\"),p:any=Promise.resolve(1);p[key]=7;const proto=Object.getPrototypeOf(p);Object.preventExtensions(p);console.log(Object.isExtensible(p),Reflect.defineProperty(p,fresh,{value:9}),Reflect.setPrototypeOf(p,proto),Reflect.setPrototypeOf(p,{}));Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,key);console.log(Object.isFrozen(p),d.value,d.writable,d.enumerable,d.configurable,Reflect.set(p,key,9),Reflect.deleteProperty(p,key));function write(value:any){\"use strict\";value[key]=9;}try{write(p);}catch(error){console.log(error instanceof TypeError);}console.log(p[key]);", "false false true false\ntrue 7 false true false false false\ntrue\n7\n"];
        yield return ["enumerable-copies", "const key=Symbol(\"key\"),hidden=Symbol(\"hidden\"),p:any=Promise.resolve(1);p.visible=9;p[key]=7;Object.defineProperty(p,hidden,{value:11});const copy:any=Object.assign({},p),spread:any={...p};console.log(Object.keys(p).join(\",\"),Object.values(p).join(\",\"),Object.entries(p)[0].join(\":\"),Object.getOwnPropertySymbols(p).length);console.log(copy.visible,copy[key],copy[hidden]===undefined,spread[key],Reflect.ownKeys(p)[1]===key,Reflect.ownKeys(p)[2]===hidden);", "visible 9 visible:9 2\n9 7 true 7 true true\n"];
        yield return ["getter-copy-order", "const key=Symbol(\"key\"),p:any=Promise.resolve(1);let reads=0;Object.defineProperty(p,key,{get(){reads++;return 7;},enumerable:true});Reflect.ownKeys(p);Object.getOwnPropertySymbols(p);Object.getOwnPropertyDescriptor(p,key);console.log(reads);const a:any=Object.assign({},p);console.log(a[key],reads);const b:any={...p};console.log(b[key],reads);", "0\n7 1\n7 2\n"];
        yield return ["define-properties", "const a=Symbol(\"a\"),b=Symbol(\"b\"),p:any=Promise.resolve(1),descriptors:any={};const events:string[]=[];Object.defineProperty(descriptors,a,{get(){events.push(\"a\");return {value:7,enumerable:true,configurable:true};},enumerable:true});Object.defineProperty(descriptors,b,{get(){events.push(\"b\");console.log(Object.hasOwn(p,a));return {value:9,enumerable:true};},enumerable:true});Object.defineProperties(p,descriptors);console.log(p[a],p[b],events.join(\",\"),Object.getOwnPropertySymbols(p)[1]===b);", "false\n7 9 a,b true\n"];
        yield return ["transparent-proxy", "const key=Symbol(\"key\"),p:any=Promise.resolve(1);p[key]=7;const proxy:any=new Proxy(p,{});console.log(proxy[key],Reflect.get(proxy,key),Object.getOwnPropertySymbols(proxy)[0]===key,Object.getOwnPropertyDescriptor(proxy,key)?.value);console.log(delete proxy[key],p[key]===undefined,Object.getOwnPropertySymbols(proxy).length);", "7 7 true 7\ntrue true 0\n"];
        yield return ["string-and-subclass", "class P extends Promise<number>{field=4;}const p:any=P.resolve(7),key=Symbol(\"key\");p[key]=9;p.extra=11;console.log(p.field,p.extra,p[key],p.then===p.then,p.constructor===P,Object.keys(p).join(\",\"));p.then((value:any)=>console.log(value));", "4 11 9 true true field,extra\n7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SymbolDescriptorsUseThePromiseOwnPropertyStore(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
