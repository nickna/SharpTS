using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericArrayLengthSetter3309Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-generic-accessors","const a:any=[];const f:any=a.push;let log='';const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v},set 1(v:number){log+='I'+v}};console.log(f.call(target,7),log,a.length);","2 LI7N2 0\n"];
        yield return ["push-own-accessors","const a:any=[];const f:any=a.push;let log='';const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v},set 1(v:number){log+='I'+v}};console.log(f.call(target,7),log,a.length);","2 LI7N2 0\n"];
        yield return ["push-setter-only-length","const f:any=Array.prototype.push;let log='';const target:any={set length(v:number){log+='N'+v},set 0(v:number){log+='I'+v}};console.log(f.call(target,7),log);","1 I7N1\n"];
        yield return ["push-getter-only-refusal","const f:any=Array.prototype.push;let log='';const target:any={get length(){log+='L';return 1},0:3};try{f.call(target,7)}catch(e){console.log(e instanceof TypeError,log,target[0],target[1])}","true L 3 7\n"];
        yield return ["push-throwing-setter","const f:any=Array.prototype.push;let log='';const token:any={};const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v;throw token},0:3};try{f.call(target,7)}catch(e){console.log(e===token,log,target[0],target[1])}","true LN2 3 7\n"];
        yield return ["push-readonly-data","const f:any=Array.prototype.push;const target:any={0:3,1:5};Object.defineProperty(target,'length',{value:2,writable:false});try{f.call(target,7)}catch(e){console.log(e instanceof TypeError,target.length,target[0],target[1],target[2])}","true 2 3 5 7\n"];
        yield return ["shift-own-accessors","const a:any=[];const f:any=a.shift;let log='';const target:any={get length(){log+='L';return 2},set length(v:number){log+='N'+v},get 0(){log+='A';return 3},set 0(v:number){log+='I'+v},get 1(){log+='B';return 7}};console.log(f.call(target),log,a.length);","3 LABI7N1 0\n"];
        yield return ["shift-setter-only-length","const f:any=Array.prototype.shift;let log='';const target:any={set length(v:number){log+='N'+v},set 0(v:number){log+='I'+v}};console.log(f.call(target),log);","undefined N0\n"];
        yield return ["shift-getter-only-refusal","const f:any=Array.prototype.shift;let log='';const target:any={get length(){log+='L';return 1},0:3};try{f.call(target)}catch(e){console.log(e instanceof TypeError,log,target[0],target[1])}","true L undefined undefined\n"];
        yield return ["shift-throwing-setter","const f:any=Array.prototype.shift;let log='';const token:any={};const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v;throw token},0:3};try{f.call(target)}catch(e){console.log(e===token,log,target[0],target[1])}","true LN0 undefined undefined\n"];
        yield return ["shift-readonly-data","const f:any=Array.prototype.shift;const target:any={0:3,1:5};Object.defineProperty(target,'length',{value:2,writable:false});try{f.call(target)}catch(e){console.log(e instanceof TypeError,target.length,target[0],target[1],target[2])}","true 2 5 undefined undefined\n"];
        yield return ["unshift-own-accessors","const a:any=[];const f:any=a.unshift;let log='';const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v},get 0(){log+='A';return 3},set 0(v:number){log+='I'+v},set 1(v:number){log+='J'+v}};console.log(f.call(target,7),log,a.length);","2 LAJ3I7N2 0\n"];
        yield return ["unshift-setter-only-length","const f:any=Array.prototype.unshift;let log='';const target:any={set length(v:number){log+='N'+v},set 0(v:number){log+='I'+v}};console.log(f.call(target,7),log);","1 I7N1\n"];
        yield return ["unshift-readonly-data","const f:any=Array.prototype.unshift;const target:any={0:3,1:5};Object.defineProperty(target,'length',{value:2,writable:false});try{f.call(target,7)}catch(e){console.log(e instanceof TypeError,target.length,target[0],target[1],target[2])}","true 2 7 3 5\n"];
        yield return ["frozen-accessor-setter","let stored=1,log='';const target:any={get length(){log+='L';return stored},set length(value:number){log+='N'+value;stored=value}};Object.freeze(target);const f:any=Array.prototype.push;console.log(f.call(target),stored,log);","1 1 LN1\n"];
        yield return ["undefined-setter-refusal","const target:any={0:1};Object.defineProperty(target,'length',{get(){return 1},set:undefined,configurable:true});try{Array.prototype.push.call(target,7)}catch(e){console.log(e instanceof TypeError,target[1])}","true 7\n"];
        yield return ["actual-array-readonly-length","const target:any=[3];Object.defineProperty(target,'length',{writable:false});try{Array.prototype.push.call(target,7)}catch(e){console.log(e instanceof TypeError,target.join(','),target.length)}","true 3 1\n"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GenericMutatorsSetAccessorLengthInOrder(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
