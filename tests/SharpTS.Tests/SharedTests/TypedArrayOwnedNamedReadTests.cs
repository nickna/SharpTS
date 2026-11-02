using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayOwnedNamedReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["existing-named-value-update", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,writable:true,configurable:true});Reflect.preventExtensions(a);console.log(Reflect.defineProperty(a,'x',{value:8}),a.x,Reflect.isExtensible(a));", "true 8 false\n"];
        yield return ["object-prevention-existing-bag", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,configurable:true});Object.preventExtensions(a);console.log(Reflect.isExtensible(a),a.x,Reflect.defineProperty(a,'y',{value:8}));", "false 7 false\n"];
        yield return ["sealed-view", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,writable:true,configurable:true});Object.seal(a);const d=Object.getOwnPropertyDescriptor(a,'x')!;console.log(Reflect.isExtensible(a),d.writable,d.configurable,Reflect.defineProperty(a,'x',{value:8}),a.x);", "false true false true 8\n"];
        yield return ["default-intrinsics", "const a:any=new Uint16Array(3);console.log(a.length,a.byteLength,a.byteOffset,a.BYTES_PER_ELEMENT,typeof a.slice);", "3 6 0 2 function\n"];
        yield return ["own-byte-length", "const a:any=new Uint8Array(2);Object.defineProperty(a,'byteLength',{value:7,configurable:true});console.log(a.byteLength,Reflect.get(a,'byteLength'));", "7 7\n"];
        yield return ["own-byte-offset", "const a:any=new Uint8Array(2);Object.defineProperty(a,'byteOffset',{value:7,configurable:true});console.log(a.byteOffset,Reflect.get(a,'byteOffset'));", "7 7\n"];
        yield return ["own-null-value", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:null});console.log(Object.hasOwn(a,'x'),a.x===null);", "true true\n"];
        yield return ["own-undefined-value", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:undefined});console.log(Object.hasOwn(a,'x'),a.x===undefined);", "true true\n"];
        yield return ["own-getter-receiver", "const a:any=new Uint8Array(1);let calls=0;Object.defineProperty(a,'x',{get(){calls++;return this===a?7:0}});console.log(a.x,calls);", "7 1\n"];
        yield return ["own-getter-abrupt", "const a:any=new Uint8Array(1);const marker={};Object.defineProperty(a,'x',{get(){throw marker}});try{console.log(a.x)}catch(e){console.log(e===marker)}", "true\n"];
        yield return ["getterless-own-accessor", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{set(v){}});console.log(Object.hasOwn(a,'x'),a.x===undefined);", "true true\n"];
        yield return ["delete-recreate-owned-slot", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,configurable:true});console.log(a.x,Reflect.deleteProperty(a,'x'),Object.hasOwn(a,'x'));Object.defineProperty(a,'x',{value:8});console.log(a.x);", "7 true false\n8\n"];
        yield return ["symbol-and-named-control", "const a:any=new Uint8Array(1);const s=Symbol('x');Object.defineProperty(a,'x',{value:7});Object.defineProperty(a,s,{value:8});console.log(a.x,a[s]);", "7 8\n"];
        yield return ["physical-element-control", "const a:any=new Uint8Array([9]);Object.defineProperty(a,'x',{value:7});console.log(a[0],a.x);a[0]=8;console.log(a[0],a.x);", "9 7\n8 7\n"];
        yield return ["all-element-kinds-owned-slots", "const views:any[]=[new Int8Array(1),new Uint8Array(1),new Uint8ClampedArray(1),new Int16Array(1),new Uint16Array(1),new Int32Array(1),new Uint32Array(1),new Float32Array(1),new Float64Array(1),new BigInt64Array(1),new BigUint64Array(1)];for(const a of views){Object.defineProperty(a,'x',{value:7});console.log(a.x);}", "7\n7\n7\n7\n7\n7\n7\n7\n7\n7\n7\n"];
        yield return ["method-value-shadow", "const a:any=new Uint8Array(1);const marker=()=>9;Object.defineProperty(a,'fill',{value:marker});const read=a.fill;console.log(read===marker,read());", "true 9\n"];
        yield return ["readonly-own-data", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,writable:false});console.log(a.x,Object.getOwnPropertyDescriptor(a,'x')!.writable);", "7 false\n"];
        yield return ["getter-identity-and-flags", "const a:any=new Uint8Array(1);let calls=0;const get=function(){calls++;return 7};Object.defineProperty(a,'x',{get,enumerable:true,configurable:true});const d=Object.getOwnPropertyDescriptor(a,'x')!;console.log(d.get===get,calls,d.enumerable,d.configurable,a.x,calls);", "true 0 true true 7 1\n"];
        yield return ["own-constructor-value", "const a:any=new Uint8Array(1);Object.defineProperty(a,'constructor',{value:23,configurable:true});console.log(a.constructor,Reflect.get(a,'constructor'));", "23 23\n"];
        yield return ["original-length-precedence", "const a:any=new Uint8Array(2);Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});a.length=8;console.log(a.length,Reflect.get(a,'length'));", "8 8\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void DirectReadsObserveOwnedDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
