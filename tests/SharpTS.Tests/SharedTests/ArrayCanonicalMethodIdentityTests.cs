using SharpTS.Tests.Infrastructure;
using Xunit;
namespace SharpTS.Tests.SharedTests;
public sealed class ArrayCanonicalMethodIdentityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-original", "const value:any=[];const fn=value.push;console.log(fn===value.push,fn===Array.prototype.push);", "true true\n"];
        yield return ["vm-original", "import * as vm from 'node:vm';const value:any=[];const fn:any=vm.runInNewContext('value.push',{value});fn.call(value,7);console.log(typeof fn,fn===value.push,value.join(','));", "function true 7\n"];
        yield return ["constructor-input", "import * as vm from 'node:vm';const a:any=new Array();const f:any=vm.runInNewContext('a.push',{a});console.log(f===a.push,f===Array.prototype.push);", "true true\n"];
        yield return ["map-input", "import * as vm from 'node:vm';const a:any=[1].map(v=>v);const f:any=vm.runInNewContext('a.push',{a});console.log(f===a.push,f===Array.prototype.push);", "true true\n"];
        yield return ["result-producers", "import * as vm from 'node:vm';const arrays:any[]=[Array.from([1]),Array.of(1),[1].map(v=>v),[1].filter(v=>true),[1].flatMap(v=>[v]),[1].slice(),[1].concat(),[1].flat(),[1].toSorted(),[1].toReversed(),[1].toSpliced(0),([1] as number[]).with(0,2),[1].splice(0,1),[1].entries().next().value];for(const a of arrays){const f:any=vm.runInNewContext('a.push',{a});console.log(f===a.push,f===Array.prototype.push)}", "true true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\ntrue true\n"];
        yield return ["foreign-result-producers", "import * as vm from 'node:vm';const arrays:any[]=vm.runInNewContext('[Array.from([1]),Array.of(1),[1].map(v=>v),[1].slice()]');for(const a of arrays)console.log(a.push===Array.prototype.push,Object.getPrototypeOf(a)===Array.prototype);", "false false\nfalse false\nfalse false\nfalse false\n"];
        yield return ["method-table", "const a:any=[];const p:any=Array.prototype;console.log(a.push===p.push,a.join===p.join,a.map===p.map,a.values===p.values,a.toSorted===p.toSorted);", "true true true true true\n"];
        yield return ["detached-and-borrowed", "const a:any=[],b:any=[];const f:any=a.push;try{f(1)}catch(e){console.log((e as Error).name)}console.log(f.call(b,7),a.length,b.join(','));", "TypeError\n1 0 7\n"];
        yield return ["generic-borrow", "const a:any=[];const f:any=a.push;const target:any={length:0};console.log(f.call(target,7),target[0],target.length);", "1 7 1\n"];
        yield return ["descriptor-precedence", "const a:any=[];const f:any=()=>7;Object.defineProperty(a,'push',{value:f,configurable:true});console.log(a.push===f);delete a.push;console.log(a.push===Array.prototype.push);", "true\ntrue\n"];
        yield return ["prototype-accessor-receiver", "const a:any=[];const f:any=()=>7;let correct=false;Object.defineProperty(Array.prototype,'push',{get(){correct=this===a;return f},configurable:true});console.log(a.push===f,correct);", "true true\n"];
        yield return ["deleted-prototype-method", "const a:any=[];delete (Array.prototype as any).push;console.log(typeof a.push);", "undefined\n"];
        yield return ["foreign-allocation-realm", "import * as vm from 'node:vm';const a:any=vm.runInNewContext('[]');console.log(a.push===Array.prototype.push,Object.getPrototypeOf(a)===Array.prototype);", "false false\n"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void MethodReadsPreserveCanonicalIdentityAndCallReceivers(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string,string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }
}
