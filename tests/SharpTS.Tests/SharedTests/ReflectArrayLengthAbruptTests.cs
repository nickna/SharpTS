using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectArrayLengthAbruptTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-prototype-negative", "const p:any=Array.prototype;try{console.log(Reflect.defineProperty(p,'length',{value:-1}));}catch(e:any){console.log(e.name);}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(p.length,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\n0 0 true false false\n"];
        yield return ["reflect-array-negative", "const p:any=[];try{console.log(Reflect.defineProperty(p,'length',{value:-1}));}catch(e:any){console.log(e.name);}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(p.length,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\n0 0 true false false\n"];
        yield return ["reflect-prototype-coercion-abrupt", "const p:any=Array.prototype,marker:any={tag:1};let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted');}catch(e){console.log(e===marker);}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["ordinary-reflect-refusal", "const p:any={x:1};Object.defineProperty(p,'x',{writable:false,configurable:false});console.log(Reflect.defineProperty(p,'x',{value:2}),p.x);", "false 1\n"];
        yield return ["reflect-prototype-fractional", "const p:any=Array.prototype;try{console.log(Reflect.defineProperty(p,'length',{value:2.5}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-prototype-nan", "const p:any=Array.prototype;try{console.log(Reflect.defineProperty(p,'length',{value:NaN}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-prototype-infinite", "const p:any=Array.prototype;try{console.log(Reflect.defineProperty(p,'length',{value:Infinity}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-prototype-overflow", "const p:any=Array.prototype;try{console.log(Reflect.defineProperty(p,'length',{value:4294967296}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-prototype-raw-null", "const p:any=Array.prototype,marker:any=null;let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted')}catch(e){console.log(e===marker)}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["reflect-prototype-raw-undefined", "const p:any=Array.prototype,marker:any=undefined;let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted')}catch(e){console.log(e===marker)}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["reflect-prototype-raw-object", "const p:any=Array.prototype,marker:any=({token:1});let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted')}catch(e){console.log(e===marker)}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["reflect-array-fractional", "const p:any=[];try{console.log(Reflect.defineProperty(p,'length',{value:2.5}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-array-nan", "const p:any=[];try{console.log(Reflect.defineProperty(p,'length',{value:NaN}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-array-infinite", "const p:any=[];try{console.log(Reflect.defineProperty(p,'length',{value:Infinity}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-array-overflow", "const p:any=[];try{console.log(Reflect.defineProperty(p,'length',{value:4294967296}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["reflect-array-raw-null", "const p:any=[],marker:any=null;let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted')}catch(e){console.log(e===marker)}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["reflect-array-raw-undefined", "const p:any=[],marker:any=undefined;let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted')}catch(e){console.log(e===marker)}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["reflect-array-raw-object", "const p:any=[],marker:any=({token:1});let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Reflect.defineProperty(p,'length',{value});console.log('accepted')}catch(e){console.log(e===marker)}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["invalid-before-attribute-refusal", "const p:any=Array.prototype;try{console.log(Reflect.defineProperty(p,'length',{value:-1,enumerable:true}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["readonly-valid-false", "const p:any=Array.prototype;Object.defineProperty(p,'length',{writable:false});let calls=0;const value:any={valueOf(){calls++;return 3}};console.log(Reflect.defineProperty(p,'length',{value}),calls);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "false 2\nnumber 0 false false false\n"];
        yield return ["readonly-invalid-still-abrupt", "const p:any=Array.prototype;Object.defineProperty(p,'length',{writable:false});try{console.log(Reflect.defineProperty(p,'length',{value:-1}))}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 false false false\n"];
        yield return ["nonextensible-false-control", "const p:any=[];Object.preventExtensions(p);console.log(Reflect.defineProperty(p,'probe',{value:1}),Object.prototype.hasOwnProperty.call(p,'probe'));", "false false\n"];
        yield return ["nonlength-value-is-not-coerced", "const p:any=[];let calls=0;const value:any={valueOf(){calls++;throw 'unexpected'}};console.log(Reflect.defineProperty(p,'probe',{value}),calls,p.probe===value);", "true 0 true\n"];
        yield return ["descriptor-abrupt-control", "const p:any=Array.prototype,marker:any={tag:1};let calls=0;try{Reflect.defineProperty(p,'length',{get value(){calls++;throw marker}})}catch(e){console.log(e===marker,calls)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true 1\nnumber 0 true false false\n"];
        yield return ["key-and-descriptor-once", "const p:any=Array.prototype;let log='';const key:any={[Symbol.toPrimitive](hint:string){log+=hint+',';return 'length'}};const value:any={valueOf(){log+='n,';return 2}};const desc:any={get value(){log+='value,';return value}};console.log(Reflect.defineProperty(p,key,desc),log,desc.value===value);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true string,value,n,n, true\nnumber 2 true false false\n"];
        yield return ["reentrant-readonly-same-value", "const p:any=Array.prototype;let calls=0;const value:any={valueOf(){calls++;Object.defineProperty(p,'length',{writable:false});return 0}};console.log(Reflect.defineProperty(p,'length',{value}),calls);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true 2\nnumber 0 false false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void DefinitionsPreserveArrayLengthCompletion(string name, string source, string expected, ExecutionMode mode)
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
