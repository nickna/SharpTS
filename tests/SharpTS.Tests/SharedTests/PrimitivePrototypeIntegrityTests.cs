using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeIntegrityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-frozen-constructor", "const p:any=String.prototype;const old=p.constructor;Object.freeze(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor===old);", "false true\n"];
        yield return ["string-sealed-update", "const p:any=String.prototype;const old=p.constructor;Object.seal(p);const rejected=Reflect.defineProperty(p,'constructor',{configurable:true});const same=p.constructor===old;const updated=Reflect.defineProperty(p,'constructor',{value:9});const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(rejected,same,updated,p.constructor,d.writable,d.enumerable,d.configurable);", "false true true 9 true false false\n"];
        yield return ["string-frozen-same-value-flags", "const p:any=String.prototype;const old=p.toString;Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(Reflect.defineProperty(p,'toString',{value:old}),p.toString===old,d?.writable,d?.enumerable,d?.configurable);", "true true false false false\n"];
        yield return ["string-prevented-update-control", "const p:any=String.prototype;Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor,Object.getOwnPropertyDescriptor(p,'constructor')!.configurable);", "true 9 true\n"];
        yield return ["string-frozen-accessor", "const p:any=String.prototype;let gets=0,value=0;const get=()=>{gets++;return 7};Object.defineProperty(p,'probe',{get,set(v:any){value=v},configurable:true});Object.freeze(p);const changed=Reflect.defineProperty(p,'probe',{get:()=>8});p.probe=9;const d:any=Object.getOwnPropertyDescriptor(p,'probe');console.log(gets,changed,d.get===get,p.probe,gets,value,d.configurable);", "0 false true 7 1 9 false\n"];
        yield return ["string-deleted-stays-deleted", "const p:any=String.prototype;delete p.toString;Object.freeze(p);console.log(Reflect.defineProperty(p,'toString',{value:9}),Object.prototype.hasOwnProperty.call(p,'toString'),Object.getOwnPropertyDescriptor(p,'toString')===undefined);", "false false true\n"];
        yield return ["number-frozen-constructor", "const p:any=Number.prototype;const old=p.constructor;Object.freeze(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor===old);", "false true\n"];
        yield return ["number-sealed-update", "const p:any=Number.prototype;const old=p.constructor;Object.seal(p);const rejected=Reflect.defineProperty(p,'constructor',{configurable:true});const same=p.constructor===old;const updated=Reflect.defineProperty(p,'constructor',{value:9});const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(rejected,same,updated,p.constructor,d.writable,d.enumerable,d.configurable);", "false true true 9 true false false\n"];
        yield return ["number-frozen-same-value-flags", "const p:any=Number.prototype;const old=p.toString;Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(Reflect.defineProperty(p,'toString',{value:old}),p.toString===old,d?.writable,d?.enumerable,d?.configurable);", "true true false false false\n"];
        yield return ["number-prevented-update-control", "const p:any=Number.prototype;Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor,Object.getOwnPropertyDescriptor(p,'constructor')!.configurable);", "true 9 true\n"];
        yield return ["number-frozen-accessor", "const p:any=Number.prototype;let gets=0,value=0;const get=()=>{gets++;return 7};Object.defineProperty(p,'probe',{get,set(v:any){value=v},configurable:true});Object.freeze(p);const changed=Reflect.defineProperty(p,'probe',{get:()=>8});p.probe=9;const d:any=Object.getOwnPropertyDescriptor(p,'probe');console.log(gets,changed,d.get===get,p.probe,gets,value,d.configurable);", "0 false true 7 1 9 false\n"];
        yield return ["number-deleted-stays-deleted", "const p:any=Number.prototype;delete p.toString;Object.freeze(p);console.log(Reflect.defineProperty(p,'toString',{value:9}),Object.prototype.hasOwnProperty.call(p,'toString'),Object.getOwnPropertyDescriptor(p,'toString')===undefined);", "false false true\n"];
        yield return ["boolean-frozen-constructor", "const p:any=Boolean.prototype;const old=p.constructor;Object.freeze(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor===old);", "false true\n"];
        yield return ["boolean-sealed-update", "const p:any=Boolean.prototype;const old=p.constructor;Object.seal(p);const rejected=Reflect.defineProperty(p,'constructor',{configurable:true});const same=p.constructor===old;const updated=Reflect.defineProperty(p,'constructor',{value:9});const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(rejected,same,updated,p.constructor,d.writable,d.enumerable,d.configurable);", "false true true 9 true false false\n"];
        yield return ["boolean-frozen-same-value-flags", "const p:any=Boolean.prototype;const old=p.toString;Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(Reflect.defineProperty(p,'toString',{value:old}),p.toString===old,d?.writable,d?.enumerable,d?.configurable);", "true true false false false\n"];
        yield return ["boolean-prevented-update-control", "const p:any=Boolean.prototype;Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor,Object.getOwnPropertyDescriptor(p,'constructor')!.configurable);", "true 9 true\n"];
        yield return ["boolean-frozen-accessor", "const p:any=Boolean.prototype;let gets=0,value=0;const get=()=>{gets++;return 7};Object.defineProperty(p,'probe',{get,set(v:any){value=v},configurable:true});Object.freeze(p);const changed=Reflect.defineProperty(p,'probe',{get:()=>8});p.probe=9;const d:any=Object.getOwnPropertyDescriptor(p,'probe');console.log(gets,changed,d.get===get,p.probe,gets,value,d.configurable);", "0 false true 7 1 9 false\n"];
        yield return ["boolean-deleted-stays-deleted", "const p:any=Boolean.prototype;delete p.toString;Object.freeze(p);console.log(Reflect.defineProperty(p,'toString',{value:9}),Object.prototype.hasOwnProperty.call(p,'toString'),Object.getOwnPropertyDescriptor(p,'toString')===undefined);", "false false true\n"];
        yield return ["array-frozen-constructor", "const p:any=Array.prototype;const old=p.constructor;Object.freeze(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor===old);", "false true\n"];
        yield return ["array-sealed-update", "const p:any=Array.prototype;const old=p.constructor;Object.seal(p);const rejected=Reflect.defineProperty(p,'constructor',{configurable:true});const same=p.constructor===old;const updated=Reflect.defineProperty(p,'constructor',{value:9});const d:any=Object.getOwnPropertyDescriptor(p,'constructor');console.log(rejected,same,updated,p.constructor,d.writable,d.enumerable,d.configurable);", "false true true 9 true false false\n"];
        yield return ["array-frozen-same-value-flags", "const p:any=Array.prototype;const old=p.toString;Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'toString');console.log(Reflect.defineProperty(p,'toString',{value:old}),p.toString===old,d?.writable,d?.enumerable,d?.configurable);", "true true false false false\n"];
        yield return ["array-prevented-update-control", "const p:any=Array.prototype;Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'constructor',{value:9}),p.constructor,Object.getOwnPropertyDescriptor(p,'constructor')!.configurable);", "true 9 true\n"];
        yield return ["array-frozen-accessor", "const p:any=Array.prototype;let gets=0,value=0;const get=()=>{gets++;return 7};Object.defineProperty(p,'probe',{get,set(v:any){value=v},configurable:true});Object.freeze(p);const changed=Reflect.defineProperty(p,'probe',{get:()=>8});p.probe=9;const d:any=Object.getOwnPropertyDescriptor(p,'probe');console.log(gets,changed,d.get===get,p.probe,gets,value,d.configurable);", "0 false true 7 1 9 false\n"];
        yield return ["array-deleted-stays-deleted", "const p:any=Array.prototype;delete p.toString;Object.freeze(p);console.log(Reflect.defineProperty(p,'toString',{value:9}),Object.prototype.hasOwnProperty.call(p,'toString'),Object.getOwnPropertyDescriptor(p,'toString')===undefined);", "false false true\n"];
        yield return ["array-frozen-physical-length", "const p:any=Array.prototype;Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,'length');console.log(Reflect.defineProperty(p,'length',{value:3}),p.length,d.writable,d.enumerable,d.configurable);", "false 0 false false false\n"];
        yield return ["array-sealed-physical-length", "const p:any=Array.prototype;Object.seal(p);console.log(Reflect.defineProperty(p,'length',{value:3}),p.length,Object.getOwnPropertyDescriptor(p,'length')!.configurable);", "true 3 false\n"];
        yield return ["string-deleted-frozen-assignment", "const p:any=String.prototype;delete p.toString;Object.freeze(p);p.toString=9;let threw=false;try{(function(){'use strict';p.toString=8})()}catch(e){threw=e instanceof TypeError}console.log(Object.hasOwn(p,'toString'),p.toString===9,threw);", "false false true\n"];
        yield return ["string-sealed-delete", "const p:any=String.prototype;const old=p.toString;Object.seal(p);console.log(delete p.toString,Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
        yield return ["number-deleted-frozen-assignment", "const p:any=Number.prototype;delete p.toString;Object.freeze(p);p.toString=9;let threw=false;try{(function(){'use strict';p.toString=8})()}catch(e){threw=e instanceof TypeError}console.log(Object.hasOwn(p,'toString'),p.toString===9,threw);", "false false true\n"];
        yield return ["number-sealed-delete", "const p:any=Number.prototype;const old=p.toString;Object.seal(p);console.log(delete p.toString,Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
        yield return ["boolean-deleted-frozen-assignment", "const p:any=Boolean.prototype;delete p.toString;Object.freeze(p);p.toString=9;let threw=false;try{(function(){'use strict';p.toString=8})()}catch(e){threw=e instanceof TypeError}console.log(Object.hasOwn(p,'toString'),p.toString===9,threw);", "false false true\n"];
        yield return ["boolean-sealed-delete", "const p:any=Boolean.prototype;const old=p.toString;Object.seal(p);console.log(delete p.toString,Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
        yield return ["array-deleted-frozen-assignment", "const p:any=Array.prototype;delete p.toString;Object.freeze(p);p.toString=9;let threw=false;try{(function(){'use strict';p.toString=8})()}catch(e){threw=e instanceof TypeError}console.log(Object.hasOwn(p,'toString'),p.toString===9,threw);", "false false true\n"];
        yield return ["array-sealed-delete", "const p:any=Array.prototype;const old=p.toString;Object.seal(p);console.log(delete p.toString,Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
        yield return ["string-freeze-iterator", "const p:any=String.prototype;const old=p[Symbol.iterator];Object.freeze(p);const d:any=Object.getOwnPropertyDescriptor(p,Symbol.iterator);console.log(p[Symbol.iterator]===old,d.writable,d.enumerable,d.configurable);", "true false false false\n"];
        yield return ["string-seal-iterator", "const p:any=String.prototype;const old=p[Symbol.iterator];Object.seal(p);const d:any=Object.getOwnPropertyDescriptor(p,Symbol.iterator);console.log(p[Symbol.iterator]===old,d.writable,d.enumerable,d.configurable);", "true true false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OwnedPrototypeIntegrityMatchesJavaScript(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
