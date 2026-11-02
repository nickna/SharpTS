using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayOwnDescriptorIsolationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inherited-accessor-original-receiver", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let value=3,gets=0,sets=0,receiver=true;Object.defineProperty(proto,'tag',{get(){gets++;receiver=receiver&&this===a;return value},set(v:any){sets++;receiver=receiver&&this===a;value=v}});Object.setPrototypeOf(a,proto);console.log(a['tag']+=2,value,gets,sets,receiver,Object.hasOwn(a,'tag'),a[0]);", "5 5 1 1 true false 9\n", false];
        yield return ["inherited-descriptors-do-not-read-getter", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let calls=0;Object.defineProperty(proto,'tag',{get(){calls++;throw 7},configurable:true});Object.setPrototypeOf(a,proto);console.log(Object.getOwnPropertyDescriptor(a,'tag')===undefined,Reflect.getOwnPropertyDescriptor(a,'tag')===undefined,Object.hasOwn(a,'tag'),calls);", "true true false 0\n", true];
        yield return ["inherited-data-is-not-own", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));Object.defineProperty(proto,'tag',{value:7,writable:true,enumerable:true});Object.setPrototypeOf(a,proto);console.log(Object.getOwnPropertyDescriptor(a,'tag')===undefined,Reflect.getOwnPropertyDescriptor(a,'tag')===undefined,Object.hasOwn(a,'tag'));", "true true false\n", true];
        yield return ["inherited-undefined-is-not-own", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));Object.defineProperty(proto,'tag',{value:undefined});Object.setPrototypeOf(a,proto);console.log(Object.getOwnPropertyDescriptor(a,'tag')===undefined,Object.hasOwn(a,'tag'));", "true false\n", true];
        yield return ["reflect-set-inherited-setter-receiver", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let value=0,calls=0,receiver=false;Object.defineProperty(proto,'tag',{set(v:any){calls++;value=v;receiver=this===a}});Object.setPrototypeOf(a,proto);console.log(Reflect.set(a,'tag',7),value,calls,receiver,Object.hasOwn(a,'tag'),a[0]);", "true 7 1 true false 9\n", true];
        yield return ["direct-assignment-inherited-setter", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let value=0,calls=0,receiver=false;Object.defineProperty(proto,'tag',{set(v:any){calls++;value=v;receiver=this===a}});Object.setPrototypeOf(a,proto);a.tag=7;console.log(value,calls,receiver,Object.hasOwn(a,'tag'),a[0]);", "7 1 true false 9\n", false];
        yield return ["reflect-set-does-not-read-paired-getter", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let value=0,gets=0,sets=0;Object.defineProperty(proto,'tag',{get(){gets++;throw 7},set(v:any){sets++;value=v}});Object.setPrototypeOf(a,proto);console.log(Reflect.set(a,'tag',5),value,gets,sets,Object.hasOwn(a,'tag'));", "true 5 0 1 false\n", true];
        yield return ["reflect-set-writable-inherited-data-creates-own", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));Object.defineProperty(proto,'tag',{value:3,writable:true});Object.setPrototypeOf(a,proto);const result=Reflect.set(a,'tag',5),d=Object.getOwnPropertyDescriptor(a,'tag')!;console.log(result,d.value,d.writable,d.enumerable,d.configurable,Object.getOwnPropertyDescriptor(proto,'tag')!.value,a[0]);", "true 5 true true true 3 9\n", true];
        yield return ["reflect-set-readonly-inherited-data-refuses", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));Object.defineProperty(proto,'tag',{value:3,writable:false});Object.setPrototypeOf(a,proto);console.log(Reflect.set(a,'tag',5),Object.hasOwn(a,'tag'),Object.getOwnPropertyDescriptor(proto,'tag')!.value,a[0]);", "false false 3 9\n", true];
        yield return ["strict-inherited-getter-only-refuses", "'use strict';const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let calls=0;Object.defineProperty(proto,'tag',{get(){calls++;return 3}});Object.setPrototypeOf(a,proto);try{a.tag=5}catch(e){console.log(e instanceof TypeError)}console.log(calls,Object.hasOwn(a,'tag'),a[0]);", "true\n0 false 9\n", false];
        yield return ["inherited-setter-abrupt-identity", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));const marker={};let calls=0,receiver=false;Object.defineProperty(proto,'tag',{set(v:any){calls++;receiver=this===a;throw marker}});Object.setPrototypeOf(a,proto);try{Reflect.set(a,'tag',5)}catch(e){console.log(e===marker)}console.log(calls,receiver,Object.hasOwn(a,'tag'),a[0]);", "true\n1 true false 9\n", true];
        yield return ["own-accessor-precedes-inherited", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let own=0,inherited=0;Object.defineProperty(proto,'tag',{set(v:any){inherited++}});Object.defineProperty(a,'tag',{set(v:any){own+=v}});Object.setPrototypeOf(a,proto);console.log(Reflect.set(a,'tag',5),own,inherited,typeof Object.getOwnPropertyDescriptor(a,'tag')!.set,a[0]);", "true 5 0 function 9\n", true];
        yield return ["own-undefined-and-readonly-preserved", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));Object.defineProperty(proto,'tag',{set(v:any){throw 7}});Object.defineProperty(a,'tag',{value:undefined,writable:false});Object.setPrototypeOf(a,proto);const d=Object.getOwnPropertyDescriptor(a,'tag')!;console.log(d.value===undefined,d.writable,Reflect.set(a,'tag',5),Object.hasOwn(a,'tag'),a[0]);", "true false false true 9\n", true];
        yield return ["physical-index-descriptor-before-prototype", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let calls=0;Object.defineProperty(proto,'0',{get(){calls++;throw 7},set(v:any){calls++}});Object.setPrototypeOf(a,proto);const d=Object.getOwnPropertyDescriptor(a,'0')!;console.log(d.value,d.writable,d.enumerable,d.configurable,calls,Reflect.set(a,'0',5),a[0],calls);", "9 true true true 0 true 5 0\n", true];
        yield return ["builtin-instance-members-are-inherited", "const a:any=new Uint8Array([9]);console.log(Object.getOwnPropertyDescriptor(a,'map')===undefined,Object.getOwnPropertyDescriptor(a,'buffer')===undefined,Object.getOwnPropertyDescriptor(a,'length')===undefined,Object.getOwnPropertyDescriptor(a,'BYTES_PER_ELEMENT')===undefined);", "true true true true\n", true];
        yield return ["own-length-shadow-remains-own", "const a:any=new Uint8Array([9]);Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});const d=Object.getOwnPropertyDescriptor(a,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable,Reflect.set(a,'length',8),Object.getOwnPropertyDescriptor(a,'length')!.value,a[0]);", "7 true false true true 8 9\n", true];
        yield return ["explicit-null-absent-remains-absent", "const a:any=new Uint8Array([9]);Object.setPrototypeOf(a,null);console.log(Object.getOwnPropertyDescriptor(a,'tag')===undefined,Reflect.set(a,'tag',5),Object.getOwnPropertyDescriptor(a,'tag')!.value,a[0]);", "true true 5 9\n", true];
        yield return ["inherited-symbol-setter-control", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));const key=Symbol('tag');let value=0,receiver=false;Object.defineProperty(proto,key,{set(v:any){value=v;receiver=this===a}});Object.setPrototypeOf(a,proto);console.log(Reflect.set(a,key,5),value,receiver,Object.getOwnPropertyDescriptor(a,key)===undefined,a[0]);", "true 5 true true 9\n", true];
        yield return ["bigint-elements-inherited-ordinary-setter", "const a:any=new BigInt64Array([9n]),proto=Object.create(Object.getPrototypeOf(a));let value:any=0,receiver=false;Object.defineProperty(proto,'tag',{set(v:any){value=v;receiver=this===a}});Object.setPrototypeOf(a,proto);console.log(Reflect.set(a,'tag',5),value,receiver,String(a[0]));", "true 5 true 9\n", true];
        yield return ["ordinary-object-accessor-control", "const proto:any={},a:any=Object.create(proto);let value=0;Object.defineProperty(proto,'tag',{get(){throw 7},set(v:any){value=v}});console.log(Object.getOwnPropertyDescriptor(a,'tag')===undefined,Reflect.set(a,'tag',5),value,Object.hasOwn(a,'tag'));", "true true 5 false\n", true];
    }

    public static IEnumerable<object[]> CompiledCases() => Cases()
        .Where(row => (bool)row[3]).Select(row => row.Take(3).ToArray());

    [Theory, MemberData(nameof(Cases))]
    public void MissingOwnDescriptorsDoNotReadInheritedValues(string name, string source, string expected, bool compiledSupported)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        _ = compiledSupported;
    }

    // Direct named-property execution is the separate #3054 boundary. Reflection
    // and physical-index controls here are independently verified in full IL.
    [Theory, MemberData(nameof(CompiledCases))]
    public void SupportedCompiledDescriptorControlsPreserveOwnIdentity(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
