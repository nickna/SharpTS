using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayObjectIndexReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["get-detaches-owner-original", "const a:any=new Uint8Array([7]);let calls=0;const k:any={toString(){calls++;structuredClone(a.buffer,{transfer:[a.buffer]});return '0'}};console.log(a[k],calls,a.length);", "undefined 1 0\n", false];
        yield return ["put-original", "const a:any=new Uint8Array([7]);let calls=0;const k:any={toString(){calls++;return '0'}};a[k]=9;console.log(a[0],calls);", "9 1\n", true];
        yield return ["numeric-get-string-hint", "const a:any=new Uint8Array([7]);let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return \"0\"}};console.log(a[key],calls);", "string\n7 1\n", true];
        yield return ["get-base-key-once", "let bases=0,keys=0,hooks=0;const a:any=new Uint8Array([7]);function base(){bases++;return a}function key():any{keys++;return {toString(){hooks++;return \"0\"}}}console.log(base()[key()],bases,keys,hooks);", "7 1 1 1\n", true];
        yield return ["put-rhs-before-key", "const events:string[]=[];const a:any=new Uint8Array([1]);function base(){events.push(\"base\");return a}function key():any{events.push(\"key-expression\");return {toString(){events.push(\"key-conversion\");return \"0\"}}}function value(){events.push(\"rhs\");return 9}const result=(base()[key()]=value());console.log(events.join(\",\"),result,a[0]);", "base,key-expression,rhs,key-conversion 9 9\n", true];
        yield return ["abrupt-get-key", "const a:any=new Uint8Array([7]);const reason={};let calls=0;const key:any={toString(){calls++;throw reason}};try{console.log(a[key])}catch(e){console.log(e===reason,calls,a[0])}", "true 1 7\n", true];
        yield return ["abrupt-put-key-after-rhs", "const a:any=new Uint8Array([7]);const reason={};let hooks=0,rhs=0,values=0;const key:any={toString(){hooks++;throw reason}},value={valueOf(){values++;return 9}};function make(){rhs++;return value}try{a[key]=make()}catch(e){console.log(e===reason,hooks,rhs,values,a[0])}", "true 1 1 0 7\n", true];
        yield return ["invalid-canonical-read", "const a:any=new Uint8Array([7]);for(const text of [\"-0\",\"0.5\",\"NaN\",\"Infinity\"]){let calls=0;const key:any={toString(){calls++;return text}};console.log(a[key],calls,a[0]);}", "undefined 1 7\nundefined 1 7\nundefined 1 7\nundefined 1 7\n", true];
        yield return ["invalid-canonical-write-converts", "const a:any=new Uint8Array([7]);let keys=0,values=0;const key:any={toString(){keys++;return \"0.5\"}},value={valueOf(){values++;return 9}};const result=(a[key]=value);console.log(result===value,keys,values,a[0],a[key]);", "true 1 1 7 undefined\n", true];
        yield return ["ordinary-owned-name", "const a:any=new Uint8Array([7]);let calls=0;const key:any={toString(){calls++;return \"tag\"}};a[key]=9;console.log(a[key],a.tag,calls,a[0]);", "9 9 2 7\n", false];
        yield return ["ordinary-inherited-getter", "const a:any=new Uint8Array([7]);const p:any={};let calls=0;Object.defineProperty(p,\"tag\",{get(){calls++;return this===a?9:0}});Object.setPrototypeOf(a,p);const key:any={toString(){return \"tag\"}};console.log(a[key],calls,a[0]);", "9 1 7\n", false];
        yield return ["ordinary-inherited-setter", "const a:any=new Uint8Array([7]);const p:any={};let seen=0;Object.defineProperty(p,\"tag\",{set(v){seen=v;console.log(this===a)}});Object.setPrototypeOf(a,p);const key:any={toString(){return \"tag\"}};a[key]=9;console.log(seen,Object.hasOwn(a,\"tag\"),a[0]);", "true\n9 false 7\n", false];
        yield return ["object-to-symbol-key", "const a:any=new Uint8Array([7]),s=Symbol(\"s\");let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return s}};a[key]=9;console.log(a[key],a[s],calls,a[0]);", "string\nstring\n9 9 2 7\n", true];
        yield return ["strict-readonly-symbol", "\"use strict\";const a:any=new Uint8Array([7]),s=Symbol(\"s\");Object.defineProperty(a,s,{value:3,writable:false});let calls=0;const key:any={[Symbol.toPrimitive](){calls++;return s}};try{a[key]=9}catch(e){console.log(e instanceof TypeError,calls,a[s],a[0])}", "true 1 3 7\n", true];
        yield return ["narrowing-and-expression-result", "const a:any=new Uint8ClampedArray([1]);let keys=0,values=0;const key:any={toString(){keys++;return \"0\"}},value={valueOf(){values++;return 999}};const result=(a[key]=value);console.log(result===value,keys,values,a[0]);", "true 1 1 255\n", true];
        yield return ["bigint-physical-value", "const a:any=new BigInt64Array([1n]);let keys=0;const key:any={toString(){keys++;return \"0\"}};const result=(a[key]=9n);console.log(String(a[key]),String(result),keys);", "9 9 2\n", true];
        yield return ["key-detaches-before-value-conversion", "const a:any=new Uint8Array([7]);let keys=0,values=0;const key:any={toString(){keys++;structuredClone(a.buffer,{transfer:[a.buffer]});return \"0\"}},value={valueOf(){values++;return 9}};const result=(a[key]=value);console.log(result===value,keys,values,a.length,a[0]);", "true 1 1 0 undefined\n", false];
        yield return ["awaited-base-and-key", "async function run(){const a:any=new Uint8Array([7]);let calls=0;const key:any={toString(){calls++;return \"0\"}};const value=(await Promise.resolve(a))[await Promise.resolve(key)];(await Promise.resolve(a))[await Promise.resolve(key)]=9;console.log(value,a[0],calls)}void run();", "7 9 2\n", true];
        yield return ["primitive-key-domains-control", "const a:any=new Uint8Array([7]),s=Symbol(\"s\");a[s]=9;console.log(a[0],a[\"0\"],a[s]);", "7 7 9\n", true];
        yield return ["ordinary-array-object-key-control", "const a:any=[7];let calls=0;const key:any={toString(){calls++;return \"0\"}};const value=a[key];a[key]=9;console.log(value,a[0],calls);", "7 9 2\n", true];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        ((bool)row[3] ? new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            : new[] { ExecutionMode.Interpreted }).Select(mode =>
                new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryObjectIndexReferencesUsePropertyKeys(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
