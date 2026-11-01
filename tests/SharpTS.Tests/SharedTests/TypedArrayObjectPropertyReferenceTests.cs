using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayObjectPropertyReferenceTests
{
    // Compiled ordinary-name indexing remains the independent P3 #3054.
    // Keep its exact Node expectations in interpretation, and verify the
    // already-supported compiled Symbol/numeric counterparts without skips.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["object-string-01", "const p:any=new Uint8Array([1,9]);Object.defineProperty(p,'01',{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);calls++;return '01'}};const result=p[key]+=2;console.log(result,Reflect.get(p,'01'),p[0],p[1],calls);", "string\nstring\n5 5 1 9 2\n", false];
        yield return ["object-string-1.0", "const p:any=new Uint8Array([1,9]);Object.defineProperty(p,'1.0',{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);calls++;return '1.0'}};const result=p[key]+=2;console.log(result,Reflect.get(p,'1.0'),p[0],p[1],calls);", "string\nstring\n5 5 1 9 2\n", false];
        yield return ["object-symbol", "const p:any=new Uint8Array([1,9]),symbol=Symbol('field');Object.defineProperty(p,symbol,{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);calls++;return symbol}};const result=p[key]+=2;console.log(result,Reflect.get(p,symbol),p[0],p[1],calls);", "string\nstring\n5 5 1 9 2\n", true];
        yield return ["object-numeric-to-name", "const p:any=new Uint8Array([1,9]);Object.defineProperty(p,'01',{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return ++calls===1?'0':'01'}};const result=p[key]+=2;console.log(result,Reflect.get(p,'01'),p[0],p[1],calls);", "string\nstring\n3 3 1 9 2\n", false];
        yield return ["object-name-to-numeric", "const p:any=new Uint8Array([1,9]);Object.defineProperty(p,'01',{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return ++calls===1?'01':'1'}};const result=p[key]+=2;console.log(result,Reflect.get(p,'01'),p[0],p[1],calls);", "string\nstring\n5 3 1 5 2\n", false];
        yield return ["object-name-postfix", "const p:any=new Uint8Array([1,9]);Object.defineProperty(p,'01',{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);calls++;return '01'}};const result=p[key]++;console.log(result,Reflect.get(p,'01'),p[0],p[1],calls);", "string\nstring\n3 4 1 9 2\n", false];
        yield return ["primitive-name-control", "const p:any=new Uint8Array([1,9]);Object.defineProperty(p,'01',{value:3,writable:true,configurable:true});console.log(p['01']+=2,Reflect.get(p,'01'),p[0],p[1]);", "5 5 1 9\n", false];
        yield return ["object-numeric-control", "const p:any=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);return ++calls===1?'0':'1'}};const result=p[key]+=2;console.log(result,p[0],p[1],calls);", "string\nstring\n3 1 3 2\n", true];
        yield return ["object-symbol-postfix", "const p:any=new Uint8Array([1,9]),symbol=Symbol('field');Object.defineProperty(p,symbol,{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){console.log(hint);calls++;return symbol}};const result=p[key]++;console.log(result,Reflect.get(p,symbol),p[0],p[1],calls);", "string\nstring\n3 4 1 9 2\n", true];
        yield return ["symbol-get-key-abrupt", "const p:any=new Uint8Array([1,9]),s=Symbol('field');let calls=0,rhs=0;const marker={};const key:any={[Symbol.toPrimitive](){calls++;throw marker}};try{p[key]+=(rhs++,2)}catch(e){console.log(e===marker)}console.log(calls,rhs,p[0],p[1]);", "true\n1 0 1 9\n", true];
        yield return ["symbol-put-key-abrupt", "const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3,writable:true,configurable:true});let calls=0,rhs=0;const marker={};const key:any={[Symbol.toPrimitive](){if(++calls===2)throw marker;return s}};try{p[key]+=(rhs++,2)}catch(e){console.log(e===marker)}console.log(calls,rhs,Reflect.get(p,s),p[0]);", "true\n2 1 3 1\n", true];
        yield return ["symbol-rhs-abrupt", "const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3,writable:true,configurable:true});let calls=0;const marker={};const key:any={[Symbol.toPrimitive](){calls++;return s}};function rhs():number{throw marker}try{p[key]+=rhs()}catch(e){console.log(e===marker)}console.log(calls,Reflect.get(p,s));", "true\n1 3\n", true];
        yield return ["symbol-getter-setter-receiver", "const p:any=new Uint8Array([1,9]),s=Symbol('field');let value=3,gets=0,sets=0,calls=0,receiver=true;Object.defineProperty(p,s,{get(){gets++;receiver=receiver&&this===p;return value},set(v:any){sets++;receiver=receiver&&this===p;value=v},configurable:true});const key:any={[Symbol.toPrimitive](){calls++;return s}};console.log(p[key]+=2,value,gets,sets,calls,receiver);", "5 5 1 1 2 true\n", true];
        yield return ["symbol-strict-readonly", "'use strict';const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};try{p[key]+=2}catch(e){console.log(e instanceof TypeError)}console.log(calls,Reflect.get(p,s));", "true\n2 3\n", true];
        yield return ["symbol-sloppy-readonly", "const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};console.log(p[key]+=2,calls,Reflect.get(p,s));", "5 2 3\n", true];
        yield return ["bigint-array-ordinary-number", "const p:any=new BigInt64Array([1n]),s=Symbol('field');Object.defineProperty(p,s,{value:3,writable:true});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};console.log(p[key]+=2,typeof Reflect.get(p,s),Reflect.get(p,s),p[0],calls);", "5 number 5 1n 2\n", true];
        yield return ["numeric-to-symbol-string-result", "const p:any=new Uint8Array([1,9]),s=Symbol('field');let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?'0':s}};const result=p[key]+='X';console.log(result,Reflect.get(p,s),p[0],p[1],calls);", "1X 1X 1 9 2\n", true];
        yield return ["symbol-to-numeric", "const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3,writable:true});let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?s:'1'}};console.log(p[key]+=2,Reflect.get(p,s),p[0],p[1],calls);", "5 3 1 5 2\n", true];
        yield return ["prefix-symbol", "const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3,writable:true});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};console.log(++p[key],Reflect.get(p,s),calls);", "4 4 2\n", true];
        yield return ["prefix-numeric-to-symbol", "const p:any=new Uint8Array([1,9]),s=Symbol('field');let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?'0':s}};console.log(++p[key],Reflect.get(p,s),p[0],calls);", "2 2 1 2\n", true];
        yield return ["named-postfix-value-before-put-key", "const p:any=new Uint8Array([1,9]);let log='';const old:any={valueOf(){log+='value,';return 7}};Object.defineProperty(p,'01',{value:old,writable:true});Object.defineProperty(p,'1.0',{value:99,writable:true});let calls=0;const key:any={[Symbol.toPrimitive](){log+='key,';return ++calls===1?'01':'1.0'}};console.log(p[key]++,Reflect.get(p,'01')===old,Reflect.get(p,'1.0'),log,calls);", "7 true 8 key,value,key, 2\n", false];
        yield return ["numeric-to-new-named-string", "const p:any=new Uint8Array([1,9]);let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?'0':'01'}};const result=p[key]+='X';console.log(result,Reflect.get(p,'01'),p[0],p[1],calls);", "1X 1X 1 9 2\n", false];
        yield return ["symbol-rhs-redefines-readonly", "'use strict';const p:any=new Uint8Array([1,9]),s=Symbol('field');Object.defineProperty(p,s,{value:3,writable:true,configurable:true});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};function rhs(){Object.defineProperty(p,s,{value:9,writable:false});return 2}try{p[key]+=rhs()}catch(e){console.log(e instanceof TypeError)}console.log(calls,Reflect.get(p,s));", "true\n2 9\n", true];
    }

    public static IEnumerable<object[]> CompiledCases() => Cases()
        .Where(row => (bool)row[3]).Select(row => row.Take(3).ToArray());

    [Theory, MemberData(nameof(Cases))]
    public void InterpretedReferencesKeepIndependentKeyPhases(
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
