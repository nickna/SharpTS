using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeIndexedKeyUpdateTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-numeric-original", "const p:any=Number.prototype;Object.defineProperty(p,'0',{value:1,writable:true,configurable:true});console.log(p[0]++,p[0]);\r\n", "1 2\n"];
        yield return ["number-object-original", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});let calls=0;const key:any={toString(){calls++;return 'probe'}};console.log(p[key]++,p.probe,calls);\r\n", "1 2 2\n"];
        yield return ["number-symbol-original", "const p:any=Number.prototype,key=Symbol('probe');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});console.log(p[key]++,p[key]);\r\n", "1 2\n"];
        yield return ["number-numeric-prefix-decrement", "const p:any=Number.prototype;Object.defineProperty(p,'0',{value:1,writable:true,configurable:true});console.log(--p[0],p[0]);\n", "0 0\n"];
        yield return ["number-symbol-postfix-decrement", "const p:any=Number.prototype,key=Symbol('probe');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});console.log(p[key]--,p[key]);\n", "1 0\n"];
        yield return ["number-object-swaps-read-write", "const p:any=Number.prototype;Object.defineProperty(p,'read',{value:1,writable:true,configurable:true});Object.defineProperty(p,'write',{value:9,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){if(hint!=='string')throw 0;return ++calls===1?'read':'write'}};console.log(p[key]++,p.read,p.write,calls);\n", "1 1 2 2\n"];
        yield return ["string-numeric-original", "const p:any=String.prototype;Object.defineProperty(p,'0',{value:1,writable:true,configurable:true});console.log(p[0]++,p[0]);\n", "1 2\n"];
        yield return ["string-object-original", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});let calls=0;const key:any={toString(){calls++;return 'probe'}};console.log(p[key]++,p.probe,calls);\n", "1 2 2\n"];
        yield return ["string-symbol-original", "const p:any=String.prototype,key=Symbol('probe');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});console.log(p[key]++,p[key]);\n", "1 2\n"];
        yield return ["string-numeric-prefix-decrement", "const p:any=String.prototype;Object.defineProperty(p,'0',{value:1,writable:true,configurable:true});console.log(--p[0],p[0]);\n", "0 0\n"];
        yield return ["string-symbol-postfix-decrement", "const p:any=String.prototype,key=Symbol('probe');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});console.log(p[key]--,p[key]);\n", "1 0\n"];
        yield return ["string-object-swaps-read-write", "const p:any=String.prototype;Object.defineProperty(p,'read',{value:1,writable:true,configurable:true});Object.defineProperty(p,'write',{value:9,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){if(hint!=='string')throw 0;return ++calls===1?'read':'write'}};console.log(p[key]++,p.read,p.write,calls);\n", "1 1 2 2\n"];
        yield return ["boolean-numeric-original", "const p:any=Boolean.prototype;Object.defineProperty(p,'0',{value:1,writable:true,configurable:true});console.log(p[0]++,p[0]);\n", "1 2\n"];
        yield return ["boolean-object-original", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:1,writable:true,configurable:true});let calls=0;const key:any={toString(){calls++;return 'probe'}};console.log(p[key]++,p.probe,calls);\n", "1 2 2\n"];
        yield return ["boolean-symbol-original", "const p:any=Boolean.prototype,key=Symbol('probe');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});console.log(p[key]++,p[key]);\n", "1 2\n"];
        yield return ["boolean-numeric-prefix-decrement", "const p:any=Boolean.prototype;Object.defineProperty(p,'0',{value:1,writable:true,configurable:true});console.log(--p[0],p[0]);\n", "0 0\n"];
        yield return ["boolean-symbol-postfix-decrement", "const p:any=Boolean.prototype,key=Symbol('probe');Object.defineProperty(p,key,{value:1,writable:true,configurable:true});console.log(p[key]--,p[key]);\n", "1 0\n"];
        yield return ["boolean-object-swaps-read-write", "const p:any=Boolean.prototype;Object.defineProperty(p,'read',{value:1,writable:true,configurable:true});Object.defineProperty(p,'write',{value:9,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){if(hint!=='string')throw 0;return ++calls===1?'read':'write'}};console.log(p[key]++,p.read,p.write,calls);\n", "1 1 2 2\n"];
        yield return ["object-reference-value-order", "const p:any=Number.prototype,log:string[]=[];let current=7,bases=0,keys=0;const old:any={[Symbol.toPrimitive](hint:string){log.push('value:'+hint);return current}};Object.defineProperty(p,'probe',{get(){log.push('get');return old},set(value:any){if(this!==p)throw 0;log.push('set:'+value);current=value},configurable:true});const raw:any={[Symbol.toPrimitive](hint:string){log.push('key:'+hint);return 'probe'}};function target(){bases++;log.push('target');return p}function key(){keys++;log.push('key-expression');return raw}console.log(target()[key()]++,current,bases,keys,log.join(','));\n", "7 8 1 1 target,key-expression,key:string,get,value:number,key:string,set:8\n"];
        yield return ["strict-symbol-readonly", "'use strict';const p:any=Number.prototype,s=Symbol('field');Object.defineProperty(p,s,{value:1,writable:false});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};try{p[key]++}catch(e:any){console.log(e.name,calls,p[s])}\n", "TypeError 2 1\n"];
        yield return ["sloppy-numeric-readonly", "const p:any=Boolean.prototype;Object.defineProperty(p,'0',{value:1,writable:false});console.log(++p[0],p[0]);\n", "2 1\n"];
        yield return ["sloppy-object-getter-only", "const p:any=String.prototype;let gets=0,calls=0;Object.defineProperty(p,'probe',{get(){gets++;return 1},configurable:true});const key:any={[Symbol.toPrimitive](){calls++;return 'probe'}};console.log(++p[key],gets,calls,Object.getOwnPropertyDescriptor(p,'probe')!.set===undefined);\n", "2 1 2 true\n"];
        yield return ["get-key-abrupt", "const p:any=Number.prototype,marker={};let keys=0,values=0;const value:any={[Symbol.toPrimitive](){values++;return 1}};Object.defineProperty(p,'probe',{value,writable:true});const key:any={[Symbol.toPrimitive](){keys++;throw marker}};try{p[key]++}catch(e){console.log(e===marker,keys,values,p.probe===value)}\n", "true 1 0 true\n"];
        yield return ["value-abrupt-prevents-put-key", "const p:any=Boolean.prototype,marker={};let keys=0,values=0;const value:any={[Symbol.toPrimitive](){values++;throw marker}};Object.defineProperty(p,'probe',{value,writable:true});const key:any={[Symbol.toPrimitive](){keys++;return 'probe'}};try{p[key]++}catch(e){console.log(e===marker,keys,values,p.probe===value)}\n", "true 1 1 true\n"];
        yield return ["put-key-abrupt", "const p:any=String.prototype,marker={};let calls=0;Object.defineProperty(p,'probe',{value:1,writable:true});const key:any={[Symbol.toPrimitive](){if(++calls===2)throw marker;return 'probe'}};try{p[key]++}catch(e){console.log(e===marker,calls,p.probe)}\n", "true 2 1\n"];
        yield return ["symbol-identity", "const p:any=Number.prototype,a=Symbol('same'),b=Symbol('same');Object.defineProperty(p,a,{value:1,writable:true});Object.defineProperty(p,b,{value:9,writable:true});console.log(p[a]++,p[a],p[b]);\n", "1 2 9\n"];
        yield return ["bigint-key-and-value", "const p:any=Boolean.prototype,key:any=1n;Object.defineProperty(p,'1',{value:2n,writable:true});console.log(++p[key],p[key],typeof p[key]);\n", "3n 3n bigint\n"];
        yield return ["null-property-key", "const p:any=Boolean.prototype,key:any=null;Object.defineProperty(p,'null',{value:1,writable:true});console.log(p[key]++,p['null']);\n", "1 2\n"];
        yield return ["undefined-property-key", "const p:any=Boolean.prototype,key:any=undefined;Object.defineProperty(p,'undefined',{value:1,writable:true});console.log(p[key]++,p['undefined']);\n", "1 2\n"];
        yield return ["boolean-property-key", "const p:any=Boolean.prototype,key:any=true;Object.defineProperty(p,'true',{value:1,writable:true});console.log(p[key]++,p['true']);\n", "1 2\n"];
        yield return ["awaited-reference-once", "async function run(){const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:1,writable:true});let bases=0,expressions=0,conversions=0;const key:any={[Symbol.toPrimitive](){conversions++;return 'probe'}};function target(){bases++;return p}function index(){expressions++;return key}console.log((await Promise.resolve(target()))[await Promise.resolve(index())]++,p.probe,bases,expressions,conversions)}void run();\n", "1 2 1 1 2\n"];
        yield return ["ordinary-receiver-control", "const p:any={read:1,write:9};let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?'read':'write'}};console.log(p[key]++,p.read,p.write,calls);\n", "1 1 2 2\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RawUpdateKeysUseSeparateGetAndPutConversions(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
