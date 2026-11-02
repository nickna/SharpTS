using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayStringReferenceTests
{
    // #3408 owns interpreter string compound/logical admission. Ordinary-name
    // compiled indexing remains P3 #3054; retain exact Node outcomes in the
    // interpreter and verify the supported compiled controls without skips.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["numeric-string-compound", "const a:any=new Uint8Array([4]);console.log(a[\"0\"]+=1,a[0]);\n", "5 5\n", true];
        yield return ["new-name-logical", "const a:any=new Uint8Array(1);console.log(a[\"tag\"]||=7,a[\"tag\"]);\n", "7 7\n", false];
        yield return ["numeric-string-logical-shortcircuit", "const a:any=new Uint8Array([0]);let calls=0;console.log(a[\"0\"]||=++calls,a[0],calls);console.log(a[\"0\"]||=++calls,a[0],calls);\n", "1 1 1\n1 1 1\n", true];
        yield return ["int8-narrowing-expression-result", "const a:any=new Int8Array([120]);const result=a['0']+=10;console.log(result,a[0]);", "130 -126\n", true];
        yield return ["clamped-expression-result", "const a:any=new Uint8ClampedArray([250]);const result=a['0']+=10;console.log(result,a[0]);", "260 255\n", true];
        yield return ["bigint-numeric-compound", "const a:any=new BigInt64Array([1n]);const result=a['0']+=2n;console.log(String(result),String(a[0]),typeof result);", "3 3 bigint\n", true];
        yield return ["bigint-mixed-operation-abrupt", "const a:any=new BigInt64Array([1n]);try{a['0']+=1}catch(e){console.log(e instanceof TypeError)}console.log(String(a[0]));", "true\n1\n", true];
        yield return ["operands-once-retained-string", "const a:any=new Uint8Array([1,9]);let targets=0,keys=0,rights=0,key='0';function target(){targets++;return a}function index(){keys++;return key}function rhs(){rights++;key='1';return 7}console.log(target()[index()]+=rhs(),a[0],a[1],targets,keys,rights,key);", "8 8 9 1 1 1 1\n", true];
        yield return ["numeric-rhs-abrupt", "const a:any=new Uint8Array([4]),marker={};let calls=0;function rhs():number{calls++;throw marker}try{a['0']+=rhs()}catch(e){console.log(e===marker)}console.log(calls,a[0]);", "true\n1 4\n", true];
        yield return ["numeric-logical-shortcircuit-operators", "const a:any=new Uint8Array([0,2]);let calls=0;console.log(a['0']&&=++calls,a[0],calls);console.log(a['1']&&=++calls,a[1],calls);console.log(a['0']??=++calls,a[0],calls);", "0 0 0\n1 1 1\n0 0 1\n", true];
        yield return ["numeric-logical-value-conversion-result", "const a:any=new Uint8Array([0]);let calls=0;const value:any={valueOf(){calls++;return 258}};const result=a['0']||=value;console.log(result===value,a[0],calls);", "true 2 1\n", true];
        yield return ["invalid-minus-zero-converts-value", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 7}};const result=a['-0']||=value;console.log(result===value,a[0],calls,Object.hasOwn(a,'-0'));", "true 9 1 false\n", false];
        yield return ["invalid-fractional-compound", "const a:any=new Uint8Array([9]);const result=a['0.5']+=2;console.log(Number.isNaN(result),a[0],Object.hasOwn(a,'0.5'));", "true 9 false\n", false];
        yield return ["invalid-index-value-abrupt", "const a:any=new Uint8Array([9]),marker={};let calls=0;const value:any={valueOf(){calls++;throw marker}};try{a['NaN']||=value}catch(e){console.log(e===marker)}console.log(a[0],calls,Object.hasOwn(a,'NaN'));", "true\n9 1 false\n", false];
        yield return ["noncanonical-new-ordinary-names", "const a:any=new Uint8Array([9]);console.log(a['01']||=3,a['1.0']??=4,Reflect.get(a,'01'),Reflect.get(a,'1.0'),a[0]);", "3 4 3 4 9\n", false];
        yield return ["new-ordinary-compound", "const a:any=new Uint8Array([9]);const result=a['tag']+=2;console.log(Number.isNaN(result),Number.isNaN(Reflect.get(a,'tag')),a[0]);", "true true 9\n", false];
        yield return ["ordinary-object-value-not-narrowed", "const a:any=new BigInt64Array([1n]);let calls=0;const value:any={valueOf(){calls++;return 7}};const result=a['tag']||=value;console.log(result===value,Reflect.get(a,'tag')===value,calls,String(a[0]));", "true true 0 1\n", false];
        yield return ["own-accessor-order-and-receiver", "const a:any=new Uint8Array([9]);let value=3,gets=0,sets=0,receiver=true;const log:string[]=[];Object.defineProperty(a,'tag',{get(){gets++;log.push('get');receiver=receiver&&this===a;return value},set(v:any){sets++;log.push('set');receiver=receiver&&this===a;value=v},configurable:true});function rhs(){log.push('rhs');return 2}console.log(a['tag']+=rhs(),value,gets,sets,receiver,log.join(','),a[0]);", "5 5 1 1 true get,rhs,set 9\n", false];
        yield return ["strict-own-readonly", "'use strict';const a:any=new Uint8Array([9]);Object.defineProperty(a,'tag',{value:3});let calls=0;function rhs(){calls++;return 2}try{a['tag']+=rhs()}catch(e){console.log(e instanceof TypeError)}console.log(calls,Reflect.get(a,'tag'),a[0]);", "true\n1 3 9\n", false];
        yield return ["sloppy-own-readonly", "const a:any=new Uint8Array([9]);Object.defineProperty(a,'tag',{value:3});console.log(a['tag']+=2,Reflect.get(a,'tag'),a[0]);", "5 3 9\n", false];
        yield return ["getter-abrupt-before-rhs", "const a:any=new Uint8Array([9]),marker={};let gets=0,rights=0;Object.defineProperty(a,'tag',{get(){gets++;throw marker}});function rhs(){rights++;return 2}try{a['tag']+=rhs()}catch(e){console.log(e===marker)}console.log(gets,rights,a[0]);", "true\n1 0 9\n", false];
        yield return ["setter-abrupt-preserves-reason", "const a:any=new Uint8Array([9]),marker={};let gets=0,sets=0;Object.defineProperty(a,'tag',{get(){gets++;return 3},set(v:any){sets++;throw marker}});try{a['tag']+=2}catch(e){console.log(e===marker)}console.log(gets,sets,a[0]);", "true\n1 1 9\n", false];
        yield return ["getter-only-logical-shortcircuit", "'use strict';const a:any=new Uint8Array([9]);let gets=0,rights=0;Object.defineProperty(a,'tag',{get(){gets++;return 3}});function rhs(){rights++;return 7}console.log(a['tag']||=rhs(),gets,rights,a[0]);", "3 1 0 9\n", false];
        yield return ["setter-only-logical-receiver", "const a:any=new Uint8Array([9]);let value=0,sets=0,receiver=false;Object.defineProperty(a,'tag',{set(v:any){sets++;receiver=this===a;value=v}});console.log(a['tag']||=7,value,sets,receiver,a[0]);", "7 7 1 true 9\n", false];
        yield return ["strict-nonextensible-new-name", "'use strict';const a:any=new Uint8Array([9]);Object.preventExtensions(a);let calls=0;function rhs(){calls++;return 7}try{a['tag']||=rhs()}catch(e){console.log(e instanceof TypeError)}console.log(calls,Object.hasOwn(a,'tag'),a[0]);", "true\n1 false 9\n", false];
        yield return ["sloppy-nonextensible-new-name", "const a:any=new Uint8Array([9]);Object.preventExtensions(a);console.log(a['tag']||=7,Object.hasOwn(a,'tag'),a[0]);", "7 false 9\n", false];
        yield return ["rhs-redefines-current-descriptor", "'use strict';const a:any=new Uint8Array([9]);Object.defineProperty(a,'tag',{value:3,writable:true,configurable:true});function rhs(){Object.defineProperty(a,'tag',{value:11,writable:false});return 2}try{a['tag']+=rhs()}catch(e){console.log(e instanceof TypeError)}console.log(Reflect.get(a,'tag'),a[0]);", "true\n11 9\n", false];
        yield return ["inherited-accessor-original-receiver", "const a:any=new Uint8Array([9]),proto=Object.create(Object.getPrototypeOf(a));let value=3,gets=0,sets=0,receiver=true;Object.defineProperty(proto,'tag',{get(){gets++;receiver=receiver&&this===a;return value},set(v:any){sets++;receiver=receiver&&this===a;value=v}});Object.setPrototypeOf(a,proto);console.log(a['tag']+=2,value,gets,sets,receiver,Object.hasOwn(a,'tag'),a[0]);", "5 5 1 1 true false 9\n", false];
        yield return ["bigint-array-ordinary-number", "const a:any=new BigInt64Array([1n]);Object.defineProperty(a,'tag',{value:3,writable:true});const result=a['tag']+=2;console.log(result,typeof Reflect.get(a,'tag'),Reflect.get(a,'tag'),String(a[0]));", "5 number 5 1\n", false];
        yield return ["async-numeric-reference", "const run=async()=>{const a:any=new Uint8Array([1]);const result=a['0']+=await Promise.resolve(2);console.log(result,a[0])};run();", "3 3\n", true];
        yield return ["raw-object-numeric-control", "const a:any=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?'0':'1'}};console.log(a[key]+=2,a[0],a[1],calls);", "3 1 3 2\n", true];
        yield return ["raw-symbol-control", "const a:any=new Uint8Array([9]),key=Symbol('tag');Object.defineProperty(a,key,{value:3,writable:true});console.log(a[key]+=2,Reflect.get(a,key),a[0]);", "5 5 9\n", true];
        yield return ["ordinary-array-string-control", "const a:any=[4];console.log(a['0']+=1,a[0]);", "5 5\n", true];
    }

    public static IEnumerable<object[]> CompiledCases() => Cases()
        .Where(row => (bool)row[3]).Select(row => row.Take(3).ToArray());

    [Theory, MemberData(nameof(Cases))]
    public void InterpretedStringReferencesPreserveGetPutAndShortCircuit(
        string name, string source, string expected, bool compiledSupported)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        _ = compiledSupported;
    }

    [Theory, MemberData(nameof(CompiledCases))]
    public void SupportedCompiledReferencesKeepTheSameOutcomes(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
