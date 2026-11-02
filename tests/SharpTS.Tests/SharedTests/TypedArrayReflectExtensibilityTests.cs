using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayReflectExtensibilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-query-original", "const a:any=new Uint8Array(1);console.log(Reflect.isExtensible(a),Object.isExtensible(a));", "true true\n"];
        yield return ["reflect-prevention-original", "const a:any=new Uint8Array(1);console.log(Reflect.preventExtensions(a),Object.isExtensible(a),Reflect.isExtensible(a));console.log(Reflect.defineProperty(a,'x',{value:7,configurable:true}),Object.hasOwn(a,'x'));", "true false false\nfalse false\n"];
        yield return ["object-prevention-query-sync", "const a:any=new Uint8Array(1);Object.preventExtensions(a);console.log(Reflect.isExtensible(a),Object.isExtensible(a));", "false false\n"];
        yield return ["repeated-reflect-prevention", "const a:any=new Uint8Array(1);console.log(Reflect.preventExtensions(a),Reflect.preventExtensions(a),Reflect.isExtensible(a));", "true true false\n"];
        yield return ["all-element-kinds", "const views:any[]=[new Int8Array(1),new Uint8Array(1),new Uint8ClampedArray(1),new Int16Array(1),new Uint16Array(1),new Int32Array(1),new Uint32Array(1),new Float32Array(1),new Float64Array(1),new BigInt64Array(1),new BigUint64Array(1)];for(const a of views){console.log(Reflect.isExtensible(a),Reflect.preventExtensions(a),Reflect.isExtensible(a));}", "true true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\ntrue true false\n"];
        yield return ["empty-view", "const a:any=new Uint8Array(0);console.log(Reflect.isExtensible(a),Reflect.preventExtensions(a),Reflect.isExtensible(a),Reflect.ownKeys(a).length);", "true true false 0\n"];
        yield return ["shared-view", "const a:any=new Uint8Array(new SharedArrayBuffer(2));console.log(Reflect.isExtensible(a),Reflect.preventExtensions(a),Reflect.isExtensible(a),Reflect.defineProperty(a,'x',{value:1}));", "true true false false\n"];
        yield return ["existing-named-value-update", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,writable:true,configurable:true});Reflect.preventExtensions(a);console.log(Reflect.defineProperty(a,'x',{value:8}),a.x,Reflect.isExtensible(a));", "true 8 false\n"];
        yield return ["new-symbol-refusal", "const a:any=new Uint8Array(1);const s=Symbol('x');Reflect.preventExtensions(a);console.log(Reflect.defineProperty(a,s,{value:7}),Object.hasOwn(a,s),Reflect.isExtensible(a));", "false false false\n"];
        yield return ["existing-symbol-update", "const a:any=new Uint8Array(1);const s=Symbol('x');Object.defineProperty(a,s,{value:7,writable:true,configurable:true});Reflect.preventExtensions(a);console.log(Reflect.defineProperty(a,s,{value:8}),a[s],Reflect.isExtensible(a));", "true 8 false\n"];
        yield return ["accessor-not-invoked", "const a:any=new Uint8Array(1);let gets=0;Object.defineProperty(a,'x',{get(){gets++;return 7},configurable:true});console.log(Reflect.isExtensible(a),Reflect.preventExtensions(a),gets,typeof Object.getOwnPropertyDescriptor(a,'x')!.get);", "true true 0 function\n"];
        yield return ["descriptor-flags-preserved", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,writable:true,enumerable:true,configurable:true});Reflect.preventExtensions(a);const d=Object.getOwnPropertyDescriptor(a,'x')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "7 true true true\n"];
        yield return ["owned-keys-preserved", "const a:any=new Uint8Array(2);const s=Symbol('x');Object.defineProperty(a,'x',{value:7,configurable:true});Object.defineProperty(a,s,{value:8,configurable:true});Reflect.preventExtensions(a);const keys=Reflect.ownKeys(a);console.log(keys.length,keys[0],keys[1],keys[2],keys[3]===s);", "4 0 1 x true\n"];
        yield return ["lazy-bag-prevention", "const a:any=new Int32Array(1);Reflect.preventExtensions(a);console.log(Reflect.defineProperty(a,'x',{value:7}),Reflect.defineProperty(a,Symbol('x'),{value:8}),Reflect.isExtensible(a));", "false false false\n"];
        yield return ["object-prevention-existing-bag", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,configurable:true});Object.preventExtensions(a);console.log(Reflect.isExtensible(a),a.x,Reflect.defineProperty(a,'y',{value:8}));", "false 7 false\n"];
        yield return ["frozen-empty-view", "const a:any=new Uint8Array(0);Object.defineProperty(a,'x',{value:7,writable:true,configurable:true});Object.freeze(a);const d=Object.getOwnPropertyDescriptor(a,'x')!;console.log(Reflect.isExtensible(a),d.writable,d.configurable,Reflect.defineProperty(a,'x',{value:8}));", "false false false false\n"];
        yield return ["sealed-view", "const a:any=new Uint8Array(1);Object.defineProperty(a,'x',{value:7,writable:true,configurable:true});Object.seal(a);const d=Object.getOwnPropertyDescriptor(a,'x')!;console.log(Reflect.isExtensible(a),d.writable,d.configurable,Reflect.defineProperty(a,'x',{value:8}),a.x);", "false true false true 8\n"];
        yield return ["physical-elements-stay-writable", "const a:any=new Uint8Array([1]);Reflect.preventExtensions(a);a[0]=7;console.log(a[0],Reflect.set(a,'0',9),a[0],Reflect.isExtensible(a));", "7 true 9 false\n"];
        yield return ["aliased-reflect-methods", "const a:any=new Uint8Array(1);const query=Reflect.isExtensible,prevent=Reflect.preventExtensions;console.log(query(a),prevent(a),query(a));", "true true false\n"];
        yield return ["ordinary-object-array-controls", "const xs:any[]=[{},[]];for(const x of xs)console.log(Reflect.isExtensible(x),Reflect.preventExtensions(x),Reflect.isExtensible(x));", "true true false\ntrue true false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectObservesTypedArrayExtensibility(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
