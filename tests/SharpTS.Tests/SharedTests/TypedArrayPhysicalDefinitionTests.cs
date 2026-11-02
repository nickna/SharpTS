using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayPhysicalDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-physical-index-descriptor-before-prototype", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let calls=0;Object.defineProperty(proto,'0',{get(){calls++;throw 7},set(v:any){calls++}});Object.setPrototypeOf(a,proto);const d=Object.getOwnPropertyDescriptor(a,'0')!;console.log(d.value,d.writable,d.enumerable,d.configurable,calls,Reflect.set(a,'0',5),a[0],calls);", "9 true true true 0 true 5 0\n"];
        yield return ["default-prototype-reflect-set", "const a:any=new Uint8Array([9]);console.log(Reflect.set(a,'0',5),a[0]);", "true 5\n"];
        yield return ["null-prototype-physical-set", "const a:any=new Uint8Array([9]);Object.setPrototypeOf(a,null);console.log(Reflect.set(a,'0',5),Reflect.get(a,'0'),a[0]);", "true 5 5\n"];
        yield return ["object-value-only-definition", "const a:any=new Uint8Array([9]);console.log(Object.defineProperty(a,'0',{value:5})===a,a[0]);const d=Object.getOwnPropertyDescriptor(a,'0')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "true 5\n5 true true true\n"];
        yield return ["reflect-empty-descriptor", "const a:any=new Uint8Array([9]);console.log(Reflect.defineProperty(a,'0',{}),a[0]);", "true 9\n"];
        yield return ["explicit-compatible-flags", "const a:any=new Uint8Array([9]);console.log(Reflect.defineProperty(a,'0',{value:5,writable:true,enumerable:true,configurable:true}),a[0]);", "true 5\n"];
        yield return ["refused-writable-before-conversion", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 5}};console.log(Reflect.defineProperty(a,'0',{value,writable:false}),a[0],calls);", "false 9 0\n"];
        yield return ["refused-enumerable-before-conversion", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 5}};console.log(Reflect.defineProperty(a,'0',{value,enumerable:false}),a[0],calls);", "false 9 0\n"];
        yield return ["refused-configurable-before-conversion", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 5}};console.log(Reflect.defineProperty(a,'0',{value,configurable:false}),a[0],calls);", "false 9 0\n"];
        yield return ["accessor-undefined-refusal", "const a:any=new Uint8Array([9]);console.log(Reflect.defineProperty(a,'0',{get:undefined}),a[0]);", "false 9\n"];
        yield return ["setter-descriptor-refusal", "const a:any=new Uint8Array([9]);let calls=0;console.log(Reflect.defineProperty(a,'0',{set(v:any){calls++}}),a[0],calls);", "false 9 0\n"];
        yield return ["descriptor-observation-before-value-conversion", "const a:any=new Uint8Array([9]),log:string[]=[];const value:any={valueOf(){log.push('number');return 5}};const d:any={get enumerable(){log.push('enumerable');return true},get configurable(){log.push('configurable');return true},get value(){log.push('value');return value},get writable(){log.push('writable');return true}};console.log(Reflect.defineProperty(a,'0',d),a[0],log.join(','));", "true 5 enumerable,configurable,value,writable,number\n"];
        yield return ["reflect-abrupt-value-conversion", "const a:any=new Uint8Array([9]),marker={};let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(a,'0',{value})}catch(e){console.log(e===marker)}console.log(a[0],calls);", "true\n9 1\n"];
        yield return ["object-abrupt-value-conversion", "const a:any=new Uint8Array([9]),marker={};let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Object.defineProperty(a,'0',{value})}catch(e){console.log(e===marker)}console.log(a[0],calls);", "true\n9 1\n"];
        yield return ["object-refusal-throws-typeerror", "const a:any=new Uint8Array([9]);try{Object.defineProperty(a,'0',{value:5,writable:false})}catch(e){console.log(e instanceof TypeError)}console.log(a[0]);", "true\n9\n"];
        yield return ["nonextensible-existing-physical-slot", "const a:any=new Uint8Array([9]);Object.preventExtensions(a);console.log(Reflect.set(a,'0',5),Reflect.defineProperty(a,'0',{value:7}),a[0],Object.isExtensible(a));", "true true 7 false\n"];
        yield return ["different-ordinary-receiver", "const a:any=new Uint8Array([9]),receiver:any={};console.log(Reflect.set(a,'0',5,receiver),a[0],receiver['0']);", "true 9 5\n"];
        yield return ["different-typedarray-receiver", "const a:any=new Uint8Array([9]),receiver:any=new Uint8Array([4]);console.log(Reflect.set(a,'0',5,receiver),a[0],receiver[0]);", "true 9 5\n"];
        yield return ["primitive-receiver-refuses", "const a:any=new Uint8Array([9]);console.log(Reflect.set(a,'0',5,7),a[0]);", "false 9\n"];
        yield return ["property-key-converts-once", "const a:any=new Uint8Array([9]);let keys=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);keys++;return '0'}};console.log(Reflect.set(a,key,5),a[0],keys);", "string\ntrue 5 1\n"];
        yield return ["numeric-object-value-once-and-narrowing", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 258}};console.log(Reflect.set(a,'0',value),a[0],calls);", "true 2 1\n"];
        yield return ["bigint-physical-definition", "const a:any=new BigInt64Array([1n]);console.log(Reflect.set(a,'0',3n),String(a[0]),Reflect.defineProperty(a,'0',{value:5n}),String(a[0]));", "true 3 true 5\n"];
        yield return ["all-eleven-physical-element-kinds", "const a0:any=new Int8Array([9]);console.log('Int8Array',Reflect.set(a0,'0',5),String(a0[0]));const a1:any=new Uint8Array([9]);console.log('Uint8Array',Reflect.set(a1,'0',5),String(a1[0]));const a2:any=new Uint8ClampedArray([9]);console.log('Uint8ClampedArray',Reflect.set(a2,'0',5),String(a2[0]));const a3:any=new Int16Array([9]);console.log('Int16Array',Reflect.set(a3,'0',5),String(a3[0]));const a4:any=new Uint16Array([9]);console.log('Uint16Array',Reflect.set(a4,'0',5),String(a4[0]));const a5:any=new Int32Array([9]);console.log('Int32Array',Reflect.set(a5,'0',5),String(a5[0]));const a6:any=new Uint32Array([9]);console.log('Uint32Array',Reflect.set(a6,'0',5),String(a6[0]));const a7:any=new Float32Array([9]);console.log('Float32Array',Reflect.set(a7,'0',5),String(a7[0]));const a8:any=new Float64Array([9]);console.log('Float64Array',Reflect.set(a8,'0',5),String(a8[0]));const a9:any=new BigInt64Array([9n]);console.log('BigInt64Array',Reflect.set(a9,'0',5n),String(a9[0]));const a10:any=new BigUint64Array([9n]);console.log('BigUint64Array',Reflect.set(a10,'0',5n),String(a10[0]));", "Int8Array true 5\nUint8Array true 5\nUint8ClampedArray true 5\nInt16Array true 5\nUint16Array true 5\nInt32Array true 5\nUint32Array true 5\nFloat32Array true 5\nFloat64Array true 5\nBigInt64Array true 5\nBigUint64Array true 5\n"];
        yield return ["subview-byteoffset-storage", "const buffer=new ArrayBuffer(8),all:any=new Uint16Array(buffer);all[1]=9;all[2]=10;const a:any=new Uint16Array(buffer,2,2);console.log(Reflect.set(a,'0',5),all[0],all[1],all[2],all[3],a[0],a[1]);", "true 0 5 10 0 5 10\n"];
        yield return ["omitted-value-compatible-flags", "const a:any=new Uint8Array([9]);console.log(Reflect.defineProperty(a,'0',{enumerable:true}),a[0]);const d=Object.getOwnPropertyDescriptor(a,'0')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "true 9\n9 true true true\n"];
        yield return ["repeated-value-only-definitions", "const a:any=new Uint8Array([9]);Object.defineProperty(a,'0',{value:5});console.log(Reflect.defineProperty(a,'0',{value:7}),a[0]);const d=Object.getOwnPropertyDescriptor(a,'0')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "true 7\n7 true true true\n"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void InterpreterUpdatesTheActualPhysicalReceiver(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SavedCompilerUpdatesTheActualPhysicalReceiver(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
