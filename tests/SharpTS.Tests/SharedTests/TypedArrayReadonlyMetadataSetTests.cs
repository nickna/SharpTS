using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayReadonlyMetadataSetTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-readonly-no-index-read", "const a:any=new Uint8Array([1]);for(const name of [\"length\",\"byteLength\",\"byteOffset\",\"buffer\",\"BYTES_PER_ELEMENT\"]){console.log(name,Reflect.set(a,name,123),Object.hasOwn(a,name));}\n", "length false false\nbyteLength false false\nbyteOffset false false\nbuffer false false\nBYTES_PER_ELEMENT false false\n"];
        yield return ["refuse-length", "const a:any=new Uint8Array([1]);let calls=0;const v:any={valueOf(){calls++;return 9}};const receiver:any={};console.log(Reflect.set(a,\"length\",v,receiver),Object.hasOwn(a,\"length\"),Object.hasOwn(receiver,\"length\"),calls);\n", "false false false 0\n"];
        yield return ["refuse-byteLength", "const a:any=new Uint8Array([1]);let calls=0;const v:any={valueOf(){calls++;return 9}};const receiver:any={};console.log(Reflect.set(a,\"byteLength\",v,receiver),Object.hasOwn(a,\"byteLength\"),Object.hasOwn(receiver,\"byteLength\"),calls);\n", "false false false 0\n"];
        yield return ["refuse-byteOffset", "const a:any=new Uint8Array([1]);let calls=0;const v:any={valueOf(){calls++;return 9}};const receiver:any={};console.log(Reflect.set(a,\"byteOffset\",v,receiver),Object.hasOwn(a,\"byteOffset\"),Object.hasOwn(receiver,\"byteOffset\"),calls);\n", "false false false 0\n"];
        yield return ["refuse-buffer", "const a:any=new Uint8Array([1]);let calls=0;const v:any={valueOf(){calls++;return 9}};const receiver:any={};console.log(Reflect.set(a,\"buffer\",v,receiver),Object.hasOwn(a,\"buffer\"),Object.hasOwn(receiver,\"buffer\"),calls);\n", "false false false 0\n"];
        yield return ["refuse-BYTES_PER_ELEMENT", "const a:any=new Uint8Array([1]);let calls=0;const v:any={valueOf(){calls++;return 9}};const receiver:any={};console.log(Reflect.set(a,\"BYTES_PER_ELEMENT\",v,receiver),Object.hasOwn(a,\"BYTES_PER_ELEMENT\"),Object.hasOwn(receiver,\"BYTES_PER_ELEMENT\"),calls);\n", "false false false 0\n"];
        yield return ["owned-writable-shadow", "const a:any=new Uint8Array([1]);for(const name of [\"length\",\"byteLength\",\"byteOffset\",\"buffer\",\"BYTES_PER_ELEMENT\"]){Object.defineProperty(a,name,{value:7,writable:true,configurable:true});console.log(name,Reflect.set(a,name,8),Object.getOwnPropertyDescriptor(a,name)!.value);}\n", "length true 8\nbyteLength true 8\nbyteOffset true 8\nbuffer true 8\nBYTES_PER_ELEMENT true 8\n"];
        yield return ["owned-setter-receiver", "const a:any=new Uint8Array(1),receiver:any={};let gets=0,sets=0;Object.defineProperty(a,\"byteLength\",{get(){gets++;return 3},set(v){sets++;console.log(this===receiver,v)}});console.log(Reflect.set(a,\"byteLength\",9,receiver),gets,sets);\n", "true 9\ntrue 0 1\n"];
        yield return ["owned-getter-refusal", "const a:any=new Uint8Array(1);let gets=0;Object.defineProperty(a,\"byteOffset\",{get(){gets++;return 3}});console.log(Reflect.set(a,\"byteOffset\",9),gets);\n", "false 0\n"];
        yield return ["explicit-null-prototype", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,null);for(const name of [\"length\",\"byteLength\",\"byteOffset\",\"buffer\",\"BYTES_PER_ELEMENT\"]){console.log(name,Reflect.set(a,name,8),Object.getOwnPropertyDescriptor(a,name)!.value);}\n", "length true 8\nbyteLength true 8\nbyteOffset true 8\nbuffer true 8\nBYTES_PER_ELEMENT true 8\n"];
        yield return ["explicit-writable-prototype", "const a:any=new Uint8Array(1);Object.setPrototypeOf(a,{byteLength:3});console.log(Reflect.set(a,\"byteLength\",8),Object.getOwnPropertyDescriptor(a,\"byteLength\")!.value);\n", "true 8\n"];
        yield return ["explicit-readonly-prototype", "const a:any=new Uint8Array(1);let gets=0;Object.setPrototypeOf(a,{get byteLength(){gets++;return 3}});console.log(Reflect.set(a,\"byteLength\",8),Object.hasOwn(a,\"byteLength\"),gets);\n", "false false 0\n"];
        yield return ["explicit-inherited-setter", "const a:any=new Uint8Array(1),receiver:any={};let calls=0;Object.setPrototypeOf(a,{set buffer(v:any){calls++;console.log(this===receiver,v)}});console.log(Reflect.set(a,\"buffer\",8,receiver),calls,Object.hasOwn(receiver,\"buffer\"));\n", "true 8\ntrue 1 false\n"];
        yield return ["sloppy-transparent-proxy", "const a:any=new Uint8Array([1]),p:any=new Proxy(a,{});p.byteLength=9;console.log(a.byteLength,Object.hasOwn(a,\"byteLength\"));\n", "1 false\n"];
        yield return ["strict-transparent-proxy", "\"use strict\";const a:any=new Uint8Array([1]),p:any=new Proxy(a,{});try{p.byteLength=9}catch(e){console.log(e instanceof TypeError)}console.log(a.byteLength,Object.hasOwn(a,\"byteLength\"));\n", "true\n1 false\n"];
        yield return ["nonextensible-implicit", "const a:any=new Uint8Array(1);Object.preventExtensions(a);console.log(Reflect.set(a,\"byteLength\",9),Reflect.set(a,\"buffer\",9),Object.hasOwn(a,\"buffer\"));\n", "false false false\n"];
        yield return ["owned-readonly-shadow", "const a:any=new Uint8Array(1);Object.defineProperty(a,\"byteLength\",{value:7,writable:false});console.log(Reflect.set(a,\"byteLength\",8),a.byteLength);\n", "false 7\n"];
        yield return ["abrupt-owned-setter", "const a:any=new Uint8Array(1),marker={};Object.defineProperty(a,\"buffer\",{set(v){throw marker}});try{Reflect.set(a,\"buffer\",8)}catch(e){console.log(e===marker)}\n", "true\n"];
        yield return ["key-once-and-symbol", "const a:any=new Uint8Array(1),s=Symbol(\"s\");let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return \"byteLength\"}};console.log(Reflect.set(a,key,8),calls,Reflect.set(a,s,9),Reflect.get(a,s));\n", "false 1 true 9\n"];
        yield return ["physical-and-ordinary-controls", "const a:any=new Uint8Array([1]),o:any={byteLength:3};console.log(Reflect.set(a,\"0\",7),a[0],Reflect.set(o,\"byteLength\",8),o.byteLength);\n", "true 7 true 8\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReadonlyMetadataRespectsActualDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
