using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayCanonicalStringReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["primitive-original", "const a:any=new Uint8Array([7]),s=Symbol(\"s\");a[s]=9;console.log(a[0],a[\"0\"],a[s]);\n", "7 7 9\n", true];
        yield return ["physical-read-original", "const a:any=new Uint8Array([7]);console.log(a[\"0\"],a[0]);\n", "7 7\n", true];
        yield return ["physical-put-original", "const a:any=new Uint8Array([7]);a[\"0\"]=9;console.log(a[0]);\n", "9\n", true];
        yield return ["invalid-put-original", "for(const key of [\"-0\",\"0.5\",\"NaN\"]){const a:any=new Uint8Array([7]);let calls=0,threw=false;const value={valueOf(){calls++;return 9}};try{a[key]=value}catch(e){threw=true}console.log(a[key],a[0],calls,threw);}\n", "undefined 7 1 false\nundefined 7 1 false\nundefined 7 1 false\n", true];
        yield return ["all-element-kinds-physical-strings", "const views:any[]=[new Int8Array([7]),new Uint8Array([7]),new Uint8ClampedArray([7]),new Int16Array([7]),new Uint16Array([7]),new Int32Array([7]),new Uint32Array([7]),new Float32Array([7]),new Float64Array([7]),new BigInt64Array([7n]),new BigUint64Array([7n])];for(const a of views){const before=a[\"0\"];a[\"0\"]=typeof a[0]===\"bigint\"?9n:9;console.log(String(before),String(a[\"0\"]));}", "7 9\n7 9\n7 9\n7 9\n7 9\n7 9\n7 9\n7 9\n7 9\n7 9\n7 9\n", true];
        yield return ["invalid-physical-string-reads", "const a:any=new Uint8Array([7]);for(const key of [\"-0\",\"0.5\",\"NaN\",\"Infinity\",\"-Infinity\",\"4294967295\"]){console.log(a[key],a[0],a.length)}", "undefined 7 1\nundefined 7 1\nundefined 7 1\nundefined 7 1\nundefined 7 1\nundefined 7 1\n", true];
        yield return ["strict-invalid-write-is-ignored", "\"use strict\";const a:any=new Uint8Array([7]);let calls=0;const value={valueOf(){calls++;return 9}};a[\"0.5\"]=value;console.log(a[\"0.5\"],a[0],calls);", "undefined 7 1\n", true];
        yield return ["narrowing-and-result-identity", "const a:any=new Uint8ClampedArray([7]);let calls=0;const value={valueOf(){calls++;return 999}};const result=(a[\"0\"]=value);console.log(result===value,a[\"0\"],calls);", "true 255 1\n", true];
        yield return ["abrupt-value-conversion", "const a:any=new Uint8Array([7]),reason={};let calls=0;const value={valueOf(){calls++;throw reason}};try{a[\"0\"]=value}catch(e){console.log(e===reason,a[\"0\"],calls)}", "true 7 1\n", true];
        yield return ["bigint-physical-string-write", "const a:any=new BigInt64Array([7n]);const result=(a[\"0\"]=9n);console.log(String(a[\"0\"]),String(result));", "9 9\n", true];
        yield return ["bigint-invalid-index-converts-first", "const a:any=new BigInt64Array([7n]);try{a[\"0.5\"]=9}catch(e){console.log(e instanceof TypeError,String(a[\"0\"]),a[\"0.5\"])}", "true 7 undefined\n", true];
        yield return ["view-offset-physical-strings", "const buffer=new ArrayBuffer(8);const bytes:any=new Uint8Array(buffer);bytes[2]=7;const a:any=new Uint8Array(buffer,2,2);a[\"1\"]=9;console.log(a[\"0\"],a[\"1\"],bytes[2],bytes[3]);", "7 9 7 9\n", true];
        yield return ["owned-length-does-not-extend-storage", "const a:any=new Uint8Array([7]);Object.defineProperty(a,\"length\",{value:9});let calls=0;const value={valueOf(){calls++;return 8}};a[\"1\"]=value;console.log(a[\"0\"],a[\"1\"],a.length,calls);", "7 undefined 9 1\n", true];
        yield return ["null-prototype-physical-strings", "const a:any=new Uint8Array([7]);Object.setPrototypeOf(a,null);a[\"0\"]=9;console.log(a[\"0\"],a[\"1\"]);", "9 undefined\n", true];
        yield return ["physical-strings-ignore-prototype-indices", "const a:any=new Uint8Array([7]),p:any={};let gets=0,sets=0;Object.defineProperty(p,\"0\",{get(){gets++;return 99},set(v){sets++}});Object.defineProperty(p,\"2\",{get(){gets++;return 88}});Object.setPrototypeOf(a,p);a[\"0\"]=9;console.log(a[\"0\"],a[\"2\"],gets,sets);", "9 undefined 0 0\n", true];
        yield return ["ordinary-record-key-control", "const a:any={0:7};let calls=0;const value={valueOf(){calls++;return 9}};a[\"0\"]=value;console.log(a[\"0\"]===value,calls);", "true 0\n", true];
        yield return ["ordinary-array-key-control", "const a:any=[7];a[\"0\"]=9;console.log(a[\"0\"],a[0]);", "9 9\n", true];
        yield return ["symbol-key-control", "const a:any=new Uint8Array([7]),s=Symbol(\"s\");a[s]=9;console.log(a[s],a[0]);", "9 7\n", true];
        yield return ["awaited-canonical-string-reference", "async function run(){const a:any=new Uint8Array([7]);const before=(await Promise.resolve(a))[\"0\"];(await Promise.resolve(a))[\"0\"]=9;console.log(before,a[\"0\"],a[0])}void run();", "7 9 9\n", true];
        yield return ["detached-physical-string-reference", "const a:any=new Uint8Array([7]);structuredClone(a.buffer,{transfer:[a.buffer]});let calls=0;const value={valueOf(){calls++;return 9}};a[\"0\"]=value;console.log(a[\"0\"],a.length,calls);", "undefined 0 1\n", false];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        ((bool)row[3] ? new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            : new[] { ExecutionMode.Interpreted }).Select(mode =>
                new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CanonicalStringReferencesUsePhysicalElementRules(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
