using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayRawNumericReferenceInterpreterTests
{
    // Raw-number interpreter references; compiler key writes are tracked by #3416.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-array-control", "const a:any=[9];a[0.5]=5;console.log(a[0.5]+=2,a[0],a.length);", "7 9 1\n"];
        yield return ["negative-fraction-compound", "const a:any=[9];a[-0.5]=5;console.log(a[-0.5]+=2,a[0],a.length);", "7 9 1\n"];
        yield return ["nonfinite-compounds", "for(const k of [NaN,Infinity,-Infinity]){const a:any=[9];a[k]=5;console.log(a[k]+=2,a[k],a[0],a.length)}", "7 7 9 1\n7 7 9 1\n7 7 9 1\n"];
        yield return ["int32-overflow-compound", "const a:any=[9],k=2147483648;a[k]=5;console.log(a[k]+=2,a[k],a[0],a.length);", "7 7 9 2147483649\n"];
        yield return ["uint32-max-ordinary-name", "const a:any=[9],k=4294967295;a[k]=5;console.log(a[k]+=2,a[k],a[0],a.length);", "7 7 9 1\n"];
        yield return ["last-valid-array-index", "const a:any=[9],k=4294967294;a[k]=5;console.log(a[k]+=2,a[k],a[0],a.length);", "7 7 9 4294967295\n"];
        yield return ["negative-integer-compound", "const a:any=[9];a[-1]=5;console.log(a[-1]+=2,a[-1],a[0],a.length);", "7 7 9 1\n"];
        yield return ["numeric-negative-zero-compound", "const a:any=[9];console.log(a[-0]+=2,a[0],a.length,Object.hasOwn(a,'-0'));", "11 11 1 false\n"];
        yield return ["fraction-prefix-postfix", "const a:any=[9];a[0.5]=5;console.log(a[0.5]++,++a[0.5],a[0.5]--,--a[0.5],a[0.5],a[0],a.length);", "5 7 7 5 5 9 1\n"];
        yield return ["nonfinite-prefix-postfix", "for(const k of [NaN,Infinity,-Infinity]){const a:any=[9];a[k]=5;console.log(a[k]++,++a[k],a[k],a[0],a.length)}", "5 7 7 9 1\n5 7 7 9 1\n5 7 7 9 1\n"];
        yield return ["large-prefix-postfix", "for(const k of [2147483648,4294967295]){const a:any=[9];a[k]=5;console.log(a[k]++,++a[k],a[k],a[0],a.length)}", "5 7 7 9 2147483649\n5 7 7 9 1\n"];
        yield return ["numeric-negative-zero-update", "const a:any=[9];console.log(a[-0]++,++a[-0],a[0],a.length,Object.hasOwn(a,'-0'));", "9 11 11 1 false\n"];
        yield return ["own-accessor-get-rhs-put-order", "const a:any=[9],log:string[]=[];let value=5,receiver=true;Object.defineProperty(a,'0.5',{get(){log.push('get');receiver=receiver&&this===a;return value},set(v:any){log.push('set');receiver=receiver&&this===a;value=v}});function rhs(){log.push('rhs');return 2}console.log(a[0.5]+=rhs(),value,receiver,log.join(','),a[0],a.length);", "7 7 true get,rhs,set 9 1\n"];
        yield return ["own-accessor-update", "const a:any=[9],log:string[]=[];let value=5,receiver=true;Object.defineProperty(a,'0.5',{get(){log.push('get');receiver=receiver&&this===a;return value},set(v:any){log.push('set');receiver=receiver&&this===a;value=v}});console.log(a[0.5]++,value,receiver,log.join(','),a[0],a.length);", "5 6 true get,set 9 1\n"];
        yield return ["inherited-accessor-update", "const a:any=[9],p=Object.create(Array.prototype);let value=5,gets=0,sets=0,receiver=true;Object.defineProperty(p,'0.5',{get(){gets++;receiver=receiver&&this===a;return value},set(v:any){sets++;receiver=receiver&&this===a;value=v}});Object.setPrototypeOf(a,p);console.log(++a[0.5],value,gets,sets,receiver,Object.hasOwn(a,'0.5'),a[0]);", "6 6 1 1 true false 9\n"];
        yield return ["strict-readonly-compound", "'use strict';const a:any=[9];Object.defineProperty(a,'0.5',{value:5,writable:false});try{a[0.5]+=2}catch(e){console.log(e instanceof TypeError)}console.log(a[0.5],a[0],a.length);", "true\n5 9 1\n"];
        yield return ["strict-readonly-update", "'use strict';const a:any=[9];Object.defineProperty(a,'0.5',{value:5,writable:false});try{a[0.5]++}catch(e){console.log(e instanceof TypeError)}console.log(a[0.5],a[0],a.length);", "true\n5 9 1\n"];
        yield return ["sloppy-readonly-result", "const a:any=[9];Object.defineProperty(a,'0.5',{value:5,writable:false});console.log(a[0.5]+=2,a[0.5],a[0],a.length);", "7 5 9 1\n"];
        yield return ["rhs-redefinition-is-observed", "'use strict';const a:any=[9],log:string[]=[];Object.defineProperty(a,'0.5',{get(){log.push('get');return 5},configurable:true});function rhs(){log.push('rhs');Object.defineProperty(a,'0.5',{value:20,writable:false});return 2}try{a[0.5]+=rhs()}catch(e){console.log(e instanceof TypeError)}console.log(a[0.5],log.join(','),a[0]);", "true\n20 get,rhs 9\n"];
        yield return ["awaited-reference-and-rhs", "async function run(){const a:any=[9],log:string[]=[];a[0.5]=5;let key=0.5;async function base(){log.push('base');return a}async function index(){log.push('key');return key}async function rhs(){log.push('rhs');key=0;return 2}const result=(await base())[await index()]+=await rhs();console.log(result,a[0.5],a[0],a.length,log.join(','))}void run();", "7 7 9 1 base,key,rhs\n"];
        yield return ["awaited-postfix-reference", "async function run(){const a:any=[9];a[0.5]=5;console.log((await Promise.resolve(a))[await Promise.resolve(0.5)]++,a[0.5],a[0],a.length)}void run();", "5 6 9 1\n"];
        yield return ["logical-write-fraction", "const a:any=[9];a[0.5]=0;let calls=0;console.log(a[0.5]||=(calls++,7),a[0.5],a[0],calls,a.length);", "7 7 9 1 1\n"];
        yield return ["logical-short-circuit-and-selected-put", "const a:any=[9];a[NaN]=3;let calls=0;console.log(a[NaN]||=(calls++,8),a[NaN]&&=(calls++,7),a[NaN]??=(calls++,4),a[NaN],a[0],calls,a.length);", "3 7 7 7 9 1 1\n"];
        yield return ["receiver-and-key-evaluated-once", "const a:any=[9];a[0.5]=5;let bases=0,keys=0;function base(){bases++;return a}function key(){keys++;return 0.5}console.log(base()[key()]++,a[0.5],bases,keys,a[0]);", "5 6 1 1 9\n"];
        yield return ["bigint-valued-named-update", "const a:any=[9];a[0.5]=5n;console.log(String(a[0.5]++),String(++a[0.5]),String(a[0.5]),a[0],a.length);", "5 7 7 9 1\n"];
        yield return ["canonical-index-accessor-update", "const a:any=[9];let value=5,gets=0,sets=0;Object.defineProperty(a,'0',{get(){gets++;return value},set(v:any){sets++;value=v}});console.log(a[0]++,value,gets,sets,a.length);", "5 6 1 1 1\n"];
        yield return ["getter-abrupt-before-rhs", "const a:any=[9],marker={};let rhs=0;Object.defineProperty(a,'0.5',{get(){throw marker}});try{a[0.5]+=(rhs++,2)}catch(e){console.log(e===marker)}console.log(rhs,a[0],a.length);", "true\n0 9 1\n"];
        yield return ["numeric-conversion-abrupt-before-put", "const a:any=[9],marker={};let calls=0;const value={valueOf(){calls++;throw marker}};a[0.5]=value;try{a[0.5]++}catch(e){console.log(e===marker)}console.log(a[0.5]===value,calls,a[0],a.length);", "true\ntrue 1 9 1\n"];
        yield return ["logical-accessor-rhs-order", "const a:any=[9],log:string[]=[];let value=0;Object.defineProperty(a,'0.5',{get(){log.push('get');return value},set(v:any){log.push('set');value=v}});function rhs(){log.push('rhs');return 7}console.log(a[0.5]||=rhs(),value,log.join(','),a[0]);", "7 7 get,rhs,set 9\n"];
        yield return ["canonical-integer-compound-control", "const a:any=[9,5];console.log(a[1]+=2,a[1]++,++a[1],a[0],a.length);", "7 7 9 9 2\n"];
        yield return ["nonextensible-existing-fraction", "const a:any=[9];a[0.5]=5;Object.preventExtensions(a);console.log(a[0.5]+=2,a[0.5]++,a[0.5],a[0],a.length);", "7 7 8 9 1\n"];
        yield return ["ordinary-object-brand-control", "const a:any={0:9,'0.5':5};console.log(a[0.5]+=2,a[0.5]++,a[0.5],a[0]);", "7 7 8 9\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void RetainsTheActualNumericPropertyKey(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
