using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayPrototypeLengthValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-object-negative", "const p:any=Array.prototype;try{Object.defineProperty(p,'length',{value:-1});console.log('accepted');}catch(e:any){console.log(e.name);}console.log(p.length);", "RangeError\n0\n"];
        yield return ["original-reflect-string-normalized", "const p:any=Array.prototype;console.log(Reflect.defineProperty(p,'length',{value:'3'}));const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof p.length,p.length,typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true\nnumber 3 number 3 true false false\n"];
        yield return ["object-fractional", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:2.5})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["object-nan", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:NaN})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["object-infinite", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:Infinity})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["object-overflow", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:4294967296})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["object-undefined", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:undefined})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "RangeError\nnumber 0 true false false\n"];
        yield return ["object-negative-zero", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:-0})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true\nnumber 0 true false false\n"];
        yield return ["object-null", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:null})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true\nnumber 0 true false false\n"];
        yield return ["object-boolean", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:true})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true\nnumber 1 true false false\n"];
        yield return ["object-max-uint", "const p:any=Array.prototype;try{console.log(Object.defineProperty(p,'length',{value:4294967295})===p)}catch(e:any){console.log(e.name)}const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true\nnumber 4294967295 true false false\n"];
        yield return ["object-string-normalized", "const p:any=Array.prototype;Object.defineProperty(p,'length',{value:'3'});const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "number 3 true false false\n"];
        yield return ["object-two-number-coercions", "const p:any=Array.prototype;let log='';const value:any={[Symbol.toPrimitive](hint:string){log+=hint+',';return 3}};Object.defineProperty(p,'length',{value});console.log(log);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "number,number,\nnumber 3 true false false\n"];
        yield return ["reflect-two-number-coercions", "const p:any=Array.prototype;let log='';const value:any={[Symbol.toPrimitive](hint:string){log+=hint+',';return 3}};Reflect.defineProperty(p,'length',{value});console.log(log);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "number,number,\nnumber 3 true false false\n"];
        yield return ["object-abrupt-value-identity", "const p:any=Array.prototype,marker:any={tag:1};let calls=0;const value:any={valueOf(){calls++;throw marker}};try{Object.defineProperty(p,'length',{value});console.log('accepted');}catch(e){console.log(e===marker);}console.log(calls,Object.getOwnPropertyDescriptor(p,'length')!.value===0);", "true\n1 true\n"];
        yield return ["descriptor-get-before-coercion", "const p:any=Array.prototype;let log='';const value:any={valueOf(){log+='n';return 2}};const desc:any={get enumerable(){log+='e';return false},get configurable(){log+='c';return false},get value(){log+='v';return value},get writable(){log+='w';return true}};Object.defineProperty(p,'length',desc);console.log(log);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "ecvwnn\nnumber 2 true false false\n"];
        yield return ["readonly-same-value-converts", "const p:any=Array.prototype;Object.defineProperty(p,'length',{writable:false});let calls=0;const value:any={valueOf(){calls++;return 0}};console.log(Object.defineProperty(p,'length',{value})===p,calls);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "true 2\nnumber 0 false false false\n"];
        yield return ["omitted-value-never-recoerces", "const p:any=Array.prototype;let calls=0;const value:any={valueOf(){calls++;return 2}};Object.defineProperty(p,'length',{value});Object.defineProperty(p,'length',{writable:false});Object.defineProperty(p,'length',{});console.log(calls);const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "2\nnumber 2 false false false\n"];
        yield return ["ordinary-object-length-unchanged", "const p:any={};Object.defineProperty(p,'length',{value:-1});console.log(p.length);const child:any=Object.create(Array.prototype);Object.defineProperty(child,'length',{value:-1});console.log(child.length);", "-1\n-1\n"];
        yield return ["ordinary-array-string-control", "const p:any=[];Object.defineProperty(p,'length',{value:'3'});const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(typeof d.value,d.value,d.writable,d.enumerable,d.configurable);", "number 3 true false false\n"];
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
